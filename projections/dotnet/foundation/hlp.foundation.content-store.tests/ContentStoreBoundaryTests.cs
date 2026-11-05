using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// T-521/content-store-host-transaction clauses 1 and 4, under the owner's ruling of 2026-11-05 (17:47 UTC): every store write
// carries its own actor and audit through the kernel boundary. A stand-in records write owns the caller's execution here,
// as a real one will. Expected values are literals, or stated properties: the caller's actor is the row's actor, and a
// rolled-back write leaves the image it found.
public sealed class ContentStoreBoundaryTests
{
    private static readonly TenantId Tenant = new("boundary-tenant");
    private static readonly byte[] Hello = "hello"u8.ToArray();
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);
    private const string Actor = "principal.alice";

    private static InMemoryContentStore Store(IContentStoreStorage backing, TimeProvider? clock = null, long record = 1000, long tenant = 1000) =>
        new(new(100_000, record, tenant, Expiry), new Keys(), clock ?? new Clock(), backing);

    private static ContentStoreWrite Write(string name, string actor = Actor, IContentBookkeepingSession? transaction = null) =>
        new(new("command-" + name, "key-" + name, "fingerprint-" + name), new("audit-" + name, actor, DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), transaction);

    private static (string Audit, string Actor)[] Rows(RecordingStorage backing) =>
        [.. backing.Memory.Snapshot().Audits.Select(audit => (audit.AuditId, audit.ActorId))];

    private static ValueTask<Guid> Put(InMemoryContentStore store, string name, IContentBookkeepingSession? transaction = null) =>
        store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null, Write(name, Actor, transaction));

    private static byte[] ReadBytes(ContentDelivery delivery)
    {
        using var body = delivery.Body;
        using var buffer = new MemoryStream();
        body.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task Clause_1_each_standalone_write_commits_its_own_transaction_with_its_own_audit_row_and_the_callers_actor()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);

        var stage = await Put(store, "put");
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("commit"));
        Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove")));
        await Put(store, "abandoned");
        clock.Advance(Expiry);
        Assert.Equal(1, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));

        Assert.Equal([("audit-put", Actor), ("audit-commit", Actor), ("audit-remove", Actor), ("audit-abandoned", Actor), ("audit-sweep", "sys.scheduler")], Rows(backing));
        Assert.Equal([ContentStoreOperation.Put, ContentStoreOperation.Commit, ContentStoreOperation.RemoveReference, ContentStoreOperation.Put, ContentStoreOperation.SweepExpired],
            backing.Commits.Select(commit => commit.Operation));
    }

    [Fact]
    public async Task Clause_1_a_write_that_changes_nothing_commits_nothing_and_writes_no_audit_row()
    {
        var backing = new RecordingStorage();
        var store = Store(backing, record: 5);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var over = await Put(store, "put-2");
        var before = Rows(backing);
        var commits = backing.Commits.Count;

        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, over, new("record-1", "1"), "b.txt", Write("refused")));
        Assert.False(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.Held, "held", Write("held")));
        Assert.False(await store.RemoveReferenceAsync(Tenant, reference with { Owner = new("record-9", "0") }, HoldStatus.None, "unknown", Write("unknown")));
        Assert.Equal(0, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));

        Assert.Equal(before, Rows(backing));
        Assert.Equal(commits, backing.Commits.Count);
    }

    [Fact]
    public async Task Clause_1_the_sweep_refuses_any_actor_but_the_scheduler_before_it_changes_anything()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        await Put(store, "abandoned");
        clock.Advance(Expiry);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SweepExpiredAsync("sweep", Write("sweep", Actor)));
        Assert.Equal(1, store.StagedCount);
        Assert.Equal("sys.scheduler", ContentStoreRunRegistration.StagingSweepActor);
    }

    [Theory]
    [InlineData("", "key", "fingerprint", "audit", Actor)]
    [InlineData("command", " ", "fingerprint", "audit", Actor)]
    [InlineData("command", "key", "", "audit", Actor)]
    [InlineData("command", "key", "fingerprint", " ", Actor)]
    [InlineData("command", "key", "fingerprint", "audit", " ")]
    public async Task Clause_1_a_write_without_a_whole_identity_and_actor_is_refused_before_any_byte_is_kept_and_no_actor_is_invented(
        string command, string key, string fingerprint, string audit, string actor)
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var write = new ContentStoreWrite(new(command, key, fingerprint), new(audit, actor, DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty));

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null, write));
        Assert.Empty(await backing.Bytes.WrittenAtOrBeforeAsync(DateTimeOffset.MaxValue));
        Assert.Empty(Rows(backing));
    }

    [Fact]
    public async Task Clause_1_an_idempotency_key_already_used_is_refused_by_the_backing_and_nothing_is_staged()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await Put(store, "same");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Put(store, "same"));
        Assert.Equal(1, store.StagedCount);
        Assert.Single(Rows(backing));
    }

    [Fact]
    public async Task Clause_1_a_joined_write_stages_its_own_audit_row_and_only_the_callers_execution_commits_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await Put(store, "put");
        var caller = new RecordWrite(backing.Bookkeeping);
        var commitsBefore = backing.Commits.Count;

        await caller.RunAsync(async session =>
        {
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("joined", Actor, session));
            Assert.Equal(commitsBefore, backing.Commits.Count); // the store did not commit the caller's transaction
            Assert.Equal(0, caller.Commits);
        });

        Assert.Equal(1, caller.Commits);
        Assert.Equal(1, store.CommittedCount);
        Assert.Equal(0, store.StagedCount);
        Assert.Equal([("audit-put", Actor), ("audit-joined", Actor)], Rows(backing));
    }

    [Fact]
    public async Task Clause_1_without_the_callers_transaction_a_write_inside_an_open_execution_is_refused_and_writes_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await Put(store, "put");
        var caller = new RecordWrite(backing.Bookkeeping);

        KernelTransactionStateException? nested = null;
        await caller.RunAsync(async _ =>
        {
            nested = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("nested")));
        });

        Assert.Equal(KernelTransactionErrors.NestedExecution, nested!.Code);
        Assert.Equal(1, store.StagedCount);
        Assert.Equal(0, store.CommittedCount);
    }

    [Fact]
    public async Task Clause_1_a_transaction_with_no_open_execution_is_refused_and_writes_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await Put(store, "put");
        await using var orphan = await backing.Bookkeeping.BeginTransactionAsync();

        var refused = await Assert.ThrowsAsync<KernelTransactionStateException>(async () =>
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("orphan", Actor, orphan)));
        Assert.Equal(KernelTransactionErrors.JoinWithoutEnclosingExecution, refused.Code);
        Assert.Equal(1, store.StagedCount);
        Assert.Equal(0, store.CommittedCount);
    }

    [Fact]
    public async Task Clause_1_a_joined_refusal_the_caller_catches_still_dooms_its_execution_so_nothing_half_written_commits()
    {
        var backing = new RecordingStorage();
        var store = Store(backing, record: 5);
        var first = await Put(store, "first");
        var second = await store.PutAsync(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null, Write("second"));
        var caller = new RecordWrite(backing.Bookkeeping);

        var doomed = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await caller.RunAsync(async session =>
        {
            await store.CommitReferenceAsync(Tenant, first, new("record-1", "0"), "a.txt", Write("fits", Actor, session));
            await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, second, new("record-1", "1"), "b.txt", Write("over", Actor, session)));
        }));

        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, doomed.Code);
        Assert.Equal(0, store.CommittedCount);
        Assert.Equal(2, store.StagedCount);
        Assert.Equal([("audit-first", Actor), ("audit-second", Actor)], Rows(backing));
    }

    [Fact]
    public async Task Clause_4_a_caller_rolled_back_after_a_commit_leaves_no_reference_the_quota_unchanged_and_the_item_still_staged()
    {
        var backing = new RecordingStorage();
        var store = Store(backing, record: 5, tenant: 5);
        var stage = await Put(store, "put");
        var before = RecordingStorage.Capture(backing.Memory);
        var caller = new RecordWrite(backing.Bookkeeping);

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("joined", Actor, session));
            Assert.Equal(1, store.CommittedCount);
            throw new InjectedFault();
        }));

        var after = RecordingStorage.Capture(backing.Memory);
        Assert.Equal(0, caller.Commits);
        Assert.Empty(after.Items);
        Assert.Empty(after.Quota);
        Assert.Equal(stage, Assert.Single(after.Staged).StageId);
        Assert.Equal(before.Audits.Length, after.Audits.Length);
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("retry")); // the quota and the stage are both still there
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, reference, true)));
    }

    [Fact]
    public async Task Clause_4_a_failed_commit_of_the_callers_transaction_undoes_the_store_write_too()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await Put(store, "put");
        var caller = new RecordWrite(backing.Bookkeeping) { FailCommit = new InjectedFault() };

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("joined", Actor, session))));

        Assert.Equal(0, store.CommittedCount);
        Assert.Equal(1, store.StagedCount);
    }

    [Fact]
    public async Task Clause_4_a_rolled_back_removal_leaves_the_reference_its_quota_and_its_bytes_in_place()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock, record: 5);
        var stage = await Put(store, "put");
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("commit"));
        backing.Events.Clear();
        var caller = new RecordWrite(backing.Bookkeeping);

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove", Actor, session)));
            throw new InjectedFault();
        }));

        Assert.DoesNotContain("bytes-delete", backing.Events);
        Assert.Empty(store.Reclamations);
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, reference, true)));
        var over = await Put(store, "over");
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, over, new("record-1", "1"), "b.txt", Write("refused")));
        clock.Advance(Expiry * 10);
        await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, reference, true)));
    }

    [Fact]
    public async Task Clause_5_a_joined_removal_keeps_its_bytes_until_the_sweep_after_the_caller_commits()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var stage = await Put(store, "put");
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("commit"));
        backing.Events.Clear();
        var caller = new RecordWrite(backing.Bookkeeping);

        await caller.RunAsync(async session =>
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove", Actor, session))));

        Assert.DoesNotContain("bytes-delete", backing.Events); // the store cannot see the caller's commit
        Assert.NotNull(backing.Bytes.ReadSegment(stage, 0));
        Assert.Equal("last-reference-removed", Assert.Single(store.Reclamations).Cause);

        clock.Advance(Expiry);
        await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler"));
        Assert.Null(backing.Bytes.ReadSegment(stage, 0));
    }

    [Fact]
    public async Task Clause_5_a_joined_put_rolled_back_leaves_bytes_only_the_sweep_takes_and_no_entry()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var caller = new RecordWrite(backing.Bookkeeping);
        var stage = Guid.Empty;

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            stage = await Put(store, "joined", session);
            throw new InjectedFault();
        }));

        Assert.Equal(0, store.StagedCount);
        Assert.NotNull(backing.Bytes.ReadSegment(stage, 0));
        clock.Advance(Expiry);
        Assert.Equal(0, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));
        Assert.Null(backing.Bytes.ReadSegment(stage, 0));
    }

    private sealed class InjectedFault : Exception;

    // A stand-in records write: it owns the boundary execution and its one transaction, and hands the store only the session.
    private sealed class RecordWrite(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<string, string>
    {
        private IContentBookkeepingTransaction? _inner;
        public int Commits { get; private set; }
        public Exception? FailCommit { get; init; }

        public async Task RunAsync(Func<IContentBookkeepingSession, Task> writes)
        {
            await KernelTransactionBoundary.ExecutePreparedAsync(async _ =>
            {
                await writes(_inner!);
                return new KernelCommand<string>(new("record-command", "record-key", "record-fingerprint"), "record",
                    new("record-audit", "principal.record-writer", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty));
            }, this);
        }

        public async ValueTask<IKernelPreparedTransaction<string, string>> BeginAsync(CancellationToken cancellationToken = default)
        {
            _inner = await bookkeeping.BeginTransactionAsync(cancellationToken);
            return new Transaction(this, _inner);
        }

        private sealed class Transaction(RecordWrite owner, IContentBookkeepingTransaction inner) : IKernelPreparedTransaction<string, string>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public async ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                if (owner.FailCommit is { } fault) throw fault;
                await inner.CommitAsync(cancellationToken);
                owner.Commits++;
                return "committed";
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => SHA256.HashData(Encoding.UTF8.GetBytes("boundary:" + tenantId.Value));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
