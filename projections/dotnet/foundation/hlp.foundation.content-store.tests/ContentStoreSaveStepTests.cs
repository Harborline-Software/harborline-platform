using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// T-521/content-store-storage-unit-of-work, carried onto the bookkeeping port by T-521/content-store-host-transaction: one
// committed unit per operation that changes state. The oracle is the backing itself: the test images its state before and
// after the operation, and a failed commit's oracle is the image taken before the operation began.
public sealed class ContentStoreSaveStepTests
{
    private static readonly TenantId Tenant = new("save-step-tenant");
    private static readonly ContentStoreOptions Options = new(300_000, 600_000, 600_000, TimeSpan.FromMinutes(1));
    private static readonly byte[] Hello = "hello"u8.ToArray();

    [Fact]
    public async Task Clause_1_4_every_state_changing_operation_commits_one_unit_after_all_its_changes()
    {
        var backing = new RecordingStorage();
        var clock = new Clock();
        var store = new InMemoryContentStore(Options, new Keys(), clock, backing);

        var stage = await SavesAsync(backing, ContentStoreOperation.Put, async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null));
        var first = await SavesAsync(backing, ContentStoreOperation.Commit, async () => await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt"));
        var again = await SavesAsync(backing, ContentStoreOperation.Put, async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null));
        var second = await SavesAsync(backing, ContentStoreOperation.Commit, async () => await store.CommitReferenceAsync(Tenant, again, new("record-2", "0"), "b.txt"));
        Assert.Equal(first.ContentId, second.ContentId); // a duplicate: the save names the held item and the removed stage
        Assert.False(await SavesAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, first, HoldStatus.None, "dispose-1")));
        Assert.True(await SavesAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, second, HoldStatus.None, "dispose-2")));

        var large = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(LargeBytes()), "text/plain", null), new("record-3", "0"), "c.txt");
        store.CorruptCiphertext(Tenant, large.ContentId, 1);
        Saves(backing, ContentStoreOperation.IntegrityFailure, () => Assert.Throws<ContentIntegrityException>(() => store.Read(Tenant, large, true, new ContentByteRange(65_536, 65_538))));
        Saves(backing, ContentStoreOperation.IntegrityFailure, () => Assert.Throws<ContentIntegrityException>(() => store.Read(Tenant, large, true, new ContentByteRange(65_536, 65_538))));
        Assert.Equal(2, Assert.Single(store.IntegrityEntries).Count);
        store.CorruptCiphertext(Tenant, large.ContentId, 1); // restores the byte
        using (var body = store.Read(Tenant, large, true).Body)
        {
            var buffer = new byte[LargeBytes().Length];
            NoSave(backing, () => body.Read(buffer, 0, 1)); // a partly read body has not verified the item yet
            Saves(backing, ContentStoreOperation.IntegrityRecovery, () => body.ReadAtLeast(buffer.AsSpan(1), buffer.Length - 1));
            Assert.Equal(LargeBytes(), buffer);
            store.CorruptCiphertext(Tenant, large.ContentId, 1);
            Saves(backing, ContentStoreOperation.IntegrityFailure, () => Assert.Throws<ContentIntegrityException>(() => store.Read(Tenant, large, true, new ContentByteRange(65_536, 65_538))));
            NoSave(backing, () => body.Read(buffer, 0, 1)); // a finished body never closes a later episode
        }

        Assert.True(await SavesAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, large, HoldStatus.None, "erase"))); // erasure closes the episode in the same unit
        Assert.True(store.IsIntegrityHealthy);

        var late = await store.PutAsync(Tenant, new MemoryStream("late"u8.ToArray()), "text/plain", null);
        var abandoned = await store.PutAsync(Tenant, new MemoryStream("abandoned"u8.ToArray()), "text/plain", null);
        clock.Advance(TimeSpan.FromMinutes(1));
        await SavesAsync(backing, ContentStoreOperation.Commit, async () => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, late, new("record-4", "0"), "d.txt")));
        Assert.Equal("commit", store.Reclamations[^1].Run);
        Assert.Equal(1, await SavesAsync(backing, ContentStoreOperation.SweepExpired, async () => await store.SweepExpiredAsync("sweep-1")));
        Assert.DoesNotContain(abandoned, backing.Memory.Snapshot().Staged.Select(item => item.StageId));
    }

    [Fact]
    public async Task Clause_1_4_reads_and_operations_that_change_nothing_never_call_the_save_step()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(new(100, 5, 1000, TimeSpan.FromMinutes(1)), new Keys(), new Clock(), backing);
        var reference = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null), new("record", "0"), "a.txt");
        var tooBig = await store.PutAsync(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null);

        NoSave(backing, () => ReadBytes(store.Read(Tenant, reference, true)));
        NoSave(backing, () => ReadBytes(store.Read(Tenant, reference, true, new ContentByteRange(1, 2))));
        NoSave(backing, () => store.Read(Tenant, reference, true, new ContentByteRange(9, 10)));
        NoSave(backing, () => Assert.Throws<ContentNotFoundException>(() => store.Read(Tenant, reference, false)));
        NoSave(backing, () => store.Export(Tenant, [reference], true));
        NoSave(backing, () => (store.Reclamations, store.StagedCount, store.CommittedCount, store.IntegrityEntries, store.IsIntegrityHealthy));
        await NoSaveAsync(backing, async () => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(Tenant, tooBig, new("record", "1"), "b.txt"))); // record quota
        await NoSaveAsync(backing, async () => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.CommitReferenceAsync(new TenantId("other"), tooBig, new("record", "1"), "b.txt")));
        await NoSaveAsync(backing, async () => await Assert.ThrowsAsync<ContentRefusedException>(async () => await store.PutAsync(Tenant, new MemoryStream(new byte[101]), "text/plain", null)));
        await NoSaveAsync(backing, async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.Held, "held"));
        await NoSaveAsync(backing, async () => await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.Unreadable, "unreadable"));
        await NoSaveAsync(backing, async () => await store.RemoveReferenceAsync(Tenant, reference with { Owner = new("record", "elsewhere") }, HoldStatus.None, "unknown-owner"));
        await NoSaveAsync(backing, async () => await store.SweepExpiredAsync("nothing-expired"));
    }

    [Fact]
    public async Task Clause_2_4_a_throwing_save_fails_a_commit_a_duplicate_commit_and_a_reference_removal_and_leaves_the_store_as_it_was()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(Options, new Keys(), new Clock(), backing);

        var stage = await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        await FailsAsync(backing, ContentStoreOperation.Commit, async () => await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt"));
        var first = await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt"); // the stage survived whole
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, first, true)));

        var again = await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        await FailsAsync(backing, ContentStoreOperation.Commit, async () => await store.CommitReferenceAsync(Tenant, again, new("record-2", "0"), "b.txt"));
        Assert.Throws<ContentNotFoundException>(() => store.Read(Tenant, first with { Owner = new("record-2", "0") }, true));
        var second = await store.CommitReferenceAsync(Tenant, again, new("record-2", "0"), "b.txt"); // the duplicate's staged copy was not destroyed
        Assert.Equal(first.ContentId, second.ContentId);

        await FailsAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, first, HoldStatus.None, "dispose-1"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, first, true)));
        Assert.False(await store.RemoveReferenceAsync(Tenant, first, HoldStatus.None, "dispose-1"));
        await FailsAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, second, HoldStatus.None, "dispose-2"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, second, true))); // the key material outlived the failed reclamation
        Assert.Empty(store.Reclamations);
        Assert.True(await store.RemoveReferenceAsync(Tenant, second, HoldStatus.None, "dispose-2"));
        Assert.Single(store.Reclamations);
    }

    [Fact]
    public async Task Clause_2_a_throwing_save_also_leaves_a_put_an_expired_commit_the_sweep_and_integrity_episodes_as_they_were()
    {
        var backing = new RecordingStorage();
        var clock = new Clock();
        var log = new Logger();
        var store = new InMemoryContentStore(Options, new Keys(), clock, backing, log);

        await FailsAsync(backing, ContentStoreOperation.Put, async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null));
        Assert.Equal(0, store.StagedCount);

        var large = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(LargeBytes()), "text/plain", null), new("record", "0"), "a.txt");
        store.CorruptCiphertext(Tenant, large.ContentId, 1);
        var range = new ContentByteRange(65_536, 65_538);
        Fails(backing, ContentStoreOperation.IntegrityFailure, () => store.Read(Tenant, large, true, range));
        Assert.Empty(log.Events); // an episode that was not saved was not announced
        Assert.Throws<ContentIntegrityException>(() => store.Read(Tenant, large, true, range));
        Fails(backing, ContentStoreOperation.IntegrityFailure, () => store.Read(Tenant, large, true, range));
        Assert.Equal(1, Assert.Single(store.IntegrityEntries).Count);
        Assert.Single(log.Events);

        store.CorruptCiphertext(Tenant, large.ContentId, 1);
        Fails(backing, ContentStoreOperation.IntegrityRecovery, () => ReadBytes(store.Read(Tenant, large, true)));
        Assert.False(store.IsIntegrityHealthy);
        Assert.Equal(LargeBytes(), ReadBytes(store.Read(Tenant, large, true)));
        Assert.True(store.IsIntegrityHealthy);

        store.CorruptCiphertext(Tenant, large.ContentId, 1);
        Assert.Throws<ContentIntegrityException>(() => store.Read(Tenant, large, true, range));
        await FailsAsync(backing, ContentStoreOperation.RemoveReference, async () => await store.RemoveReferenceAsync(Tenant, large, HoldStatus.None, "erase"));
        Assert.False(store.IsIntegrityHealthy); // erasure's episode closure is undone with the rest of its unit

        var late = await store.PutAsync(Tenant, new MemoryStream("late"u8.ToArray()), "text/plain", null);
        clock.Advance(TimeSpan.FromMinutes(1));
        await FailsAsync(backing, ContentStoreOperation.Commit, async () => await store.CommitReferenceAsync(Tenant, late, new("record", "1"), "b.txt"));
        await FailsAsync(backing, ContentStoreOperation.SweepExpired, async () => await store.SweepExpiredAsync("sweep-1"));
        Assert.Equal(1, store.StagedCount);
        Assert.Equal(1, await store.SweepExpiredAsync("sweep-1"));
        Assert.Equal("staging-expired", Assert.Single(store.Reclamations).Cause);
    }

    // Runs one operation and checks its single committed unit against the before and after images of the backing.
    private static T Saves<T>(RecordingStorage backing, ContentStoreOperation operation, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        var result = act();
        var after = Image.Of(backing);
        var save = Assert.Single(backing.Commits.Skip(count));
        Assert.Equal(operation, save.Operation);
        Assert.Equal(after.Text, Image.Of(save.Image).Text); // every change was made before the commit and none after it
        Assert.NotEqual(before.Text, after.Text);
        return result;
    }

    private static async Task<T> SavesAsync<T>(RecordingStorage backing, ContentStoreOperation operation, Func<Task<T>> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        var result = await act();
        var after = Image.Of(backing);
        var save = Assert.Single(backing.Commits.Skip(count));
        Assert.Equal(operation, save.Operation);
        Assert.Equal(after.Text, Image.Of(save.Image).Text); // every change was made before the commit and none after it
        Assert.NotEqual(before.Text, after.Text);
        return result;
    }

    private static async Task NoSaveAsync<T>(RecordingStorage backing, Func<Task<T>> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        await act();
        Assert.Equal(count, backing.Commits.Count);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static async Task FailsAsync<T>(RecordingStorage backing, ContentStoreOperation operation, Func<Task<T>> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        backing.FailNextCommit = true;
        var failure = await Assert.ThrowsAsync<IOException>(async () => await act());
        Assert.Same(backing.Failure, failure);
        Assert.Equal(operation, Assert.Single(backing.Commits.Skip(count)).Operation);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static void NoSave<T>(RecordingStorage backing, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        act();
        Assert.Equal(count, backing.Commits.Count);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static void Fails<T>(RecordingStorage backing, ContentStoreOperation operation, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Commits.Count;
        backing.FailNextCommit = true;
        var failure = Assert.Throws<IOException>(() => act());
        Assert.Same(backing.Failure, failure);
        Assert.Equal(operation, Assert.Single(backing.Commits.Skip(count)).Operation);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static byte[] ReadBytes(ContentDelivery delivery)
    {
        using var body = delivery.Body;
        using var buffer = new MemoryStream();
        body.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] LargeBytes() => Enumerable.Repeat((byte)'a', (65_536 * 2) + 1).ToArray();

    // Every stored value a durable backing would write, rendered so two images compare as text.
    private sealed record Image(string Text)
    {
        public static Image Of(RecordingStorage backing) => Of(RecordingStorage.Capture(backing.Memory));

        public static Image Of(BookkeepingImage image) => new(string.Join('\n',
            image.Items.Select(item => "item " + Render(item)).Order(StringComparer.Ordinal)
                .Concat(image.Staged.Select(item => "staged " + Render(item)).Order(StringComparer.Ordinal))
                .Concat(image.Quota.Select(pair => "quota " + pair.Key.Tenant.Value + "/" + pair.Key.Record + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal))
                .Concat(image.Audits.Select(audit => "audit " + audit.AuditId + "|" + audit.ActorId))
                .Concat(image.Reclamations.Select(entry => "reclaimed " + string.Join('|', entry.ContentId.Value, entry.TenantId.Value, entry.Cause, entry.Run)))
                .Concat(image.Integrity.Select(entry => "integrity " + Render(entry)).Order(StringComparer.Ordinal))));

        private static string Render(ContentStoreItem item) => string.Join('|', item.StageId, item.Tenant.Value, item.ContentId.Value,
            item.Length.ToString(CultureInfo.InvariantCulture), item.MediaType, item.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), item.State,
            string.Join(',', item.Owners.Select(owner => owner.Record + "#" + owner.Element)), Convert.ToHexString(item.WrappedDataKey),
            Convert.ToHexString(item.WrapNonce), Convert.ToHexString(item.WrapTag), Convert.ToHexString(item.NoncePrefix),
            item.SegmentSize.ToString(CultureInfo.InvariantCulture));

        private static string Render(ContentIntegrityEntry entry) => string.Join('|', entry.ItemKey, entry.TenantId.Value,
            entry.FirstSeenUtc.ToString("O", CultureInfo.InvariantCulture), entry.LastSeenUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.Count.ToString(CultureInfo.InvariantCulture), entry.SegmentIndex.ToString(CultureInfo.InvariantCulture), entry.Active,
            entry.TraceId, entry.OperationId, entry.PrincipalId, entry.Owner.Record + "#" + entry.Owner.Element);
    }

    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => SHA256.HashData(Encoding.UTF8.GetBytes("save-step:" + tenantId.Value));
    }

    private sealed class Logger : IContentIntegrityLogger
    {
        public List<ContentIntegrityLogEvent> Events { get; } = [];
        public void LogIntegrityFailure(ContentIntegrityLogEvent integrityEvent) => Events.Add(integrityEvent);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
