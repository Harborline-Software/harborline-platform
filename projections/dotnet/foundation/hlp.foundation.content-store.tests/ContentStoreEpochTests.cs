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

    // A backing that keeps one audit chain per tenant places a transaction's row by the tenant its calls name (the api node
    // refuses "a content-store write named no tenant"). Rotation's transaction names it by pinning the new epoch.
    [Fact]
    public async Task Clause_4_a_rotation_transaction_names_its_tenant_by_pinning_the_new_epoch()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await Commit(store, Hello);
        lock (backing.Pins) backing.Pins.Clear();

        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));

        Assert.Equal([(Tenant, rotation.To)], backing.Pins);
    }

    // CodeRabbit on #431: a backing can retire the new epoch before the rotation pins it. The rotation must then refuse and
    // destroy the key, never make a retired epoch current.
    [Fact]
    public async Task Clause_4_a_rotation_whose_new_epoch_is_already_retired_refuses_and_destroys_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var first = (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId;
        lock (backing.Pins) backing.Pins.Clear();
        backing.EpochEvents.Clear();
        backing.RefuseNextPin = true;

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RotateEpochAsync(Tenant, Write("rotate-retired")));

        Assert.Equal("That epoch is being destroyed.", refusal.Message);
        var (tenant, made) = Assert.Single(backing.Pins);
        Assert.Equal(Tenant, tenant);
        Assert.NotEqual(first, made);
        Assert.Equal(first, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
        Assert.Null(await backing.Epochs.GetAsync(Tenant, made));
        Assert.Equal(["epoch-created", "epoch-destroyed"], backing.EpochEvents);
        Assert.Equal(Hello, await Read(store, reference));
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
        Assert.False(await unit.RetireEpochIfUnnamedAsync(Tenant, moved)); // its own write names it
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
        backing.EpochEvents.Clear();

        backing.FailNextCommit = true;
        await Assert.ThrowsAsync<IOException>(async () => await store.RotateEpochAsync(Tenant, Write("audit-fails")));

        Assert.Equal(["epoch-created", "epoch-destroyed"], backing.EpochEvents); // the port can't list keys, so nothing could find this one later
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
        var third = new ContentEpochKey(Guid.NewGuid(), [7], [8], [9]);
        await keys.CreateAsync(Tenant, third);
        Assert.True(await keys.TrySetCurrentAsync(Tenant, null, third.EpochId));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await keys.CreateAsync(Tenant, null!));
    }

    [Fact]
    public async Task Clause_3_replacing_a_wrap_waits_for_the_transaction_holding_that_row_whether_staged_or_committed()
    {
        var memory = new InMemoryContentBookkeeping();
        var owner = new ContentOwner("record-1", "0");
        var staged = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('b', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, Guid.NewGuid());
        var committed = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('c', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, Guid.NewGuid()) { State = ContentLifecycleState.Committed, Owners = [owner] };
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.AddStagedAsync(staged);
            await seed.TryCreateItemAsync(committed);
            await seed.CommitAsync();
        }

        var holder = await memory.BeginTransactionAsync();
        Assert.True(await holder.RemoveStagedAsync(staged.StageId));
        Assert.True(await holder.RemoveOwnerAsync(new(Tenant, committed.ContentId), owner));
        await using var other = await memory.BeginTransactionAsync();
        var waitingStaged = Task.Run(async () => await other.ReplaceWrapAsync(staged, Guid.NewGuid(), [9], [9], [9]));
        var waitingCommitted = Task.Run(async () => await other.ReplaceWrapAsync(committed, Guid.NewGuid(), [9], [9], [9]));
        await Task.Delay(100);
        Assert.False(waitingStaged.IsCompleted);
        Assert.False(waitingCommitted.IsCompleted);

        await holder.RollbackAsync();

        Assert.True(await waitingStaged.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(await waitingCommitted.WaitAsync(TimeSpan.FromSeconds(30)));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await other.ReplaceWrapAsync(null!, Guid.NewGuid(), [9], [9], [9]));
    }

    [Fact]
    public async Task Clause_3_a_transaction_lists_a_committed_row_it_moved_only_under_the_new_epoch_and_only_once()
    {
        var memory = new InMemoryContentBookkeeping();
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var committed = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('d', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, from) { State = ContentLifecycleState.Committed };
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.TryCreateItemAsync(committed);
            await seed.CommitAsync();
        }

        await using var unit = await memory.BeginTransactionAsync();
        Assert.True(await unit.ReplaceWrapAsync(committed, to, [9], [9], [9]));
        Assert.Empty(await unit.ItemsUnderEpochAsync(Tenant, from, 5));
        Assert.Single(await unit.ItemsUnderEpochAsync(Tenant, to, 5));
        Assert.Equal(from, (await memory.FindItemAsync(new(Tenant, committed.ContentId)))!.EpochId); // other readers still see it committed
    }

    [Fact]
    public async Task Clause_3_an_integrity_failure_in_a_rewrap_names_the_committed_owner_or_a_blank_one_for_a_staged_item()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var bad = await Commit(store, "bad"u8.ToArray());
        await store.CorruptWrapTagAsync(Tenant, bad.ContentId);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(async () => await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("rewrap")));
        Assert.Equal(new ContentOwner("record-1", "0"), failure.Owner);

        var second = new RecordingStorage();
        var other = Store(second);
        var stage = await other.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        var tag = second.Memory.Snapshot().Staged.Single().WrapTag;
        tag[0] ^= 1;
        var turned = await other.RotateEpochAsync(Tenant, Write("rotate"));
        var stagedFailure = await Assert.ThrowsAsync<ContentIntegrityException>(async () => await other.RewrapEpochAsync(Tenant, turned.From!.Value, turned.To, 5, Write("rewrap")));
        Assert.Equal(stage, stagedFailure.ItemKey);
        Assert.Equal(new ContentOwner("", ""), stagedFailure.Owner);
    }

    [Fact]
    public async Task Clause_4_the_current_epoch_is_refused_even_when_no_item_names_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var refused = await Assert.ThrowsAsync<ContentEpochInUseException>(async () => await store.DestroyEpochAsync(Tenant, rotation.To, Write("destroy")));
        Assert.Equal(rotation.To, refused.EpochId);
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, rotation.To));
    }

    [Fact]
    public async Task The_store_states_why_a_rewrap_or_a_joined_key_write_is_refused()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var same = await Assert.ThrowsAsync<ArgumentException>(async () => await store.RewrapEpochAsync(Tenant, rotation.To, rotation.To, 5, Write("same")));
        Assert.StartsWith("A rewrap moves items to a different epoch.", same.Message, StringComparison.Ordinal);
        await using var orphan = await backing.Bookkeeping.BeginTransactionAsync();
        var joined = await Assert.ThrowsAsync<ArgumentException>(async () => await store.RotateEpochAsync(Tenant, Write("joined", Scheduler, orphan)));
        Assert.StartsWith("A key write can't roll back, so it runs in its own transaction.", joined.Message, StringComparison.Ordinal);
        Assert.Equal(ContentId.Algorithm + new string('a', 64), new ContentId(ContentId.Algorithm + new string('a', 64)).ToString());
    }

    [Fact]
    public async Task A_transactions_own_episode_outranks_the_committed_one_it_is_replacing()
    {
        var memory = new InMemoryContentBookkeeping();
        var key = Guid.NewGuid();
        var first = new ContentIntegrityEntry(key, Tenant, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 1, 0, true, "t", "o", "p", new("r", "e"));
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.SetIntegrityAsync(first);
            await seed.CommitAsync();
        }

        await using var unit = await memory.BeginTransactionAsync();
        var second = first with { Count = 2 };
        await unit.SetIntegrityAsync(second);
        Assert.Same(second, await unit.FindIntegrityAsync(Tenant, key));
    }

    [Fact]
    public async Task A_conditional_delete_clears_the_segments_it_held()
    {
        var bytes = new InMemoryContentByteStore();
        var key = Guid.NewGuid();
        var writer = await bytes.OpenWriteAsync(key, DateTimeOffset.UnixEpoch);
        await writer.AppendAsync(new([7], [8]));
        await writer.CompleteAsync(DateTimeOffset.UnixEpoch);
        var live = bytes.LiveSegments(key);
        Assert.True(await bytes.DeleteIfWrittenAtOrBeforeAsync(key, DateTimeOffset.UnixEpoch));
        Assert.Empty(live);
    }

    private static Task<T> OnAnotherRequest<T>(Func<Task<T>> work)
    {
        using (ExecutionContext.SuppressFlow()) return Task.Run(work);
    }

    [Fact]
    public async Task A_destroy_waits_for_a_joined_rewrap_into_that_epoch_and_is_refused_once_the_rewrap_commits()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var first = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        var second = await store.RotateEpochAsync(Tenant, Write("rotate-2")); // first.To is now an old epoch no row names
        var caller = new CallerTransaction(backing.Bookkeeping);
        Task<bool>? destroy = null;

        await caller.RunAsync(async session =>
        {
            Assert.Equal(1, await store.RewrapEpochAsync(Tenant, first.From!.Value, first.To, 5, Write("joined", Scheduler, session)));
            destroy = OnAnotherRequest(async () => await store.DestroyEpochAsync(Tenant, first.To, Write("destroy")));
            await Task.Delay(100);
            Assert.False(destroy.IsCompleted); // the rewrap's pin holds it
        });

        await Assert.ThrowsAsync<ContentEpochInUseException>(async () => await destroy!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, first.To));
        Assert.Equal(first.To, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
        Assert.NotEqual(first.To, second.To);
    }

    [Fact]
    public async Task A_destroy_goes_ahead_once_a_joined_rewrap_into_that_epoch_rolls_back_and_the_items_keep_their_old_epoch()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var first = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        await store.RotateEpochAsync(Tenant, Write("rotate-2"));
        var caller = new CallerTransaction(backing.Bookkeeping);
        Task<bool>? destroy = null;

        await Assert.ThrowsAsync<InjectedFault>(async () => await caller.RunAsync(async session =>
        {
            await store.RewrapEpochAsync(Tenant, first.From!.Value, first.To, 5, Write("joined", Scheduler, session));
            destroy = OnAnotherRequest(async () => await store.DestroyEpochAsync(Tenant, first.To, Write("destroy")));
            await Task.Delay(100);
            throw new InjectedFault();
        }));

        Assert.True(await destroy!.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(first.From, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task A_rewrap_into_a_retired_epoch_is_refused_and_moves_nothing()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var first = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        await store.RotateEpochAsync(Tenant, Write("rotate-2"));
        backing.OnEpochStep = current => { if (current == "before-destroy") throw new Crash(); };
        await Assert.ThrowsAsync<Crash>(async () => await store.DestroyEpochAsync(Tenant, first.To, Write("retires-then-crashes")));
        backing.OnEpochStep = null;
        Assert.NotNull(await backing.Epochs.GetAsync(Tenant, first.To)); // the key survived, but its retirement committed

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(async () => await store.RewrapEpochAsync(Tenant, first.From!.Value, first.To, 5, Write("rewrap")));

        Assert.Equal("That epoch is being destroyed.", refused.Message);
        Assert.Equal(first.From, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.True(await store.DestroyEpochAsync(Tenant, first.To, Write("retried"))); // the retirement is idempotent
    }

    [Fact]
    public async Task A_put_that_read_an_epoch_destroyed_before_it_could_pin_it_wraps_under_the_current_one_instead()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var seeded = await Commit(store, World);
        var first = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        await store.RewrapEpochAsync(Tenant, first.From!.Value, first.To, 5, Write("rewrap-1"));
        Guid? current = null;
        backing.BeforePin = async () => current = await OnAnotherRequest(async () =>
        {
            var next = await store.RotateEpochAsync(Tenant, Write("rotate-2"));
            await store.RewrapEpochAsync(Tenant, first.To, next.To, 5, Write("rewrap-2"));
            await store.DestroyEpochAsync(Tenant, first.To, Write("destroy"));
            return next.To;
        });

        var reference = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null), new("record-2", "0"), "b.txt");

        Assert.Equal(current, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
        Assert.Equal(World, await Read(store, seeded));
    }

    [Fact]
    public async Task A_rewrap_of_a_staged_item_is_not_undone_by_a_reference_commit_of_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var rewrap = new CallerTransaction(backing.Bookkeeping);
        Task<ContentReference>? commit = null;

        await rewrap.RunAsync(async session =>
        {
            Assert.Equal(1, await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("joined", Scheduler, session)));
            commit = OnAnotherRequest(async () => await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt", Write("commit", ContentStoreWriteEvidence.Actor)));
            await Task.Delay(100);
            Assert.False(commit.IsCompleted); // the staged row is held by the rewrap
        });

        var reference = await commit!.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
        Assert.True(await store.DestroyEpochAsync(Tenant, rotation.From!.Value, Write("destroy")));
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task The_in_memory_backing_orders_pins_and_retirement()
    {
        var memory = new InMemoryContentBookkeeping();
        var epoch = Guid.NewGuid();

        var pinned = await memory.BeginTransactionAsync();
        Assert.True(await pinned.PinEpochAsync(Tenant, epoch));
        var alsoPinned = await memory.BeginTransactionAsync();
        Assert.True(await alsoPinned.PinEpochAsync(Tenant, epoch)); // shared
        var retiring = await memory.BeginTransactionAsync();
        var retire = Task.Run(async () => await retiring.RetireEpochIfUnnamedAsync(Tenant, epoch));
        await Task.Delay(50);
        Assert.False(retire.IsCompleted);
        await pinned.RollbackAsync();
        await Task.Delay(50);
        Assert.False(retire.IsCompleted); // still one pin
        await alsoPinned.RollbackAsync();
        Assert.True(await retire.WaitAsync(TimeSpan.FromSeconds(30)));

        var late = await memory.BeginTransactionAsync();
        var waiting = Task.Run(async () => await late.PinEpochAsync(Tenant, epoch));
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted); // an open retirement holds it
        await retiring.RollbackAsync();
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(30))); // rolled back, so it isn't retired
        await late.RollbackAsync();

        await using (var commit = await memory.BeginTransactionAsync())
        {
            Assert.True(await commit.PinEpochAsync(Tenant, epoch)); // its own pin doesn't hold it up
            Assert.True(await commit.RetireEpochIfUnnamedAsync(Tenant, epoch));
            Assert.True(await commit.RetireEpochIfUnnamedAsync(Tenant, epoch));
            Assert.True(await commit.PinEpochAsync(Tenant, epoch)); // nor does its own retirement
            await commit.CommitAsync();
        }

        await using var after = await memory.BeginTransactionAsync();
        Assert.False(await after.PinEpochAsync(Tenant, epoch));
        Assert.True(await after.PinEpochAsync(Other, epoch)); // another tenant's epoch is separate
        Assert.True(await after.RetireEpochIfUnnamedAsync(Tenant, epoch)); // a retry after a crash
    }

    [Fact]
    public async Task A_waiting_pin_or_retirement_can_be_cancelled()
    {
        var memory = new InMemoryContentBookkeeping();
        var epoch = Guid.NewGuid();
        await using var holder = await memory.BeginTransactionAsync();
        Assert.True(await holder.PinEpochAsync(Tenant, epoch));
        await using var retiring = await memory.BeginTransactionAsync();
        using var cancelRetire = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await retiring.RetireEpochIfUnnamedAsync(Tenant, epoch, cancelRetire.Token));
        await holder.RollbackAsync();
        Assert.True(await retiring.RetireEpochIfUnnamedAsync(Tenant, epoch));
        await using var pinning = await memory.BeginTransactionAsync();
        using var cancelPin = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pinning.PinEpochAsync(Tenant, epoch, cancelPin.Token));
    }

    [Fact]
    public async Task A_read_that_took_its_row_just_before_a_rotation_finished_reads_the_new_wrap_and_reports_no_integrity_failure()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var rotated = Guid.Empty;
        backing.BeforeEpochGet = async () =>
        {
            var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
            await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("rewrap"));
            await store.DestroyEpochAsync(Tenant, rotation.From!.Value, Write("destroy"));
            rotated = rotation.To;
        };

        Assert.Equal(Hello, await Read(store, reference));

        Assert.NotEqual(Guid.Empty, rotated);
        Assert.Empty(store.IntegrityEntries);
        Assert.True(store.IsIntegrityHealthy);
    }

    [Fact]
    public async Task A_read_racing_two_rotations_follows_the_row_until_it_finds_a_key()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        async Task Turn(string name)
        {
            var rotation = await store.RotateEpochAsync(Tenant, Write("rotate-" + name));
            await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("rewrap-" + name));
            await store.DestroyEpochAsync(Tenant, rotation.From!.Value, Write("destroy-" + name));
        }

        backing.BeforeEpochGet = async () =>
        {
            await Turn("first");
            backing.BeforeEpochGet = () => Turn("second");
        };

        Assert.Equal(Hello, await Read(store, reference));
        Assert.Empty(store.IntegrityEntries);
    }

    [Fact]
    public async Task A_read_that_loses_its_authorization_while_following_the_row_is_refused_as_not_found()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var epoch = (await store.StorageItemAsync(Tenant, reference.ContentId)).EpochId;
        backing.BeforeEpochGet = async () =>
        {
            await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose");
            await backing.MemoryEpochs.DestroyAsync(Tenant, epoch);
        };

        await Assert.ThrowsAsync<ContentNotFoundException>(() => Read(store, reference));
        Assert.Empty(store.IntegrityEntries);
    }

    [Fact]
    public async Task A_put_doesnt_open_an_epoch_key_until_it_holds_a_pin_on_it()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var seeded = await Commit(store, World);
        var first = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        await store.RewrapEpochAsync(Tenant, first.From!.Value, first.To, 5, Write("rewrap-1"));
        var stale = (await backing.Epochs.GetCurrentAsync(Tenant))!;
        backing.BeforePin = async () => await OnAnotherRequest(async () =>
        {
            var next = await store.RotateEpochAsync(Tenant, Write("rotate-2"));
            await store.RewrapEpochAsync(Tenant, first.To, next.To, 5, Write("rewrap-2"));
            await store.DestroyEpochAsync(Tenant, first.To, Write("destroy"));
            return 0;
        });

        var reference = await store.CommitReferenceAsync(Tenant, await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null), new("record-2", "0"), "b.txt");

        Assert.Equal(new byte[32], stale.WrappedKey); // the key it first looked at was destroyed under it, and it never tried to open that
        Assert.Equal(Hello, await Read(store, reference));
        Assert.Equal(World, await Read(store, seeded));
    }

    [Fact]
    public async Task A_rewrap_whose_whole_batch_moved_concurrently_takes_the_next_batch_and_zero_still_means_drained()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var a = await Commit(store, "aaa"u8.ToArray());
        var b = await Commit(store, "bbb"u8.ToArray());
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        backing.AfterEpochListing = async () => await OnAnotherRequest(async () =>
            await store.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("rival")));

        // The rival takes this call's one row and commits, so this call finds it moved and the other row is still there.
        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("mine")));

        Assert.Equal(0, await store.RewrapEpochAsync(Tenant, from, rotation.To, 1, Write("drained")));
        Assert.True(await store.DestroyEpochAsync(Tenant, from, Write("destroy")));
        Assert.Equal("aaa"u8.ToArray(), await Read(store, a));
        Assert.Equal("bbb"u8.ToArray(), await Read(store, b));
    }

    [Fact]
    public async Task A_rewrap_that_finds_its_rows_all_gone_returns_zero_and_writes_no_audit_row()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        backing.AfterEpochListing = async () => await OnAnotherRequest(async () =>
            await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("rival")));

        Assert.Equal(0, await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("mine")));

        Assert.DoesNotContain(backing.Memory.Snapshot().Audits, audit => audit.AuditId == "audit-mine");
    }

    [Fact]
    public async Task A_rewrap_skips_a_row_reclaimed_after_its_listing_rather_than_reporting_the_zeroed_wrap_as_corruption()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        backing.AfterEpochListing = async () => await OnAnotherRequest(async () =>
            await store.RemoveReferenceAsync(Tenant, reference, HoldStatus.None, "dispose"));

        Assert.Equal(0, await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("mine")));

        Assert.DoesNotContain(backing.Memory.Snapshot().Audits, audit => audit.AuditId == "audit-mine");
    }

    [Fact]
    public async Task A_rewrap_follows_a_staged_row_promoted_after_its_listing_and_moves_the_committed_item()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var stage = await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        ContentReference? reference = null;
        backing.AfterEpochListing = async () => reference = await OnAnotherRequest(async () =>
            await store.CommitReferenceAsync(Tenant, stage, new("record-1", "0"), "a.txt"));

        Assert.Equal(1, await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("mine")));

        Assert.Equal(rotation.To, (await store.StorageItemAsync(Tenant, reference!.ContentId)).EpochId);
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task A_rewrap_and_a_promotion_of_the_same_content_lock_in_one_order_so_neither_waits_on_the_other()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var stage = await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        await using var promotion = await backing.Bookkeeping.BeginTransactionAsync();
        Assert.NotNull(await promotion.FindStagedForUpdateAsync(stage)); // a promotion's first lock

        var rewrap = OnAnotherRequest(async () => await store.RewrapEpochAsync(Tenant, rotation.From!.Value, rotation.To, 5, Write("rewrap")));
        await Task.Delay(100);
        Assert.False(rewrap.IsCompleted);

        // A promotion's second lock. A rewrap that took the committed row first would hold it here while it waits for the staged row.
        Assert.NotNull(await promotion.FindItemForUpdateAsync(new(Tenant, reference.ContentId)).AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        await promotion.RollbackAsync();

        Assert.Equal(2, await rewrap.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task The_in_memory_backing_lists_an_epochs_staged_entries_before_its_committed_ones()
    {
        var memory = new InMemoryContentBookkeeping();
        var epoch = Guid.NewGuid();
        ContentStoreItem Row(char digit, ContentLifecycleState state) =>
            new(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string(digit, 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, epoch) { State = state };
        var (seededItem, seededStage, ownItem, ownStage) = (Row('a', ContentLifecycleState.Committed), Row('b', ContentLifecycleState.Staged), Row('c', ContentLifecycleState.Committed), Row('d', ContentLifecycleState.Staged));
        await using (var seed = await memory.BeginTransactionAsync())
        {
            await seed.TryCreateItemAsync(seededItem);
            await seed.AddStagedAsync(seededStage);
            await seed.CommitAsync();
        }

        await using var unit = await memory.BeginTransactionAsync();
        await unit.TryCreateItemAsync(ownItem);
        await unit.AddStagedAsync(ownStage);

        Assert.Equal([seededStage.StageId, ownStage.StageId, seededItem.StageId, ownItem.StageId], (await unit.ItemsUnderEpochAsync(Tenant, epoch, 5)).Select(row => row.StageId));
        Assert.Equal([seededStage.StageId], (await unit.ItemsUnderEpochAsync(Tenant, epoch, 1)).Select(row => row.StageId));
    }

    [Fact]
    public async Task The_in_memory_backing_refuses_an_entry_whose_state_does_not_match_its_table()
    {
        var memory = new InMemoryContentBookkeeping();
        var epoch = Guid.NewGuid();
        var staged = new ContentStoreItem(Guid.NewGuid(), Tenant, new(ContentId.Algorithm + new string('e', 64)), 5, "text/plain", DateTimeOffset.UnixEpoch, [1], [2], [3], [4], 65_536, epoch);
        await using var unit = await memory.BeginTransactionAsync();

        var create = await Assert.ThrowsAsync<ArgumentException>(async () => await unit.TryCreateItemAsync(staged));
        var stage = await Assert.ThrowsAsync<ArgumentException>(async () => await unit.AddStagedAsync(staged with { State = ContentLifecycleState.Committed }));

        Assert.StartsWith("Only a committed item can be created.", create.Message);
        Assert.StartsWith("Only a staged item can be staged.", stage.Message);
        Assert.Empty(await unit.ItemsUnderEpochAsync(Tenant, epoch, 5));
    }

    [Fact]
    public async Task A_put_after_the_current_key_was_destroyed_starts_a_new_epoch()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        await store.PutAsync(Tenant, new MemoryStream(Hello), "text/plain", null);
        var destroyed = (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId;
        await backing.Epochs.DestroyAsync(Tenant, destroyed);

        await OnAnotherRequest(async () => await store.PutAsync(Tenant, new MemoryStream("fresh"u8.ToArray()), "text/plain", null)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotEqual(destroyed, (await backing.Epochs.GetCurrentAsync(Tenant))!.EpochId);
    }

    [Fact]
    public async Task A_rewrap_whose_source_was_retired_before_it_could_pin_it_returns_zero_without_touching_the_key()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var reference = await Commit(store, Hello);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate"));
        var from = rotation.From!.Value;
        backing.BeforePin = async () => await OnAnotherRequest(async () =>
        {
            await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("rival"));
            await store.DestroyEpochAsync(Tenant, from, Write("destroy"));
            return 0;
        });

        Assert.Equal(0, await store.RewrapEpochAsync(Tenant, from, rotation.To, 5, Write("mine")));

        Assert.Null(await backing.Epochs.GetAsync(Tenant, from));
        Assert.Equal(Hello, await Read(store, reference));
    }

    [Fact]
    public async Task A_destroy_waits_for_a_rewrap_reading_that_source_epoch()
    {
        var backing = new RecordingStorage();
        var store = Store(backing);
        var rotation = await store.RotateEpochAsync(Tenant, Write("rotate-1"));
        var turned = await store.RotateEpochAsync(Tenant, Write("rotate-2"));
        var caller = new CallerTransaction(backing.Bookkeeping);
        Task<bool>? destroy = null;

        await caller.RunAsync(async session =>
        {
            Assert.Equal(0, await store.RewrapEpochAsync(Tenant, rotation.To, turned.To, 5, Write("joined", Scheduler, session))); // nothing to move, but its pin stands
            destroy = OnAnotherRequest(async () => await store.DestroyEpochAsync(Tenant, rotation.To, Write("destroy")));
            await Task.Delay(100);
            Assert.False(destroy.IsCompleted);
        });

        Assert.True(await destroy!.WaitAsync(TimeSpan.FromSeconds(30)));
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
