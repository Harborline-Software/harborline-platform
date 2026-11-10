using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.ContentStore;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class AttachmentWriteTests
{
    [Fact]
    public async Task Pack_limit_above_store_installs_with_named_warning_and_unchanged_limit_and_put_uses_store_limit()
    {
        var fixture = await Fixture.Create();
        var head = (await fixture.Records.GetPublishedHeadAsync("tenant-a", "eam.evidence"))!;
        var entry = new RecordTypeDefinitionPackageEntry("eam.evidence", "1.0.0", PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(head)));
        var installed = await fixture.Records.AdmitInstallAsync("tenant-a", entry, new([], []));
        Assert.Empty(installed.Refusals);
        var warning = Assert.Single(installed.Warnings);
        Assert.Equal("content.field-limit-above-store", warning.Code);
        Assert.Equal("evidence", warning.FieldKey);
        Assert.Equal(200, warning.DeclaredLimitBytes);
        Assert.Equal(100, warning.StoreLimitBytes);
        Assert.Equal("200", head.Fields[0].Binding!.Kind.Parameters["item_limit_bytes"]);
        var error = await Assert.ThrowsAsync<ContentRefusedException>(async () => await fixture.Upload(new string('a', 101)));
        Assert.Equal("content.too-large", error.Code);
        Assert.Equal(100, error.Detail["limit"]);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Fact]
    public async Task Records_promotes_stages_in_order_and_read_schema_refuses_staged_entries()
    {
        var fixture = await Fixture.Create();
        var first = await fixture.Upload("first");
        var second = await fixture.Upload("second");
        var write = await fixture.Write("create", List(first, second));
        Assert.True(write.Committed, write.Refusal?.Code);
        using var row = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        var entries = row.RootElement.GetProperty("evidence");
        Assert.Equal(["first.txt", "second.txt"], entries.EnumerateArray().Select(item => item.GetProperty("display_name").GetString()));
        Assert.All(entries.EnumerateArray(), item => { Assert.False(item.TryGetProperty("stage_id", out _)); Assert.Equal(write.Receipt!.RecordId, item.GetProperty("owner").GetProperty("record").GetString()); });
        Assert.Equal(2, fixture.Store.CommittedCount);
        var publication = (await fixture.Records.ResolvePublishedSchemaAsync("tenant-a", "eam.evidence"))!;
        Assert.True((await fixture.Registry.ValidateAsync(publication.Schema.Id, Assert.Single(fixture.Port.Rows).Body)).IsValid);
        Assert.False((await fixture.Registry.ValidateAsync(publication.Schema.Id, Encoding.UTF8.GetBytes(List(first).ToJsonString()))).IsValid);
        var refused = await fixture.Write("second-write", List(first));
        Assert.False(refused.Committed);
        Assert.Equal("content.staging-expired", refused.Refusal!.Code);
        Assert.Single(fixture.Port.Rows);
    }

    [Fact]
    public async Task A_refused_later_commit_rolls_back_earlier_reference_and_the_record()
    {
        var fixture = await Fixture.Create();
        var good = await fixture.Upload("first");
        var refused = await fixture.Write("refused", List(good, Guid.Parse("11111111-1111-1111-1111-111111111111")));
        Assert.Equal("content.staging-expired", refused.Refusal!.Code);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
        Assert.True((await fixture.Write("retry", List(good))).Committed);
    }

    [Theory]
    [InlineData("open", false, true)]
    [InlineData("closed", false, false)]
    [InlineData("closed", true, true)]
    public async Task Evidence_condition_requires_an_attachment_exactly_when_true(string status, bool attach, bool expected)
    {
        var fixture = await Fixture.Create(condition: true);
        var body = attach ? List(await fixture.Upload("evidence")) : new JsonObject { ["evidence"] = new JsonArray() };
        body["status"] = status;
        var outcome = await fixture.Write("condition", body);
        Assert.Equal(expected, outcome.Committed);
        if (!expected) { Assert.Equal("field.evidence_required", outcome.Refusal!.Code); Assert.Empty(fixture.Port.Rows); Assert.Equal(0, fixture.Store.CommittedCount); }
    }

    [Fact]
    public async Task Kept_and_new_entries_round_trip_in_order_through_one_enclosing_write()
    {
        var fixture = await Fixture.Create();
        var first = await fixture.Upload("first");
        var created = await fixture.Write("create", List(first));
        using var saved = JsonDocument.Parse(Assert.Single(fixture.Port.Rows).Body);
        var kept = saved.RootElement.GetProperty("evidence")[0].Clone();
        var next = await fixture.Upload("second");
        var mixed = JsonSerializer.SerializeToElement(new JsonArray(JsonNode.Parse(kept.GetRawText()), Stage(next, "new.txt")));
        // The host boundary models a subsequent write of the same record, with the existing reference kept verbatim.
        JsonArray? resolved = null;
        var port = new ArrayPort(fixture.Storage.Bookkeeping);
        await KernelTransactionBoundary.ExecutePreparedAsync(async token =>
        {
            resolved = await fixture.Runtime.ResolveWriteAsync(fixture.Attachment, mixed, new("tenant-a"), new(created.Receipt!.RecordId, "evidence"),
                Evidence(port.Session), token);
            return new KernelCommand<JsonArray>(new("mixed", "mixed", "literal-fingerprint"), resolved, Evidence().Audit);
        }, port);
        var read = Assert.IsType<JsonArray>(port.Saved);
        Assert.Equal(kept.GetRawText(), JsonSerializer.SerializeToElement(read[0]).GetRawText());
        Assert.Equal(["first.txt", "new.txt"], read.Select(item => item!["display_name"]!.GetValue<string>()));
        Assert.All(read, item => Assert.Null(item!["stage_id"]));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Records_refuses_missing_attachment_runtime_or_transaction_instead_of_committing_separately(bool runtime, bool session)
    {
        var fixture = await Fixture.Create(composeRuntime: runtime, exposeSession: session);
        var stage = await fixture.Upload("evidence");
        var outcome = await fixture.Write("uncomposed", List(stage));
        Assert.False(outcome.Committed);
        Assert.Equal("bind", outcome.Refusal!.Stage);
        Assert.Equal("content.transaction-required", outcome.Refusal.Code);
        Assert.Equal(["/evidence"], outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
        Assert.Equal(1, fixture.Store.StagedCount);
    }

    private static JsonObject List(params Guid[] stages) => new() { ["evidence"] = new JsonArray(stages.Select((stage, index) => (JsonNode)Stage(stage, index == 0 ? "first.txt" : "second.txt")).ToArray()) };
    private static JsonObject Stage(Guid stage, string name) => new() { ["stage_id"] = stage.ToString(), ["display_name"] = name };
    private static ContentStoreWrite Evidence(IContentBookkeepingSession? session = null) => new(new(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "literal-fingerprint"), new(Guid.NewGuid().ToString("N"), "operator-7", DateTimeOffset.UnixEpoch, ReadOnlyMemory<byte>.Empty), session);

    private sealed class Fixture
    {
        public InMemoryContentStoreStorage Storage { get; } = new();
        public InMemoryContentStore Store { get; }
        public AttachmentFieldRuntime Runtime { get; }
        public ICompiledAttachmentFieldKind Attachment { get; }
        public RecordTypeDefinitionStore Records { get; }
        public InMemorySchemaRegistry Registry { get; }
        public Port Port { get; }
        public RecordWriteOperation Operation { get; }
        private Fixture(bool condition, bool composeRuntime, bool exposeSession)
        {
            Store = new(new(100, 1000, 10000, TimeSpan.FromHours(1)), new Keys(), TimeProvider.System, Storage);
            Runtime = new(Store);
            var kinds = new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), Store.ItemLimitBytes);
            var kind = new FieldKindReference("attachment", "1.0.0", new Dictionary<string, string> { ["item_limit_bytes"] = "200" });
            Attachment = (ICompiledAttachmentFieldKind)kinds.Bind(kind, "/kind");
            var validator = new RecordsIntentValidator();
            var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
            Registry = new(fieldKindRuntime: kinds);
            var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            { [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator), [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window) });
            var classes = new ClassDefinitionStore(catalogue);
            classes.CreateDraftAsync(new("tenant-a", "eam", "Equipment", "1.0.0", new(1, 0), "eam-core"), "class").AsTask().GetAwaiter().GetResult();
            classes.PublishAsync("tenant-a", "eam.equipment", "1.0.0", 1, "class-publish").AsTask().GetAwaiter().GetResult();
            Records = new(catalogue, compiler, new(kinds), Registry, Window, roles: CreationRoles);
            var field = new FieldDefinition("evidence", "Evidence", new(kind, new(false, 0, null, [], null)))
            { RequiredCondition = condition ? JsonSerializer.Deserialize<JsonElement>("""{"==":[{"var":"status"},"closed"]}""") : null };
            Records.CreateDraftAsync(new("tenant-a", "eam", "Evidence", "1.0.0", [field, new("status", "Status")], new(1, 0),
                ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core", CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author"), "type").AsTask().GetAwaiter().GetResult();
            Records.PublishAsync("tenant-a", "eam.evidence", "1.0.0", 1, "publish").AsTask().GetAwaiter().GetResult();
            Port = new(Storage.Bookkeeping) { ExposeSession = exposeSession };
            Operation = new(new AccessProvider(new Gate()), Records, Registry, Port, Port, TimeProvider.System, kinds, RecordWriteFieldRuntimeTests.FixtureDomains(TimeProvider.System)) { Attachments = composeRuntime ? Runtime : null };
        }
        public static Task<Fixture> Create(bool condition = false, bool composeRuntime = true, bool exposeSession = true) => Task.FromResult(new Fixture(condition, composeRuntime, exposeSession));
        public ValueTask<Guid> Upload(string text) => Runtime.PutAsync(Attachment, new("tenant-a"), "evidence", new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence());
        public ValueTask<RecordWriteOutcome> Write(string operation, JsonObject body) => Operation.CreateAsync(new("tenant-a", "operator-7"), new("eam.evidence", operation, body));
    }
    private sealed class Keys : ITenantContentKeyProvider { public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes("test:" + tenant.Value)); }
    private sealed class Gate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "grant:writer", [], []));
    }
    private sealed class Port(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        public bool ExposeSession { get; init; } = true;
        public List<RecordWriteRow> Rows { get; } = [];
        private readonly Dictionary<string, RecordWriteCommittedOperation> receipts = new(StringComparer.Ordinal);
        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default) => ValueTask.FromResult(receipts.GetValueOrDefault(key.IdempotencyKey));
        public async ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            var transaction = new Transaction(this, await bookkeeping.BeginTransactionAsync(cancellationToken));
            return ExposeSession ? transaction : new WithoutContent(transaction);
        }
        private sealed class WithoutContent(IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit> inner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => inner.StageOperationAsync(operation, cancellationToken);
            public ValueTask StageRecordAsync(RecordWriteRow row, CancellationToken cancellationToken = default) => inner.StageRecordAsync(row, cancellationToken);
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => inner.StageAuditAsync(audit, cancellationToken);
            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
        private sealed class Transaction(Port owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>, IRecordContentTransaction
        {
            public IContentBookkeepingSession ContentSession => content;
            private RecordWriteRow row = null!;
            private KernelOperationIdentity operation = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity value, CancellationToken cancellationToken = default) { operation = value; return ((IContentBookkeepingSession)content).StageOperationAsync(value, cancellationToken); }
            public ValueTask StageRecordAsync(RecordWriteRow value, CancellationToken cancellationToken = default) { row = value; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ((IContentBookkeepingSession)content).StageAuditAsync(audit, cancellationToken);
            public async ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.RecordedAt);
                await content.CommitAsync(cancellationToken);
                owner.Rows.Add(row); owner.receipts.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                return new(RecordWriteCommitDisposition.Created, receipt);
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => content.RollbackAsync(cancellationToken);
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
    }
    private sealed class ArrayPort(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<JsonArray, bool>
    {
        public IContentBookkeepingSession Session { get; private set; } = null!;
        public JsonArray? Saved { get; private set; }
        public async ValueTask<IKernelPreparedTransaction<JsonArray, bool>> BeginAsync(CancellationToken cancellationToken = default)
        { var transaction = await bookkeeping.BeginTransactionAsync(cancellationToken); Session = transaction; return new Transaction(this, transaction); }
        private sealed class Transaction(ArrayPort owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<JsonArray, bool>
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
