using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;

// T-616: the released Records write contract, from packages only. A gate denial refuses before any lookup or store; an
// unpublished type refuses at bind; numbers reach the field facets as submitted; one create commits one set; the same
// caller's equivalent retry (3.0, 3, 3e0) returns the first receipt without re-executing; another caller's reuse of the
// operation id is its own operation. 003c (D6): a create commits the gate's evidence as its audit's AuthoritySnapshot with a
// seven-key payload, a conflict refuses records.write.replay_conflict at /operation_id, and a 256-byte id refuses.
// D1/K9/W9: the gate is asked for a typed target (the requested Record Type), the probed type is role-gated, and a direct
// create of a form_only type refuses records.write.form_only at /record_type_id even when the gate allows.
internal static class RecordWriteOperationProbe
{
    private const string Tenant = "tenant-consumer";
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RecordWriteScope Scope = new(Tenant, "operator-7");

    internal static async Task VerifyAsync()
    {
        var (records, registry) = await PublishedAsync();
        var port = new Port();

        var denied = await Operation(records, registry, port, allow: false).CreateAsync(Scope, Request("op-deny", """{"notes":"spare"}"""));
        if (denied.Refusal is not { Stage: "authorize", Code: "NoEffectiveRole" } || port.Begun != 0 || port.Lookups != 0
            || denied.Decision is not { Allowed: false, Refusal: "NoEffectiveRole", Principal: "operator-7", RecordTypeId: "eam.asset-class" }
            || denied.Decision.Project().Count != 4)
            throw new InvalidOperationException("Packed Records write did not refuse at the gate, with its typed trace, before any lookup or store.");

        var operation = Operation(records, registry, port, allow: true);
        var formOnly = await operation.CreateAsync(Scope,
            new("eam.note", "op-form-only", JsonNode.Parse("""{"notes":"spare"}""")!.AsObject()));
        if (formOnly.Refusal is not { Stage: "bind", Code: "records.write.form_only" }
            || formOnly.Refusal.Pointers.Count != 1 || formOnly.Refusal.Pointers[0] != "/record_type_id"
            || formOnly.Decision is not { Allowed: true, RecordTypeId: "eam.note" } || port.Begun != 0)
            throw new InvalidOperationException("Packed Records write created a form_only type directly.");
        var unknown = await operation.CreateAsync(Scope,
            new("eam.unpublished-type", "op-unknown", JsonNode.Parse("""{"notes":"spare"}""")!.AsObject()));
        if (unknown.Refusal is not { Stage: "bind", Code: "entity.validation.schema_unknown" } || port.Begun != 0)
            throw new InvalidOperationException("Packed Records write did not refuse an unpublished type by name at bind.");

        var invalid = await operation.CreateAsync(Scope, Request("op-invalid", """{"notes":5}"""));
        var lexeme = await operation.CreateAsync(Scope, Request("op-lexeme", """{"amount":3.0}"""));
        if (invalid.Refusal is not { Stage: "validate", Code: "entity.validation.body_invalid" }
            || invalid.Refusal.Pointers.Count != 1 || invalid.Refusal.Pointers[0] != "/notes"
            || lexeme.Refusal is not { Stage: "validate", Code: "entity.validation.body_invalid" }
            || lexeme.Refusal.Pointers.Count != 1 || lexeme.Refusal.Pointers[0] != "/amount"
            || port.Committed.Count != 0)
            throw new InvalidOperationException("Packed Records write did not refuse invalid bodies and submitted lexemes with only their pointers.");

        var created = await operation.CreateAsync(Scope, Request("op-0001", """{"measure":3.0}"""));
        var begun = port.Begun;
        var integer = await operation.CreateAsync(Scope, Request("op-0001", """{"measure":3}"""));
        var exponent = await operation.CreateAsync(Scope, Request("op-0001", """{"measure":3e0}"""));
        var text = await operation.CreateAsync(Scope, Request("op-0001", """{"measure":"3"}"""));
        // The same caller's retries settle from the stored operation and never begin a transaction, so Begun is checked
        // here, before another caller's first-seen create begins its own.
        if (!created.Committed || created.Replayed || created.Decision is not null
            || created.Receipt is not { RecordTypeId: "eam.asset-class", Version: "1.0.0", OperationId: "op-0001" }
            || created.Receipt.RecordedAt != At
            || !integer.Replayed || integer.Receipt != created.Receipt
            || !exponent.Replayed || exponent.Receipt != created.Receipt
            || port.Begun != begun
            || text.Refusal is not { Stage: "commit", Code: "records.write.replay_conflict" }
            || text.Refusal.Pointers.Count != 1 || text.Refusal.Pointers[0] != "/operation_id"
            || port.Committed.Count != 1
            || Encoding.UTF8.GetString(port.Bodies[created.Receipt.RecordId]) != """{"measure":3.0}""")
            throw new InvalidOperationException("Packed Records write did not settle equivalent retries from the first receipt without re-executing.");

        var other = await operation.CreateAsync(new(Tenant, "operator-8"), Request("op-0001", """{"measure":3.0}"""));
        if (!other.Committed || other.Replayed || other.Receipt!.RecordId == created.Receipt.RecordId
            || port.Begun != begun + 1
            || port.Committed.Count != 2)
            throw new InvalidOperationException("Packed Records write did not settle another caller's reuse of the operation id as its own operation.");

        var audit = port.Audits[created.Receipt.RecordId];
        var snapshot = Encoding.UTF8.GetString(audit.AuthoritySnapshot.Span);
        var payload = Encoding.UTF8.GetString(audit.Payload.Span);
        var keys = JsonNode.Parse(payload)!.AsObject().Select(member => member.Key).ToArray();
        if (!snapshot.Contains("\"target:record/" + created.Receipt.RecordId + "\"", StringComparison.Ordinal)
            || !snapshot.Contains("\"verdict:allowed\"", StringComparison.Ordinal)
            || !keys.SequenceEqual(new[] { "act", "operation_id", "record_id", "record_type_id", "version", "definition_digest", "schema_id" })
            || payload.Contains("verdict", StringComparison.Ordinal) || payload.Contains("operator-7", StringComparison.Ordinal))
            throw new InvalidOperationException("Packed Records write did not commit its gate evidence as the audit's authority snapshot beside a seven-key payload.");

        var tooLong = await operation.CreateAsync(Scope, Request(new string('a', 256), """{"measure":3.0}"""));
        if (tooLong.Refusal is not { Stage: "validate", Code: "records.write.operation_id_invalid" }
            || tooLong.Refusal.Pointers.Count != 1 || tooLong.Refusal.Pointers[0] != "/operation_id" || port.Committed.Count != 2)
            throw new InvalidOperationException("Packed Records write did not refuse a 256-byte operation id at its pointer.");

        Console.WriteLine("RECORDS_WRITE_PACKAGE_PASS: typed gate target, form_only direct create refused, gate before lookup with its decision trace, named bind and body refusals, submitted lexemes, one committed set with its authority snapshot and seven-key audit, caller-scoped exact-intent replay, replay_conflict at /operation_id, bounded operation id");
    }

    private static RecordCreateRequest Request(string operationId, string body)
        => new("eam.asset-class", operationId, JsonNode.Parse(body)!.AsObject());

    private static RecordWriteOperation Operation(RecordTypeDefinitionStore records, ISchemaRegistry registry, Port port, bool allow)
        => new(new AccessProvider(new FixtureGate(allow)), records, registry, port, port, new FixedClock(At));

    private static async Task<(RecordTypeDefinitionStore Records, ISchemaRegistry Registry)> PublishedAsync()
    {
        var window = new DefinitionContractWindow(1, 0, 1);
        var contract = new DefinitionContractVersion(1, 0);
        var validator = new RecordsIntentValidator();
        var kinds = new FieldKindRuntime(new FieldKindRegistry(new[] { new AdmittedFieldKind("amount", "1.0.0", null, FieldScalarValueShape.Number) }));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(window),
        });
        var classes = new ClassDefinitionStore(catalogue);
        await classes.CreateDraftAsync(new NewClass(Tenant, "eam", "Equipment", "1.0.0", contract, "eam-core"), "equipment");
        await classes.PublishAsync(Tenant, "eam.equipment", "1.0.0", 1, "equipment-publish");
        var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
        var roles = RoleVocabulary.FromApi(new[]
        {
            new RoleDefinition(Guid.Parse("0f5e2a54-7f2b-4d55-9d4c-000000000001"), RoleReference.Domain("asset-author"), "Asset author",
                new RoleOwner(RoleOwnerKind.Package, "eam-core"), false),
        });
        var records = new RecordTypeDefinitionStore(catalogue, compiler, new RecordFieldDefaults(kinds), registry, window, roles: roles);
        var fields = new[]
        {
            new FieldDefinition("notes", "Notes"),
            Amount("amount", "Amount", new Dictionary<string, string> { ["total_digits"] = "4", ["fraction_digits"] = "0" }),
            Amount("measure", "Measure", new Dictionary<string, string>()),
        };
        await records.CreateDraftAsync(new NewRecordType(Tenant, "eam", "Asset Class", "1.0.0", fields,
            contract, ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core",
            CreationGate: RecordCreationGate.Role, CreationRoleId: "asset-author"), "create");
        await records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        await records.CreateDraftAsync(new NewRecordType(Tenant, "eam", "Note", "1.0.0", fields,
            contract, ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core"), "create-note");
        await records.PublishAsync(Tenant, "eam.note", "1.0.0", 1, "publish-note");
        if (await records.GetCreationPolicyAsync(Tenant, "eam.asset-class") != new RecordCreationPolicy(RecordCreationGate.Role, "asset-author")
            || await records.GetCreationPolicyAsync(Tenant, "eam.note") != new RecordCreationPolicy(RecordCreationGate.FormOnly, null))
            throw new InvalidOperationException("Packed Records type store did not read the governing creation policy.");
        return (records, registry);

        static FieldDefinition Amount(string key, string name, Dictionary<string, string> parameters)
            => new(key, name, new FieldBindingDefinition(new FieldKindReference("amount", "1.0.0", parameters),
                new FieldConstraintDefinition(false, 0, 1, Array.Empty<string>(), null)));
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    // The consumer's stand-in for its adapter to the host gate; it reports a fixed verdict and computes nothing.
    private sealed class FixtureGate(bool allow) : IAuthorizationDecider
    {
        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allow,
                allow ? "None" : "NoEffectiveRole", "grant:records-writer", Array.Empty<AuthorizationEvidenceRole>(),
                Array.Empty<AuthorizationEvidenceStanding>()));
    }

    // A host transaction that enforces idempotency on the staged operation identity, and reads committed operations from
    // the same state, as the port contracts assign to hosts.
    private sealed class Port : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        public int Begun { get; private set; }
        public int Lookups { get; private set; }
        public Dictionary<string, RecordWriteCommittedOperation> Committed { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Bodies { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, KernelAuditEvidence> Audits { get; } = new(StringComparer.Ordinal);

        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            Lookups++;
            return ValueTask.FromResult(Committed.TryGetValue(key.IdempotencyKey, out var committed) ? committed : null);
        }

        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Begun++;
            return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
        }

        private sealed class Transaction(Port owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            private KernelOperationIdentity? _operation;
            private RecordWriteRow? _row;
            private KernelAuditEvidence? _audit;

            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            {
                _operation = operation;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default)
            {
                _row = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                _audit = audit;
                return ValueTask.CompletedTask;
            }

            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                // The unique operation key: an accepted operation publishes nothing and is returned exactly as stored.
                if (owner.Committed.TryGetValue(_operation!.IdempotencyKey, out var prior))
                    return ValueTask.FromResult(new RecordWriteCommit(prior.IntentDigest == _operation.Fingerprint
                        ? RecordWriteCommitDisposition.Replayed : RecordWriteCommitDisposition.Conflict, null) { Stored = prior });
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner.Committed.Add(_operation.IdempotencyKey, new RecordWriteCommittedOperation(row.IntentEncodingVersion, row.IntentDigest, receipt));
                owner.Bodies.Add(row.RecordId, row.Body.ToArray());
                owner.Audits.Add(row.RecordId, _audit!);
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, receipt));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
