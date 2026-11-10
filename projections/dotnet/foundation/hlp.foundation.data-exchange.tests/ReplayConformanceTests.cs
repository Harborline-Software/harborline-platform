using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ReplayConformanceTests
{
    [Fact]
    public async Task Closure_refuses_a_census_that_disagrees_with_complete_effects()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var forged = commit with { Id = new CommitRunId("forged-census"), Census = new ExchangeCensus(1, 0, 0, 1, 0, 0) };

        var refusal = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runs.SaveCommitRunAsync(forged).AsTask());
        Assert.Equal("run.census_incomplete", refusal.Code);
        Assert.Null(await runs.GetCommitRunAsync(forged.Id));
    }

    [Fact]
    public async Task Closure_refuses_a_terminal_status_that_disagrees_with_the_census()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var forged = commit with { Id = new CommitRunId("forged-terminal"), TerminalStatus = ExchangeRunTerminalStatus.Halted };

        var refusal = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runs.SaveCommitRunAsync(forged).AsTask());
        Assert.Equal("run.terminal_inconsistent", refusal.Code);
        Assert.Null(await runs.GetCommitRunAsync(forged.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Closure_refuses_changed_batch_evidence(int changedPart)
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var forged = changedPart switch
        {
            0 => commit with { ApprovedDryRunId = DryRunId.New() },
            1 => commit with { BatchDerivation = commit.BatchDerivation with { TenantId = "other" } },
            _ => commit with { BatchIdentity = new BatchIdentity("other") },
        };

        var refusal = Assert.Throws<DataExchangeCommitRefusedException>(() => ExchangeRunClosure.Validate(dryRun, forged));
        Assert.Equal("run.stale", refusal.Code);
    }

    [Fact]
    public async Task Closure_refuses_duplicate_effect_identity_even_when_count_matches()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var forged = commit with { Effects = [commit.Effects[0], commit.Effects[0]] };

        var refusal = Assert.Throws<DataExchangeCommitRefusedException>(() => ExchangeRunClosure.Validate(dryRun, forged));
        Assert.Equal("run.census_incomplete", refusal.Code);
    }

    [Fact]
    public async Task Closure_refuses_changed_metadata_even_if_another_entry_matches()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var effect = commit.Effects[0];
        var metadata = effect.Effect.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata["digest"] = "tampered";
        var forged = commit with { Effects = [effect with { Effect = effect.Effect with { Metadata = metadata } }, commit.Effects[1]] };

        var refusal = Assert.Throws<DataExchangeCommitRefusedException>(() => ExchangeRunClosure.Validate(dryRun, forged));
        Assert.Equal("run.stale", refusal.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Changed_mapping_or_dependency_with_same_source_reuses_no_success(bool mapping)
    {
        var runs = new InMemoryExchangeRunStore();
        var runtime = new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy());
        var request = Fixtures.DryRunRequest();
        var first = await runtime.CreateDryRunAsync(request);
        var target = new RecordingTarget();
        var committer = Create(runs, target);
        var committed = await committer.CommitAsync(first.Id);
        var changed = await runtime.CreateDryRunAsync(request with
        {
            ExpectedCheckpoint = "cursor:b",
            Proposal = mapping
                ? request.Proposal with { MappingVersion = "2.0.0" }
                : request.Proposal with { DependencyFingerprint = "sha256:new-transform" },
        });
        var second = await committer.CommitAsync(changed.Id);
        Assert.NotEqual(committed.BatchIdentity, second.BatchIdentity);
        Assert.Equal(4, target.Applied.Count);
        Assert.All(second.Effects, effect => Assert.False(effect.ReplayedFromLedger));
    }

    [Fact]
    public async Task Missing_effect_from_census_refuses_closure()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var incomplete = commit with { Id = new CommitRunId("incomplete"), Effects = commit.Effects.Take(1).ToArray() };
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runs.SaveCommitRunAsync(incomplete).AsTask());
        Assert.Equal("run.census_incomplete", refused.Code);
        Assert.Null(await runs.GetCommitRunAsync(incomplete.Id));
    }

    [Fact]
    public async Task Effect_identity_cannot_hide_changed_reviewed_effect_content()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var changed = commit with
        {
            Id = new CommitRunId("changed-effect"),
            Effects = commit.Effects.Select((result, index) => index == 0
                ? result with { Effect = result.Effect with { BoundaryAfter = "cursor:tampered" } }
                : result).ToArray(),
        };
        var refusal = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runs.SaveCommitRunAsync(changed).AsTask());
        Assert.Equal("run.stale", refusal.Code);
    }

    [Fact]
    public async Task Unknown_terminal_effect_status_refuses_closure()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new ScriptedTarget(new Dictionary<string, Queue<ExchangeEffectStatus>>
        {
            ["customer-42"] = new([(ExchangeEffectStatus)999]),
            ["customer-43"] = new([ExchangeEffectStatus.Applied]),
        });
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => Create(runs, target).CommitAsync(dryRun.Id).AsTask());
        Assert.Equal("run.outcome_unknown", refused.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_or_missing_source_policy_refuses_before_effects(bool unknown)
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new RecordingTarget();
        var policies = new FakeSourcePolicies { Policy = unknown
            ? FakeSourcePolicies.Known with { SafeOutcomes = new HashSet<ExchangeEffectStatus> { (ExchangeEffectStatus)999 } }
            : null };
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => Create(runs, target, policies).CommitAsync(dryRun.Id).AsTask());
        Assert.Equal("commit.outcome_policy_unknown", refused.Code);
        Assert.Empty(target.Applied);
    }

    [Fact]
    public async Task Unknown_run_terminal_status_refuses_closure()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var commit = await Create(runs, new RecordingTarget()).CommitAsync(dryRun.Id);
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => runs.SaveCommitRunAsync(
            commit with { Id = new CommitRunId("unknown"), TerminalStatus = (ExchangeRunTerminalStatus)999 }).AsTask());
        Assert.Equal("run.terminal_unknown", refused.Code);
    }

    [Fact]
    public void Every_semantic_dependency_changes_batch_identity_and_attempt_is_absent()
    {
        var inputs = BatchIdentityInputs.From("tenant-a", Fixtures.DryRunRequest().Proposal);
        var baseline = ExchangeIdentity.DeriveBatch(inputs);
        foreach (var property in typeof(BatchIdentityInputs).GetProperties())
        {
            var changed = inputs with { };
            property.SetValue(changed, "changed:" + property.Name);
            Assert.NotEqual(baseline, ExchangeIdentity.DeriveBatch(changed));
        }
        Assert.DoesNotContain(typeof(BatchIdentityInputs).GetProperties(), property => property.PropertyType == typeof(AttemptId));
        Assert.DoesNotContain(typeof(ExchangeIdentity).GetMethods().SelectMany(method => method.GetParameters()),
            parameter => parameter.ParameterType == typeof(AttemptId));
    }

    private static DataExchangeCommitter Create(IExchangeRunStore runs, ICanonicalTargetCommandPort target, FakeSourcePolicies? policies = null)
        => new(runs, new RecordingCommitAuthority(true), new RecordingTarget(), target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), policies ?? new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
}
