using Harborline.Foundation.DataExchange;
using Xunit;

namespace Harborline.Foundation.DataExchange.Tests;

public sealed class CommitProtocolIntegrityTests
{
    [Theory]
    [InlineData(ExchangeEffectStatus.Failed)]
    [InlineData(ExchangeEffectStatus.Halted)]
    public void Retryable_outcomes_cannot_be_acknowledgement_safe(ExchangeEffectStatus status)
    {
        Assert.Throws<ArgumentException>(() => new AcknowledgementPolicy(
            "invalid",
            new HashSet<ExchangeEffectStatus> { status },
            new HashSet<ExchangeEffectStatus>()));
    }

    [Fact]
    public async Task Effect_claim_prevents_duplicate_delivery_and_stale_completion()
    {
        var store = new RecordingTarget();
        var batch = new BatchIdentity("batch");
        var effect = new EffectIdempotencyIdentity("effect");
        var firstAttempt = AttemptId.New();
        var secondAttempt = AttemptId.New();
        var now = DateTimeOffset.UtcNow;

        var firstClaim = await store.ClaimAsync(batch, effect, firstAttempt, now, now.AddMinutes(1));
        var concurrentClaim = await store.ClaimAsync(batch, effect, secondAttempt, now, now.AddMinutes(1));
        var proposed = Fixtures.DryRunRequest().Effects[0];
        await store.ExecuteAndRecordAsync(new(
            new CommitRunId("commit"),
            batch,
            effect,
            firstAttempt,
            "records.customer/v1",
            proposed,
            new TargetAuthorizationContext("tenant", "actor", "authz"),
            new CanonicalEffectPayload("records.customer/v1", new Dictionary<string, object?>())));
        var replayClaim = await store.ClaimAsync(batch, effect, secondAttempt, now.AddSeconds(1), now.AddMinutes(2));

        Assert.True(firstClaim.Acquired);
        Assert.False(concurrentClaim.Acquired);
        Assert.Null(concurrentClaim.ExistingOutcome);
        Assert.False(replayClaim.Acquired);
        Assert.Equal(ExchangeEffectStatus.Applied, replayClaim.ExistingOutcome?.Outcome.Status);
    }

    [Fact]
    public async Task Checkpoint_compare_and_swap_rejects_stale_or_regressive_promotion()
    {
        var store = new InMemoryAcquisitionCheckpointStore();
        var firstBatch = new BatchIdentity("batch-1");
        var secondBatch = new BatchIdentity("batch-2");

        await store.PromoteAsync("tenant", "definition", null, "cursor:a", firstBatch, 1);
        await Assert.ThrowsAsync<CheckpointConflictException>(() => store.PromoteAsync(
            "tenant", "definition", null, "cursor:b", secondBatch, 2).AsTask());
        await store.PromoteAsync("tenant", "definition", null, "cursor:d", firstBatch, 4);
        await Assert.ThrowsAsync<CheckpointConflictException>(() => store.PromoteAsync(
            "tenant", "definition", null, "cursor:c", firstBatch, 3).AsTask());

        Assert.Equal("cursor:d", await store.GetAsync("tenant", "definition"));
    }

    [Fact]
    public async Task Checkpoint_conflict_leaves_commit_and_pending_finalization_as_recovery_evidence()
    {
        var runs = new CapturingRunStore();
        var request = Fixtures.DryRunRequest() with { ExpectedCheckpoint = "cursor:expected" };
        var dryRun = await new DataExchangeRuntime(runs, TimeProvider.System, new FakeLifecyclePolicy()).CreateDryRunAsync(request);
        var target = new RecordingTarget();
        var committer = new DataExchangeCommitter(
            runs,
            new RecordingCommitAuthority(true),
            target,
            target,
            new AlwaysConflictingCheckpointStore(),
            new InMemoryProtectedEffectPayloadStore(),
            TimeProvider.System,
            new CommitBounds(100, 1, 64 * 1024),
            new FakeProposalEvaluator(),
            new FakeSourcePolicies(),
            new FakeLifecyclePolicy(),
            new FakeTargetRegistry());

        await Assert.ThrowsAsync<CheckpointConflictException>(
            () => committer.CommitAsync(dryRun.Id).AsTask());

        Assert.NotNull(runs.LastCommitRun);
        var stored = await runs.GetCommitRunAsync(runs.LastCommitRun.Id);
        var finalization = await runs.GetCheckpointFinalizationAsync(runs.LastCommitRun.Id);
        Assert.NotNull(stored);
        Assert.Equal(CheckpointFinalizationStatus.Pending, finalization?.Status);
        Assert.Equal("cursor:expected", finalization?.ExpectedCheckpoint);
        Assert.Equal("cursor:b", finalization?.PromotedCheckpoint);
    }
}

internal sealed class AlwaysConflictingCheckpointStore : IAcquisitionCheckpointStore
{
    public ValueTask<string?> GetAsync(
        string tenantId,
        string definitionId,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult<string?>("cursor:other");

    public ValueTask PromoteAsync(
        string tenantId,
        string definitionId,
        string? expectedCurrentCheckpoint,
        string checkpoint,
        BatchIdentity batchIdentity,
        int sourceOrdinal,
        CancellationToken cancellationToken = default)
        => ValueTask.FromException(new CheckpointConflictException("stale checkpoint"));
}

internal sealed class CapturingRunStore : IExchangeRunStore
{
    private readonly InMemoryExchangeRunStore _inner = new();

    public CommitRunArtifact? LastCommitRun { get; private set; }

    public ValueTask SaveDryRunAsync(DryRunArtifact artifact, CancellationToken cancellationToken = default)
        => _inner.SaveDryRunAsync(artifact, cancellationToken);

    public ValueTask<DryRunArtifact?> GetDryRunAsync(DryRunId id, CancellationToken cancellationToken = default)
        => _inner.GetDryRunAsync(id, cancellationToken);

    public ValueTask SaveCommitRunAsync(CommitRunArtifact artifact, CancellationToken cancellationToken = default)
        => _inner.SaveCommitRunAsync(artifact, cancellationToken);

    public ValueTask SaveCommitRunWithCheckpointFinalizationAsync(
        CommitRunArtifact artifact,
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default)
    {
        LastCommitRun = artifact;
        return _inner.SaveCommitRunWithCheckpointFinalizationAsync(artifact, finalization, cancellationToken);
    }

    public ValueTask<CommitRunArtifact?> GetCommitRunAsync(CommitRunId id, CancellationToken cancellationToken = default)
        => _inner.GetCommitRunAsync(id, cancellationToken);

    public ValueTask SaveCheckpointFinalizationAsync(
        CommitCheckpointFinalization finalization,
        CancellationToken cancellationToken = default)
        => _inner.SaveCheckpointFinalizationAsync(finalization, cancellationToken);

    public ValueTask<CommitCheckpointFinalization?> GetCheckpointFinalizationAsync(
        CommitRunId id,
        CancellationToken cancellationToken = default)
        => _inner.GetCheckpointFinalizationAsync(id, cancellationToken);
}
