using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

// T-521/content-store-epoch-wrap-key (ADR 0116 decisions 1, 4 and 5). Expected values are literals from the slice record and
// ADR 0116, a reference unwrap written here from DES-0054 eng-10, or stated properties: every item stays readable through every
// step of a rotation, and a wrap taken before a destroyed epoch can't be opened afterwards.
public sealed class ContentStoreEpochTests
{
    private static readonly TenantId Tenant = new("epoch-tenant");
    private static readonly TenantId Other = new("other-tenant");
    private static readonly byte[] Hello = "hello"u8.ToArray();
    private static readonly byte[] World = "world"u8.ToArray();
    private static readonly byte[] TenantKey = SHA256.HashData(Encoding.UTF8.GetBytes("epoch"));
    private static readonly ContentReadContext Reader = new("trace", "operation", "tenant-opaque", "principal.reader");
    private const string Scheduler = "sys.scheduler";

    private static InMemoryContentStore Store(IContentStoreStorage backing, TimeProvider? clock = null) =>
        new(new(1_000_000, 1_000_000, 1_000_000, TimeSpan.FromMinutes(1)), new Keys(), clock ?? new Clock(), backing);

    private static ContentStoreWrite Write(string name, string actor = Scheduler, IContentBookkeepingSession? transaction = null) =>
        new(new("command-" + name, "key-" + name, "fingerprint-" + name), new("audit-" + name, actor, DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), transaction);

    private static async Task<ContentReference> Commit(InMemoryContentStore store, byte[] bytes, string record = "record-1") =>
        await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(bytes), "text/plain", null), new(record, "0"), "a.txt");

    private static async Task<byte[]> Read(InMemoryContentStore store, ContentReference reference)
    {
        await using var body = (await store.ReadAsync(Tenant, reference, true, Reader)).Body;
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    private static byte[] WrapSubkey(TenantId tenant)
    {
        var key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, TenantKey, key, Encoding.UTF8.GetBytes(tenant.Value), "harborline-content-store:data-key-wrap:v1"u8.ToArray());
        return key;
    }

    private static byte[] OpenEpoch(ContentEpochKey epoch, TenantId tenant)
    {
        var plain = new byte[32];
        using var aead = new AesGcm(WrapSubkey(tenant), 16);
        aead.Decrypt(epoch.WrapNonce, epoch.WrappedKey, epoch.WrapTag, plain, Encoding.UTF8.GetBytes("harborline-content-store:epoch-key:v1|" + epoch.EpochId.ToString("N") + "|" + tenant.Value));
        return plain;
    }

    private static byte[] OpenItem(ContentStoreItem item, byte[] epochKey)
    {
        var plain = new byte[32];
        using var aead = new AesGcm(epochKey, 16);
        aead.Decrypt(item.WrapNonce, item.WrappedDataKey, item.WrapTag, plain, Encoding.ASCII.GetBytes(item.ContentId.Value));
        return plain;
    }

    [Fact]
    public async Task Clause_1_the_key_port_holds_only_a_wrapped_256_bit_epoch_key_bound_to_its_tenant_and_id()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var item = await store.StorageItemAsync(Tenant, reference.ContentId);

        var epoch = (await backing.Epochs.GetCurrentAsync(Tenant))!;
        Assert.Equal(item.EpochId, epoch.EpochId);
        Assert.Same(epoch, await backing.Epochs.GetAsync(Tenant, epoch.EpochId));
        Assert.Equal(32, epoch.WrappedKey.Length);
        Assert.Equal(32, OpenEpoch(epoch, Tenant).Length);
        Assert.NotEqual(epoch.WrappedKey, OpenEpoch(epoch, Tenant));
        Assert.ThrowsAny<CryptographicException>(() => OpenEpoch(epoch, Other));
        Assert.ThrowsAny<CryptographicException>(() => OpenEpoch(epoch with { EpochId = Guid.NewGuid() }, Tenant));
    }

    [Fact]
    public async Task Clause_1_tenants_get_separate_epochs_and_a_tenants_first_put_makes_one()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        Assert.Null(await backing.Epochs.GetCurrentAsync(Tenant));
        await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        await store.PutAsync(Tenant, new MemoryStream(World), "text/plain", null);
        await store.PutAsync(Other, new MemoryStream(Hello), "text/plain", null);

        Assert.Equal(2, backing.EpochEvents.Count(entry => entry == "epoch-created")); // one for each tenant
        Assert.NotEqual((await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId, (await backing.Epochs.GetCurrentAsync(Other))!.EpochId);
    }

    [Fact]
    public async Task Clause_1_two_first_puts_that_race_settle_on_one_current_epoch_and_the_loser_destroys_its_own()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        Guid? rival = null;
        backing.BeforeFirstSetCurrent = async () =>
        {
            rival = Guid.NewGuid();
            await backing.MemoryEpochs.CreateAsync(Tenant, new(rival.Value, new byte[32], new byte[12], new byte[16]));
            Assert.True(await backing.MemoryEpochs.TrySetCurrentAsync(Tenant, null, rival.Value));
        };

        await Assert.ThrowsAnyAsync<CryptographicException>(async () => await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null)); // the rival's key is junk, which proves the put took it

        Assert.Equal(rival, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
        Assert.Contains("epoch-destroyed", backing.EpochEvents);
    }

    [Fact]
    public async Task Clause_2_an_item_stays_readable_after_a_rotation_both_before_and_after_its_rewrap()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var before = await store.StorageItemAsync(Tenant, reference.ContentId);

        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        Assert.Equal(before.EpochId, rotation.From);
        Assert.NotEqual(before.EpochId, rotation.To);
        Assert.Equal(rotation.To, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
        Assert.Equal(before.EpochId, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));

        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, before.EpochId, rotation.To, 10, Write("rewrap")));
        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));

        var after = await Commit(store, World);
        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, after.ContentId)).EpochId);
    }

    [Fact]
    public async Task Clause_2_a_read_whose_epoch_is_gone_fails_as_integrity_not_as_anything_else()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var item = await store.StorageItemAsync(Tenant, reference.ContentId);
        await backing.MemoryEpochs.DestroyAsync(Tenant, item.EpochId); // bypasses the store's refusal, as lost key storage would

        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(() => Read(store, reference));
        Assert.Equal("The item's epoch key is gone.", failure.InnerException!.Message);
        Assert.False(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task Clause_3_a_rewrap_moves_a_stated_batch_with_one_audit_row_until_none_remain()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var a = await Commit(store, "aaa"u8.ToArray());
        var b = await Commit(store, "bbb"u8.ToArray());
        var c = await Commit(store, "ccc"u8.ToArray());
        var staged = await store.PutAsync(Tenant, new MemoryStream("ddd"u8.ToArray()), "text/plain", null);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        var rows = backing.Memory.Snapshot().Audits.Length;

        Assert.Equal(3, await store.RewrapEpochAsync(Tenant, from, rotation.To, 3, Write("batch-1")));
        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, from, rotation.To, 3, Write("batch-2")));
        Assert.Equal(0, await store.RewrapEpochAsync(Tenant, from, rotation.To, 3, Write("batch-3")));

        var audits = backing.Memory.Snapshot().Audits[rows..];
        Assert.Equal(["audit-batch-1", "audit-batch-2"], audits.Select(audit => audit.AuditId));
        Assert.Equal("aaa"u8.ToArray(), await Read(store, a));
        Assert.Equal("bbb"u8.ToArray(), await Read(store, b));
        Assert.Equal("ccc"u8.ToArray(), await Read(store, c));
        var moved = await store.CommitReferenceAsync(Tenant, staged, new("record-2", "0"), "d.txt"); // a staged item moved too
        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, moved.ContentId)).EpochId);
        Assert.Equal("ddd"u8.ToArray(), await Read(store, moved));
    }

    [Fact]
    public async Task Clause_3_a_rewrap_changes_the_wrap_never_writes_a_plaintext_key_and_returns_only_a_count()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var before = await store.StorageItemAsync(Tenant, reference.ContentId);
        var plain = OpenItem(before, OpenEpoch((await backing.Epochs.GetAsync(Tenant, before.EpochId))!, Tenant));
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));

        await store.RewrapEpochAsync(Tenant, before.EpochId, rotation.To, 1, Write("rewrap"));

        var after = await store.StorageItemAsync(Tenant, reference.ContentId);
        Assert.Equal(plain, OpenItem(after, OpenEpoch((await backing.Epochs.GetAsync(Tenant, rotation.To))!, Tenant)));
        Assert.NotEqual(before.WrappedDataKey, after.WrappedDataKey);
        Assert.NotEqual(before.WrapNonce, after.WrapNonce);
        Assert.NotEqual(plain, after.WrappedDataKey);
        Assert.Equal(typeof(ValueTask<int>), typeof(InMemoryContentStore).GetMethod(nameof(InMemoryContentStore.RewrapEpochAsync))!.ReturnType);
        foreach (var audit in backing.Memory.Snapshot().Audits) Assert.True(audit.Payload.IsEmpty);
    }

    [Fact]
    public async Task Clause_3_an_item_under_a_legal_hold_is_rewrapped_like_any_other_and_stays()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var held = await Commit(store, Hello);
        Assert.False(await store.RemoveReferenceAsync(Tenant, held, HoldStatus.Held, "dispose"));
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));

        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("rewrap")));

        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, held.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, held));
    }

    [Fact]
    public async Task Clause_3_a_rewrap_in_the_callers_transaction_commits_and_rolls_back_with_it_and_writes_its_own_audit()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;

        var rolledBack = new CallerTransaction(backing.Bookkeeping);
        await Assert.ThrowsAsync<InjectedFault>(async () => await rolledBack.RunAsync(async session =>
        {
            Assert.Equal(1, await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("joined-1", Scheduler, session)));
            Assert.Equal(from, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId); // not visible until commit
            throw new InjectedFault();
        }));
        Assert.Equal(from, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
        Assert.DoesNotContain(backing.Memory.Snapshot().Audits, audit => audit.AuditId == "audit-joined-1");

        var committed = new CallerTransaction(backing.Bookkeeping);
        await committed.RunAsync(async session =>
            Assert.Equal(1, await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("joined-2", Scheduler, session))));
        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Contains(backing.Memory.Snapshot().Audits, audit => audit.AuditId == "audit-joined-2");
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task Clause_3_a_row_changed_after_it_was_listed_is_left_alone_and_not_counted()
    {
        var memory = new InMemoryContentBookkeeping();
        var item = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('a', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, Guid.NewGuid());
        var moved = Guid.NewGuid();
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.AddStagedAsync(item);
            await seed.CommitAsync();
        }

        await using var unit = await memory.BeginTransactionAsync();
        Assert.False(await unit.ReplaceWrapAsync(item with { EpochId = Guid.NewGuid() }, moved, [9], [9], [9]));
        Assert.False(await unit.ReplaceWrapAsync(item with { StageId = Guid.NewGuid() }, moved, [9], [9], [9]));
        Assert.True(await unit.ReplaceWrapAsync(item, moved, [9], [8], [7]));
        Assert.False(await unit.ReplaceWrapAsync(item, moved, [9], [9], [9]));
        var stored = (await unit.FindStagedAsync(item.StageId))!;
        Assert.Equal(moved, stored.EpochId);
        Assert.Equal(new byte[] { 9 }, stored.WrappedDataKey);
        Assert.Equal(new byte[] { 8 }, stored.WrapNonce);
        Assert.Equal(new byte[] { 7 }, stored.WrapTag);
        Assert.Empty(await unit.ItemsUnderEpochAsync(Tenant, item.EpochId, 5));
        Assert.Single(await unit.ItemsUnderEpochAsync(Tenant, moved, 5));
        Assert.Empty(await unit.ItemsUnderEpochAsync(Other, moved, 5));
        Assert.False(await unit.NamesEpochAsync(Other, moved));
        Assert.True(await unit.NamesEpochAsync(Tenant, moved));
    }

    [Fact]
    public async Task Clause_3_a_wrap_that_fails_authentication_stops_the_batch_typed_and_moves_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var good = await Commit(store, "good"u8.ToArray());
        var bad = await Commit(store, "bad"u8.ToArray());
        var goodStage = (await store.StorageItemAsync(Tenant, good.ContentId)).StageId;
        var badItem = await store.StorageItemAsync(Tenant, bad.ContentId);
        await store.CorruptWrapTagAsync(Tenant, bad.ContentId);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;

        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(async () => await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("rewrap")));

        Assert.Equal(badItem.StageId, failure.ItemKey);
        Assert.Equal(from, (await store.StorageItemAsync(Tenant, good.ContentId)).EpochId);
        Assert.Equal("good"u8.ToArray(), await Read(store, good));
        Assert.NotEqual(goodStage, badItem.StageId);
    }

    [Fact]
    public async Task Clause_3_a_rewrap_refuses_bad_arguments_before_writing_anything()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        var begun = backing.TransactionsBegun;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await store.RewrapEpochAsync(Tenant, from, rotation.To, 0, Write("zero")));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RewrapEpochAsync(Tenant, from, from, 5, Write("same")));
        Assert.Equal("That epoch does not exist.", (await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RewrapEpochAsync(Tenant, Guid.NewGuid(), rotation.To, 5, Write("no-from")))).Message);
        Assert.Equal("That epoch does not exist.", (await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RewrapEpochAsync(Tenant, from, Guid.NewGuid(), 5, Write("no-to")))).Message);
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, new(new(" ", "k", "f"), Write("x").Audit)));
        Assert.Equal(begun, backing.TransactionsBegun);
    }

    [Fact]
    public async Task Clause_4_destroying_an_epoch_is_refused_while_any_item_names_it_or_it_is_current()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var staged = await store.PutAsync(Tenant, new MemoryStream(World), "text/plain", null);
        var named = (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId;

        var current = await Assert.ThrowsAsync<ContentEpochInUseException>(async () => await store.DestroyEpochAsync(Tenant, named, Write("current")));
        Assert.Equal(named, current.EpochId);
        Assert.Equal("That epoch is still in use.", current.Message);

        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var rows = backing.Memory.Snapshot().Audits.Length;
        await Assert.ThrowsAsync<ContentEpochInUseException>(async () => await store.DestroyEpochAsync(Tenant, named, Write("still-named")));
        Assert.Equal(rows, backing.Memory.Snapshot().Audits.Length);
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, named));

        Assert.Equal(2, await store.RewrapEpochAsync(Tenant, named, rotation.To, 1, Write("one")) + await store.RewrapEpochAsync(Tenant, named, rotation.To, 1, Write("two")));
        Assert.True(await store.DestroyEpochAsync(Tenant, named, Write("destroy")));
        Assert.Null(await backing.Epochs.GetAsync(Tenant, named));
        Assert.False(await store.DestroyEpochAsync(Tenant, named, Write("again")));
        Assert.Equal(Hello, await Read(store, reference));
        Assert.Equal(World, await Read(store, await store.CommitReferenceAsync(Tenant, staged, new("record-2", "0"), "b.txt")));
    }

    [Fact]
    public async Task Clause_4_an_epoch_named_only_by_another_tenant_is_not_in_use_for_this_one()
    {
        var store = Store(new RecordingStorage());
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        var second = await store.RotateEpochAsync(Tenant, Write("rotate-2"));
        Assert.Equal(rotation.To, second.From);
        Assert.True(await store.DestroyEpochAsync(Tenant, rotation.To, Write("destroy")));
    }

    [Theory]
    [InlineData("epoch-created")]
    [InlineData("before-switch")]
    [InlineData("epoch-switched")]
    public async Task Clause_4_a_crash_in_the_rotation_leaves_every_item_readable_after_a_restart_and_a_retry_finishes(string step)
    {
        var backing = new RecordingStorage();
        var reference = await Commit(Store(backing), Hello);
        var from = (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId;
        backing.OnEpochStep = current => { if (current == step) throw new Crash(); };

        await Assert.ThrowsAsync<Crash>(async () => await Store(backing).RotateEpochAsync(Tenant, Write("crashed")));
        backing.OnEpochStep = null;

        var restarted = Store(backing);
        Assert.Equal(Hello, await Read(restarted, reference));
        var retry = await restarted.RotateEpochAsync(Tenant, Write("retry"));
        Assert.Equal(1, await restarted.RewrapEpochAsync(Tenant, from, retry.To, 5, Write("rewrap")));
        Assert.Equal(Hello, await Read(restarted, reference));
        Assert.True(await restarted.DestroyEpochAsync(Tenant, from, Write("destroy")));
        Assert.Equal(Hello, await Read(restarted, reference));
    }

    [Fact]
    public async Task Clause_4_a_crash_mid_batch_leaves_items_on_both_epochs_readable_and_a_restart_finishes_the_move()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var a = await Commit(store, "aaa"u8.ToArray());
        var b = await Commit(store, "bbb"u8.ToArray());
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("first")));

        backing.FailNextCommit = true; // the second batch's transaction dies
        await Assert.ThrowsAsync<IOException>(async () => await store.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("dies")));

        var restarted = Store(backing);
        Assert.Equal("aaa"u8.ToArray(), await Read(restarted, a));
        Assert.Equal("bbb"u8.ToArray(), await Read(restarted, b));
        await Assert.ThrowsAsync<ContentEpochInUseException>(async () => await restarted.DestroyEpochAsync(Tenant, from, Write("too-early")));
        Assert.Equal(1, await restarted.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("second")));
        Assert.True(await restarted.DestroyEpochAsync(Tenant, from, Write("destroy")));
        Assert.Equal("aaa"u8.ToArray(), await Read(restarted, a));
        Assert.Equal("bbb"u8.ToArray(), await Read(restarted, b));
    }

    [Fact]
    public async Task Clause_4_a_crash_before_the_destroy_keeps_the_key_and_a_failed_audit_keeps_it_too_and_a_retry_destroys_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("rewrap"));

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.DestroyEpochAsync(Tenant, from, Write("audit-fails")));
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, from));

        backing.OnEpochStep = current => { if (current == "before-destroy") throw new Crash(); };
        await Assert.ThrowsAsync<Crash>(async () => await store.DestroyEpochAsync(Tenant, from, Write("crashes")));
        backing.OnEpochStep = null;
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, from));
        Assert.Equal(Hello, await Read(Store(backing), reference));

        Assert.True(await Store(backing).DestroyEpochAsync(Tenant, from, Write("retried")));
        Assert.Null(await backing.Epochs.GetAsync(Tenant, from));
    }

    [Fact]
    public async Task Clause_4_a_failed_rotation_audit_leaves_the_old_epoch_current_and_nothing_names_the_new_one()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var from = (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId;

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.RotateEpochAsync(Tenant, Write("audit-fails")));

        Assert.Equal(from, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task Clause_4_a_rotation_that_loses_the_switch_destroys_its_own_epoch_and_changes_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        Guid? rival = null;
        backing.BeforeFirstSetCurrent = async () => rival = (await Store(backing).RotateEpochAsync(Tenant, Write("rival"))).To;

        var lost = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RotateEpochAsync(Tenant, Write("loser")));

        Assert.Equal("Another rotation of this tenant's epoch finished first.", lost.Message);
        Assert.Equal(rival, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
        Assert.Contains("epoch-destroyed", backing.EpochEvents);
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task Clause_5_after_a_full_rewrap_and_destroy_a_wrap_copied_before_the_rewrap_cannot_be_unwrapped()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var item = await store.StorageItemAsync(Tenant, reference.ContentId);
        var leftover = item with { WrappedDataKey = [.. item.WrappedDataKey], WrapNonce = [.. item.WrapNonce], WrapTag = [.. item.WrapTag] }; // a dead tuple, a WAL page, a block on disk
        var oldEpoch = (await backing.Epochs.GetAsync(Tenant, item.EpochId))!;
        var plain = OpenItem(leftover, OpenEpoch(oldEpoch, Tenant)); // readable while the epoch lives: the threat this removes

        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        await store.RewrapEpochAsync(Tenant, item.EpochId, rotation.To, 5, Write("rewrap"));
        Assert.True(await store.DestroyEpochAsync(Tenant, item.EpochId, Write("destroy")));

        Assert.Null(await backing.Epochs.GetAsync(Tenant, item.EpochId));
        Assert.Equal(new byte[32], oldEpoch.WrappedKey); // even the object the backing held is cleared
        var newKey = OpenEpoch((await backing.Epochs.GetCurrentAsync(Tenant))!, Tenant);
        Assert.ThrowsAny<CryptographicException>(() => OpenItem(leftover, newKey));
        Assert.ThrowsAny<CryptographicException>(() => OpenItem(leftover, WrapSubkey(Tenant)));
        Assert.Equal(plain, OpenItem(await store.StorageItemAsync(Tenant, reference.ContentId), newKey)); // the live key is the same key, under the new wrap
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task Rotation_and_destroy_carry_evidence_and_run_alone_because_a_key_write_cannot_roll_back()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await Commit(store, Hello);
        var rows = backing.Memory.Snapshot().Audits.Length;
        var from = (await store.RotateEpochAsync(Tenant, Write("rotate", "principal.operator"))).From!.Value;
        var audit = Assert.Single(backing.Memory.Snapshot().Audits[rows..]);
        Assert.Equal(("audit-rotate", "principal.operator"), (audit.AuditId, audit.ActorId));
        Assert.Contains(backing.Commits, commit => commit.Operation == ContentStoreOperation.EpochRotation);

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RotateEpochAsync(Tenant, new(new(" ", "k", "f"), Write("x").Audit)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DestroyEpochAsync(Tenant, from, new(new(" ", "k", "f"), Write("x").Audit)));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.RotateEpochAsync(Tenant, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.DestroyEpochAsync(Tenant, from, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await store.RewrapEpochAsync(Tenant, from, Guid.NewGuid(), 1, null!));

        var caller = new CallerTransaction(backing.Bookkeeping);
        var doomed = await Assert.ThrowsAsync<KernelTransactionStateException>(async () => await caller.RunAsync(async session =>
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.RotateEpochAsync(Tenant, Write("joined-rotate", Scheduler, session)));
            await Assert.ThrowsAsync<ArgumentException>(async () => await store.DestroyEpochAsync(Tenant, from, Write("joined-destroy", Scheduler, session)));
        }));
        Assert.Equal(KernelTransactionErrors.EnclosingExecutionDoomed, doomed.Code);
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, from));
    }

    [Fact]
    public async Task The_in_memory_key_port_switches_conditionally_and_forgets_a_destroyed_current_epoch()
    {
        var keys = new InMemoryContentEpochKeyStore();
        var first = new ContentEpochKey(Guid.NewGuid(), [1], [2], [3]);
        var second = new ContentEpochKey(Guid.NewGuid(), [4], [5], [6]);
        await keys.CreateAsync(Tenant, first);
        await keys.CreateAsync(Tenant, second);

        Assert.Null(await keys.GetCurrentAsync(Tenant));
        Assert.False(await keys.TrySetCurrentAsync(Tenant, Guid.NewGuid(), first.EpochId));
        Assert.False(await keys.TrySetCurrentAsync(Tenant, null, Guid.NewGuid()));
        Assert.True(await keys.TrySetCurrentAsync(Tenant, null, first.EpochId));
        Assert.False(await keys.TrySetCurrentAsync(Tenant, null, second.EpochId));
        Assert.True(await keys.TrySetCurrentAsync(Tenant, first.EpochId, second.EpochId));
        Assert.Same(second, await keys.GetCurrentAsync(Tenant));
        Assert.Null(await keys.GetCurrentAsync(Other));
        Assert.Null(await keys.GetAsync(Other, first.EpochId));

        await keys.DestroyAsync(Tenant, first.EpochId);
        Assert.Equal(new byte[1], first.WrappedKey);
        Assert.Same(second, await keys.GetCurrentAsync(Tenant));
        await keys.DestroyAsync(Tenant, Guid.NewGuid());
        await keys.DestroyAsync(Tenant, second.EpochId);
        Assert.Null(await keys.GetCurrentAsync(Tenant));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await keys.CreateAsync(Tenant, null!));
    }

    private sealed class Crash : Exception;

    private sealed class InjectedFault : Exception;

    private sealed class CallerTransaction(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<string, string>
    {
        private IContentBookkeepingTransaction? _inner;

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
            return new Transaction(_inner);
        }

        private sealed class Transaction(IContentBookkeepingTransaction inner) : IKernelPreparedTransaction<string, string>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageRecordAsync(string record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public async ValueTask<string> CommitAsync(CancellationToken cancellationToken = default)
            {
                await inner.CommitAsync(cancellationToken);
                return "committed";
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private sealed class Keys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => TenantKey;
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }
}
