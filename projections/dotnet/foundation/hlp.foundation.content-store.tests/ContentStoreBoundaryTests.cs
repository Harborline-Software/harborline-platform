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

    private static InMemoryContentStore Store(IContentStoreStorage backing, TimeProvider? clock = null, long record = 1_000_000, long tenant = 1_000_000) =>
        new(new(100_000, record, tenant, Expiry), new Keys(), clock ?? new Clock(), backing);

    private static ContentStoreWrite Write(string name, string actor = Actor, IContentBookkeepingSession? transaction = null) =>
        new(new("command-" + name, "key-" + name, "fingerprint-" + name), new("audit-" + name, actor, DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), transaction);

    private static (string Audit, string Actor)[] Rows(RecordingStorage backing) =>
        [.. backing.Memory.Snapshot().Audits.Select(audit => (audit.AuditId, audit.ActorId))];

    private static ValueTask<Guid> Put(InMemoryContentStore store, string name, IContentBookkeepingSession? transaction = null) =>
        store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null, Write(name, Actor, transaction));

    private static async Task<byte[]> ReadBytes(ContentDelivery delivery)
    {
        using var body = delivery.Body;
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer);
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
            Assert.Equal(0, store.CommittedCount); // uncommitted, so no other reader sees it
            throw new InjectedFault();
        }));

        var after = RecordingStorage.Capture(backing.Memory);
        Assert.Equal(0, caller.Commits);
        Assert.Empty(after.Items);
        Assert.Empty(after.Quota);
        Assert.Equal(stage, Assert.Single(after.Staged).StageId);
        Assert.Equal(before.Audits.Length, after.Audits.Length);
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("retry")); // the quota and the stage are both still there
        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, reference, true)));
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
        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, reference, true)));
        var over = await Put(store, "over");
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, over, new("record-1", "1"), "b.txt", Write("refused")));
        clock.Advance(Expiry * 10);
        await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler"));
        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, reference, true)));
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
        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(stage, 0));
        Assert.Equal("last-reference-removed", Assert.Single(store.Reclamations).Cause);

        clock.Advance(Expiry);
        await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler"));
        Assert.Null(await backing.Bytes.ReadSegmentAsync(stage, 0));
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
        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(stage, 0));
        clock.Advance(Expiry);
        Assert.Equal(0, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));
        Assert.Null(await backing.Bytes.ReadSegmentAsync(stage, 0));
    }

    [Fact]
    public async Task Clause_1_a_joined_write_stages_its_record_and_audit_and_one_that_changes_nothing_stages_neither()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var stage = await Put(store, "put-2");
        var rows = Rows(backing);
        backing.StagedRecords.Clear();
        var caller = new RecordWrite(backing.Bookkeeping);

        await caller.RunAsync(async session =>
        {
            Assert.False(await store.RemoveReferenceAsync(Tenant, reference with { Owner = new("record-9", "0") }, HoldStatus.None, "unknown", Write("unknown", Actor, session)));
            Assert.Empty(backing.StagedRecords);
            Assert.Equal(rows, Rows(backing));
            await store.CommitReferenceAsync(Tenant, stage, new("record-2", "0"), "b.txt", Write("joined", Actor, session));
            Assert.Equal([ContentStoreOperation.Commit], backing.StagedRecords);
        });

        Assert.Equal([.. rows, ("audit-joined", Actor)], Rows(backing));
    }

    [Fact]
    public async Task Clause_1_removal_and_the_sweep_refuse_evidence_without_a_whole_identity_and_a_null_write()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var blank = new ContentStoreWrite(new("command", " ", "fingerprint"), new("audit", "sys.scheduler", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty));

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.Held, "dispose", blank));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SweepExpiredAsync("sweep", blank));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SweepExpiredAsync("sweep", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SweepExpiredAsync("sweep", new(null!, Write("x").Audit)));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.SweepExpiredAsync("sweep", new(Write("x").Operation, null!)));
    }

    [Fact]
    public async Task Clause_1_the_sweeps_refusal_names_the_actor_it_requires()
    {
        var store = Store(new RecordingStorage());
        var refused = await Assert.ThrowsAsync<ArgumentException>(async () => await store.SweepExpiredAsync("sweep", Write("sweep", Actor)));
        Assert.StartsWith("The staging sweep acts as sys.scheduler.", refused.Message, StringComparison.Ordinal);
    }

    private static readonly ContentReadContext Alice = new("trace-a", "operation-a", "tenant-opaque", "principal.reader-alice");
    private static readonly ContentReadContext Bob = new("trace-b", "operation-b", "tenant-opaque", "principal.reader-bob");
    private static readonly byte[] TwoSegments = [.. Enumerable.Repeat((byte)'a', 65_536), .. "bbb"u8.ToArray()];

    private static async Task<ContentReference> CommitTwoSegments(InMemoryContentStore store) =>
        await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(TwoSegments), "text/plain", null, Write("put-large")), new("record-1", "0"), "a.txt", Write("commit-large"));

    // Another request: its own async flow, so the caller's open execution does not come with it.
    private static Task<T> OnAnotherRequest<T>(Func<Task<T>> work)
    {
        using (ExecutionContext.SuppressFlow()) return Task.Run(work);
    }

    private static async Task ReadAll(InMemoryContentStore store, ContentReference reference, ContentReadContext reader)
    {
        await using var body = (await store.ReadAsync(Tenant, reference, true, reader)).Body;
        await body.CopyToAsync(Stream.Null);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Clause_1_a_blank_reader_is_refused_before_any_byte_is_read_and_never_as_an_integrity_failure(string principal)
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 0);
        var rows = Rows(backing);
        backing.SegmentReads.Clear(); // the corruption hook read one

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.ReadAsync(Tenant, reference, true, new("trace", "operation", "tenant", principal)));

        Assert.Empty(backing.SegmentReads);
        Assert.Empty(store.IntegrityEntries);
        Assert.Equal(rows, Rows(backing));
    }

    [Fact]
    public async Task Clause_1_a_read_or_export_with_no_reader_is_refused_before_any_byte_is_read()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ReadAsync(Tenant, reference, true, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.ExportAsync(Tenant, [reference], true, null!));
        Assert.Empty(backing.SegmentReads);
    }

    [Fact]
    public async Task Clause_1_each_episode_write_has_its_own_audit_row_naming_that_reader_and_revealing_nothing_else()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        var before = backing.Memory.Snapshot().Audits.Length;
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);

        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Bob));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1); // restores the byte
        await ReadAll(store, reference, Bob);
        await ReadAll(store, reference, Alice); // no active episode, so nothing to close and no row

        var rows = backing.Memory.Snapshot().Audits[before..];
        Assert.Equal(["principal.reader-alice", "principal.reader-bob", "principal.reader-bob"], rows.Select(row => row.ActorId));
        Assert.Equal(3, rows.Select(row => row.AuditId).Distinct(StringComparer.Ordinal).Count());
        Assert.Collection(rows,
            row => Assert.StartsWith("content-integrity-failure:", row.AuditId, StringComparison.Ordinal),
            row => Assert.StartsWith("content-integrity-failure:", row.AuditId, StringComparison.Ordinal),
            row => Assert.StartsWith("content-integrity-recovery:", row.AuditId, StringComparison.Ordinal));
        Assert.All(rows, row =>
        {
            Assert.True(row.Payload.IsEmpty);
            Assert.DoesNotContain(reference.ContentId.Value, row.AuditId, StringComparison.Ordinal);
            Assert.DoesNotContain(Tenant.Value, row.AuditId, StringComparison.Ordinal);
        });
        Assert.Equal(
            [ContentStoreOperation.IntegrityFailure, ContentStoreOperation.IntegrityFailure, ContentStoreOperation.IntegrityRecovery],
            backing.Commits.Select(commit => commit.Operation).Where(operation => operation is ContentStoreOperation.IntegrityFailure or ContentStoreOperation.IntegrityRecovery));
        var entry = Assert.Single(store.IntegrityEntries);
        Assert.Equal("principal.reader-bob", entry.PrincipalId);
        Assert.False(entry.Active);
    }

    [Fact]
    public async Task Clause_1_a_damaged_first_segment_fails_inside_the_read_call_before_a_body_exists()
    {
        var store = Store(new RecordingStorage());
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 0);

        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(async () => await store.ReadAsync(Tenant, reference, true, Alice));

        Assert.Equal(0, failure.SegmentIndex);
        Assert.Equal("principal.reader-alice", Assert.Single(store.IntegrityEntries).PrincipalId);
    }

    [Fact]
    public async Task Clause_1_the_bodys_synchronous_reads_refuse_and_leave_it_readable_asynchronously()
    {
        var store = Store(new RecordingStorage());
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        await using var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;

        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1].AsSpan()));
        Assert.Throws<NotSupportedException>(() => body.ReadByte());
        Assert.Throws<NotSupportedException>(() => body.CopyTo(Stream.Null));

        var bytes = new byte[5];
        Assert.Equal(5, await body.ReadAsync(bytes, 0, 5));
        Assert.Equal(Hello, bytes);
    }

    [Fact]
    public async Task Clause_4_a_read_inside_an_open_execution_is_refused_before_any_byte_is_read_and_the_write_still_commits()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var caller = new RecordWrite(backing.Bookkeeping);

        await caller.RunAsync(async _ =>
        {
            var refused = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await store.ReadAsync(Tenant, reference, true, Alice));
            Assert.Equal(KernelTransactionErrors.NestedExecution, refused.Code);
        });

        Assert.Empty(backing.SegmentReads);
        Assert.Equal(1, caller.Commits);
        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, reference, true, Alice))); // the probe left nothing behind
    }

    [Fact]
    public async Task Clause_4_a_body_read_inside_an_execution_writes_no_episode_there_so_a_rollback_has_none_to_erase_and_a_later_read_opens_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        await using (body)
        {
            var caller = new RecordWrite(backing.Bookkeeping);
            await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async _ =>
            {
                var refused = await Assert.ThrowsAsync<KernelTransactionStateException>(() => body.CopyToAsync(Stream.Null));
                Assert.Equal(KernelTransactionErrors.NestedExecution, refused.Code); // the failed episode write surfaces as itself
                Assert.Empty(store.IntegrityEntries);
                throw new InjectedFault();
            }));

            await Assert.ThrowsAsync<ContentIntegrityException>(() => body.CopyToAsync(Stream.Null)); // the body keeps its typed failure
        }

        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Bob));
        Assert.Equal("principal.reader-bob", Assert.Single(store.IntegrityEntries).PrincipalId);
    }

    [Fact]
    public async Task Clause_1_a_read_cancelled_on_a_segment_fetch_resumes_on_that_segment_and_skips_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await using var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        backing.BeforeSegmentRead = index =>
        {
            if (index == 1) throw new OperationCanceledException();
        };
        var bytes = new byte[TwoSegments.Length];

        Assert.Equal(65_536, await body.ReadAsync(bytes.AsMemory())); // what it copied is handed over before the next fetch
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await body.ReadAsync(bytes.AsMemory()));
        backing.BeforeSegmentRead = null;
        var rest = new byte[3];
        await body.ReadExactlyAsync(rest.AsMemory());

        Assert.Equal("bbb"u8.ToArray(), rest);
        Assert.Equal(0, await body.ReadAsync(rest.AsMemory()));
        Assert.Empty(store.IntegrityEntries);
    }

    [Fact]
    public async Task Clause_5_a_sweep_during_an_open_removal_keeps_its_bytes_because_the_rollback_needs_them()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var stage = await Put(store, "put");
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("commit"));
        clock.Advance(Expiry);
        var caller = new RecordWrite(backing.Bookkeeping);

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove", Actor, session)));
            await OnAnotherRequest(async () => await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));
            Assert.NotNull(await backing.Bytes.ReadSegmentAsync(stage, 0));
            throw new InjectedFault();
        }));

        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, reference, true, Alice)));
    }

    [Fact]
    public async Task Clause_3_quota_an_open_removal_would_free_stays_unavailable_until_it_commits()
    {
        var backing = new RecordingStorage();
        var store = Store(backing, record: 5, tenant: 5);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var waiting = await store.PutAsync(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null, Write("put-2"));
        var caller = new RecordWrite(backing.Bookkeeping);

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove", Actor, session)));
            var refused = await OnAnotherRequest(async () => await Assert.ThrowsAsync<ContentRefusedException>(async () =>
                await store.CommitReferenceAsync(Tenant, waiting, new("record-1", "1"), "b.txt", Write("competing"))));
            Assert.Equal(ContentRefusals.RecordQuota, refused.Code);
            throw new InjectedFault();
        }));

        Assert.Equal(1, store.CommittedCount);
        Assert.All(backing.Memory.Snapshot().Quota, pair => Assert.Equal(5, pair.Value)); // never over the limit
    }

    [Fact]
    public async Task Clause_3_a_removal_overlapping_an_open_one_waits_for_it_and_a_rollback_leaves_the_content_whole()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var a = await store.CommitReferenceAsync(Tenant, await Put(store, "put-a"), new("record-a", "0"), "a.txt", Write("commit-a"));
        var b = await store.CommitReferenceAsync(Tenant, await Put(store, "put-b"), new("record-b", "0"), "b.txt", Write("commit-b"));
        Assert.Equal(a.ContentId, b.ContentId);
        var caller = new RecordWrite(backing.Bookkeeping);
        Task<bool>? overlapping = null;

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            Assert.False(await store.RemoveReferenceAsync(Tenant, a, HoldStatus.None, "dispose-a", Write("remove-a", Actor, session)));
            overlapping = OnAnotherRequest(async () => await store.RemoveReferenceAsync(Tenant, b, HoldStatus.None, "dispose-b", Write("remove-b")));
            await Task.Delay(100);
            Assert.False(overlapping.IsCompleted); // the entry's row lock holds it
            throw new InjectedFault();
        }));

        Assert.False(await overlapping!.WaitAsync(TimeSpan.FromSeconds(30))); // a's owner came back, so b was not the last
        Assert.Equal(Hello, await ReadBytes(await store.ReadAsync(Tenant, a, true, Alice)));
        Assert.Empty(store.Reclamations);
    }

    [Fact]
    public async Task Clause_4_a_transactions_writes_are_invisible_to_other_readers_until_it_commits()
    {
        var memory = new InMemoryContentBookkeeping();
        var owner = new ContentOwner("record-1", "0");
        var item = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('d', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536);
        var key = new ContentStorageItemKey(Tenant, item.ContentId);
        await using var open = await memory.BeginTransactionAsync();
        await open.AddStagedAsync(item);
        Assert.NotNull(await open.FindStagedAsync(item.StageId)); // it reads its own write
        Assert.False(await memory.NamesBytesAsync(item.StageId));
        Assert.Equal(0, memory.StagedCount);
        await open.RemoveStagedAsync(item.StageId);
        Assert.Null(await open.TryCreateItemAsync(item with { Owners = [owner] }));
        Assert.NotNull(await open.FindItemAsync(key));
        Assert.Null(await memory.FindItemAsync(key));
        await open.CommitAsync();
        Assert.NotNull(await memory.FindItemAsync(key));
        Assert.True(await memory.NamesBytesAsync(item.StageId));
    }

    [Fact]
    public async Task Clause_1_an_episode_a_read_racing_erasure_would_open_is_not_written_and_writes_no_audit_row()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        await using (body)
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("erase")));
            var rows = Rows(backing);
            await Assert.ThrowsAsync<ContentIntegrityException>(() => body.CopyToAsync(Stream.Null));
            Assert.Equal(rows, Rows(backing));
        }

        Assert.Empty(store.IntegrityEntries);
    }

    [Theory]
    [InlineData("add-owner")]
    [InlineData("remove-owner")]
    [InlineData("delete-unowned")]
    public async Task Clause_3_each_write_to_an_entry_waits_for_the_transaction_holding_its_row(string write)
    {
        var memory = new InMemoryContentBookkeeping();
        var owner = new ContentOwner("record-1", "0");
        var item = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('e', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536) { Owners = [owner] };
        var key = new ContentStorageItemKey(Tenant, item.ContentId);
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.TryCreateItemAsync(item);
            await seed.CommitAsync();
        }

        var holder = await memory.BeginTransactionAsync();
        Assert.True(await holder.RemoveOwnerAsync(key, owner));
        var other = await memory.BeginTransactionAsync();
        var waiting = Task.Run(async () => write switch
        {
            "add-owner" => await other.AddOwnerAsync(key, new("record-2", "0")),
            "remove-owner" => await other.RemoveOwnerAsync(key, owner),
            _ => await other.DeleteItemIfUnownedAsync(key),
        });
        await Task.Delay(100);
        Assert.False(waiting.IsCompleted);
        await holder.RollbackAsync();

        Assert.Equal(write != "delete-unowned", await waiting.WaitAsync(TimeSpan.FromSeconds(30))); // the owner came back, so it is not unowned
        await other.DisposeAsync();
    }

    [Fact]
    public async Task Clause_3_one_caller_can_replace_a_reference_within_a_full_quota_because_its_own_release_counts_for_itself()
    {
        var backing = new RecordingStorage();
        var store = Store(backing, record: 5, tenant: 5);
        var old = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        var replacement = await store.PutAsync(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null, Write("put-2"));
        var caller = new RecordWrite(backing.Bookkeeping);

        await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, old, HoldStatus.None, "replace", Write("remove", Actor, session)));
            await store.CommitReferenceAsync(Tenant, replacement, new("record-1", "0"), "b.txt", Write("replace", Actor, session));
        });

        Assert.Equal(1, store.CommittedCount);
        Assert.All(backing.Memory.Snapshot().Quota, pair => Assert.Equal(5, pair.Value));
    }

    [Fact]
    public async Task Clause_4_an_integrity_entry_is_read_by_its_own_transaction_and_by_no_one_else_until_commit()
    {
        var memory = new InMemoryContentBookkeeping();
        var key = Guid.NewGuid();
        var first = new ContentIntegrityEntry(key, Tenant, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 0, true, "t", "o", "p", new("r", "e"));
        await using var open = await memory.BeginTransactionAsync();
        Assert.Null(await open.FindIntegrityAsync(Tenant, key));
        await open.SetIntegrityAsync(first);
        Assert.Same(first, await open.FindIntegrityAsync(Tenant, key));
        Assert.Null(await memory.FindIntegrityAsync(Tenant, key));
        await open.CommitAsync();

        await using var later = await memory.BeginTransactionAsync();
        Assert.Same(first, await later.FindIntegrityAsync(Tenant, key));
    }

    [Fact]
    public async Task Clause_1_a_missing_segment_fails_as_integrity_with_its_stated_cause_and_clears_the_data_key()
    {
        var store = Store(new InMemoryContentStoreStorage());
        var reference = await CommitTwoSegments(store);
        await store.DropLastSegmentKeepingLengthAsync(Tenant, reference.ContentId);
        await using var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        var dataKey = Assert.Single(body.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Select(field => field.GetValue(body)).OfType<byte[]>(), bytes => bytes.Length == 32);
        Assert.Contains(dataKey, value => value != 0);

        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(() => body.CopyToAsync(Stream.Null));

        Assert.Equal("A ciphertext segment is missing.", failure.InnerException!.Message);
        Assert.All(dataKey, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task Q34_an_episode_during_an_open_erasure_waits_and_a_committed_erasure_leaves_no_episode_and_a_healthy_store()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        var caller = new RecordWrite(backing.Bookkeeping);
        Task<int>? failing = null;

        await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "erase", Write("erase", Actor, session)));
            failing = OnAnotherRequest(async () =>
            {
                await body.CopyToAsync(Stream.Null);
                return 0;
            });
            await Task.Delay(100);
            Assert.False(failing.IsCompleted); // the erasure holds the item's row
        });

        await Assert.ThrowsAsync<ContentIntegrityException>(() => failing!.WaitAsync(TimeSpan.FromSeconds(30)));
        await body.DisposeAsync();
        Assert.Empty(store.IntegrityEntries);
        Assert.True(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Q34_an_episode_during_an_erasure_that_rolls_back_is_written_once_the_row_is_free()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        var caller = new RecordWrite(backing.Bookkeeping);
        Task<int>? failing = null;

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "erase", Write("erase", Actor, session)));
            failing = OnAnotherRequest(async () =>
            {
                await body.CopyToAsync(Stream.Null);
                return 0;
            });
            await Task.Delay(100);
            throw new InjectedFault();
        }));

        await Assert.ThrowsAsync<ContentIntegrityException>(() => failing!.WaitAsync(TimeSpan.FromSeconds(30)));
        await body.DisposeAsync();
        Assert.Equal("principal.reader-alice", Assert.Single(store.IntegrityEntries).PrincipalId);
        Assert.False(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Q34_an_erasure_after_a_committed_episode_closes_it()
    {
        var store = Store(new RecordingStorage());
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        Assert.False(store.IsIntegrityHealthy);

        Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "erase", Write("erase")));
        Assert.True(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Clause_1_a_joined_commit_of_an_expired_stage_dooms_the_callers_execution_even_when_caught()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var expired = await Put(store, "expired");
        clock.Advance(Expiry);
        var fresh = await store.PutAsync(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null, Write("fresh"));
        var caller = new RecordWrite(backing.Bookkeeping);

        var doomed = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await caller.RunAsync(async session =>
        {
            await store.CommitReferenceAsync(Tenant, fresh, new("record-1", "0"), "a.txt", Write("fits", Actor, session));
            var refused = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, expired, new("record-1", "1"), "b.txt", Write("expired-commit", Actor, session)));
            Assert.Equal(ContentRefusals.StagingExpired, refused.Code);
        }));

        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, doomed.Code);
        Assert.Equal(0, caller.Commits);
        Assert.Equal(0, store.CommittedCount);
        Assert.Equal(2, store.StagedCount);
        Assert.Empty(store.Reclamations);
        Assert.Equal(1, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler"))); // the sweep takes the expired stage
    }

    [Fact]
    public async Task Clause_5_a_removal_that_waited_through_an_erase_and_recreate_destroys_the_new_items_bytes_not_the_old_ones()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var oldStage = await Put(store, "put");
        var reference = await store.CommitReferenceAsync(Tenant, oldStage, new("record-1", "0"), "a.txt", Write("commit"));
        var key = new ContentStorageItemKey(Tenant, reference.ContentId);
        var incarnation = await store.StorageItemAsync(Tenant, reference.ContentId);
        var newStage = Guid.NewGuid();
        var writer = await backing.Bytes.OpenWriteAsync(newStage, DateTimeOffset.UnixEpoch);
        await writer.AppendAsync(new([1], [2]));
        await writer.CompleteAsync(DateTimeOffset.UnixEpoch);

        var holder = await backing.Bookkeeping.BeginTransactionAsync();
        Assert.True(await holder.RemoveOwnerAsync(key, reference.Owner));
        Assert.True(await holder.DeleteItemIfUnownedAsync(key));
        Assert.Null(await holder.TryCreateItemAsync(incarnation with { StageId = newStage }));
        var removal = OnAnotherRequest(async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose", Write("remove")));
        await Task.Delay(100);
        Assert.False(removal.IsCompleted);
        await holder.CommitAsync();

        Assert.True(await removal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Null(await backing.Bytes.ReadSegmentAsync(newStage, 0));
        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(oldStage, 0));
    }

    public static TheoryData<string> FailuresBeforeTheBoundary => ["too-large", "media-type", "digest", "blank-evidence", "null-stream"];

    [Theory]
    [MemberData(nameof(FailuresBeforeTheBoundary))]
    public async Task Clause_1_a_joined_put_refused_before_it_reaches_the_boundary_still_dooms_the_callers_execution(string failure)
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(new(10, 1000, 1000, Expiry), new Keys(), new Clock(), backing);
        var stage = await Put(store, "put");
        var caller = new RecordWrite(backing.Bookkeeping);

        var doomed = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await caller.RunAsync(async session =>
        {
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("fits", Actor, session));
            var write = Write("refused", Actor, session);
            Exception caught = failure switch
            {
                "too-large" => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(new byte[11]), "text/plain", null, write)),
                "media-type" => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "application/pdf", null, write)),
                "digest" => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", new string('0', 64), write)),
                "blank-evidence" => await Assert.ThrowsAsync<ArgumentException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null,
                    new(new(" ", "key", "fingerprint"), write.Audit, session))),
                _ => await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.PutAsync(Tenant, null!, "text/plain", null, write)),
            };
            Assert.IsNotType<KernelTransactionStateException>(caught); // the caller sees the real failure
        }));

        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, doomed.Code);
        Assert.Equal(0, caller.Commits);
        Assert.Equal(0, store.CommittedCount);
        Assert.Equal(1, store.StagedCount);
        Assert.DoesNotContain(backing.Memory.Snapshot().Audits, audit => audit.ActorId == "content-store:never-staged");
    }

    [Fact]
    public async Task Clause_1_a_refused_write_with_a_transaction_but_no_open_execution_surfaces_its_own_failure()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(new(10, 1000, 1000, Expiry), new Keys(), new Clock(), backing);
        await using var orphan = await backing.Bookkeeping.BeginTransactionAsync();
        var refused = await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(new byte[11]), "text/plain", null, Write("x", Actor, orphan)));
        Assert.Equal(ContentRefusals.TooLarge, refused.Code);
    }

    [Fact]
    public async Task Clause_1_joined_removal_and_sweep_failures_before_the_boundary_doom_the_callers_execution_too()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));

        var removal = new RecordWrite(backing.Bookkeeping);
        await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await removal.RunAsync(async session =>
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, " ", Write("blank-run", Actor, session)))));

        var sweep = new RecordWrite(backing.Bookkeeping);
        await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await sweep.RunAsync(async session =>
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.SweepExpiredAsync("sweep", Write("wrong-actor", Actor, session)))));

        var commit = new RecordWrite(backing.Bookkeeping);
        await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await commit.RunAsync(async session =>
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.CommitReferenceAsync(Tenant, Guid.NewGuid(), new("record-1", "1"), " ", Write("blank-name", Actor, session)))));

        Assert.Equal(0, removal.Commits + sweep.Commits + commit.Commits);
    }

    [Fact]
    public async Task Q34_a_recovery_that_lost_the_race_to_a_newer_failure_leaves_the_episode_open_with_its_count()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1); // restores the byte
        backing.BeforeFindItemForUpdate = () => OnAnotherRequest(async () =>
        {
            await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
            await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Bob));
            await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
            return 0;
        });

        await ReadAll(store, reference, Alice);

        var entry = Assert.Single(store.IntegrityEntries);
        Assert.True(entry.Active);
        Assert.Equal(2, entry.Count);
        Assert.Equal("principal.reader-bob", entry.PrincipalId);
        await ReadAll(store, reference, Alice); // a later verified read still closes it
        Assert.True(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Q34_a_verified_read_that_began_before_an_episode_opened_cannot_close_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await using var early = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;

        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Bob));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1); // restored, so the early read verifies
        await early.CopyToAsync(Stream.Null);

        Assert.False(store.IsIntegrityHealthy);
        Assert.Equal("principal.reader-bob", Assert.Single(store.IntegrityEntries).PrincipalId);
        await ReadAll(store, reference, Alice); // a read that began during the episode does close it
        Assert.True(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Q34_a_verified_read_cannot_close_a_second_episode_opened_after_the_one_it_saw_ended()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await using var early = (await store.ReadAsync(Tenant, reference, true, Alice)).Body; // sees the first episode
        await ReadAll(store, reference, Bob); // closes it

        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Bob)); // opens a second, also with count 1
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await early.CopyToAsync(Stream.Null);

        Assert.False(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Clause_1_each_episode_write_has_its_own_stated_operation_identity()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        backing.StagedOperations.Clear();
        await ReadAll(store, reference, Alice);

        var operation = Assert.Single(backing.StagedOperations);
        Assert.StartsWith("content-integrity-recovery:", operation.CommandId, StringComparison.Ordinal);
        Assert.Equal(operation.IdempotencyKey, operation.Fingerprint);
        Assert.DoesNotContain("-", operation.IdempotencyKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Q34_a_finished_body_read_again_opens_no_second_recovery()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await CommitTwoSegments(store);
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await Assert.ThrowsAsync<ContentIntegrityException>(() => ReadAll(store, reference, Alice));
        await store.CorruptCiphertextAsync(Tenant, reference.ContentId, 1);
        await using var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        await body.CopyToAsync(Stream.Null);
        var begun = backing.TransactionsBegun;

        Assert.Equal(0, await body.ReadAsync(new byte[1].AsMemory()));
        Assert.Equal(begun, backing.TransactionsBegun);
    }

    [Fact]
    public async Task Clause_1_a_null_write_is_refused_on_each_store_write()
    {
        var store = Store(new RecordingStorage());
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a.txt", Write("commit"));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null, (ContentStoreWrite)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.CommitReferenceAsync(Tenant, Guid.NewGuid(), new("r", "e"), "a.txt", (ContentStoreWrite)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "run", (ContentStoreWrite)null!));
    }

    [Fact]
    public async Task Eng7_a_display_name_ending_in_part_of_a_percent_escape_stays_a_plain_filename()
    {
        var store = Store(new RecordingStorage());
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, "put"), new("record-1", "0"), "a%4", Write("commit"));
        await using var body = (await store.ReadAsync(Tenant, reference, true, Alice)).Body;
        Assert.Equal("attachment; filename=\"a%4\"", (await store.ReadAsync(Tenant, reference, true, Alice)).ContentDisposition);
    }

    [Fact]
    public async Task Clause_1_a_sweep_that_only_deletes_orphan_bytes_writes_its_audit_row_and_deletes_them_after_the_commit()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var orphan = Guid.NewGuid();
        var writer = await backing.Bytes.OpenWriteAsync(orphan, clock.GetUtcNow());
        await writer.AppendAsync(new([1], [2]));
        await writer.CompleteAsync(clock.GetUtcNow());
        clock.Advance(Expiry);

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.SweepExpiredAsync("sweep-failed", Write("sweep-failed", "sys.scheduler")));
        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(orphan, 0)); // nothing deleted without the commit
        Assert.Empty(Rows(backing));

        backing.Events.Clear();
        Assert.Equal(0, await store.SweepExpiredAsync("sweep", Write("sweep", "sys.scheduler")));
        Assert.Equal(["unit-commit", "bytes-delete"], backing.Events);
        Assert.Equal([("audit-sweep", "sys.scheduler")], Rows(backing));
        Assert.Null(await backing.Bytes.ReadSegmentAsync(orphan, 0));

        Assert.Equal(0, await store.SweepExpiredAsync("sweep-empty", Write("sweep-empty", "sys.scheduler"))); // nothing left, so no row
        Assert.Single(Rows(backing));
    }

    [Fact]
    public async Task Clause_1_a_sweep_refuses_a_wrong_actor_before_deleting_orphan_bytes_and_inside_an_open_execution()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var orphan = Guid.NewGuid();
        var writer = await backing.Bytes.OpenWriteAsync(orphan, clock.GetUtcNow());
        await writer.AppendAsync(new([1], [2]));
        await writer.CompleteAsync(clock.GetUtcNow());
        clock.Advance(Expiry);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.SweepExpiredAsync("sweep", Write("wrong", Actor)));
        var caller = new RecordWrite(backing.Bookkeeping);
        await caller.RunAsync(async _ =>
        {
            var nested = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await store.SweepExpiredAsync("sweep", Write("nested", "sys.scheduler")));
            Assert.Equal(KernelTransactionErrors.NestedExecution, nested.Code);
        });
        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(orphan, 0));
    }

    [Fact]
    public async Task Clause_5_a_joined_sweep_leaves_orphan_bytes_for_a_sweep_whose_commit_it_can_see()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var orphan = Guid.NewGuid();
        var writer = await backing.Bytes.OpenWriteAsync(orphan, clock.GetUtcNow());
        await writer.AppendAsync(new([1], [2]));
        await writer.CompleteAsync(clock.GetUtcNow());
        clock.Advance(Expiry);
        var caller = new RecordWrite(backing.Bookkeeping);

        await caller.RunAsync(async session => await store.SweepExpiredAsync("sweep", Write("joined", "sys.scheduler", session)));

        Assert.NotNull(await backing.Bytes.ReadSegmentAsync(orphan, 0));
        Assert.Equal([("audit-joined", "sys.scheduler")], Rows(backing));
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
