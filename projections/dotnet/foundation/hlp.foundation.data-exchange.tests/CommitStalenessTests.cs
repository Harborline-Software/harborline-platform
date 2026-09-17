using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitStalenessTests
{
    [Fact]
    public async Task Proposal_drift_refuses_before_authority_or_target_dispatch_and_preserves_review()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy())
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var authority = new RecordingCommitAuthority(true);
        var target = new RecordingTarget();
        var evaluator = new FakeProposalEvaluator { Current = Fixtures.DryRunRequest() with { Proposal = dryRun.Proposal with { DependencyFingerprint = "sha256:changed-match-data" } } };
        var committer = new DataExchangeCommitter(
            runs,
            authority,
            target,
            target,
            new InMemoryAcquisitionCheckpointStore(),
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024), evaluator, new FakeSourcePolicies(), new FakeLifecyclePolicy(), new FakeTargetRegistry());

        var exception = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(
            () => committer.CommitAsync(dryRun.Id).AsTask());

        Assert.Equal("run.stale", exception.Code);
        Assert.NotNull(exception.SupersedingDryRunId);
        var superseding = await runs.GetDryRunAsync(exception.SupersedingDryRunId.Value);
        Assert.NotNull(superseding);
        Assert.Equal(dryRun.Id, superseding.SupersedesDryRunId);
        Assert.Equal(evaluator.Current.Proposal, superseding.Proposal);
        Assert.Equal(0, authority.Calls);
        Assert.Empty(target.Applied);
        var preserved = await runs.GetDryRunAsync(dryRun.Id);
        Assert.Equal("sha256:dependencies", preserved!.Proposal.DependencyFingerprint);
    }
}

internal sealed class RecordingCommitAuthority(bool allowed) : IExchangeCommitAuthority
{
    public int Calls { get; private set; }

    public ValueTask<bool> CanCommitAsync(
        DryRunArtifact dryRun,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(allowed);
    }
}

internal sealed class RecordingTarget : FakeTargetCommandPort, ITargetAccessGate, ICanonicalTargetCommandPort
{
    public List<EffectIdempotencyIdentity> Applied { get; } = [];

    public ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public override ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default)
    {
        Applied.Add(command.EffectIdentity);
        return ValueTask.FromResult(new EffectTerminalOutcome(
            ExchangeEffectStatus.Applied,
            "records.applied"));
    }
}
