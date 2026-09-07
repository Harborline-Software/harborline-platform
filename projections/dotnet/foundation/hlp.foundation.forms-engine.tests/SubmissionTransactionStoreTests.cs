using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class SubmissionTransactionStoreTests
{
    private static readonly TenantId Tenant = new("tenant-engine");

    [Fact]
    public async Task SaveAsync_AuditFailureLeavesNoSubmissionOrOutbox()
    {
        var state = new InMemoryFormSubmissionState();
        var store = new InMemoryFormSubmissionStore(state, _ => new IOException("injected"));
        await Assert.ThrowsAsync<IOException>(async () => await store.CommitAsync(Commit("key", "fingerprint")));
        Assert.Equal((0, 0, 0, 0), await store.CountsAsync());
    }

    [Fact]
    public async Task SaveAsync_SameIdempotencyKeyAndFingerprint_ReturnsOriginalReceipt()
    {
        var store = new InMemoryFormSubmissionStore();
        var first = await store.CommitAsync(Commit("key", "same"));
        var replay = await store.CommitAsync(Commit("key", "same", localPart: "different"));
        Assert.Equal(FormSubmissionCommitDisposition.Created, first.Disposition);
        Assert.Equal(FormSubmissionCommitDisposition.Replayed, replay.Disposition);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal((1, 1, 1, 0), await store.CountsAsync());
    }

    [Fact]
    public async Task SaveAsync_SameIdempotencyKeyDifferentFingerprint_Conflicts()
    {
        var store = new InMemoryFormSubmissionStore();
        await store.CommitAsync(Commit("key", "first"));
        var conflict = await store.CommitAsync(Commit("key", "different", localPart: "other"));
        Assert.Equal(FormSubmissionCommitDisposition.Conflict, conflict.Disposition);
        Assert.Null(conflict.Receipt);
        Assert.Equal((1, 1, 1, 0), await store.CountsAsync());
    }

    [Fact]
    public async Task SaveAsync_ConcurrentSameKey_CreatesOneSubmissionAuditAndOutbox()
    {
        var store = new InMemoryFormSubmissionStore();
        var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => store.CommitAsync(Commit("key", "same")).AsTask()));
        Assert.Single(results, result => result.Disposition == FormSubmissionCommitDisposition.Created);
        Assert.Equal(31, results.Count(result => result.Disposition == FormSubmissionCommitDisposition.Replayed));
        Assert.Equal((1, 1, 1, 0), await store.CountsAsync());
    }

    [Fact]
    public async Task Pending_outbox_survives_adapter_reconstruction_and_completes_once()
    {
        var state = new InMemoryFormSubmissionState();
        var first = new InMemoryFormSubmissionStore(state);
        await first.CommitAsync(Commit("key", "same"));
        await first.ReleaseProjectionLeaseAsync("outbox-instance");
        var restarted = new InMemoryFormSubmissionStore(state);
        var row = Assert.Single(await restarted.LeasePendingAsync(10));
        await restarted.RetryProjectionAsync(row.OutboxId, "projection.unavailable");
        var retried = Assert.Single(await restarted.LeasePendingAsync(10));
        Assert.Equal(1, retried.Attempts);
        await restarted.CompleteProjectionAsync(row.OutboxId, Array.Empty<FormProjectionSkip>());
        await restarted.CompleteProjectionAsync(row.OutboxId, Array.Empty<FormProjectionSkip>());
        Assert.Empty(await restarted.LeasePendingAsync(10));
        Assert.Equal((1, 1, 0, 1), await restarted.CountsAsync());
    }

    private static FormSubmissionCommit Commit(string key, string fingerprint, string localPart = "instance")
    {
        var instant = DateTimeOffset.Parse("2026-08-08T12:00:00Z");
        var instance = new EntityId("harborline", "forms", localPart);
        return new(
            key,
            new(instance, Tenant, Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice", new("inspection"), new(1, 0, 0), fingerprint, "{\"protected\":true}"u8.ToArray(), instant),
            new($"audit-{localPart}", instance, Tenant, "alice", "{\"op\":\"submit\"}"u8.ToArray(), instant),
            new($"outbox-{localPart}", instance, Tenant, Guid.Parse("11111111-1111-1111-1111-111111111111"), "alice",
                new("inspection"), new(1, 0, 0), "case-1", "{\"accepted\":true}"u8.ToArray(), instant),
            new(instance, instant, FormProjectionStatus.Pending, Array.Empty<FormProjectionSkip>()));
    }
}
