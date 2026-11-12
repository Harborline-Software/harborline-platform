using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.ContentStore;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class AttachmentFieldRuntimeTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":\" \"}")]
    [InlineData("{\"content_id\":\"bad\"}")]
    public async Task Malformed_entry_after_duplicate_ids_takes_no_row_locks(string malformed)
    {
        var f = new Fixture();
        var stage = await f.Upload("one");
        var kept = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("two"), display_name = "two.txt" } }));
        var valid = JsonSerializer.SerializeToElement(new[] { new { stage_id = stage, display_name = "one.txt" } });
        await using var transaction = await f.Storage.Bookkeeping.BeginTransactionAsync();
        var session = DispatchProxy.Create<IContentBookkeepingSession, LockCountingSession>();
        var counter = (LockCountingSession)session;
        counter.Inner = transaction;
        var entries = JsonSerializer.SerializeToElement(new JsonArray(
            JsonNode.Parse(valid[0].GetRawText()), JsonNode.Parse(valid[0].GetRawText()),
            kept[0]!.DeepClone(), kept[0]!.DeepClone(), JsonNode.Parse(malformed)));

        var error = await Assert.ThrowsAsync<FieldAdmissionException>(async () =>
            await f.Runtime.LockEntriesAsync([valid, entries], new("tenant"), Evidence(session)));
        Assert.Equal("field.value_type_mismatch", Assert.Single(error.Refusals).Code);
        Assert.Equal(0, counter.Locks);
    }

    [Fact]
    public async Task Duplicate_ids_across_lists_lock_each_row_once()
    {
        var f = new Fixture();
        var stage = await f.Upload("one");
        var entries = JsonSerializer.SerializeToElement(new[] {
            new { stage_id = stage, display_name = "one.txt" },
            new { stage_id = stage, display_name = "again.txt" } });
        await using var transaction = await f.Storage.Bookkeeping.BeginTransactionAsync();
        var session = DispatchProxy.Create<IContentBookkeepingSession, LockCountingSession>();
        var counter = (LockCountingSession)session;
        counter.Inner = transaction;
        await f.Runtime.LockEntriesAsync([entries, entries], new("tenant"), Evidence(session));
        Assert.Equal(2, counter.Locks); // One stage row, then its content row.
    }

    public class LockCountingSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public int Locks { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name is "FindStagedForUpdateAsync" or "FindItemForUpdateAsync") Locks++;
            return targetMethod.Invoke(Inner, args);
        }
    }

    [Fact]
    public async Task Ordered_mixed_write_keeps_reference_metadata_and_put_enforces_the_field_limit()
    {
        var f = new Fixture();
        Assert.Equal(100, f.Runtime.StoreLimitBytes);
        var first = await f.Upload("one");
        var initial = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = first, display_name = "one.txt" } }));
        var second = await f.Upload("two");
        var mixed = JsonSerializer.SerializeToElement(new JsonArray(initial[0]!.DeepClone(), new JsonObject { ["stage_id"] = second.ToString(), ["display_name"] = "two.txt" }));
        var saved = await f.Write(mixed);
        Assert.Equal(initial[0]!.ToJsonString(), saved[0]!.ToJsonString());
        Assert.Equal(["one.txt", "two.txt"], saved.Select(item => item!["display_name"]!.GetValue<string>()));
        Assert.All(saved, item => { Assert.Null(item!["stage_id"]); Assert.Equal("record", item!["owner"]!["record"]!.GetValue<string>()); });
        Assert.Equal(2, f.Store.CommittedCount);
        Assert.Equal(0, f.Store.StagedCount);
        Assert.Equal("hmac-sha256:", saved[0]!["content_id"]!.GetValue<string>()[..12]);
        Assert.Equal(3, saved[0]!["length"]!.GetValue<long>());
        Assert.Equal("text/plain", saved[0]!["media_type"]!.GetValue<string>());
        var refusal = await Assert.ThrowsAsync<ContentRefusedException>(async () => await f.Upload(new string('x', 51)));
        Assert.Equal("content.too-large", refusal.Code);
        Assert.Equal(50, refusal.Detail["limit"]);
    }

    // DES-0054 ck-3, ck-4, ck-6, eng-4 (Terra P2-6, Codex P2; M11 ruling option 1): identical text bytes are stored once,
    // but each upload keeps its own declared text subtype on its own reference, so a CSV-only field accepts a CSV upload of
    // bytes the tenant first stored as text/plain, and each reference is served with its own type. Dedup is never visible.
    [Fact]
    public async Task Content_store_ck_6_identical_text_bytes_keep_each_uploads_own_type_on_its_reference()
    {
        var f = new Fixture();
        var csvOnly = (ICompiledAttachmentFieldKind)new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
            .Bind(new("attachment", "1.0.0", new Dictionary<string, string> { ["accepted_media_types"] = "[\"text/csv\"]" }), "/kind");
        const string bytes = "a,b\n1,2\n";
        var plainStage = await f.Runtime.PutAsync(f.Field, new("tenant"), "notes", new MemoryStream(Encoding.UTF8.GetBytes(bytes)), "text/plain", null, Evidence());
        var notes = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = plainStage, display_name = "notes.txt" } }), "notes");
        var csvStage = await f.Runtime.PutAsync(csvOnly, new("tenant"), "sheet", new MemoryStream(Encoding.UTF8.GetBytes(bytes)), "text/csv", null, Evidence());
        var sheet = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = csvStage, display_name = "sheet.csv" } }), "sheet", csvOnly);

        Assert.Equal(1, f.Store.CommittedCount);
        Assert.Equal(notes[0]!["content_id"]!.GetValue<string>(), sheet[0]!["content_id"]!.GetValue<string>());
        Assert.Equal("text/plain", notes[0]!["media_type"]!.GetValue<string>());
        Assert.Equal("text/csv", sheet[0]!["media_type"]!.GetValue<string>());
        var context = new ContentReadContext("trace", "operation", "tenant-opaque", "reader");
        foreach (var (written, type) in new[] { (notes[0]!, "text/plain"), (sheet[0]!, "text/csv") })
        {
            var reference = new ContentReference(new(written["content_id"]!.GetValue<string>()), written["length"]!.GetValue<long>(), written["media_type"]!.GetValue<string>(),
                written["display_name"]!.GetValue<string>(), new(written["owner"]!["record"]!.GetValue<string>(), written["owner"]!["element"]!.GetValue<string>()));
            var delivery = await f.Store.ReadAsync(new("tenant"), reference, true, context);
            await using var body = delivery.Body;
            Assert.Equal(type, delivery.ContentType);
            Assert.Equal(bytes, await new StreamReader(body).ReadToEndAsync());
        }
        // A kept reference still names its own text subtype; a signature type cannot be relabelled.
        var kept = await f.Write(JsonSerializer.SerializeToElement(new[] { sheet[0] }), "sheet", csvOnly);
        Assert.Equal("text/csv", kept[0]!["media_type"]!.GetValue<string>());
        Assert.False(InMemoryContentStore.MediaTypeFits("application/pdf", "text/plain"));
    }

    [Fact]
    public async Task A_kept_reference_cannot_change_its_prior_media_type_to_fit_a_narrowed_field()
    {
        var f = new Fixture();
        var prior = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }));
        prior[0]!["media_type"] = "text/csv";
        var csvOnly = (ICompiledAttachmentFieldKind)new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
            .Bind(new("attachment", "1.0.0", new Dictionary<string, string> { ["accepted_media_types"] = "[\"text/csv\"]" }), "/kind");
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await f.Write(JsonSerializer.SerializeToElement(prior), field: csvOnly));
        Assert.Equal("content.media-type-mismatch", error.Code);
        Assert.Equal(1, f.Store.CommittedCount);
    }

    [Fact]
    public async Task A_kept_reference_may_change_only_its_display_name()
    {
        var f = new Fixture();
        var prior = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }));
        prior[0]!["display_name"] = "renamed.txt";
        var saved = await f.Write(JsonSerializer.SerializeToElement(prior));
        Assert.Equal("renamed.txt", saved[0]!["display_name"]!.GetValue<string>());
        Assert.Equal("text/plain", saved[0]!["media_type"]!.GetValue<string>());
        Assert.Equal(3, saved[0]!["length"]!.GetValue<long>());
        Assert.Equal(1, f.Store.CommittedCount);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"stage_id\":3,\"display_name\":\"x\"}]")]
    [InlineData("[{\"stage_id\":\"bad\",\"display_name\":\"x\"}]")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\"}]")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":42}]")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":null}]")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":\" \"}]")]
    [InlineData("[{\"stage_id\":\"11111111-1111-1111-1111-111111111111\",\"display_name\":\"x\",\"extra\":true}]")]
    public async Task Malformed_write_entries_refuse_without_changing_the_store(string entries)
    {
        var f = new Fixture();
        var refused = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await f.Write(JsonSerializer.Deserialize<JsonElement>(entries)));
        var refusal = Assert.Single(refused.Refusals);
        Assert.Equal("field.value_type_mismatch", refusal.Code);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
        Assert.True(refusal.JsonPointer.Length == 0 || refusal.JsonPointer.StartsWith('/'));
        Assert.Equal(0, f.Store.CommittedCount);
    }

    [Fact]
    public async Task Refused_later_stage_rolls_back_the_earlier_promotion()
    {
        var f = new Fixture();
        var stage = await f.Upload("one");
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await f.Write(JsonSerializer.SerializeToElement(new[]
        { new { stage_id = stage, display_name = "one.txt" }, new { stage_id = Guid.Parse("11111111-1111-1111-1111-111111111111"), display_name = "missing.txt" } })));
        Assert.Equal("content.staging-expired", error.Code);
        Assert.Equal(0, f.Store.CommittedCount);
        Assert.Equal(1, f.Store.StagedCount);
    }

    [Theory]
    [InlineData("record", "content.staging-expired")]
    [InlineData("element", "content.staging-expired")]
    [InlineData("length", "content.staging-expired")]
    [InlineData("media_type", "content.media-type-mismatch")]
    [InlineData("content_id", "content.staging-expired")]
    public async Task Kept_reference_must_match_the_same_record_field_and_prior_reference(string member, string code)
    {
        var f = new Fixture();
        var saved = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }));
        if (member is "record" or "element") saved[0]!["owner"]![member] = "other";
        else if (member == "length") saved[0]![member] = 4;
        else if (member == "media_type") saved[0]![member] = "image/png";
        else saved[0]![member] = "hmac-sha256:" + new string('f',64);
        var refusal = await Assert.ThrowsAsync<ContentRefusedException>(async () => await f.Write(JsonSerializer.SerializeToElement(saved)));
        Assert.Equal(code, refusal.Code);
        Assert.Equal(1, f.Store.CommittedCount);
    }

    [Fact]
    public async Task A_write_and_a_removal_both_require_an_enclosing_transaction()
    {
        var f = new Fixture();
        var entries = JsonSerializer.Deserialize<JsonElement>("[]");
        var write = await Assert.ThrowsAsync<ArgumentException>(async () => await f.Runtime.ResolveWriteAsync(f.Field, entries, new("tenant"), new("record", "evidence"), Evidence(), default));
        var remove = await Assert.ThrowsAsync<ArgumentException>(async () => await f.Runtime.RemoveAsync(entries, new("tenant"), HoldStatus.None, "purge", Evidence(), default));
        Assert.Contains("transaction", write.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transaction", remove.Message, StringComparison.OrdinalIgnoreCase);
    }

    // content-store-cc-2 (Codex P2 on a6760bba; same ruling as Terra P1-4): two disposals that remove the same two items
    // listed in opposite orders lock their rows in one canonical order, so both finish instead of waiting forever.
    [Fact]
    public async Task Content_store_cc_2_opposite_order_removals_of_shared_content_both_finish()
    {
        var f = new Fixture();
        var a = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("x-bytes"), display_name = "x.txt" }, new { stage_id = await f.Upload("y-bytes"), display_name = "y.txt" } }), "a");
        var b = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("y-bytes"), display_name = "y.txt" }, new { stage_id = await f.Upload("x-bytes"), display_name = "x.txt" } }), "b");
        Assert.Equal(2, f.Store.CommittedCount);
        var first = new RendezvousPort(f.Storage.Bookkeeping);
        var second = new RendezvousPort(f.Storage.Bookkeeping);
        (first.Partner, second.Partner) = (second, first);
        Task<KernelTransactionResult<bool>> Remove(RendezvousPort port, JsonArray references) => KernelTransactionBoundary.ExecutePreparedAsync(async token =>
        {
            await f.Runtime.RemoveAsync(JsonSerializer.SerializeToElement(references), new("tenant"), HoldStatus.None, "record-purge", Evidence(port.Session), token).ConfigureAwait(false);
            return new KernelCommand<JsonArray>(Evidence().Operation, new(), Evidence().Audit);
        }, port).AsTask();
        var results = await Task.WhenAll(Remove(first, a), Remove(second, b)).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, result => Assert.True(result.Committed));
        Assert.Equal(0, f.Store.CommittedCount);
    }

    [Fact]
    public async Task Attachment_promotion_and_epoch_rewrap_lock_stages_in_the_same_order()
    {
        var f = new Fixture();
        var a = await f.Upload("one");
        var b = await f.Upload("two");
        var rotation = await f.Store.RotateEpochAsync(new("tenant"), Evidence());
        var entries = JsonSerializer.SerializeToElement(new[] {
            new { stage_id = a, display_name = "one.txt" },
            new { stage_id = b, display_name = "two.txt" } });
        var writer = new RendezvousPort(f.Storage.Bookkeeping);
        var rewrap = new RendezvousPort(f.Storage.Bookkeeping);
        (writer.Partner, rewrap.Partner) = (rewrap, writer);
        writer.LockMethod = rewrap.LockMethod = "FindStagedForUpdateAsync";
        rewrap.ReverseEpochListing = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var write = KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            new KernelCommand<JsonArray>(Evidence().Operation,
                await f.Runtime.ResolveWriteAsync(f.Field, entries, new("tenant"), new("record", "evidence"), Evidence(writer.Session), default, token),
                Evidence().Audit), writer, timeout.Token).AsTask();
        var move = KernelTransactionBoundary.ExecutePreparedAsync(async token =>
        {
            await f.Store.RewrapEpochAsync(new("tenant"), rotation.From!.Value, rotation.To, 10, Evidence(rewrap.Session), token);
            return new KernelCommand<JsonArray>(Evidence().Operation, new(), Evidence().Audit);
        }, rewrap, timeout.Token).AsTask();

        var results = await Task.WhenAll(write, move).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.All(results, result => Assert.True(result.Committed));
        Assert.Equal(2, f.Store.CommittedCount);
        Assert.Equal(0, f.Store.StagedCount);
        await using var read = await f.Storage.Bookkeeping.BeginTransactionAsync();
        var references = await read.ItemsUnderEpochAsync(new("tenant"), rotation.To, 10);
        Assert.Equal(2, references.Count);
        Assert.All(references, item => Assert.Equal(rotation.To, item.EpochId));
    }

    // Meets its partner after its first selected row lock, so a lock-order inversion deadlocks deterministically; a
    // transaction whose partner is blocked waits out the bound and carries on.
    public class RendezvousSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RendezvousSession Partner { get; set; } = null!;
        public string LockMethod { get; set; } = "FindItemForUpdateAsync";
        public bool ReverseEpochListing { get; set; }
        private int met;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name == "ItemsUnderEpochAsync" && ReverseEpochListing)
                return Reverse((ValueTask<IReadOnlyList<ContentStoreItem>>)result!);
            if (targetMethod.Name != LockMethod || Interlocked.Exchange(ref met, 1) == 1) return result;
            return Meet((ValueTask<ContentStoreItem?>)result!);
        }
        private static async ValueTask<IReadOnlyList<ContentStoreItem>> Reverse(ValueTask<IReadOnlyList<ContentStoreItem>> operation)
            => (await operation.ConfigureAwait(false)).OrderByDescending(item => item.StageId.ToString("N"), StringComparer.Ordinal).ToArray();
        private async ValueTask<ContentStoreItem?> Meet(ValueTask<ContentStoreItem?> operation)
        {
            var item = await operation.ConfigureAwait(false);
            Arrived.TrySetResult();
            try { await Partner.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            return item;
        }
    }

    private sealed class RendezvousPort(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<JsonArray, bool>
    {
        private readonly RendezvousSession proxy = (RendezvousSession)(object)DispatchProxy.Create<IContentBookkeepingSession, RendezvousSession>();
        public RendezvousPort Partner { set => proxy.Partner = value.proxy; }
        public string LockMethod { set => proxy.LockMethod = value; }
        public bool ReverseEpochListing { set => proxy.ReverseEpochListing = value; }
        public IContentBookkeepingSession Session => (IContentBookkeepingSession)(object)proxy;
        public async ValueTask<IKernelPreparedTransaction<JsonArray, bool>> BeginAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken);
            proxy.Inner = transaction;
            return new Transaction(transaction);
        }
        private sealed class Transaction(IContentBookkeepingTransaction content) : IKernelPreparedTransaction<JsonArray, bool>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(JsonArray record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default) { await content.CommitAsync(cancellationToken); return true; }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(HoldStatus.None, 0)]
    [InlineData(HoldStatus.Held, 1)]
    [InlineData(HoldStatus.Unreadable, 1)]
    public async Task Removal_joins_the_transaction_and_holds_keep_the_bytes(HoldStatus hold, int remaining)
    {
        var f = new Fixture();
        var refs = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }));
        var port = new Port(f.Storage.Bookkeeping);
        await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
        {
            await f.Runtime.RemoveAsync(JsonSerializer.SerializeToElement(refs), new("tenant"), hold, "record-purge", Evidence(port.Session), token);
            return new KernelCommand<JsonArray>(Evidence().Operation, new(), Evidence().Audit);
        }, port);
        Assert.Equal(remaining, f.Store.CommittedCount);
        Assert.Equal(remaining == 0 ? 1 : 0, f.Store.Reclamations.Count);
    }

    [Theory]
    [InlineData("[\"text/plain\"]", 51, "content.too-large")]
    [InlineData("[\"application/pdf\"]", 3, "content.field-media-type-not-allowed")]
    public async Task A_stage_uploaded_without_field_narrowing_is_rechecked_at_commit(string acceptedMedia, int size, string code)
    {
        var f = new Fixture();
        var field = (ICompiledAttachmentFieldKind)new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
            .Bind(new("attachment", "1.0.0", new Dictionary<string,string> { ["item_limit_bytes"] = "50", ["accepted_media_types"] = acceptedMedia }), "/kind");
        var stage = await f.Store.PutAsync(new("tenant"), new MemoryStream(Encoding.UTF8.GetBytes(new string('x', size))), "text/plain", null, Evidence());
        var entries = JsonSerializer.SerializeToElement(new[] { new { stage_id = stage, display_name = "evidence.txt" } });
        var port = new Port(f.Storage.Bookkeeping);
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            new KernelCommand<JsonArray>(Evidence().Operation, await f.Runtime.ResolveWriteAsync(field, entries, new("tenant"), new("record", "evidence"), Evidence(port.Session), default, token), Evidence().Audit), port));
        Assert.Equal(code, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Equal("evidence", error.Definition);
        if (code == "content.too-large") { Assert.Equal(50, error.Detail["limit"]); Assert.Equal(51, error.Detail["observed"]); }
        Assert.Equal(0, f.Store.CommittedCount);
        Assert.Equal(1, f.Store.StagedCount);
    }

    [Theory]
    [InlineData("other-record", "evidence/slot")]
    [InlineData("record", "other/slot")]
    [InlineData("record", "evidence_extra/slot")]
    [InlineData("record", "evidence/photos/slot")]
    public async Task A_real_store_owner_from_another_record_or_field_cannot_be_kept(string record, string element)
    {
        var f = new Fixture();
        var reference = await f.Store.CommitReferenceAsync(new("tenant"), await f.Upload("one"), new(record, element), "one.txt", Evidence());
        var entries = JsonSerializer.SerializeToElement(new[] { new
        { content_id = reference.ContentId.Value, length = reference.Length, media_type = reference.MediaType, display_name = reference.DisplayName,
          owner = new { record = reference.Owner.Record, element = reference.Owner.Element } } });
        var refusal = await Assert.ThrowsAsync<ContentRefusedException>(async () => await f.Write(entries));
        Assert.Equal("content.staging-expired", refusal.Code);
        Assert.Equal(1, f.Store.CommittedCount);
    }

    [Fact]
    public async Task A_field_key_containing_a_separator_keeps_its_own_reference()
    {
        var f = new Fixture();
        var first = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }), "evidence/photos");
        var kept = await f.Write(JsonSerializer.SerializeToElement(first), "evidence/photos");
        Assert.Equal(first.ToJsonString(), kept.ToJsonString());
        Assert.Equal(1, f.Store.CommittedCount);
    }

    [Fact]
    public async Task A_cancelled_write_stops_before_examining_its_entries()
    {
        var f = new Fixture(); var port = new Port(f.Storage.Bookkeeping);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            new KernelCommand<JsonArray>(Evidence().Operation,
                await f.Runtime.ResolveWriteAsync(f.Field, JsonSerializer.Deserialize<JsonElement>("[{}]"), new("tenant"), new("record", "evidence"), Evidence(port.Session), default, cancelled.Token),
                Evidence().Audit), port));
        Assert.Equal(0, f.Store.CommittedCount);
    }

    [Theory]
    [InlineData("staged", 2)]
    [InlineData("kept", 1)]
    [InlineData("remove", 0)]
    public async Task Field_operations_do_not_capture_a_callers_context_during_async_store_IO(string operation, int items)
    {
        var f = new Fixture();
        var initial = await f.Write(JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("one"), display_name = "one.txt" } }));
        var entries = operation == "staged"
            ? JsonSerializer.SerializeToElement(new[] { new { stage_id = await f.Upload("two"), display_name = "two.txt" } })
            : JsonSerializer.SerializeToElement(initial);
        var port = new ContextPort(f.Storage.Bookkeeping, operation == "staged" ? "FindStagedForUpdateAsync" : "FindItemForUpdateAsync");
        var context = new CountingContext(); var previous = SynchronizationContext.Current;
        Task<KernelTransactionResult<bool>> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = KernelTransactionBoundary.ExecutePreparedAsync(async token =>
            {
                JsonArray resolved = [];
                if (operation == "remove") await f.Runtime.RemoveAsync(entries, new("tenant"), HoldStatus.None, "record-purge", Evidence(port.Session), token).ConfigureAwait(false);
                else resolved = await f.Runtime.ResolveWriteAsync(f.Field, entries, new("tenant"), new("record", "evidence"), Evidence(port.Session), JsonSerializer.SerializeToElement(initial), token).ConfigureAwait(false);
                return new KernelCommand<JsonArray>(Evidence().Operation, resolved, Evidence().Audit);
            }, port).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(port.Delay.Entered); Assert.False(pending.IsCompleted);
        port.Delay.Release.SetResult();
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(30))).Committed);
        Assert.Equal(0, context.Posts);
        Assert.Equal(items, f.Store.CommittedCount);
    }

    private sealed class CountingContext : SynchronizationContext
    {
        private int posts;
        public int Posts => Volatile.Read(ref posts);
        public override void Post(SendOrPostCallback callback, object? state)
        { Interlocked.Increment(ref posts); ThreadPool.QueueUserWorkItem(_ => callback(state)); }
    }
    public class DelayedSession : DispatchProxy
    {
        public IContentBookkeepingSession Inner { get; set; } = null!;
        public string Method { get; set; } = "";
        public bool Entered { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name != Method) return result;
            Entered = true; return Wait((ValueTask<ContentStoreItem?>)result!);
        }
        private async ValueTask<ContentStoreItem?> Wait(ValueTask<ContentStoreItem?> operation)
        { await Release.Task.ConfigureAwait(false); return await operation.ConfigureAwait(false); }
    }
    private sealed class ContextPort(IContentBookkeeping bookkeeping, string method) : IKernelPreparedTransactionPort<JsonArray, bool>
    {
        public DelayedSession Delay { get; private set; } = null!;
        public IContentBookkeepingSession Session { get; private set; } = null!;
        public async ValueTask<IKernelPreparedTransaction<JsonArray, bool>> BeginAsync(CancellationToken cancellationToken = default)
        {
            var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            Session = DispatchProxy.Create<IContentBookkeepingSession, DelayedSession>();
            Delay = (DelayedSession)Session; Delay.Inner = transaction; Delay.Method = method;
            return new Transaction(transaction);
        }
        private sealed class Transaction(IContentBookkeepingTransaction content) : IKernelPreparedTransaction<JsonArray, bool>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(JsonArray record, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default) => content.CommitAsync(cancellationToken);
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }

    private static ContentStoreWrite Evidence(IContentBookkeepingSession? session = null) =>
        new(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "fingerprint"), new(Guid.NewGuid().ToString("N"), "writer", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), session);
    private sealed class Fixture
    {
        public InMemoryContentStoreStorage Storage { get; } = new();
        public InMemoryContentStore Store { get; }
        public AttachmentFieldRuntime Runtime { get; }
        public ICompiledAttachmentFieldKind Field { get; }
        public Fixture()
        {
            Store = new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, Storage);
            Runtime = new(Store);
            Field = (ICompiledAttachmentFieldKind)new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100)
                .Bind(new("attachment", "1.0.0", new Dictionary<string,string> { ["item_limit_bytes"] = "50" }), "/kind");
        }
        public ValueTask<Guid> Upload(string text) => Runtime.PutAsync(Field, new("tenant"), "evidence", new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence());
        private readonly Dictionary<string, JsonElement> prior = new(StringComparer.Ordinal);
        public async Task<JsonArray> Write(JsonElement entries, string fieldKey = "evidence", ICompiledAttachmentFieldKind? field = null)
        {
            var port = new Port(Storage.Bookkeeping);
            await KernelTransactionBoundary.ExecutePreparedAsync(async token => new KernelCommand<JsonArray>(Evidence().Operation,
                await Runtime.ResolveWriteAsync(field ?? Field, entries, new("tenant"), new("record", fieldKey), Evidence(port.Session), prior.GetValueOrDefault(fieldKey), token), Evidence().Audit), port);
            prior[fieldKey] = JsonSerializer.SerializeToElement(port.Saved!);
            return port.Saved!;
        }
    }
    private sealed class Keys : ITenantContentKeyProvider { public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes(tenant.Value)); }
    private sealed class Port(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<JsonArray, bool>
    {
        public IContentBookkeepingSession Session { get; private set; } = null!;
        public JsonArray? Saved { get; private set; }
        public async ValueTask<IKernelPreparedTransaction<JsonArray, bool>> BeginAsync(CancellationToken cancellationToken = default)
        { var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken); Session = transaction; return new Transaction(this, transaction); }
        private sealed class Transaction(Port owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<JsonArray, bool>
        {
            private JsonArray pending = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(JsonArray record, CancellationToken cancellationToken = default) { pending = record; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default) { await content.CommitAsync(cancellationToken); owner.Saved = pending; return true; }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }
}
