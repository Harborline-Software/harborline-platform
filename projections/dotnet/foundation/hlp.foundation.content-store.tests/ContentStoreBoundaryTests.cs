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
    private static readonly byte[] TwoSegments = Enumerable.Repeat((byte)'a', 65_536 + 3).ToArray();

    private static async Task<ContentReference> CommitTwoSegments(InMemoryContentStore store) =>
        await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(TwoSegments), "text/plain", null, Write("put-large")), new("record-1", "0"), "a.txt", Write("commit-large"));

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

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await body.ReadAsync(bytes.AsMemory()));
        backing.BeforeSegmentRead = null;
        await body.ReadExactlyAsync(bytes.AsMemory(0, 3));

        Assert.Equal("aaa"u8.ToArray(), bytes[..3]);
        Assert.Empty(store.IntegrityEntries);
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
