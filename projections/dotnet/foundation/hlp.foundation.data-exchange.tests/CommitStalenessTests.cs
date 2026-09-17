using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitStalenessTests
{
    [Fact]
    public async Task Proposal_drift_refuses_before_authority_or_target_dispatch_and_preserves_review()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System)
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var authority = new RecordingCommitAuthority(true);
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(
            runs,
            authority,
            target,
            target,
            new InMemoryEffectOutcomeStore(),
            new InMemoryAcquisitionCheckpointStore(),
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024));

        var changed = dryRun.Proposal with { DependencyFingerprint = "sha256:changed-match-data" };
        var exception = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(
            () => committer.CommitAsync(dryRun.Id, changed).AsTask());

        Assert.Equal("run.stale", exception.Code);
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

internal sealed class RecordingTarget : ITargetAccessGate, ICanonicalRecordsCommandPort
{
    public List<EffectIdempotencyIdentity> Applied { get; } = [];

    public ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(true);

    public ValueTask<EffectTerminalOutcome> ApplyAsync(
        CanonicalRecordsCommand command,
        CancellationToken cancellationToken = default)
    {
        Applied.Add(command.EffectIdentity);
        return ValueTask.FromResult(new EffectTerminalOutcome(
            ExchangeEffectStatus.Applied,
            "records.applied"));
    }
}
