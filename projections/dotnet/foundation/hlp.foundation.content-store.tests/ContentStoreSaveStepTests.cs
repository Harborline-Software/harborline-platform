using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// T-521/content-store-storage-unit-of-work: the storage port's one save step per operation. The oracle for each list of
// changed entries is the backing itself: the test images every collection before and after the operation and diffs the
// two images, so no expected name comes from the store's own bookkeeping. A failed save's oracle is the image taken
// before the operation began.
public sealed class ContentStoreSaveStepTests
{
    private static readonly TenantId Tenant = new("save-step-tenant");
    private static readonly ContentStoreOptions Options = new(300_000, 600_000, 600_000, TimeSpan.FromMinutes(1));
    private static readonly byte[] Hello = "hello"u8.ToArray();

    [Fact]
    public void Clause_1_4_every_state_changing_operation_saves_once_inside_the_lock_after_all_its_changes_naming_exactly_what_changed()
    {
        var backing = new RecordingStorage();
        var clock = new Clock();
        var store = new InMemoryContentStore(Options, new Keys(), clock, backing);

        var stage = Saves(backing, ContentStoreOperation.Put, () => store.Put(Tenant, new MemoryStream(Hello), "text/plain", null));
        var first = Saves(backing, ContentStoreOperation.Commit, () => store.Commit(Tenant, stage, new("record-1", "0"), "a.txt"));
        var again = Saves(backing, ContentStoreOperation.Put, () => store.Put(Tenant, new MemoryStream(Hello), "text/plain", null));
        var second = Saves(backing, ContentStoreOperation.Commit, () => store.Commit(Tenant, again, new("record-2", "0"), "b.txt"));
        Assert.Equal(first.ContentId, second.ContentId); // a duplicate: the save names the held item and the removed stage
        Assert.False(Saves(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, first, HoldStatus.None, "dispose-1")));
        Assert.True(Saves(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, second, HoldStatus.None, "dispose-2")));

        var large = store.Commit(Tenant, store.Put(Tenant, new MemoryStream(LargeBytes()), "text/plain", null), new("record-3", "0"), "c.txt");
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

        Assert.True(Saves(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, large, HoldStatus.None, "erase"))); // erasure closes the episode in the same unit
        Assert.True(store.IsIntegrityHealthy);

        var late = store.Put(Tenant, new MemoryStream("late"u8.ToArray()), "text/plain", null);
        var abandoned = store.Put(Tenant, new MemoryStream("abandoned"u8.ToArray()), "text/plain", null);
        clock.Advance(TimeSpan.FromMinutes(1));
        Saves(backing, ContentStoreOperation.Commit, () => Assert.Throws<ContentRefusedException>(() => store.Commit(Tenant, late, new("record-4", "0"), "d.txt")));
        Assert.Equal("commit", store.Reclamations[^1].Run);
        Assert.Equal(1, Saves(backing, ContentStoreOperation.SweepExpired, () => store.SweepExpired("sweep-1")));
        Assert.DoesNotContain(abandoned, backing.StagedItems.Keys);
    }

    [Fact]
    public void Clause_1_4_reads_and_operations_that_change_nothing_never_call_the_save_step()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(new(100, 5, 1000, TimeSpan.FromMinutes(1)), new Keys(), new Clock(), backing);
        var reference = store.Commit(Tenant, store.Put(Tenant, new MemoryStream(Hello), "text/plain", null), new("record", "0"), "a.txt");
        var tooBig = store.Put(Tenant, new MemoryStream("world"u8.ToArray()), "text/plain", null);

        NoSave(backing, () => ReadBytes(store.Read(Tenant, reference, true)));
        NoSave(backing, () => ReadBytes(store.Read(Tenant, reference, true, new ContentByteRange(1, 2))));
        NoSave(backing, () => store.Read(Tenant, reference, true, new ContentByteRange(9, 10)));
        NoSave(backing, () => Assert.Throws<ContentNotFoundException>(() => store.Read(Tenant, reference, false)));
        NoSave(backing, () => store.Export(Tenant, [reference], true));
        NoSave(backing, () => (store.Reclamations, store.StagedCount, store.CommittedCount, store.IntegrityEntries, store.IsIntegrityHealthy));
        NoSave(backing, () => Assert.Throws<ContentRefusedException>(() => store.Commit(Tenant, tooBig, new("record", "1"), "b.txt"))); // record quota
        NoSave(backing, () => Assert.Throws<ContentRefusedException>(() => store.Commit(new TenantId("other"), tooBig, new("record", "1"), "b.txt")));
        NoSave(backing, () => Assert.Throws<ContentRefusedException>(() => store.Put(Tenant, new MemoryStream(new byte[101]), "text/plain", null)));
        NoSave(backing, () => store.RemoveReference(Tenant, reference, HoldStatus.Held, "held"));
        NoSave(backing, () => store.RemoveReference(Tenant, reference, HoldStatus.Unreadable, "unreadable"));
        NoSave(backing, () => store.RemoveReference(Tenant, reference with { Owner = new("record", "elsewhere") }, HoldStatus.None, "unknown-owner"));
        NoSave(backing, () => store.SweepExpired("nothing-expired"));
    }

    [Fact]
    public void Clause_2_4_a_throwing_save_fails_a_commit_a_duplicate_commit_and_a_reference_removal_and_leaves_the_store_as_it_was()
    {
        var backing = new RecordingStorage();
        var store = new InMemoryContentStore(Options, new Keys(), new Clock(), backing);

        var stage = store.Put(Tenant, new MemoryStream(Hello), "text/plain", null);
        Fails(backing, ContentStoreOperation.Commit, () => store.Commit(Tenant, stage, new("record-1", "0"), "a.txt"));
        var first = store.Commit(Tenant, stage, new("record-1", "0"), "a.txt"); // the stage survived whole
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, first, true)));

        var again = store.Put(Tenant, new MemoryStream(Hello), "text/plain", null);
        Fails(backing, ContentStoreOperation.Commit, () => store.Commit(Tenant, again, new("record-2", "0"), "b.txt"));
        Assert.Throws<ContentNotFoundException>(() => store.Read(Tenant, first with { Owner = new("record-2", "0") }, true));
        var second = store.Commit(Tenant, again, new("record-2", "0"), "b.txt"); // the duplicate's staged copy was not destroyed
        Assert.Equal(first.ContentId, second.ContentId);

        Fails(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, first, HoldStatus.None, "dispose-1"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, first, true)));
        Assert.False(store.RemoveReference(Tenant, first, HoldStatus.None, "dispose-1"));
        Fails(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, second, HoldStatus.None, "dispose-2"));
        Assert.Equal(Hello, ReadBytes(store.Read(Tenant, second, true))); // the key material outlived the failed reclamation
        Assert.Empty(store.Reclamations);
        Assert.True(store.RemoveReference(Tenant, second, HoldStatus.None, "dispose-2"));
        Assert.Single(store.Reclamations);
    }

    [Fact]
    public void Clause_2_a_throwing_save_also_leaves_a_put_an_expired_commit_the_sweep_and_integrity_episodes_as_they_were()
    {
        var backing = new RecordingStorage();
        var clock = new Clock();
        var log = new Logger();
        var store = new InMemoryContentStore(Options, new Keys(), clock, backing, log);

        Fails(backing, ContentStoreOperation.Put, () => store.Put(Tenant, new MemoryStream(Hello), "text/plain", null));
        Assert.Equal(0, store.StagedCount);

        var large = store.Commit(Tenant, store.Put(Tenant, new MemoryStream(LargeBytes()), "text/plain", null), new("record", "0"), "a.txt");
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
        Fails(backing, ContentStoreOperation.RemoveReference, () => store.RemoveReference(Tenant, large, HoldStatus.None, "erase"));
        Assert.False(store.IsIntegrityHealthy); // erasure's episode closure is undone with the rest of its unit

        var late = store.Put(Tenant, new MemoryStream("late"u8.ToArray()), "text/plain", null);
        clock.Advance(TimeSpan.FromMinutes(1));
        Fails(backing, ContentStoreOperation.Commit, () => store.Commit(Tenant, late, new("record", "1"), "b.txt"));
        Fails(backing, ContentStoreOperation.SweepExpired, () => store.SweepExpired("sweep-1"));
        Assert.Equal(1, store.StagedCount);
        Assert.Equal(1, store.SweepExpired("sweep-1"));
        Assert.Equal("staging-expired", Assert.Single(store.Reclamations).Cause);
    }

    // Runs one operation and checks its single save step against the before and after images of the backing.
    private static T Saves<T>(RecordingStorage backing, ContentStoreOperation operation, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Saves.Count;
        var result = act();
        var after = Image.Of(backing);
        var save = Assert.Single(backing.Saves.Skip(count));
        Assert.True(save.InsideLock);
        Assert.Equal(operation, save.Change.Operation);
        Assert.Equal(after.Text, save.Image.Text); // every change was made before the save step and none after it
        Assert.Equal(Changed(before.Items, after.Items), save.Change.Items.Select(Image.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Changed(before.Staged, after.Staged), save.Change.StagedItems.Select(Image.Key).Order(StringComparer.Ordinal));
        Assert.Equal(Changed(before.Integrity, after.Integrity), save.Change.IntegrityEntries.Select(Image.Key).Order(StringComparer.Ordinal));
        Assert.Equal(after.Reclamations.Skip(before.Reclamations.Count), save.Change.Reclamations.Select(Image.Render));
        Assert.NotEqual(before.Text, after.Text);
        return result;
    }

    private static void NoSave<T>(RecordingStorage backing, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Saves.Count;
        act();
        Assert.Equal(count, backing.Saves.Count);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static void Fails<T>(RecordingStorage backing, ContentStoreOperation operation, Func<T> act)
    {
        var before = Image.Of(backing);
        var count = backing.Saves.Count;
        backing.FailNext = true;
        var failure = Assert.Throws<IOException>(() => act());
        Assert.Same(backing.Failure, failure);
        Assert.Equal(operation, Assert.Single(backing.Saves.Skip(count)).Change.Operation);
        Assert.Equal(before.Text, Image.Of(backing).Text);
    }

    private static string[] Changed(SortedDictionary<string, string> before, SortedDictionary<string, string> after) =>
        [.. before.Keys.Union(after.Keys).Where(key => before.GetValueOrDefault(key) != after.GetValueOrDefault(key)).Order(StringComparer.Ordinal)];

    private static byte[] ReadBytes(ContentDelivery delivery)
    {
        using var body = delivery.Body;
        using var buffer = new MemoryStream();
        body.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static byte[] LargeBytes() => Enumerable.Repeat((byte)'a', (65_536 * 2) + 1).ToArray();

    // Every stored value a durable backing would write, rendered so two images compare as text.
    private sealed record Image(SortedDictionary<string, string> Items, SortedDictionary<string, string> Staged, List<string> Reclamations,
        SortedDictionary<string, string> Integrity)
    {
        public string Text => string.Join('\n', Items.Select(pair => "item " + pair.Key + "=" + pair.Value)
            .Concat(Staged.Select(pair => "staged " + pair.Key + "=" + pair.Value))
            .Concat(Reclamations.Select(entry => "reclaimed " + entry))
            .Concat(Integrity.Select(pair => "integrity " + pair.Key + "=" + pair.Value)));

        public static Image Of(IContentStoreStorage storage) => new(
            new(storage.Items.ToDictionary(pair => Key(pair.Key), pair => Render(pair.Value)), StringComparer.Ordinal),
            new(storage.StagedItems.ToDictionary(pair => Key(pair.Key), pair => Render(pair.Value)), StringComparer.Ordinal),
            [.. storage.Reclamations.Select(Render)],
            new(storage.IntegrityEntries.ToDictionary(pair => Key(pair.Key), pair => Render(pair.Value)), StringComparer.Ordinal));

        public static string Key(ContentStorageItemKey key) => key.TenantId.Value + "/" + key.ContentId.Value;
        public static string Key(Guid stageId) => stageId.ToString();
        public static string Key((TenantId TenantId, Guid ItemKey) key) => key.TenantId.Value + "/" + key.ItemKey;

        public static string Render(ReclamationEntry entry) => string.Join('|', entry.ContentId.Value, entry.TenantId.Value, entry.Cause, entry.Run);

        private static string Render(ContentStoreItem item) => string.Join('|', item.StageId, item.Tenant.Value, item.ContentId.Value,
            item.Length.ToString(CultureInfo.InvariantCulture), item.MediaType, item.CreatedUtc.ToString("O", CultureInfo.InvariantCulture), item.State,
            string.Join(',', item.Owners.Select(owner => owner.Record + "#" + owner.Element)), Convert.ToHexString(item.WrappedDataKey),
            Convert.ToHexString(item.WrapNonce), Convert.ToHexString(item.WrapTag), Convert.ToHexString(item.NoncePrefix),
            item.SegmentSize.ToString(CultureInfo.InvariantCulture),
            string.Join(',', item.Segments.Select(segment => Convert.ToHexString(SHA256.HashData(segment.Ciphertext)) + ":" + Convert.ToHexString(segment.Tag))));

        private static string Render(ContentIntegrityEntry entry) => string.Join('|', entry.ItemKey, entry.TenantId.Value,
            entry.FirstSeenUtc.ToString("O", CultureInfo.InvariantCulture), entry.LastSeenUtc.ToString("O", CultureInfo.InvariantCulture),
            entry.Count.ToString(CultureInfo.InvariantCulture), entry.SegmentIndex.ToString(CultureInfo.InvariantCulture), entry.Active,
            entry.TraceId, entry.OperationId, entry.PrincipalId, entry.Owner.Record + "#" + entry.Owner.Element);
    }

    // A backing whose save step records what it was handed, whether the caller held SyncRoot, and the collections as the
    // save step saw them; told to, it throws once in place of writing.
    private sealed class RecordingStorage : IContentStoreStorage
    {
        public object SyncRoot { get; } = new();
        public IDictionary<ContentStorageItemKey, ContentStoreItem> Items { get; } = new Dictionary<ContentStorageItemKey, ContentStoreItem>();
        public IDictionary<Guid, ContentStoreItem> StagedItems { get; } = new Dictionary<Guid, ContentStoreItem>();
        public IList<ReclamationEntry> Reclamations { get; } = new List<ReclamationEntry>();
        public IDictionary<(TenantId TenantId, Guid ItemKey), ContentIntegrityEntry> IntegrityEntries { get; } = new Dictionary<(TenantId TenantId, Guid ItemKey), ContentIntegrityEntry>();
        public List<(ContentStoreChange Change, bool InsideLock, Image Image)> Saves { get; } = [];
        public bool FailNext { get; set; }
        public IOException? Failure { get; private set; }

        public void Save(ContentStoreChange change)
        {
            Saves.Add((change, Monitor.IsEntered(SyncRoot), Image.Of(this)));
            if (!FailNext) return;
            FailNext = false;
            Failure = new IOException("The backing could not write the unit.");
            throw Failure;
        }
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
