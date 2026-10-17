using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;

internal static class AcceptedRecordCreatePackageProbe
{
    private const string Tenant = "tenant-a";
    private const string Submitter = "submitter-a";
    private const string Worker = "consumer-worker";
    private const string Delegate = "consumer-delegate";
    private const string AcceptedKey = "8:tenant-a11:submitter-a10:accepted-116:accepted-command";
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

    // ADR-0110 decisions 1/2/4/6: package composition, not a durable accepting store or a W10 adapter.
    internal static async Task VerifyAsync()
    {
        var window = new DefinitionContractWindow(1, 0, 1);
        var validator = new RecordsIntentValidator();
        var kinds = new FieldKindRuntime(new FieldKindRegistry(Array.Empty<AdmittedFieldKind>()));
        var schemas = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(window),
        });
        var classes = new ClassDefinitionStore(catalogue);
        await classes.CreateDraftAsync(new NewClass(Tenant, "ops", "Equipment", "1.0.0", new(1, 0), "ops-core"), "class-draft");
        await classes.PublishAsync(Tenant, "ops.equipment", "1.0.0", 1, "class-publish");
        var records = new RecordTypeDefinitionStore(catalogue,
            new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission()),
            new RecordFieldDefaults(kinds), schemas, window);
        await records.CreateDraftAsync(new NewRecordType(Tenant, "ops", "Accepted Note", "1.2.3",
            [new("notes", "Notes")], new(1, 0), ClassId: "ops.equipment", RecordClass: RecordClass.Master,
            PackageId: "ops-core", CreationGate: RecordCreationGate.FormOnly), "record-draft");
        await records.PublishAsync(Tenant, "ops.accepted-note", "1.2.3", 1, "record-publish");
        var events = new List<string>();
        var gate = new Gate(events);
        var port = new Port(events);
        var operation = new RecordWriteOperation(new AccessProvider(gate), records, schemas, port, port, new Clock());
        var command = Command();

        gate.Allow = false;
        var denied = await operation.CompleteAcceptedAsync(new(Tenant, Worker), command);
        Refusal(denied, "authorize", "NoGrant");
        Require(denied.Decision is { Allowed: false, Principal: "consumer-worker" },
            "Denied completion lost the worker's own denied decision.");
        Require(events.SequenceEqual(["gate:" + Worker]) && port.Begun == 0 && port.Lookups.Count == 0,
            "Denied completion reached lookup or staging before the worker's bounded decision.");

        gate.Allow = true;
        foreach (var invalid in new[]
        {
            (Command: command with { AcceptanceSnapshot = Encoding.UTF8.GetBytes("not an acceptance snapshot") }, Code: "records.command.not_accepted"),
            (Command: command with { FormRevision = "substituted@9.0.0" }, Code: "records.command.not_accepted"),
            (Command: command with { Body = JsonNode.Parse("""{"notes":"substituted"}""")!.AsObject() }, Code: "records.command.binding_mismatch"),
        })
        {
            events.Clear();
            Refusal(await operation.CompleteAcceptedAsync(new(Tenant, Worker), invalid.Command), "bind", invalid.Code);
            Require(events.SequenceEqual(["gate:" + Worker]) && port.Begun == 0 && port.Lookups.Count == 0,
                "An invalid acceptance or substituted binding reached lookup or staging.");
        }

        events.Clear();
        var first = await operation.CompleteAcceptedAsync(new(Tenant, Worker), command);
        Require(first.Committed && !first.Replayed && first.Decision is null
            && first.Receipt is { RecordTypeId: "ops.accepted-note", Version: "1.2.3", OperationId: "accepted-1" },
            "The packaged form_only command did not complete with the bound published receipt.");
        Require(events.SequenceEqual(["gate:" + Worker, "lookup", "begin", "operation", "record", "audit", "commit", "dispose"]),
            "The package did not decide the worker before lookup and one prepared transaction.");
        var row = port.Row ?? throw new InvalidOperationException("No record was staged.");
        var audit = port.Audit ?? throw new InvalidOperationException("No audit was staged.");
        Require(row.ActorId == Worker && row.OriginatorId == Submitter && row.AcceptedCommandId == "command-1"
            && row.AcceptanceAuditId == "acceptance-1" && row.RequestedVersion is null
            && Encoding.UTF8.GetString(row.Body.Span) == """{"notes":"accepted"}"""
            && audit.ActorId == Worker && audit.OriginatorId == Submitter,
            "Completion lost the worker actor, submitter provenance, acceptance link or mapped body.");
        var snapshot = AuthoritySnapshotEncoding.TryDecode(audit.AuthoritySnapshot.Span);
        Require(snapshot is not null && snapshot.Principal == Worker
            && AuthoritySnapshotEncoding.Fact(snapshot, "act", "act:") == "records:complete-accepted"
            && AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:") == "allowed",
            "The committed audit did not carry the worker's own authority snapshot.");

        events.Clear();
        var replay = await operation.CompleteAcceptedAsync(new(Tenant, Delegate), command);
        Require(replay.Replayed && replay.Receipt == first.Receipt && port.Begun == 1 && port.Commits == 1
            && events.SequenceEqual(["gate:" + Delegate, "lookup"])
            && port.Lookups.All(key => key.IdempotencyKey == AcceptedKey),
            "Another worker did not replay the submitter-scoped accepted receipt without insertion.");

        gate.Allow = false;
        events.Clear();
        var lookups = port.Lookups.Count;
        Refusal(await operation.CompleteAcceptedAsync(new(Tenant, Delegate), command), "authorize", "NoGrant");
        Require(port.Lookups.Count == lookups && events.SequenceEqual(["gate:" + Delegate]),
            "An accepted receipt was looked up before the current worker decision.");

        gate.Allow = true;
        port.Stored = port.Stored! with { Receipt = null };
        events.Clear();
        var unreadable = await operation.CompleteAcceptedAsync(new(Tenant, Delegate), command);
        Refusal(unreadable, "commit", "records.write.receipt_unsupported");
        Require(!AcceptedRecordCreateOutcomes.IsTerminalRefusal(unreadable) && port.Begun == 1 && port.Commits == 1
            && events.SequenceEqual(["gate:" + Delegate, "lookup"]),
            "An unreadable accepted receipt authorized another insertion or became a terminal refusal.");
        // Keep the acceptance-time invariant explicit beside the outcomes, even though Gate also rejects other principals.
        Require(gate.Asked.All(request => request.Principal != Submitter),
            "Completion re-decided the submitter instead of preserving acceptance-time authority.");
        Console.WriteLine("ACCEPTED_RECORD_CREATE_PACKAGE_PASS: bounded worker before lookup, immutable acceptance, form_only completion, actor/provenance, delegate replay and unreadable receipt refusal");
    }

    private static AcceptedRecordCreate Command()
    {
        var acceptedAt = At.AddMinutes(-5);
        var body = JsonNode.Parse("""{"notes":"accepted"}""")!.AsObject();
        var acceptance = AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(
            new AccessRequest("forms:submit-gate", Submitter, Tenant,
                new AccessRecord(Tenant, "form", "capture@1.0.0", new Dictionary<string, JsonNode?>()), acceptedAt),
            true, "None", "grant:accepted-fixture", [], [], kind: "Gate"));
        return new(Tenant, "command-1", Submitter, "accepted-1", "ops.accepted-note", body,
            "records-write-intent/1", RecordWriteIntent.Digest(RecordWriteIntent.Encode("ops.accepted-note", null, body)),
            "capture@1.0.0", "acceptance-1", acceptedAt, acceptedAt, acceptance);
    }

    private static void Refusal(RecordWriteOutcome outcome, string stage, string code)
        => Require(outcome.Refusal is not null && outcome.Refusal.Stage == stage && outcome.Refusal.Code == code
            && outcome.Receipt is null && !outcome.Committed && !outcome.Replayed,
            "The accepted completion did not return the literal public refusal without a receipt.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => At;
    }

    private sealed class Gate(List<string> events) : IAuthorizationDecider
    {
        public bool Allow { get; set; }
        public List<AccessRequest> Asked { get; } = [];

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            events.Add("gate:" + request.Principal);
            Asked.Add(request);
            Require(request.Operation == "records:complete-accepted" && request.Tenant == Tenant && request.At == At
                && request.Record.Tenant == Tenant && request.Record.Kind == "accepted-command" && request.Record.Id == "command-1"
                && request.Record.RecordTypeId == "ops.accepted-note" && request.Record.Fields.Count == 2
                && request.Record.Fields["submitter"]!.GetValue<string>() == Submitter
                && request.Record.Fields["operation_id"]!.GetValue<string>() == "accepted-1",
                "The package asked for an unbounded or incorrectly bound worker act.");
            var allowed = Allow && request.Principal is Worker or Delegate;
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed, allowed ? "None" : "NoGrant",
                allowed ? "grant:worker-fixture" : "none", [], []));
        }
    }

    private sealed class Port(List<string> events) : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private List<string> Events { get; } = events;
        public int Begun { get; private set; }
        public int Commits { get; private set; }
        public List<RecordWriteOperationKey> Lookups { get; } = [];
        public RecordWriteRow? Row { get; private set; }
        public KernelAuditEvidence? Audit { get; private set; }
        public RecordWriteCommittedOperation? Stored { get; set; }

        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            Events.Add("lookup");
            Lookups.Add(key);
            Require(key.IdempotencyKey == AcceptedKey, "Accepted lookup used another tenant, submitter or origin.");
            return ValueTask.FromResult(Stored);
        }

        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Events.Add("begin");
            Begun++;
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
                Require(_operation is not null && _operation.IdempotencyKey == AcceptedKey
                    && _row is not null && _audit is not null && owner.Stored is null,
                    "The package did not stage exactly one complete first accepted operation.");
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner.Row = row;
                owner.Audit = _audit;
                owner.Stored = new(row.IntentEncodingVersion, row.IntentDigest, receipt);
                owner.Commits++;
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, receipt));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            { owner.Events.Add("rollback"); return ValueTask.CompletedTask; }
            public ValueTask DisposeAsync()
            { owner.Events.Add("dispose"); return ValueTask.CompletedTask; }
        }
    }
}
