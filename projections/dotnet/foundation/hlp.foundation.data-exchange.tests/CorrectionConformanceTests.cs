using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CorrectionConformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicted_effect_invokes_domain_correction_only_when_policy_requests_it(bool correct)
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new ScriptedTarget(new Dictionary<string, Queue<ExchangeEffectStatus>>
        {
            ["customer-42"] = new([ExchangeEffectStatus.Conflicted]),
            ["customer-43"] = new([ExchangeEffectStatus.Applied]),
        });
        var policies = new FakeSourcePolicies { Policy = FakeSourcePolicies.Known with { CorrectConflicts = correct } };
        var committer = new DataExchangeCommitter(runs, new RecordingCommitAuthority(true), target, target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), policies, new FakeLifecyclePolicy(), new FakeTargetRegistry());
        var committed = await committer.CommitAsync(dryRun.Id);
        Assert.Equal(correct ? 1 : 0, target.Corrections.Count);
        Assert.Equal(correct ? 2 : 1, committed.Census.Applied);
        if (correct)
        {
            var correction = Assert.Single(target.Corrections);
            Assert.Equal("customer-42", correction.OriginalCommand.Effect.SourceRecordIdentity);
            Assert.Equal(ExchangeEffectStatus.Conflicted, correction.Conflict.Status);
            Assert.Equal(dryRun.AuthorizationContextReference, correction.OriginalCommand.Authorization.ContextReference);
            Assert.Equal(ExchangeEffectStatus.Applied,
                (await target.GetOutcomeAsync(correction.OriginalCommand.EffectIdentity))!.Outcome.Status);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rollback_or_multi_command_atomic_request_is_explicitly_refused(bool rollback, bool atomic)
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(runs, new RecordingCommitAuthority(true), target, target,
            new InMemoryAcquisitionCheckpointStore(), TimeProvider.System,
            new CommitBounds(100, 1, 65536), new FakeProposalEvaluator(), new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());
        var refused = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(() => committer.CommitAsync(
            dryRun.Id, new CommitOptions(rollback, atomic)).AsTask());
        Assert.Equal("commit.rollback_refused", refused.Code);
        Assert.Empty(target.Applied);
    }
}
