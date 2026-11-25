using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

// Oracles: DES-0015 records-ck-10..12, records-eng-16, records-auth-34, section 10 (no cycles);
// ADR-0162 D1 / records-ck-31 / records-eng-22: periods are half-open, any instant can be read.
// SQL:2011 period-aware foreign keys (Kulkarni and Michels section 2.2, via ADR-0162 and R-0095)
// require checking the whole child period, including a correction that keeps its parent.
public sealed class RecordReferenceWriteTests
{
    private const string Type = "eam.node";
    private static readonly DateTimeOffset T0 = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddDays(1);
    private static readonly DateTimeOffset T2 = T0.AddDays(2);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task records_ck_12_checks_future_pieces_and_the_exclusive_end(bool bounded)
    {
        using var f = await Fixture.Create();
        var a = Created(await f.CreateRow("a"));
        var b = Created(await f.CreateRow("b"));
        _ = Created(await f.Amend(b, "b-future", a.Receipt.RecordId, T2));
        var before = File.ReadAllBytes(f.PathName);

        var result = await f.Amend(a, "a-parent", b.Receipt.RecordId, T1, bounded ? T2 : null);

        if (bounded) Assert.Equal(RecordTemporalWriteStatus.Created, result.Status);
        else
        {
            Refused(result, "records.reference.parent_cycle", "/parent");
            Assert.Equal(before, File.ReadAllBytes(f.PathName));
        }
    }

    [Fact]
    public async Task records_ck_12_concurrent_overlapping_amends_cannot_both_commit()
    {
        using var f = await Fixture.Create();
        var a = Created(await f.CreateRow("a"));
        var b = Created(await f.CreateRow("b"));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<RecordTemporalWriteOutcome> Write(RecordTemporalReceipt row, string operation, string parent)
        {
            await start.Task;
            return await f.Amend(row, operation, parent, T1);
        }
        var first = Task.Run(() => Write(a, "a-parent", b.Receipt.RecordId));
        var second = Task.Run(() => Write(b, "b-parent", a.Receipt.RecordId));
        start.SetResult();
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(results, r => r.Status == RecordTemporalWriteStatus.Created);
        Refused(Assert.Single(results, r => r.Status == RecordTemporalWriteStatus.Refused),
            "records.reference.parent_cycle", "/parent");
    }

    [Fact]
    public async Task records_ck_10_unresolved_target_refuses_without_record_receipt_or_audit()
    {
        using var f = await Fixture.Create();
        var result = await f.CreateRow("missing", "no-such-record");
        Refused(result, "records.reference.target_unresolved", "/parent");
        Assert.False(File.Exists(f.PathName));
    }

    private static RecordTemporalReceipt Created(RecordTemporalWriteOutcome result)
    {
        Assert.Equal(RecordTemporalWriteStatus.Created, result.Status);
        return Assert.IsType<RecordTemporalReceipt>(result.Receipt);
    }

    [Theory]
    [InlineData(true, true, null)]
    [InlineData(true, false, "records.reference.trait_absent")]
    [InlineData(false, true, "records.reference.class_mismatch")]
    [InlineData(false, false, "records.reference.class_mismatch")]
    public async Task records_eng_16_records_auth_34_records_ck_11_write_and_predicate_agree(
        bool member, bool trait, string? code)
    {
        using var f = await Fixture.Create(requiredTrait: true, parent: false, many: true);
        await f.HostTypes.Classes.CreateDraftAsync(new(Tenant, "eam", "Other", "1.0.0", new(1, 0), "eam-core"), "other");
        await f.HostTypes.Classes.PublishAsync(Tenant, "eam.other", "1.0.0", 1, "other-publish");
        var traits = trait ? new TraitReference[] { new("locatable", "1.0.0", []) } : null;
        var home = member ? "eam.equipment" : "eam.other";
        await f.HostTypes.Records.CreateDraftAsync(new(Tenant, "eam", "Target", "1.0.0", [], new(1, 0), Traits: traits,
            ClassId: home, RecordClass: RecordClass.Master, PackageId: "eam-core", CreationGate: RecordCreationGate.Role,
            CreationRoleId: "asset-author"), "target");
        await f.HostTypes.Records.PublishAsync(Tenant, "eam.target", "1.0.0", 1, "target-publish");
        var target = await f.Writer.CreateAsync(new(Tenant, "writer"), new("eam.target", "target-row", []));
        Assert.True(target.Committed);
        var before = File.ReadAllBytes(f.PathName);
        var body = new JsonObject { ["parent"] = new JsonArray(new JsonObject { ["type"] = "eam.target", ["id"] = target.Receipt!.RecordId }) };
        var result = await f.Writer.CreateAsync(new(Tenant, "writer"), new(Type, "reference", body));
        var reference = new RecordReferenceDefinition(null, "eam.equipment", ReferenceCardinality.Many, ReferenceDeleteBehavior.Block, "locatable");
        Assert.Equal(member && trait, RecordReferenceEligibility.Admits(reference, "eam.target", home, traits));
        Assert.Equal(member && trait, result.Committed);
        if (code is not null)
        {
            Assert.Equal(code, result.Refusal!.Code);
            Assert.Equal(["/parent/0"], result.Refusal.Pointers);
            Assert.Equal(before, File.ReadAllBytes(f.PathName));
        }
    }

    // ADR-0162 D5 and DES-0015 past availability: unavailable never means a root.
    [Theory]
    [InlineData("compacted")]
    [InlineData("off")]
    public async Task records_ck_12_unavailable_ancestor_history_refuses(string availability)
    {
        using var f = await Fixture.Create();
        var seed = Created(await f.CreateRow("seed"));
        var row = (await f.Store.FindAsync(Tenant, seed.Receipt.RecordId))!;
        if (availability == "off")
        {
            // A retained legacy publication without a history declaration, not the new-type default.
            var document = (await f.HostTypes.Records.GetPublishedHeadAsync(Tenant, Type))! with { HistoryPolicy = null };
            var key = RecordTypeDefinitionStore.KeyOf(Tenant, Type);
            await f.HostTypes.Catalogue.SaveDraftAsync(new(key, "2.0.0", "2.0.0",
                System.Text.Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document))), 2, "off-draft");
            await f.HostTypes.Catalogue.PublishAsync(key, "2.0.0", 3, "off-publish");
        }
        var port = new ReferencePort(row, (_, from, to) => availability == "compacted" ? new(false, 0, [])
            : new(true, 1, [new(row, from, to)]));
        var result = await f.Compose(port).CreateAsync(new(Tenant, "writer"), new(Type, availability, Fixture.Body(row.RecordId)));
        Assert.Equal("records.reference.parent_history_unavailable", result.Refusal!.Code);
        Assert.Equal(["/parent"], result.Refusal.Pointers);
        Assert.Equal(0, port.Commits);
    }

    [Fact]
    public async Task records_ck_12_L1422_correction_that_keeps_the_parent_rechecks_its_whole_period()
    {
        using var f = await Fixture.Create();
        var a = Created(await f.CreateRow("a"));
        var b = Created(await f.CreateRow("b"));
        var aWithParent = Created(await f.Amend(a, "a-bounded", b.Receipt.RecordId, T0, T1));
        _ = Created(await f.Amend(b, "b-later", a.Receipt.RecordId, T1));
        var before = File.ReadAllBytes(f.PathName);
        var result = await f.Amend(aWithParent, "a-correction", b.Receipt.RecordId, T0);
        Refused(result, "records.reference.parent_cycle", "/parent");
        Assert.Equal(before, File.ReadAllBytes(f.PathName));
    }

    [Fact]
    public async Task records_ck_12_ancestor_parent_uses_its_retained_definition_not_a_renamed_head_field()
    {
        using var f = await Fixture.Create();
        var a = Created(await f.CreateRow("a"));
        var b = Created(await f.CreateRow("b"));
        _ = Created(await f.Amend(b, "b-future", a.Receipt.RecordId, T2));
        var document = (await f.HostTypes.Records.GetPublishedHeadAsync(Tenant, Type))!;
        await f.HostTypes.Records.SaveDraftAsync(Type, document with
            { Fields = [document.Fields[0] with { FieldKey = "within" }] }, "2.0.0", 2, "rename");
        await f.HostTypes.Records.PublishAsync(Tenant, Type, "2.0.0", 3, "rename-publish");
        var body = new JsonObject { ["within"] = new JsonObject { ["type"] = Type, ["id"] = b.Receipt.RecordId } };
        var result = await f.Writer.AmendAsync(new(Tenant, "writer"), new(a.Receipt.RecordId, "new-field",
            new(a.Temporal.Revision, a.BindingHash), body, T1, null, "correction"));
        // DES-0015 retained historical meaning: renaming today's field cannot erase an old row's parent.
        Refused(result, "records.reference.parent_cycle", "/within");
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public async Task records_ck_12_depth_bound_is_100_ancestors(int length, bool admitted)
    {
        using var f = await Fixture.Create();
        var seed = Created(await f.CreateRow("seed"));
        var row = (await f.Store.FindAsync(Tenant, seed.Receipt.RecordId))!;
        var port = new ReferencePort(row, (id, from, to) =>
        {
            var index = int.Parse(id, System.Globalization.CultureInfo.InvariantCulture);
            var body = Fixture.Body(index + 1 < length ? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            return new(true, 1, [new(row with { RecordId = id, Body = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body) }, from, to)]);
        });
        var result = await f.Compose(port).CreateAsync(new(Tenant, "writer"), new(Type, "depth", Fixture.Body("0")));
        Assert.Equal(admitted, result.Committed);
        if (!admitted) Assert.Equal("records.reference.parent_depth_exceeded", result.Refusal!.Code);
        Assert.Equal(admitted ? 1 : 0, port.Commits);
    }

    [Theory]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    public async Task records_ck_12_total_row_bound_is_1000_even_for_a_shallow_tree(int count, bool admitted)
    {
        using var f = await Fixture.Create();
        var seed = Created(await f.CreateRow("seed"));
        var row = (await f.Store.FindAsync(Tenant, seed.Receipt.RecordId))!;
        // One ancestor identity with many effective pieces, not a deep chain. Literal count is the scoping bound.
        var port = new ReferencePort(row, (_, from, to) => new(true, count,
            Enumerable.Range(0, count).Select(i => new RecordReferenceSegment(row,
                from.AddMinutes(i), i + 1 == count ? to : from.AddMinutes(i + 1))).ToArray()));
        var result = await f.Compose(port).CreateAsync(new(Tenant, "writer"), new(Type, "rows", Fixture.Body(row.RecordId)));
        Assert.Equal(admitted, result.Committed);
        if (!admitted) Assert.Equal("records.reference.parent_depth_exceeded", result.Refusal!.Code);
        Assert.Equal(admitted ? 1 : 0, port.Commits);
    }

    [Fact]
    public async Task records_ck_10_a_transaction_without_a_reference_reader_fails_closed()
    {
        using var f = await Fixture.Create();
        var seed = Created(await f.CreateRow("seed"));
        var row = (await f.Store.FindAsync(Tenant, seed.Receipt.RecordId))!;
        var port = new ReferencePort(row, (_, _, _) => new(true, 0, [])) { HasReader = false };
        var result = await f.Compose(port).CreateAsync(new(Tenant, "writer"), new(Type, "no-reader", Fixture.Body(row.RecordId)));
        Assert.Equal("records.reference.target_unresolved", result.Refusal!.Code);
        Assert.Equal(0, port.Commits);
    }

    private sealed class ReferencePort(RecordWriteRow identity,
        Func<string, DateTimeOffset, DateTimeOffset?, RecordReferenceHistory> history)
        : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private readonly RecordWriteRow _identity = identity;
        private readonly Func<string, DateTimeOffset, DateTimeOffset?, RecordReferenceHistory> _history = history;
        public bool HasReader { get; init; } = true;
        public int Commits { get; private set; }
        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<RecordWriteCommittedOperation?>(null);
        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(HasReader ? new ReadingTransaction(this) : new Transaction(this));
        private class Transaction(ReferencePort owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            protected ReferencePort Owner { get; } = owner;
            private RecordWriteRow? _row;
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default) { _row = record; return ValueTask.CompletedTask; }
            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                Owner.Commits++;
                var r = _row!;
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created,
                    new(r.RecordId, r.OperationId, r.RecordTypeId, r.Version, r.DefinitionDigest, r.Schema, r.RecordedAt)));
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class ReadingTransaction(ReferencePort owner) : Transaction(owner), IRecordReferenceTransaction
        {
            public ValueTask<RecordWriteRow?> FindReferenceAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
                => ValueTask.FromResult<RecordWriteRow?>(Owner._identity with { RecordId = recordId });
            public ValueTask<RecordReferenceHistory> ReadParentHistoryAsync(string tenant, string recordId, DateTimeOffset from,
                DateTimeOffset? to, int rowBudget, CancellationToken cancellationToken = default)
                => ValueTask.FromResult(Owner._history(recordId, from, to));
        }
    }

    private static void Refused(RecordTemporalWriteOutcome result, string code, string pointer)
    {
        Assert.Equal(RecordTemporalWriteStatus.Refused, result.Status);
        Assert.Null(result.Receipt);
        Assert.Equal("validate", result.Refusal!.Stage);
        Assert.Equal(code, result.Refusal.Code);
        Assert.Equal([pointer], result.Pointers);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => T0;
    }

    private sealed class Gate : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, true, "None", "fixture-grant", [], []));
    }

    private sealed class Fixture : IDisposable
    {
        public string PathName { get; } = Path.Combine(Path.GetTempPath(), "harborline", "t615-refchecks", "test-runs",
            Guid.NewGuid().ToString("N"), "records.json");
        public required RecordWriteOperation Writer { get; set; }
        public required TestHost HostTypes { get; init; }
        public FileJournalRecordWriteStore Store { get; private set; } = null!;
        public RecordWriteOperation Compose(ReferencePort port) => new(new AccessProvider(new Gate()), HostTypes.Records,
            HostTypes.Registry, port, port, new Clock(), RecordWriteFieldRuntimeTests.FixtureKinds(),
            RecordWriteFieldRuntimeTests.FixtureDomains(new Clock()));
        public static async Task<Fixture> Create(bool requiredTrait = false, bool parent = true, bool many = false)
        {
            var host = Host();
            await host.Records.CreateDraftAsync(new(Tenant, "eam", "Node", "1.0.0",
                [new("parent", "Parent", Reference: new(null, "eam.equipment", many ? ReferenceCardinality.Many : ReferenceCardinality.One,
                    ReferenceDeleteBehavior.Block, RequiredTraitId: requiredTrait ? "locatable" : null, Parent: parent))],
                new(1, 0), ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core",
                CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author")
                { AmendmentPolicy = new(true) { Mode = RecordAmendmentMode.Supersession }, HistoryPolicy = RecordHistoryPolicy.Enabled }, "type");
            await host.Records.PublishAsync(Tenant, Type, "1.0.0", 1, "publish");
            var f = new Fixture { Writer = null!, HostTypes = host };
            var clock = new Clock();
            var store = new FileJournalRecordWriteStore(f.PathName, clock);
            f.Store = store;
            f.Writer = new(new AccessProvider(new Gate()), host.Records, host.Registry, store, store, clock,
                RecordWriteFieldRuntimeTests.FixtureKinds(), RecordWriteFieldRuntimeTests.FixtureDomains(clock), store, store, store);
            return f;
        }
        public Task<RecordTemporalWriteOutcome> CreateRow(string operation, string? parent = null)
            => Writer.CreateEffectiveAsync(new(Tenant, "writer"), new(Type, operation, Body(parent), T0, null)).AsTask();
        public Task<RecordTemporalWriteOutcome> Amend(RecordTemporalReceipt row, string operation, string? parent,
            DateTimeOffset from, DateTimeOffset? to = null)
            => Writer.AmendAsync(new(Tenant, "writer"), new(row.Receipt.RecordId, operation,
                new(row.Temporal.Revision, row.BindingHash), Body(parent), from, to, "correction")).AsTask();
        internal static JsonObject Body(string? parent) => parent is null ? []
            : new() { ["parent"] = new JsonObject { ["type"] = Type, ["id"] = parent } };
        public void Dispose()
        {
            var directory = Path.GetDirectoryName(PathName)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
