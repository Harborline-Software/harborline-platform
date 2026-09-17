using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class ReplayConformanceTests
{
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
