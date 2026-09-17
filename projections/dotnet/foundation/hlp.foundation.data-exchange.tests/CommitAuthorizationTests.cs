using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitAuthorizationTests
{
    [Fact]
    public async Task Exchange_permission_is_necessary_but_each_effect_still_passes_target_access()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System)
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            new DenyOneTargetAccess("customer-43"),
            target,
            new InMemoryEffectOutcomeStore(),
            new InMemoryAcquisitionCheckpointStore(),
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024));

        var commit = await committer.CommitAsync(dryRun.Id, dryRun.Proposal);

        Assert.Single(target.Applied);
        Assert.Equal(1, commit.Census.Applied);
        Assert.Equal(1, commit.Census.Rejected);
        Assert.Equal(ExchangeRunTerminalStatus.CompletedWithRefusals, commit.TerminalStatus);
        var stored = await runs.GetCommitRunAsync(commit.Id);
        Assert.NotNull(stored);
        Assert.Equal(dryRun.Id, stored.ApprovedDryRunId);
        Assert.Equal(commit.BatchIdentity, stored.BatchIdentity);
        Assert.Equal("standard-7y", stored.RetentionClass);
        Assert.Equal(dryRun.RetainUntil, stored.RetainUntil);
    }

    [Fact]
    public async Task Exchange_commit_permission_refuses_before_target_access()
    {
        var runs = new InMemoryExchangeRunStore();
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System)
            .CreateDryRunAsync(Fixtures.DryRunRequest());
        var access = new DenyOneTargetAccess("none");
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(false),
            access,
            target,
            new InMemoryEffectOutcomeStore(),
            new InMemoryAcquisitionCheckpointStore(),
            TimeProvider.System,
            new CommitBounds(100, 4, 64 * 1024));

        var exception = await Assert.ThrowsAsync<DataExchangeCommitRefusedException>(
            () => committer.CommitAsync(dryRun.Id, dryRun.Proposal).AsTask());

        Assert.Equal("commit.forbidden", exception.Code);
        Assert.Equal(0, access.Calls);
        Assert.Empty(target.Applied);
    }
}

internal sealed class DenyOneTargetAccess(string deniedIdentity) : ITargetAccessGate
{
    public int Calls { get; private set; }

    public ValueTask<bool> CanApplyAsync(
        ProposedEffect effect,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        return ValueTask.FromResult(effect.SourceRecordIdentity != deniedIdentity);
    }
}
