using System.Reflection;
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
        Assert.Equal("/fields/0/binding/kind/parameters/item_limit_bytes", warning.Pointer);
        var publishedWarning = Assert.Single(fixture.Publication.PolicyWarnings, item => item.Code == "content.field-limit-above-store");
        Assert.Equal("/fields/0/binding/kind/parameters/item_limit_bytes", publishedWarning.Pointer);
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
        if (!expected) { Assert.Equal("field.evidence_required", outcome.Refusal!.Code); Assert.Equal("rules", outcome.Refusal.Stage); Assert.Equal(["/evidence"], outcome.Refusal.Pointers); Assert.Empty(fixture.Port.Rows); Assert.Equal(0, fixture.Store.CommittedCount); }
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Concurrent_attachment_retries_settle_the_committed_operation_after_stage_consumption(bool temporal, bool divergent)
    {
        var fixture = await Fixture.Create(temporalMode: temporal);
        var stage = await fixture.Upload("evidence");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Port.BeforeNextLookup = async () => { entered.SetResult(); await release.Task.ConfigureAwait(false); };
        var loserBody = List(stage);
        if (divergent) loserBody["status"] = "different intent";
        var loser = fixture.WriteEither(temporal, "same-operation", loserBody);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var winner = await fixture.WriteEither(temporal, "same-operation", List(stage));
        Assert.True(winner.Committed); Assert.False(winner.Replayed); Assert.Null(winner.Decision);
        release.SetResult();
        var settled = await loser.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(settled.Decision!.Allowed);
        Assert.NotEqual(winner.Receipt!.RecordId, settled.Decision.RecordId);
        if (divergent)
        {
            Assert.False(settled.Committed);
            Assert.Equal("commit", settled.Refusal!.Stage);
            Assert.Equal("records.write.replay_conflict", settled.Refusal.Code);
            Assert.Equal(["/operation_id"], settled.Refusal.Pointers);
        }
        else
        {
            Assert.True(settled.Replayed, settled.Refusal?.Code);
            Assert.Equal(winner.Receipt, settled.Receipt);
        }
        Assert.Single(fixture.Port.Rows);
        Assert.Equal(1, fixture.Port.Rollbacks);
        Assert.Equal(1, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Raced_replay_does_not_capture_a_callers_context_during_receipt_IO(bool temporal)
    {
        var fixture = await Fixture.Create(temporalMode: temporal);
        var stage = await fixture.Upload("evidence");
        var original = await fixture.WriteEither(temporal, "receipt-io", List(stage));
        Assert.True(original.Committed);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = false;
        fixture.Port.HideNextLookup = true;
        fixture.Port.BeforeNextLookup = async () => { entered = true; await release.Task.ConfigureAwait(false); };
        var context = new CountingContext(); var previous = SynchronizationContext.Current;
        Task<RecordWriteOutcome> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = fixture.WriteEither(temporal, "receipt-io", List(stage));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(entered); Assert.False(pending.IsCompleted);
        release.SetResult();
        var replay = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(replay.Replayed, replay.Refusal?.Code);
        Assert.Equal(original.Receipt, replay.Receipt);
        Assert.Equal(0, context.Posts);
        Assert.Equal(1, fixture.Port.Rollbacks);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Temporal_unknown_or_consumed_stages_under_a_new_operation_remain_refusals(bool consumed, int rows)
    {
        var fixture = await Fixture.Create(temporalMode: true);
        var stage = consumed ? await fixture.Upload("evidence") : Guid.Parse("11111111-1111-1111-1111-111111111111");
        if (consumed) Assert.True((await fixture.WriteEither(true, "first-operation", List(stage))).Committed);
        var outcome = await fixture.Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"),
            new("eam.evidence", "new-operation", List(stage), DateTimeOffset.UnixEpoch, null));
        Assert.Equal(RecordTemporalWriteStatus.Refused, outcome.Status);
        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("content.staging-expired", outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.Detail);
        Assert.True(outcome.Decision!.Allowed);
        Assert.Equal(rows, fixture.Port.Rows.Count);
        Assert.Equal(rows, fixture.Store.CommittedCount);
        Assert.Equal(0, fixture.Store.StagedCount);
        Assert.Equal(1, fixture.Port.Rollbacks);
    }

    [Theory]
    [InlineData("evidence", "/evidence")]
    [InlineData("proof/~", "/proof~1~0")]
    public async Task Malformed_entries_become_a_structured_refusal_with_an_escaped_field_pointer(string field, string pointer)
    {
        var fixture = await Fixture.Create(fieldKey: field);
        var outcome = await fixture.Write("malformed", new JsonObject { [field] = new JsonArray(new JsonObject()) });
        Assert.False(outcome.Committed);
        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", outcome.Refusal.Code);
        Assert.Equal([pointer], outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(0, fixture.Store.CommittedCount);
    }

    [Fact]
    public async Task Records_does_not_capture_a_callers_context_while_promoting_a_stage()
    {
        var fixture = await Fixture.Create();
        var stage = await fixture.Upload("evidence");
        var session = DispatchProxy.Create<IContentBookkeepingSession, DelayedSession>();
        var delay = (DelayedSession)session;
        fixture.Port.DecorateSession = inner => { delay.Inner = inner; return session; };
        var context = new CountingContext(); var previous = SynchronizationContext.Current;
        Task<RecordWriteOutcome> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = fixture.Write("context", List(stage)).AsTask();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.True(delay.Entered); Assert.False(pending.IsCompleted);
        delay.Release.SetResult();
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(30))).Committed);
        Assert.Equal(0, context.Posts);
        Assert.Single(fixture.Port.Rows);
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
        public bool Entered { get; private set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var result = targetMethod!.Invoke(Inner, args);
            if (targetMethod.Name != "FindStagedForUpdateAsync") return result;
            Entered = true; return Wait((ValueTask<ContentStoreItem?>)result!);
        }
        private async ValueTask<ContentStoreItem?> Wait(ValueTask<ContentStoreItem?> operation)
        { await Release.Task.ConfigureAwait(false); return await operation.ConfigureAwait(false); }
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
        public RecordTypePublication Publication { get; }
        private Fixture(bool condition, bool composeRuntime, bool exposeSession, string fieldKey, bool temporalMode)
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
            var field = new FieldDefinition(fieldKey, "Evidence", new(kind, new(false, 0, null, [], null)))
            { RequiredCondition = condition ? JsonSerializer.Deserialize<JsonElement>("""{"==":[{"var":"status"},"closed"]}""") : null };
            Records.CreateDraftAsync(new("tenant-a", "eam", "Evidence", "1.0.0", [field, new("status", "Status")], new(1, 0),
                ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core", CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author") { AmendmentPolicy = new(true) { Mode = temporalMode ? RecordAmendmentMode.Supersession : null } }, "type").AsTask().GetAwaiter().GetResult();
            Publication = Records.PublishAsync("tenant-a", "eam.evidence", "1.0.0", 1, "publish").AsTask().GetAwaiter().GetResult();
            Port = new(Storage.Bookkeeping) { ExposeSession = exposeSession };
            Operation = new(new AccessProvider(new Gate()), Records, Registry, Port, Port, TimeProvider.System, kinds, RecordWriteFieldRuntimeTests.FixtureDomains(TimeProvider.System), Port, Port, Port) { Attachments = composeRuntime ? Runtime : null };
        }
        public static Task<Fixture> Create(bool condition = false, bool composeRuntime = true, bool exposeSession = true, string fieldKey = "evidence", bool temporalMode = false) => Task.FromResult(new Fixture(condition, composeRuntime, exposeSession, fieldKey, temporalMode));
        public ValueTask<Guid> Upload(string text) => Runtime.PutAsync(Attachment, new("tenant-a"), "evidence", new MemoryStream(Encoding.UTF8.GetBytes(text)), "text/plain", null, Evidence());
        public async Task<RecordWriteOutcome> WriteEither(bool temporal, string operation, JsonObject body)
        {
            if (!temporal) return await Write(operation, body).ConfigureAwait(false);
            var outcome = await Operation.CreateEffectiveAsync(new("tenant-a", "operator-7"), new("eam.evidence", operation, body, DateTimeOffset.UnixEpoch, null)).ConfigureAwait(false);
            return new(outcome.Receipt?.Receipt, outcome.Status == RecordTemporalWriteStatus.Replayed, outcome.Refusal) { Decision = outcome.Decision };
        }
        public ValueTask<RecordWriteOutcome> Write(string operation, JsonObject body) => Operation.CreateAsync(new("tenant-a", "operator-7"), new("eam.evidence", operation, body));
    }
    private sealed class Keys : ITenantContentKeyProvider { public byte[] GetKey(TenantId tenant) => SHA256.HashData(Encoding.UTF8.GetBytes("test:" + tenant.Value)); }
    private sealed class Gate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "grant:writer", [], []));
    }
    private sealed class Port(IContentBookkeeping bookkeeping) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader,
        IRecordTemporalWritePort, IRecordTemporalReceiptReader, IRecordTemporalReadStore
    {
        public bool ExposeSession { get; init; } = true;
        public Func<IContentBookkeepingSession, IContentBookkeepingSession>? DecorateSession { get; set; }
        public List<RecordWriteRow> Rows { get; } = [];
        public Func<ValueTask>? BeforeNextLookup { get; set; }
        public bool HideNextLookup { get; set; }
        public int Rollbacks { get; private set; }
        public bool HasAuthorityClock => true;
        private readonly Dictionary<string, RecordTemporalCommittedOperation> temporalReceipts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RecordWriteCommittedOperation> receipts = new(StringComparer.Ordinal);
        public async ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        { var prior = receipts.GetValueOrDefault(key.IdempotencyKey); if (HideNextLookup) { HideNextLookup = false; return null; } await PauseLookupAsync().ConfigureAwait(false); return prior; }
        public async ValueTask<RecordTemporalCommittedOperation?> FindTemporalOperationAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        { var prior = temporalReceipts.GetValueOrDefault(key.IdempotencyKey); if (HideNextLookup) { HideNextLookup = false; return null; } await PauseLookupAsync().ConfigureAwait(false); return prior; }
        private async ValueTask PauseLookupAsync()
        { var pause = BeforeNextLookup; BeforeNextLookup = null; if (pause is not null) await pause().ConfigureAwait(false); }
        public ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId, DateTimeOffset effectiveAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        async ValueTask<IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>> IKernelPreparedTransactionPort<RecordTemporalWrite, RecordTemporalCommit>.BeginAsync(CancellationToken cancellationToken)
            => new TemporalTransaction(this, await bookkeeping.BeginTransactionAsync(cancellationToken));
        private sealed class TemporalTransaction(Port owner, IContentBookkeepingTransaction content) : IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>, IRecordContentTransaction
        {
            public IContentBookkeepingSession ContentSession => content;
            private RecordTemporalWrite write = null!;
            private KernelOperationIdentity operation = null!;
            private KernelAuditEvidence audit = null!;
            public ValueTask StageOperationAsync(KernelOperationIdentity value, CancellationToken cancellationToken = default) { operation = value; return ((IContentBookkeepingSession)content).StageOperationAsync(value, cancellationToken); }
            public ValueTask StageRecordAsync(RecordTemporalWrite value, CancellationToken cancellationToken = default) { write = value; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence value, CancellationToken cancellationToken = default) { audit = value; return ((IContentBookkeepingSession)content).StageAuditAsync(value, cancellationToken); }
            public async ValueTask<RecordTemporalCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                var row = write.Row;
                var temporal = new RecordTemporalEvidence(1, null, [], write.EffectiveFrom, write.EffectiveTo, null, DateTimeOffset.UnixEpoch);
                var receipt = new RecordTemporalReceipt(new(row.RecordId, row.OperationId, row.RecordTypeId, row.Version, row.DefinitionDigest, row.Schema, row.RecordedAt),
                    temporal, RecordTemporalJournalBinding.HashBinding(operation.IdempotencyKey, operation, row, audit, temporal));
                await content.CommitAsync(cancellationToken);
                owner.Rows.Add(row); owner.temporalReceipts.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                return new(RecordTemporalCommitDisposition.Created, receipt);
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { owner.Rollbacks++; return content.RollbackAsync(cancellationToken); }
            public ValueTask DisposeAsync() => content.DisposeAsync();
        }
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
            public IContentBookkeepingSession ContentSession => owner.DecorateSession?.Invoke(content) ?? content;
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
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) { owner.Rollbacks++; return content.RollbackAsync(cancellationToken); }
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
