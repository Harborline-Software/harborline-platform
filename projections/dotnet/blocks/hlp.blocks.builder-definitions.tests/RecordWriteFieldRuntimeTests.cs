using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// DES-0015 RW-2 and records-eng-15; DES-0030's shared domain and instance validation contract;
/// ADR-0095 rulings 1, 2 and 4. These tests call the real published-type binder, schema registry,
/// kind runtime, domain intersection and value validator. Only immutable source facts, read-authority
/// answers, authorization evidence and atomic persistence are host fixtures. Expected bodies,
/// values, principals, stages, codes and pointers are literal external-contract oracles.
/// </summary>
public sealed class RecordWriteFieldRuntimeTests
{
    private const string Tenant = "tenant-a";
    private const string Actor = "operator-7";
    private const string Worker = "worker-1";
    private const string Type = "eam.runtime-check";
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    // Constructor setup for the existing tests: the same admitted revisions as their TestHost.
    internal static IFieldKindRuntime FixtureKinds() => new FieldKindRuntime(new FieldKindRegistry([
        new("count", "1.0.0", null, FieldScalarValueShape.Integer),
        new("text", "1.0.0", new FieldGovernanceDefinition(true, false, true, "internal"), FieldScalarValueShape.Text),
        new("date", "1.0.0", null, FieldScalarValueShape.Text, [FieldKindCapability.RetentionClock]),
        new("date", "2.0.0", null, FieldScalarValueShape.Text),
    ]));

    internal static IFieldDomainRuntime FixtureDomains(TimeProvider clock)
    {
        var source = new Source();
        return new ValueDomainRuntime(source, new ReadAuthority(), clock);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task literal_domain_membership_runs_before_staging_on_both_write_entries(bool accepted)
    {
        var fixture = await Fixture.CreateAsync(accepted: accepted);

        var published = (await fixture.Records.ResolvePublishedSchemaAsync(Tenant, Type))!;
        Assert.True((await fixture.Registry.ValidateAsync(published.Schema.Id,
            Encoding.UTF8.GetBytes("""{"status":"C"}"""))).IsValid);

        var refused = await fixture.WriteAsync("domain-bad", """{"status":"C"}""");

        Refused(refused, "/status");
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Rows);
        Assert.Equal(["A", "B"], fixture.Reads.Members);
        Assert.All(fixture.Reads.Scopes, scope =>
        {
            Assert.Equal("tenant-a", scope.Tenant.Value);
            Assert.Equal(accepted ? "worker-1" : "operator-7", scope.Principal);
            Assert.NotEqual("submitter-1", scope.Principal);
        });

        fixture.Port.Events.Clear();
        var allowed = await fixture.WriteAsync("domain-good", """{"status":"A"}""");
        Assert.True(allowed.Committed);
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
        var row = Assert.Single(fixture.Port.Rows);
        Assert.Equal("""{"status":"A"}""", Encoding.UTF8.GetString(row.Body.Span));
        Assert.Equal(accepted ? "worker-1" : "operator-7", row.ActorId);
        Assert.Equal(accepted ? "submitter-1" : null, row.OriginatorId);
        Assert.Equal(accepted ? "submitter-1" : null, Assert.Single(fixture.Port.Audits).OriginatorId);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task missing_and_null_are_absent_but_a_scalar_meets_one_value(bool required, int minimum)
    {
        var fixture = await Fixture.CreateAsync(constraints: new(required, minimum, 1, [], null));
        Refused(await fixture.WriteAsync("missing", "{}"), "/status");
        Assert.Empty(fixture.Port.Rows);
        // The existing scalar schema also refuses null; no claim that this case uniquely detects the new call.
        Refused(await fixture.WriteAsync("null", """{"status":null}"""), "/status");
        Assert.Empty(fixture.Port.Rows);
        Assert.True((await fixture.WriteAsync("scalar", """{"status":"A"}""")).Committed);
        Assert.Single(fixture.Port.Rows);
    }

    [Fact]
    public async Task scalar_count_one_exceeds_zero_but_absence_fits_zero()
    {
        var fixture = await Fixture.CreateAsync(constraints: new(false, 0, 0, [], null));
        Refused(await fixture.WriteAsync("one", """{"status":"A"}"""), "/status");
        Assert.Empty(fixture.Port.Rows);
        Assert.True((await fixture.WriteAsync("zero", "{}")).Committed);
        Assert.Equal("{}", Encoding.UTF8.GetString(Assert.Single(fixture.Port.Rows).Body.Span));
    }

    [Fact]
    public async Task field_identity_is_literal_and_public_pointer_escapes_slash_and_tilde()
    {
        var fixture = await Fixture.CreateAsync(key: "a/b~c");
        var refused = await fixture.WriteAsync("escaped-bad", """{"a/b~c":"SECRET-C"}""");
        Refused(refused, "/a~1b~0c");
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(refused.Refusal));
        Assert.DoesNotContain("field.", JsonSerializer.Serialize(refused.Refusal));
        Assert.Empty(fixture.Port.Rows);
        Assert.True((await fixture.WriteAsync("escaped-good", """{"a/b~c":"A"}""")).Committed);
        Assert.Equal("""{"a/b~c":"A"}""", Encoding.UTF8.GetString(Assert.Single(fixture.Port.Rows).Body.Span));
    }

    [Fact]
    public async Task a_dot_in_a_key_does_not_traverse_an_unbound_object()
    {
        var fixture = await Fixture.CreateAsync(key: "a.b", constraints: new(true, 1, 1, [], null));
        Refused(await fixture.WriteAsync("dot-missing", "{}"), "/a.b");
        Assert.True((await fixture.WriteAsync("dot-present", """{"a.b":"A"}""")).Committed);
        Assert.Equal("""{"a.b":"A"}""", Encoding.UTF8.GetString(Assert.Single(fixture.Port.Rows).Body.Span));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task denied_gate_reads_no_domain_and_opens_no_transaction(bool accepted)
    {
        var fixture = await Fixture.CreateAsync(accepted: accepted);
        fixture.Gate.Allow = false;
        var outcome = await fixture.WriteAsync("denied", """{"status":"A"}""");
        Assert.Equal("authorize", outcome.Refusal!.Stage);
        Assert.Equal("NoGrant", outcome.Refusal.Code);
        Assert.Equal(0, fixture.Source.Opens);
        Assert.Empty(fixture.Reads.Scopes);
        Assert.Empty(fixture.Port.Events);
        Assert.Empty(fixture.Port.Rows);
        fixture.Gate.Allow = true;
        Assert.True((await fixture.WriteAsync("permitted", """{"status":"A"}""")).Committed);
        Assert.True(fixture.Source.Opens > 0);
    }

    [Fact]
    public async Task unreadable_member_is_not_accepted_even_when_it_belongs_to_the_complete_domain()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Reads.HideB = true;
        Refused(await fixture.WriteAsync("hidden", """{"status":"B"}"""), "/status");
        Assert.Empty(fixture.Port.Rows);
        Assert.Contains("B", fixture.Reads.Members); // Actual read-authority adapter was consulted for the hidden member.
        Assert.True((await fixture.WriteAsync("visible", """{"status":"A"}""")).Committed);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("incomplete")]
    [InlineData("revision")]
    [InlineData("unresolved")]
    [InlineData("admission")]
    public async Task unsafe_source_resolution_refuses_without_staging_and_recovers_with_real_facts(string poison)
    {
        var taxonomy = new FieldConstraintDefinition(false, 0, 1, [],
            new(TaxonomyScheme: new("status-scheme", "1.0.0")));
        var fixture = await Fixture.CreateAsync(constraints: taxonomy);
        fixture.Source.Poison = poison;
        var outcome = await fixture.WriteAsync("unsafe", """{"status":"A"}""");
        Refused(outcome, "/status");
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.DoesNotContain("source-secret", JsonSerializer.Serialize(outcome.Refusal));
        Assert.Empty(fixture.Port.Rows);
        fixture.Source.Poison = null;
        Assert.True((await fixture.WriteAsync("resolved", """{"status":"A"}""")).Committed);
        Assert.Single(fixture.Port.Rows);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("timeout")]
    public async Task unexpected_source_failures_propagate_with_rollback_and_no_partial_command(string fault)
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Source.Poison = fault;
        if (fault == "io")
            await Assert.ThrowsAsync<IOException>(() => fixture.WriteAsync("source-fault", """{"status":"A"}"""));
        else
            await Assert.ThrowsAsync<TimeoutException>(() => fixture.WriteAsync("source-fault", """{"status":"A"}"""));
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Rows);
        Assert.Empty(fixture.Port.Audits);
        fixture.Source.Poison = null;
        Assert.True((await fixture.WriteAsync("source-recovered", """{"status":"A"}""")).Committed);
    }

    [Fact]
    public async Task caller_cancellation_during_domain_resolution_propagates_and_rolls_back()
    {
        var fixture = await Fixture.CreateAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Source.OnOpen = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.WriteAsync("cancelled", """{"status":"A"}""", cancellation.Token));
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.Empty(fixture.Port.Rows);
        Assert.Empty(fixture.Port.Audits);
        fixture.Source.OnOpen = null;
        Assert.True((await fixture.WriteAsync("after-cancel", """{"status":"A"}""")).Committed);
    }

    [Fact]
    public async Task equivalent_replay_keeps_original_bytes_and_binding_without_reopening_the_domain()
    {
        var fixture = await Fixture.CreateAsync(number: true);
        var first = await fixture.WriteAsync("original", """{"status":"A","amount":3.0}""");
        Assert.True(first.Committed);
        var original = Assert.Single(fixture.Port.Rows);
        Assert.Equal("""{"amount":3.0,"status":"A"}""", Encoding.UTF8.GetString(original.Body.Span));
        Assert.Equal("1.0.0", original.Version);
        await fixture.PublishNextAsync(["B"]);
        var reads = fixture.Source.Opens;
        fixture.Source.Poison = "io";
        var replay = await fixture.WriteAsync("original", """{"amount":3e0,"status":"A"}""");
        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(reads, fixture.Source.Opens);
        Assert.Single(fixture.Port.Rows);
        Assert.Equal("""{"amount":3.0,"status":"A"}""", Encoding.UTF8.GetString(original.Body.Span));
        fixture.Source.Poison = null;
        Refused(await fixture.WriteAsync("new-head-bad", """{"status":"A","amount":3}"""), "/status");
        var latest = await fixture.WriteAsync("new-head-good", """{"status":"B","amount":3}""");
        Assert.Equal("1.1.0", latest.Receipt!.Version);
        var pinned = await fixture.Operation.CreateAsync(new(Tenant, Actor),
            new(Type, "pinned", Body("""{"status":"A","amount":3}"""), "1.0.0"));
        Assert.True(pinned.Committed);
        Assert.Equal("1.0.0", pinned.Receipt!.Version);
        Assert.Equal(3, fixture.Port.Rows.Count);
    }

    [Fact]
    public async Task declared_field_is_a_real_narrowing_of_the_exact_trait_revision_and_still_validates_instances()
    {
        var fixture = await Fixture.CreateAsync(trait: true);
        fixture.Traits!.Reads.Clear();
        Refused(await fixture.WriteAsync("trait-outside-field", """{"status":"C"}"""), "/status");
        Assert.NotEmpty(fixture.Traits.Reads);
        Assert.All(fixture.Traits.Reads, identity => Assert.Equal(("status-trait", "1.0.0"), identity));
        Assert.Empty(fixture.Port.Rows);
        Assert.True((await fixture.WriteAsync("trait-inside-field", """{"status":"A"}""")).Committed);
        Assert.Single(fixture.Port.Rows);
    }

    [Fact]
    public async Task accepted_replay_also_returns_the_original_receipt_before_any_new_domain_read()
    {
        var fixture = await Fixture.CreateAsync(accepted: true);
        var first = await fixture.WriteAsync("accepted-original", """{"status":"A"}""");
        Assert.True(first.Committed);
        var reads = fixture.Source.Opens;
        fixture.Source.Poison = "io";
        var replay = await fixture.WriteAsync("accepted-original", """{"status":"A"}""");
        Assert.True(replay.Replayed);
        Assert.Equal(first.Receipt, replay.Receipt);
        Assert.Equal(reads, fixture.Source.Opens);
        Assert.Single(fixture.Port.Rows);
        Assert.Single(fixture.Port.Audits);
    }

    [Fact]
    public async Task both_field_runtime_collaborators_are_mandatory()
    {
        var fixture = await Fixture.CreateAsync();
        var access = new AccessProvider(fixture.Gate);
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, fixture.Records, fixture.Registry,
            fixture.Port, fixture.Port, fixture.Clock, null!, fixture.Domains));
        Assert.Throws<ArgumentNullException>(() => new RecordWriteOperation(access, fixture.Records, fixture.Registry,
            fixture.Port, fixture.Port, fixture.Clock, fixture.Kinds, null!));
    }

    private static JsonObject Body(string json) => JsonNode.Parse(json)!.AsObject();
    private static void Refused(RecordWriteOutcome outcome, string pointer)
    {
        Assert.False(outcome.Committed);
        Assert.Null(outcome.Receipt);
        Assert.Equal("validate", outcome.Refusal!.Stage);
        Assert.Equal("entity.validation.body_invalid", outcome.Refusal.Code);
        Assert.Equal([pointer], outcome.Refusal.Pointers);
    }

    private sealed class Fixture
    {
        private Fixture(bool accepted, string key, bool number, RecordTypeDefinitionStore records, InMemorySchemaRegistry registry,
            IFieldKindRuntime kinds, IFieldDomainRuntime domains, Source source, ReadAuthority reads, TraitSource? traits)
        {
            Accepted = accepted; Key = key; Number = number; Records = records; Registry = registry;
            Kinds = kinds; Domains = domains; Source = source; Reads = reads; Traits = traits;
            Operation = new(new AccessProvider(Gate), records, registry, Port, Port, Clock, kinds, domains);
        }
        public bool Accepted { get; }
        public string Key { get; }
        public bool Number { get; }
        public RecordTypeDefinitionStore Records { get; }
        public InMemorySchemaRegistry Registry { get; }
        public IFieldKindRuntime Kinds { get; }
        public IFieldDomainRuntime Domains { get; }
        public Source Source { get; }
        public ReadAuthority Reads { get; }
        public TraitSource? Traits { get; }
        public FixedClock Clock { get; } = new();
        public Gate Gate { get; } = new();
        public Port Port { get; } = new();
        public RecordWriteOperation Operation { get; }

        public static async Task<Fixture> CreateAsync(bool accepted = false, string key = "status",
            FieldConstraintDefinition? constraints = null, bool number = false, bool trait = false)
        {
            var kinds = new FieldKindRuntime(new FieldKindRegistry([
                new("text", "1.0.0", null, FieldScalarValueShape.Text),
                new("amount", "1.0.0", null, FieldScalarValueShape.Number),
            ]));
            var source = new Source();
            var reads = new ReadAuthority();
            var domains = new ValueDomainRuntime(source, reads, new FixedClock());
            var traits = trait ? new TraitSource() : null;
            var validator = new RecordsIntentValidator(traits);
            var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission(), domains);
            var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
            var window = new DefinitionContractWindow(1, 0, 1);
            var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            {
                [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(window, validator),
                [DefinitionKind.Classes] = ClassDefinitionStore.Admission(window),
            });
            var classes = new ClassDefinitionStore(store);
            await classes.CreateDraftAsync(new(Tenant, "eam", "Equipment", "1.0.0", new(1, 0), "eam-core"), "class-draft");
            await classes.PublishAsync(Tenant, "eam.equipment", "1.0.0", 1, "class-publish");
            var records = new RecordTypeDefinitionStore(store, compiler, new RecordFieldDefaults(kinds), registry, window,
                new(TenantId.FromString(Tenant), "type-author"), CreationRoles);
            constraints ??= new(trait, trait ? 1 : 0, 1, [], new(LiteralValues: ["A", "B"]));
            List<FieldDefinition> fields = [Field(key, constraints)];
            if (number) fields.Add(new("amount", "Amount", new(new("amount", "1.0.0", new Dictionary<string, string>()),
                new(false, 0, 1, [], null))));
            await records.CreateDraftAsync(new(Tenant, "eam", "Runtime Check", "1.0.0", fields, new(1, 0),
                Traits: trait ? [new("status-trait", "1.0.0", [new("status-slot", key)])] : null,
                ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core",
                CreationGate: accepted ? RecordCreationGate.FormOnly : RecordCreationGate.Role,
                CreationRoleId: accepted ? null : "asset-author"), "type-draft");
            await records.PublishAsync(Tenant, Type, "1.0.0", 1, "type-publish");
            source.Opens = 0;
            reads.Scopes.Clear(); reads.Members.Clear();
            return new(accepted, key, number, records, registry, kinds, domains, source, reads, traits);
        }

        public async Task PublishNextAsync(string[] values)
        {
            var head = (await Records.GetPublishedHeadAsync(Tenant, Type))!;
            var fields = head.Fields.ToArray();
            fields[0] = fields[0] with { Binding = fields[0].Binding! with
            { Constraints = fields[0].Binding!.Constraints with { ValueDomain = new(LiteralValues: values) } } };
            await Records.SaveDraftAsync(Type, head with { Fields = fields }, "1.1.0", 2, "next-draft");
            await Records.PublishAsync(Tenant, Type, "1.1.0", 3, "next-publish");
        }

        public Task<RecordWriteOutcome> WriteAsync(string operation, string json, CancellationToken cancellationToken = default)
        {
            var body = Body(json);
            if (!Accepted) return Operation.CreateAsync(new(Tenant, Actor), new(Type, operation, body), cancellationToken).AsTask();
            var acceptance = AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(
                new AccessRequest("forms:submit-gate", "submitter-1", Tenant,
                    new AccessRecord(Tenant, "form", "capture@1.0.0", new Dictionary<string, JsonNode?>()), At),
                true, "None", "grant:submit@1", [], [], kind: "Gate"));
            var command = new AcceptedRecordCreate(Tenant, "command-" + operation, "submitter-1", operation, Type, body,
                "records-write-intent/1", RecordWriteIntent.Digest(RecordWriteIntent.Encode(Type, null, body)),
                "capture@1.0.0", "acceptance-1", At, At, acceptance);
            return Operation.CompleteAcceptedAsync(new(Tenant, Worker), command, cancellationToken).AsTask();
        }
    }

    private static FieldDefinition Field(string key, FieldConstraintDefinition constraints)
        => new(key, "Status", new(new("text", "1.0.0", new Dictionary<string, string>()), constraints));

    private sealed class TraitSource : IRecordTraitSource
    {
        public List<(string Id, string Version)> Reads { get; } = [];
        public TraitDefinition? Resolve(string traitId, string version)
        {
            Reads.Add((traitId, version));
            return traitId == "status-trait" && version == "1.0.0"
                ? new("status-trait", "1.0.0", [new("status-slot", new(true, 1, 1, [],
                    new(LiteralValues: ["A", "B", "C"])), true, true)]) : null;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class Gate : IAuthorizationDecider
    {
        public bool Allow { get; set; } = true;
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, Allow, Allow ? "None" : "NoGrant", "grant:write@1", [], []));
    }

    private sealed class Source : IFieldDomainSource
    {
        public int Opens { get; set; }
        public string? Poison { get; set; }
        public Action? OnOpen { get; set; }
        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
        {
            Opens++;
            OnOpen?.Invoke();
            if (Poison == "io") throw new IOException("source-secret");
            if (Poison == "timeout") throw new TimeoutException("source-secret");
            if (Poison == "admission") throw new FieldAdmissionException([new("field.value_domain_source_unresolved", "/source-secret", "source-secret")]);
            return ValueTask.FromResult<IFieldDomainSnapshot>(new Snapshot(
                TenantId.FromString(Poison == "tenant" ? "tenant-b" : tenant.Value),
                Poison != "incomplete", Poison == "revision" ? "" : "domain@1", Poison == "unresolved"));
        }
    }

    private sealed class Snapshot(TenantId tenant, bool complete, string revision, bool unresolved) : IFieldDomainSnapshot
    {
        public TenantId Tenant => tenant;
        public bool IsComplete => complete;
        public string Revision => revision;
        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => !unresolved && scheme == new TaxonomySchemeReference("status-scheme", "1.0.0")
                ? [new("A", "status-A", default), new("B", "status-B", default)] : null;
        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => null;
    }

    private sealed class ReadAuthority : IFieldDomainReadAuthority
    {
        public bool HideB { get; set; }
        public List<FieldDomainScope> Scopes { get; } = [];
        public List<string> Members { get; } = [];
        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain, FieldDomainMember member,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scopes.Add(scope); Members.Add(member.Value);
            return ValueTask.FromResult(scope.Tenant.Value == "tenant-a"
                && (scope.Principal is "operator-7" or "worker-1" or "type-author")
                && (member.Value is "A" or "B" or "C") && !(HideB && member.Value == "B"));
        }
    }

    private sealed class Port : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private readonly Dictionary<string, RecordWriteCommittedOperation> _committed = new(StringComparer.Ordinal);
        public List<string> Events { get; } = [];
        public List<RecordWriteRow> Rows { get; } = [];
        public List<KernelAuditEvidence> Audits { get; } = [];
        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_committed.GetValueOrDefault(key.IdempotencyKey));
        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
        }
        private sealed class Transaction(Port owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            private KernelOperationIdentity? _operation;
            private RecordWriteRow? _row;
            private KernelAuditEvidence? _audit;
            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            { owner.Events.Add("operation"); _operation = operation; return ValueTask.CompletedTask; }
            public ValueTask StageRecordAsync(RecordWriteRow row, CancellationToken cancellationToken = default)
            { owner.Events.Add("record"); _row = row; return ValueTask.CompletedTask; }
            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            { owner.Events.Add("audit"); _audit = audit; return ValueTask.CompletedTask; }
            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner._committed.Add(_operation!.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                owner.Rows.Add(row); owner.Audits.Add(_audit!);
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, receipt));
            }
            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            { owner.Events.Add("rollback"); return ValueTask.CompletedTask; }
            public ValueTask DisposeAsync()
            { owner.Events.Add("dispose"); return ValueTask.CompletedTask; }
        }
    }
}
