using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// T-521/content-store-host-transaction clauses 2, 3 and 5. Expected values are literals from the slice record and DES-0054,
// or stated properties: exactly one of two commits survives, the order of what reaches the ports, and what remains readable.
public sealed class ContentStorePortTests
{
    private static readonly TenantId Tenant = new("port-tenant");
    private static readonly byte[] Hello = "hello"u8.ToArray();
    private static readonly byte[] World = "world"u8.ToArray();
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(1);

    private static InMemoryContentStore Store(IContentStoreStorage backing, TimeProvider? clock = null, long record = 1000, long tenant = 1000) =>
        new(new(100_000, record, tenant, Expiry), new Keys(), clock ?? new Clock(), backing);

    private static Task<Guid> Put(InMemoryContentStore store, byte[] bytes) => store.PutAsync(Tenant, new MemoryStream(bytes), "text/plain", null).AsTask();

    private static byte[] ReadBytes(ContentDelivery delivery)
    {
        using var body = delivery.Body;
        using var buffer = new MemoryStream();
        body.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void Clause_2_no_type_on_the_bookkeeping_port_can_carry_a_ciphertext_segment()
    {
        var seen = new HashSet<Type>();
        void Walk(Type type)
        {
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) Walk(argument);
            if (type.Assembly != typeof(IContentBookkeeping).Assembly || !seen.Add(type)) return;
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                switch (member)
                {
                    case PropertyInfo property: Walk(property.PropertyType); break;
                    case MethodInfo method:
                        Walk(method.ReturnType);
                        foreach (var parameter in method.GetParameters()) Walk(parameter.ParameterType);
                        break;
                }
            }
        }

        Walk(typeof(IContentBookkeeping));
        Walk(typeof(IContentBookkeepingTransaction));
        Assert.Contains(typeof(ContentStoreItem), seen); // the walk really reached the entry
        Assert.DoesNotContain(typeof(ContentCiphertextSegment), seen);
        Assert.DoesNotContain(typeof(IContentByteStore), seen);
    }

    [Fact]
    public async Task Clause_2_a_put_writes_ciphertext_to_the_byte_port_and_only_metadata_to_the_bookkeeping_port()
    {
        var backing = new RecordingStorage();
        var stage = await Put(Store(backing), Hello);
        var segment = Assert.IsType<ContentCiphertextSegment>(backing.MemoryBytes.ReadSegment(stage, 0));
        Assert.Equal(5, segment.Ciphertext.Length);
        Assert.NotEqual(Hello, segment.Ciphertext);
        Assert.Null(backing.MemoryBytes.ReadSegment(stage, 1));
        Assert.Equal(stage, Assert.Single(backing.Memory.Snapshot().Staged).StageId);
    }

    [Fact]
    public async Task Clause_3_two_concurrent_commits_that_each_fit_alone_leave_exactly_one_at_record_grain()
    {
        for (var round = 0; round < 25; round++)
        {
            var (failure, store) = await RaceDifferentBytes(record: 5, tenant: 1000, "record-1", "record-1");
            Assert.Equal(ContentRefusals.RecordQuota, failure.Code);
            Assert.Equal(5, failure.Detail["limit"]);
            Assert.Equal(10, failure.Detail["observed"]);
            Assert.Equal(1, store.CommittedCount);
        }
    }

    [Fact]
    public async Task Clause_3_two_concurrent_commits_that_each_fit_alone_leave_exactly_one_at_tenant_grain()
    {
        for (var round = 0; round < 25; round++)
        {
            var (failure, store) = await RaceDifferentBytes(record: 1000, tenant: 5, "record-1", "record-2");
            Assert.Equal(ContentRefusals.TenantQuota, failure.Code);
            Assert.Equal(5, failure.Detail["limit"]);
            Assert.Equal(10, failure.Detail["observed"]);
            Assert.Equal(1, store.CommittedCount);
        }
    }

    // The barrier holds both commits after their read and before either's conditional update. A store that still took a
    // process lock around the operation could never bring both to the barrier.
    private static async Task<(ContentRefusedException Failure, InMemoryContentStore Store)> RaceDifferentBytes(long record, long tenant, string firstRecord, string secondRecord)
    {
        var backing = new RecordingStorage();
        var built = Store(backing, record: record, tenant: tenant);
        var first = await Put(built, Hello);
        var second = await Put(built, World);
        using var barrier = new Barrier(2);
        backing.BeforeFindStaged = () => Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        var outcomes = await Race(async () => await built.CommitReferenceAsync(Tenant, first, new(firstRecord, "0"), "a.txt"), async () => await built.CommitReferenceAsync(Tenant, second, new(secondRecord, "0"), "b.txt"));
        Assert.Single(outcomes, outcome => outcome.Failure is null);
        return (Assert.IsType<ContentRefusedException>(Assert.Single(outcomes, outcome => outcome.Failure is not null).Failure), built);
    }

    [Fact]
    public async Task Clause_3_the_same_bytes_committed_concurrently_converge_on_one_item_with_both_references()
    {
        for (var round = 0; round < 25; round++)
        {
            var backing = new RecordingStorage();
            var store = Store(backing);
            var first = await Put(store, Hello);
            var second = await Put(store, Hello);
            using var barrier = new Barrier(2);
            backing.BeforeTryCreate = () => Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
            var outcomes = await Race(async () => await store.CommitReferenceAsync(Tenant, first, new("record-1", "0"), "a.txt"), async () => await store.CommitReferenceAsync(Tenant, second, new("record-2", "0"), "b.txt"));

            Assert.All(outcomes, outcome => Assert.Null(outcome.Failure));
            Assert.Equal(1, store.CommittedCount);
            Assert.Equal(0, store.StagedCount);
            var item = Assert.Single(backing.Memory.Snapshot().Items);
            Assert.Equal(["record-1", "record-2"], item.Owners.Select(owner => owner.Record).Order(StringComparer.Ordinal));
            Assert.Equal(item.StageId, Assert.Single(await backing.MemoryBytes.WrittenAtOrBeforeAsync(DateTimeOffset.MaxValue)));
            Assert.All(outcomes, outcome => Assert.Equal(Hello, ReadBytes(store.Read(Tenant, outcome.Reference!, true))));
        }
    }

    [Fact]
    public async Task Clause_3_one_stage_committed_concurrently_by_two_owners_commits_once_and_counts_once()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await Put(store, Hello);
        using var barrier = new Barrier(2);
        backing.BeforeFindStaged = () => Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        var outcomes = await Race(async () => await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt"), async () => await store.CommitReferenceAsync(Tenant, stage, new("record-2", "0"), "b.txt"));
        var refused = Assert.IsType<ContentRefusedException>(Assert.Single(outcomes, outcome => outcome.Failure is not null).Failure);
        Assert.Equal(ContentRefusals.StagingExpired, refused.Code);
        var counts = backing.Memory.Snapshot().Quota;
        Assert.Equal(5, Assert.Single(counts, pair => pair.Key.Record is null).Value);
        Assert.Equal(2, counts.Length); // the refused owner's record reservation rolled back
    }

    [Fact]
    public async Task Clause_3_the_backing_lets_a_total_equal_to_the_limit_through_and_refuses_one_byte_more_changing_neither_count()
    {
        var memory = new InMemoryContentBookkeeping();
        await using var unit = await memory.BeginTransactionAsync();
        Assert.Null((await unit.TryReserveQuotaAsync(Tenant, "record-1", 10, 10, 15)).Refused);
        Assert.Equal(new ContentQuotaReservation(ContentQuotaGrain.Record, 11), await unit.TryReserveQuotaAsync(Tenant, "record-1", 1, 10, 100));
        Assert.Equal(new ContentQuotaReservation(ContentQuotaGrain.Tenant, 16), await unit.TryReserveQuotaAsync(Tenant, "record-2", 6, 100, 15));
        Assert.Null((await unit.TryReserveQuotaAsync(Tenant, "record-2", 5, 100, 15)).Refused);
        Assert.Equal(new ContentQuotaReservation(ContentQuotaGrain.Record, 16), await unit.TryReserveQuotaAsync(Tenant, "record-1", 6, 10, 15)); // record is checked first
        var counts = memory.Snapshot().Quota.ToDictionary(pair => pair.Key.Record ?? "tenant", pair => pair.Value);
        Assert.Equal(10, counts["record-1"]);
        Assert.Equal(5, counts["record-2"]);
        Assert.Equal(15, counts["tenant"]);
    }

    [Fact]
    public async Task Clause_3_removing_a_reference_gives_its_bytes_back_to_both_counts()
    {
        var store = Store(new InMemoryContentStoreStorage(), record: 5, tenant: 5);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        var waiting = await Put(store, World);
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, waiting, new("record-1", "1"), "b.txt"));
        Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose"));
        Assert.Equal(World, ReadBytes(store.Read(Tenant, await store.CommitReferenceAsync(Tenant, waiting, new("record-1", "1"), "b.txt"), true)));
    }

    [Fact]
    public async Task Clause_3_a_create_waits_for_a_concurrent_creator_of_the_same_key_and_sees_its_outcome()
    {
        var memory = new InMemoryContentBookkeeping();
        var item = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('a', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536);
        var rollingBack = await memory.BeginTransactionAsync();
        Assert.Null(await rollingBack.TryCreateItemAsync(item));
        var waiter = Task.Run(async () =>
        {
            await using var unit = await memory.BeginTransactionAsync();
            var held = await unit.TryCreateItemAsync(item with { StageId = Guid.NewGuid() });
            await unit.CommitAsync();
            return held;
        });
        await Task.Delay(100);
        Assert.False(waiter.IsCompleted); // a unique index blocks the second writer
        await rollingBack.DisposeAsync();
        Assert.Null(await waiter.WaitAsync(TimeSpan.FromSeconds(30))); // the first rolled back, so the second creates
        Assert.NotEqual(item.StageId, Assert.Single(memory.Snapshot().Items).StageId);
    }

    [Fact]
    public async Task Clause_5_bytes_are_durable_before_the_staged_entry_and_the_unit_commits_after_it()
    {
        var backing = new RecordingStorage();
        await Put(Store(backing), Hello);
        Assert.Equal(["bytes-durable", "staged-entry", "unit-commit"], backing.Events);
    }

    [Fact]
    public async Task Clause_5_bytes_are_deleted_only_after_the_unit_that_removed_their_last_entry_commits()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);

        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        backing.Events.Clear();
        Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose"));
        Assert.Equal(["unit-commit", "bytes-delete"], backing.Events);

        var kept = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        var duplicate = await Put(store, Hello);
        backing.Events.Clear();
        await store.CommitReferenceAsync(Tenant, duplicate, new("record-2", "0"), "b.txt");
        Assert.Equal(["unit-commit", "bytes-delete"], backing.Events);
        Assert.Null(backing.MemoryBytes.ReadSegment(duplicate, 0));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, kept, true)));

        var expiredAtCommit = await Put(store, World);
        clock.Advance(Expiry);
        backing.Events.Clear();
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, expiredAtCommit, new("record-3", "0"), "c.txt"));
        Assert.Equal(["unit-commit", "bytes-delete"], backing.Events);

        var abandoned = await Put(store, World);
        clock.Advance(Expiry);
        backing.Events.Clear();
        Assert.Equal(1, await store.SweepExpiredAsync("sweep"));
        Assert.Equal(["unit-commit", "bytes-delete"], backing.Events);
        Assert.Null(backing.MemoryBytes.ReadSegment(abandoned, 0));
    }

    [Fact]
    public async Task Clause_5_a_failed_commit_deletes_no_bytes()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var first = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        var duplicate = await Put(store, Hello);

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.CommitReferenceAsync(Tenant, duplicate, new("record-2", "0"), "b.txt"));
        Assert.NotNull(backing.MemoryBytes.ReadSegment(duplicate, 0));

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.RemoveReferenceAsync(Tenant, first, HoldStatus.None, "dispose"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, first, true)));
    }

    [Fact]
    public async Task Clause_5_a_crash_between_the_byte_write_and_the_staged_entry_leaves_bytes_the_sweep_reclaims_only_after_the_grace_period()
    {
        var clock = new Clock();
        var backing = new RecordingStorage { ThrowOnBegin = new InvalidOperationException("process lost") };
        var store = Store(backing, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Put(store, Hello));
        Assert.Equal(["bytes-durable"], backing.Events);
        backing.ThrowOnBegin = null;

        var restarted = Store(backing, clock);
        Assert.Equal(0, restarted.StagedCount);
        var orphan = Assert.Single(await backing.MemoryBytes.WrittenAtOrBeforeAsync(DateTimeOffset.MaxValue));

        clock.Advance(Expiry - TimeSpan.FromTicks(1));
        Assert.Equal(0, await restarted.SweepExpiredAsync("sweep-early"));
        Assert.NotNull(backing.MemoryBytes.ReadSegment(orphan, 0));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(0, await restarted.SweepExpiredAsync("sweep-late"));
        Assert.Null(backing.MemoryBytes.ReadSegment(orphan, 0));
        Assert.Empty(restarted.Reclamations); // no item ever existed, so there is no reclamation to record
    }

    [Fact]
    public async Task Clause_5_the_sweep_never_deletes_bytes_a_committed_entry_names_however_old()
    {
        var clock = new Clock();
        var store = Store(new RecordingStorage(), clock);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        clock.Advance(Expiry * 10);
        Assert.Equal(0, await store.SweepExpiredAsync("sweep"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, reference, true)));
    }

    [Fact]
    public async Task Clause_5_the_grace_period_counts_from_when_the_bytes_became_durable_not_from_when_the_upload_began()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var slow = new SlowStream(Hello, clock, Expiry / 2);
        backing.ThrowOnBegin = new InvalidOperationException("process lost");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.PutAsync(Tenant, slow, "text/plain", null));
        backing.ThrowOnBegin = null;
        var orphan = Assert.Single(await backing.MemoryBytes.WrittenAtOrBeforeAsync(DateTimeOffset.MaxValue));

        clock.Advance((Expiry / 2) + TimeSpan.FromTicks(1)); // a minute after the upload began, under a minute after durable
        await store.SweepExpiredAsync("sweep");
        Assert.NotNull(backing.MemoryBytes.ReadSegment(orphan, 0));
    }

    [Fact]
    public async Task Clause_5_a_write_that_crashed_before_it_completed_is_still_swept_and_a_swept_write_cannot_complete()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var key = Guid.NewGuid();
        var abandoned = await backing.Bytes.OpenWriteAsync(key, clock.GetUtcNow()); // never disposed, as after a crash
        await abandoned.AppendAsync(new([1], [2]));

        clock.Advance(Expiry);
        await store.SweepExpiredAsync("sweep");
        Assert.Null(backing.MemoryBytes.ReadSegment(key, 0));
        await Assert.ThrowsAsync<IOException>(async () => await abandoned.CompleteAsync(clock.GetUtcNow()));
    }

    [Fact]
    public async Task Clause_5_a_failed_byte_delete_does_not_fail_the_committed_removal_and_the_sweep_finishes_it()
    {
        var clock = new Clock();
        var backing = new RecordingStorage();
        var store = Store(backing, clock);
        var stage = await Put(store, Hello);
        var reference = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt");

        backing.ThrowOnDelete = new IOException("the file is open elsewhere");
        Assert.True(await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose"));
        Assert.Throws<ContentNotFoundException>(() => store.Read(Tenant, reference, true));
        Assert.NotNull(backing.MemoryBytes.ReadSegment(stage, 0));
        clock.Advance(Expiry);
        Assert.Equal(0, await store.SweepExpiredAsync("sweep-while-still-failing"));
        Assert.NotNull(backing.MemoryBytes.ReadSegment(stage, 0));

        backing.ThrowOnDelete = null;
        await store.SweepExpiredAsync("sweep");
        Assert.Null(backing.MemoryBytes.ReadSegment(stage, 0));
    }

    [Fact]
    public async Task Clause_5_any_other_failure_from_a_byte_delete_is_not_mistaken_for_a_deferred_delete()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await store.CommitReferenceAsync(Tenant, await Put(store, Hello), new("record-1", "0"), "a.txt");
        backing.ThrowOnDelete = new InvalidOperationException("a defect in the backing");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose"));
    }

    [Fact]
    public async Task Clause_5_a_refused_put_leaves_no_bytes_behind()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(new(10, 100, 100, Expiry), new Keys(), new Clock(), backing);
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(new byte[11]), "text/plain", null));
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", new string('0', 64)));
        await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "application/pdf", null));
        Assert.Empty(await backing.MemoryBytes.WrittenAtOrBeforeAsync(DateTimeOffset.MaxValue));
        Assert.DoesNotContain("bytes-durable", backing.Events);
    }

    [Fact]
    public async Task Clause_5_an_expiry_longer_than_the_calendar_expires_nothing_instead_of_throwing()
    {
        var backing = new InMemoryContentStoreStorage();
        var store = new InMemoryContentStore(new(100, 100, 100, TimeSpan.MaxValue), new Keys(), new Clock(), backing);
        var stage = await Put(store, Hello);
        Assert.Equal(0, await store.SweepExpiredAsync("sweep"));
        Assert.NotNull(backing.Bytes.ReadSegment(stage, 0));
    }

    [Fact]
    public async Task Clause_6_the_in_memory_backing_undoes_every_kind_of_write_when_a_unit_is_not_committed()
    {
        var memory = new InMemoryContentBookkeeping();
        var owner = new ContentOwner("record-1", "0");
        var staged = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('b', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536);
        var key = new ContentStorageItemKey(Tenant, staged.ContentId);
        var episode = new ContentIntegrityEntry(staged.StageId, Tenant, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 0, true, "t", "o", "p", owner);
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.AddStagedAsync(staged);
            await seed.CommitAsync();
        }

        await using (var unit = await memory.BeginTransactionAsync())
        {
            Assert.True(await unit.RemoveStagedAsync(staged.StageId));
            Assert.False(await unit.RemoveStagedAsync(staged.StageId));
            Assert.Null((await unit.TryReserveQuotaAsync(Tenant, owner.Record, 5, 10, 10)).Refused);
            Assert.Null(await unit.TryCreateItemAsync(staged with { Owners = [owner] }));
            Assert.True(await unit.AddOwnerAsync(key, new("record-2", "0")));
            await unit.AppendReclamationAsync(new(staged.ContentId, Tenant, "cause", "run"));
            await unit.SetIntegrityAsync(episode);
        }

        var (items, stagedAfter, quota, audits) = memory.Snapshot();
        Assert.Empty(audits);
        Assert.Empty(items);
        Assert.Equal(staged.StageId, Assert.Single(stagedAfter).StageId);
        Assert.Empty(quota);
        Assert.Empty(memory.Reclamations);
        Assert.Empty(memory.IntegrityEntries);
        Assert.False(await memory.NamesBytesAsync(Guid.NewGuid()));
        Assert.True(await memory.NamesBytesAsync(staged.StageId));

        await using (var commit = await memory.BeginTransactionAsync())
        {
            Assert.Null(await commit.TryCreateItemAsync(staged with { Owners = [owner] }));
            await commit.SetIntegrityAsync(episode);
            await commit.CommitAsync();
        }

        await using (var removal = await memory.BeginTransactionAsync())
        {
            Assert.False(await removal.DeleteItemIfUnownedAsync(key)); // still owned
            Assert.False(await removal.RemoveOwnerAsync(key, new("record-9", "0")));
            Assert.True(await removal.RemoveOwnerAsync(key, owner));
            Assert.True(await removal.DeleteItemIfUnownedAsync(key));
            Assert.False(await removal.AddOwnerAsync(key, owner));
            Assert.False(await removal.RemoveOwnerAsync(key, owner));
            await removal.SetIntegrityAsync(episode with { Active = false });
        }

        Assert.Equal([owner], memory.FindItem(key)!.Owners);
        Assert.True(memory.FindIntegrity(Tenant, staged.StageId)!.Active);
    }

    private static async Task<(ContentReference? Reference, Exception? Failure)[]> Race(params Func<Task<ContentReference>>[] commits) =>
        await Task.WhenAll(commits.Select(commit => Task.Run(async () =>
        {
            try
            {
                return ((ContentReference?)await commit(), (Exception?)null);
            }
            catch (ContentRefusedException refused)
            {
                return (null, refused);
            }
        }))).WaitAsync(TimeSpan.FromSeconds(60));

    // A stream that takes a stated time to deliver its one chunk, moving the clock as it does.
    private sealed class SlowStream(byte[] bytes, Clock clock, TimeSpan delay) : MemoryStream(bytes)
    {
        private bool _delayed;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_delayed)
            {
                _delayed = true;
                clock.Advance(delay);
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => SHA256.HashData(Encoding.UTF8.GetBytes("ports:" + tenantId.Value));
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
