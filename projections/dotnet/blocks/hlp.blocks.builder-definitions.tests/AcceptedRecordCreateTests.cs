using System.Text;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;
using Harborline.Kernel.Core;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// Plan W8 (ADR-0110 decisions 1 to 4, rulings D3, D4, D6, K2, K3, K7): the Records form-origin entry completes a durably
/// accepted create as the host's worker. The worker's own bounded act is decided first, the submitter is never decided for
/// again and never impersonated, every command binding is checked at runtime, and the operation is keyed by the submitter
/// and the original operation id, so duplicate delivery and delegate recovery replay and never insert twice; a missing or
/// malformed receipt for the accepted operation never authorizes another insertion. The record, its operation identity and
/// its audit (actor the worker, originator the submitter, the worker's own snapshot) commit once. A principal's direct
/// create stays decided at commit against current authority (ADR-0100 decision 3) and still cannot create a
/// <c>form_only</c> type.
/// Oracles: literal codes, stages, ids and key bytes; the acceptance snapshot is built here from a literal request; the
/// fixture gate's verdict is a literal set membership, never computed from grants.
/// </summary>
public sealed class AcceptedRecordCreateTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset AcceptedAt = At.AddMinutes(-5);
    private const string Worker = "sys.records-worker";
    private const string Delegate = "sys.records-worker-2";
    private const string Submitter = "alice";
    private const string ValidBody = """{"asset_tag":"A-1","quantity":3,"notes":"spare"}""";

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_worker_completes_an_accepted_form_only_create_once_with_the_submitter_as_provenance()
    {
        var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        Assert.True(outcome.Committed);
        Assert.False(outcome.Replayed);
        Assert.Null(outcome.Decision);
        Assert.Equal(("eam.asset-class", "1.0.0", "op-accepted-1"), (outcome.Receipt!.RecordTypeId, outcome.Receipt.Version, outcome.Receipt.OperationId));
        // The one gate request: the worker's bounded act on this exact command, never the submitter's records:write.
        var asked = Assert.Single(fixture.Gate.Asked);
        Assert.Equal("records:complete-accepted", asked.Operation);
        Assert.Equal(Worker, asked.Principal);
        Assert.Equal(Tenant, asked.Tenant);
        Assert.Equal("accepted-command", asked.Record.Kind);
        Assert.Equal("cmd-1", asked.Record.Id);
        Assert.Equal("eam.asset-class", asked.Record.RecordTypeId);
        Assert.Equal(Submitter, asked.Record.Fields["submitter"]!.GetValue<string>());
        Assert.Equal("op-accepted-1", asked.Record.Fields["operation_id"]!.GetValue<string>());
        Assert.Equal(At, asked.At);
        // Provenance: the executor is the worker, the submitter is the originator, and the row links the command.
        var (operation, row, audit) = Assert.Single(fixture.Port.Published);
        Assert.Equal("8:tenant-a5:alice13:op-accepted-116:accepted-command", operation.IdempotencyKey);
        Assert.Equal(Worker, row.ActorId);
        Assert.Equal(Submitter, row.OriginatorId);
        Assert.Equal("cmd-1", row.AcceptedCommandId);
        Assert.Equal("acceptance-audit-1", row.AcceptanceAuditId);
        Assert.Null(row.RequestedVersion);
        Assert.Equal(Worker, audit.ActorId);
        Assert.Equal(Submitter, audit.OriginatorId);
        // D6: the authority snapshot committed with the record is the worker's own decision, in the shared grammar.
        var snapshot = AuthoritySnapshotEncoding.TryDecode(audit.AuthoritySnapshot.Span);
        Assert.NotNull(snapshot);
        Assert.Equal(Worker, snapshot.Principal);
        Assert.Equal("records:complete-accepted", AuthoritySnapshotEncoding.Fact(snapshot, "act", "act:"));
        Assert.Equal("allowed", AuthoritySnapshotEncoding.Fact(snapshot, "verdict", "verdict:"));
        Assert.Equal(
            """{"act":"records:write","operation_id":"op-accepted-1","record_id":"__ID__","record_type_id":"eam.asset-class","version":"1.0.0","definition_digest":"__DIGEST__","schema_id":"__SCHEMA__"}"""
                .Replace("__ID__", row.RecordId, StringComparison.Ordinal)
                .Replace("__DIGEST__", row.DefinitionDigest, StringComparison.Ordinal)
                .Replace("__SCHEMA__", row.Schema.Value, StringComparison.Ordinal),
            Encoding.UTF8.GetString(audit.Payload.Span));
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "dispose"], fixture.Port.Events);
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task Duplicate_delivery_and_delegate_recovery_replay_the_original_operation_and_never_insert_twice()
    {
        var fixture = await Fixture.CreateAsync();
        var first = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        var duplicate = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());
        var delegated = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Delegate), Command());

        Assert.True(first.Committed);
        Assert.True(duplicate.Replayed);
        Assert.True(delegated.Replayed);
        Assert.Equal(first.Receipt, duplicate.Receipt);
        Assert.Equal(first.Receipt, delegated.Receipt);
        Assert.Single(fixture.Port.Published);
        // The delegate's lookup is the submitter's key, not its own.
        Assert.Equal(["8:tenant-a5:alice13:op-accepted-116:accepted-command"], fixture.Port.Lookups.Select(key => key.IdempotencyKey).Distinct());
        Assert.False(AcceptedRecordCreateOutcomes.IsTerminalRefusal(delegated));
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_concurrent_completion_that_loses_the_atomic_stage_settles_as_a_replay_and_inserts_nothing()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());
        fixture.Port.HideNextLookup = true;

        var racing = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Delegate), Command());

        Assert.True(racing.Replayed);
        Assert.Single(fixture.Port.Published);
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task An_ordinary_revocation_after_acceptance_does_not_cancel_the_command_and_the_submitter_is_never_asked_again()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Gate.Revoked.Add(Submitter);

        var completed = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());
        // ADR-0100 decision 3 is preserved for ordinary work: the revoked submitter's own direct create is decided at
        // commit against current authority and refused.
        var direct = await fixture.Operation.CreateAsync(new(Tenant, Submitter), new("eam.meter", "op-direct-1", Body()));

        Assert.True(completed.Committed);
        Assert.Equal(Submitter, Assert.Single(fixture.Port.Published).Row.OriginatorId);
        Assert.Equal(("authorize", "NoEffectiveRole"), (direct.Refusal!.Stage, direct.Refusal.Code));
        Assert.Equal([("records:complete-accepted", Worker), ("records:write", Submitter)],
            fixture.Gate.Asked.Select(request => (request.Operation, request.Principal)));
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_refused_worker_is_decided_first_learns_nothing_and_stays_pending()
    {
        var fixture = await Fixture.CreateAsync(workers: []);

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        Assert.Equal(("authorize", "NoGrant"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.False(outcome.Decision!.Allowed);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Equal(0, fixture.Port.Begun);
        Assert.False(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    public static TheoryData<string> MissingReceipts => ["missing", "unsupported-encoding", "blank-digest", "foreign-operation"];

    [Theory]
    [MemberData(nameof(MissingReceipts))]
    [Trait("Proves", "platform")]
    public async Task A_missing_or_malformed_receipt_for_the_accepted_operation_never_authorizes_another_insertion(string shape)
    {
        var fixture = await Fixture.CreateAsync();
        var key = new RecordWriteOperationKey(Tenant, Submitter, "op-accepted-1") { Origin = RecordWriteOperationKey.AcceptedCommandOrigin };
        var digest = RecordWriteIntent.Digest(RecordWriteIntent.Encode("eam.asset-class", null, Body()));
        var receipt = new RecordWriteReceipt("record-0", "op-accepted-1", "eam.asset-class", "1.0.0", "digest", new("schema"), AcceptedAt);
        fixture.Port.Seed(key, shape switch
        {
            "missing" => new(RecordWriteIntent.EncodingVersion, digest, null),
            "unsupported-encoding" => new("records-write-intent/0", digest, receipt),
            "blank-digest" => new(RecordWriteIntent.EncodingVersion, " ", receipt),
            _ => new(RecordWriteIntent.EncodingVersion, digest, receipt with { OperationId = "op-other" }),
        });

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        Assert.Equal(("commit", "records.write.receipt_unsupported"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Null(outcome.Receipt);
        Assert.Equal(0, fixture.Port.Begun);
        Assert.Empty(fixture.Port.Published);
        Assert.False(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_lookup_miss_is_the_only_way_to_a_first_write_and_a_direct_operation_never_settles_an_accepted_one()
    {
        var fixture = await Fixture.CreateAsync();
        var direct = new RecordWriteOperationKey(Tenant, Submitter, "op-accepted-1");
        fixture.Port.Seed(direct, new(RecordWriteIntent.EncodingVersion, "digest-of-something-else",
            new("record-direct", "op-accepted-1", "eam.meter", "1.0.0", "digest", new("schema"), AcceptedAt)));

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        // The same principal's direct operation with the same id is another scope: it is neither disclosed nor a conflict.
        Assert.True(outcome.Committed);
        Assert.NotEqual("record-direct", outcome.Receipt!.RecordId);
        Assert.Equal("8:tenant-a5:alice13:op-accepted-1", direct.IdempotencyKey);
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task Another_payload_under_the_original_operation_conflicts_terminally_and_inserts_nothing()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());

        // A substituted body whose digest was also substituted passes the M4 check and is still caught by the operation.
        var substituted = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Delegate),
            Bound(Command() with { Body = JsonNode.Parse("""{"asset_tag":"A-2","quantity":3,"notes":"spare"}""")!.AsObject() }));

        Assert.Equal(("commit", "records.write.replay_conflict"), (substituted.Refusal!.Stage, substituted.Refusal.Code));
        Assert.Equal(["/operation_id"], substituted.Refusal.Pointers);
        Assert.Single(fixture.Port.Published);
        Assert.True(AcceptedRecordCreateOutcomes.IsTerminalRefusal(substituted));
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_body_that_is_not_the_one_bound_at_acceptance_is_refused_before_any_lookup_even_on_a_first_write()
    {
        var fixture = await Fixture.CreateAsync();

        // Review M4: the digest bound at acceptance is of the original body; the host now hands over another one.
        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker),
            Command() with { Body = JsonNode.Parse("""{"asset_tag":"A-2","quantity":3,"notes":"spare"}""")!.AsObject() });

        Assert.Equal(("bind", "records.command.binding_mismatch"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(fixture.Port.Lookups);
        Assert.Equal(0, fixture.Port.Begun);
        Assert.True(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    public static TheoryData<string, string> BindingSubstitutions => new()
    {
        { "worker-tenant", "records.command.binding_mismatch" },
        { "worker-is-submitter", "records.command.binding_mismatch" },
        { "snapshot-principal", "records.command.not_accepted" },
        { "snapshot-tenant", "records.command.not_accepted" },
        { "snapshot-denied", "records.command.not_accepted" },
        { "snapshot-after-acceptance", "records.command.not_accepted" },
        { "snapshot-malformed", "records.command.not_accepted" },
        { "snapshot-missing", "records.command.not_accepted" },
        { "snapshot-other-instant", "records.command.not_accepted" },
        { "snapshot-other-form", "records.command.not_accepted" },
        { "snapshot-other-act", "records.command.not_accepted" },
        { "snapshot-other-kind", "records.command.not_accepted" },
        { "snapshot-act-stage-principal", "records.command.not_accepted" },
        { "decided-after-acceptance", "records.command.not_accepted" },
        { "no-acceptance-audit", "records.command.not_accepted" },
        { "body-digest", "records.command.binding_mismatch" },
        { "body-digest-encoding", "records.command.binding_mismatch" },
    };

    [Theory]
    [MemberData(nameof(BindingSubstitutions))]
    [Trait("Proves", "platform")]
    public async Task A_command_whose_binding_or_acceptance_evidence_does_not_hold_is_refused_terminally_before_any_lookup(string shape, string code)
    {
        var fixture = await Fixture.CreateAsync(workers: [Worker, Submitter]);
        var command = Command();
        var worker = new RecordWorkerScope(Tenant, Worker);
        switch (shape)
        {
            case "worker-tenant": worker = new("tenant-b", Worker); break;
            case "worker-is-submitter": worker = new(Tenant, Submitter); break;
            case "snapshot-principal": command = command with { AcceptanceSnapshot = Acceptance(principal: "mallory") }; break;
            case "snapshot-tenant": command = command with { AcceptanceSnapshot = Acceptance(tenant: "tenant-b") }; break;
            case "snapshot-denied": command = command with { AcceptanceSnapshot = Acceptance(allowed: false) }; break;
            case "snapshot-after-acceptance": command = command with { AcceptanceSnapshot = Acceptance(at: AcceptedAt.AddTicks(1)) }; break;
            case "snapshot-malformed": command = command with { AcceptanceSnapshot = "{\"tenant\":\"tenant-a\"}"u8.ToArray() }; break;
            // Review M3: any other allowed decision of the same submitter is not this command's acceptance.
            case "snapshot-other-instant": command = command with { AcceptanceSnapshot = Acceptance(at: AcceptedAt.AddMinutes(-1)) }; break;
            case "snapshot-other-form": command = command with { AcceptanceSnapshot = Acceptance(formRevision: "other@1.0.0") }; break;
            case "snapshot-other-act": command = command with { AcceptanceSnapshot = Acceptance(operation: "forms:read") }; break;
            case "snapshot-other-kind": command = command with { AcceptanceSnapshot = Acceptance(kind: "SeparationOfDuty") }; break;
            case "snapshot-act-stage-principal":
                command = command with
                {
                    AcceptanceSnapshot = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Acceptance())
                        .Replace("\"principal:alice\"", "\"principal:mallory\"", StringComparison.Ordinal)),
                };
                break;
            case "decided-after-acceptance":
                command = command with { AcceptanceDecidedAt = AcceptedAt.AddTicks(1), AcceptanceSnapshot = Acceptance(at: AcceptedAt.AddTicks(1)) };
                break;
            case "no-acceptance-audit": command = command with { AcceptanceAuditId = " " }; break;
            case "body-digest": command = command with { BodyDigest = new string('0', 64) }; break;
            case "body-digest-encoding": command = command with { BodyDigestEncoding = "records-write-intent/0" }; break;
            case "snapshot-missing": command = command with { AcceptanceSnapshot = ReadOnlyMemory<byte>.Empty }; break;
            default: throw new InvalidOperationException(shape);
        }

        var outcome = await fixture.Operation.CompleteAcceptedAsync(worker, command);

        Assert.Equal(("bind", code), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(outcome.Refusal.Pointers);
        Assert.Empty(fixture.Port.Lookups);
        Assert.Equal(0, fixture.Port.Begun);
        Assert.True(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    public static TheoryData<string, string> ExecutionTimeTypes => new()
    {
        { "eam.ledger-line", "records.write.engine_only" },
        { "eam.meter", "records.command.creation_gate_changed" },
        { "eam.unpublished", "entity.validation.schema_unknown" },
    };

    [Theory]
    [MemberData(nameof(ExecutionTimeTypes))]
    [Trait("Proves", "platform")]
    public async Task An_execution_time_type_invariant_refuses_at_bind_terminally_and_stages_nothing(string type, string code)
    {
        var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Bound(Command() with { RecordTypeId = type }));

        Assert.Equal(("bind", code), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Equal(0, fixture.Port.Begun);
        Assert.True(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_body_the_bound_schema_refuses_persists_nothing_and_is_terminal()
    {
        var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker),
            Bound(Command() with { Body = JsonNode.Parse("""{"asset_tag":"A-1","notes":5}""")!.AsObject() }));

        Assert.Equal(("validate", "entity.validation.body_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
        Assert.Empty(fixture.Port.Published);
        Assert.Equal(["begin", "rollback", "dispose"], fixture.Port.Events);
        Assert.True(AcceptedRecordCreateOutcomes.IsTerminalRefusal(outcome));
    }

    public static TheoryData<string, bool> OperationIds => new()
    {
        { "", false },
        { new string('é', 127) + "a", true },
        { new string('é', 128), false },
    };

    [Theory]
    [MemberData(nameof(OperationIds))]
    [Trait("Proves", "platform")]
    public async Task The_original_operation_id_keeps_the_D6_bound(string operationId, bool admitted)
    {
        var fixture = await Fixture.CreateAsync();

        var outcome = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command() with { OperationId = operationId });

        if (admitted)
        {
            Assert.True(outcome.Committed);
            Assert.Equal(operationId, outcome.Receipt!.OperationId);
        }
        else
        {
            Assert.Equal(("validate", "records.write.operation_id_invalid"), (outcome.Refusal!.Stage, outcome.Refusal.Code));
            Assert.Equal(["/operation_id"], outcome.Refusal.Pointers);
            Assert.Empty(fixture.Port.Lookups);
        }
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_commit_fault_rolls_the_record_receipt_audit_and_snapshot_back_together()
    {
        var fixture = await Fixture.CreateAsync();
        fixture.Port.Fault = "commit";

        await Assert.ThrowsAsync<InjectedFault>(async () => await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command()));

        Assert.Empty(fixture.Port.Published);
        // The snapshot was staged in the one audit call, inside the transaction that rolled back.
        var staged = Assert.Single(fixture.Port.StagedAudits);
        Assert.Equal(Worker, staged.ActorId);
        Assert.Equal(Submitter, staged.OriginatorId);
        Assert.False(staged.AuthoritySnapshot.IsEmpty);
        Assert.Equal(["begin", "operation", "record", "audit", "commit", "rollback", "dispose"], fixture.Port.Events);

        // Recovery after the fault completes the command exactly once.
        fixture.Port.Fault = null;
        var recovered = await fixture.Operation.CompleteAcceptedAsync(new(Tenant, Worker), Command());
        Assert.True(recovered.Committed);
        Assert.Single(fixture.Port.Published);
    }

    [Fact]
    [Trait("Proves", "platform")]
    public async Task A_principals_direct_create_of_the_form_only_type_is_still_refused_and_keeps_its_unchanged_key()
    {
        var fixture = await Fixture.CreateAsync();

        var direct = await fixture.Operation.CreateAsync(new(Tenant, Submitter), new("eam.asset-class", "op-1", Body()));

        Assert.Equal(("bind", "records.write.form_only"), (direct.Refusal!.Stage, direct.Refusal.Code));
        Assert.Equal("8:tenant-a5:alice4:op-1", Assert.Single(fixture.Port.Lookups).IdempotencyKey);
        Assert.Equal(0, fixture.Port.Begun);
    }

    [Fact]
    [Trait("Proves", "platform")]
    public void A_key_without_an_origin_is_byte_identical_to_the_three_frame_key_and_an_origin_is_a_fourth_frame()
    {
        Assert.Equal("8:tenant-a10:operator-74:op-1", new RecordWriteOperationKey("tenant-a", "operator-7", "op-1").IdempotencyKey);
        Assert.Equal("8:tenant-a10:operator-74:op-116:accepted-command",
            new RecordWriteOperationKey("tenant-a", "operator-7", "op-1") { Origin = "accepted-command" }.IdempotencyKey);
        // An operation id that spells the origin frame stays a three-frame key and differs.
        Assert.NotEqual(
            new RecordWriteOperationKey("tenant-a", "operator-7", "op-1") { Origin = "accepted-command" }.IdempotencyKey,
            new RecordWriteOperationKey("tenant-a", "operator-7", "op-116:accepted-command").IdempotencyKey);
    }

    private static JsonObject Body() => JsonNode.Parse(ValidBody)!.AsObject();

    // The accepted command as the accepting store holds it: acceptance audit, form revision, the decision at acceptance and
    // the digest of its mapped body, computed here with the Records intent encoder (records-write-intent/1).
    private static AcceptedRecordCreate Command() => Bound(new(Tenant, "cmd-1", Submitter, "op-accepted-1", "eam.asset-class", Body(),
        "records-write-intent/1", "", "capture@1.0.0", "acceptance-audit-1", AcceptedAt, AcceptedAt, Acceptance()));

    // The command with the digest of its own body and type, as the acceptance would have bound it.
    private static AcceptedRecordCreate Bound(AcceptedRecordCreate command) =>
        command with { BodyDigest = RecordWriteIntent.Digest(RecordWriteIntent.Encode(command.RecordTypeId, null, command.Body)) };

    // The acceptance decision a form's submit gate made for the submitter, as the accepting store committed it.
    private static byte[] Acceptance(string principal = Submitter, string tenant = Tenant, bool allowed = true, DateTimeOffset? at = null,
        string formRevision = "capture@1.0.0", string operation = "forms:submit-gate", string kind = "Gate")
        => AuthoritySnapshotEncoding.Encode(new AuthorizationDecisionEvidence(
            new AccessRequest(operation, principal, tenant,
                new AccessRecord(tenant, "form", formRevision, new Dictionary<string, JsonNode?>()), at ?? AcceptedAt),
            allowed, allowed ? "None" : "NoGrant", "grant:fixture@1", [], [], kind: kind));

    private sealed class Fixture
    {
        private Fixture(WorkerGate gate, AcceptedCommandPort port, RecordWriteOperation operation)
            => (Gate, Port, Operation) = (gate, port, operation);

        public WorkerGate Gate { get; }
        public AcceptedCommandPort Port { get; }
        public RecordWriteOperation Operation { get; }

        // The test host with three published types: the form_only asset class, a role-gated meter and an engine ledger line.
        public static async Task<Fixture> CreateAsync(string[]? workers = null)
        {
            var host = Host();
            await PublishAsync(host, AssetClass("1.0.0"), "eam.asset-class");
            await PublishAsync(host, AssetClass("1.0.0") with { Name = "Meter", CreationGate = RecordCreationGate.Role, CreationRoleId = "meter-author" }, "eam.meter");
            await PublishAsync(host, AssetClass("1.0.0") with { Name = "Ledger Line", CreationGate = RecordCreationGate.Engine }, "eam.ledger-line");
            var gate = new WorkerGate(workers ?? [Worker, Delegate]);
            var port = new AcceptedCommandPort();
            var clock = new FixedClock(At);
            return new(gate, port, new RecordWriteOperation(new AccessProvider(gate), host.Records, host.Registry, port, port, clock,
                RecordWriteFieldRuntimeTests.FixtureKinds(), RecordWriteFieldRuntimeTests.FixtureDomains(clock)));
        }

        private static async Task PublishAsync(TestHost host, NewRecordType type, string expectedId)
        {
            var draft = await host.Records.CreateDraftAsync(type, "create-" + expectedId);
            Assert.Equal(expectedId, draft.RecordTypeId);
            await host.Records.PublishAsync(Tenant, expectedId, "1.0.0", 1, "publish-" + expectedId);
        }
    }

    // Stands in for the host adapter: allows the bounded completion act only for the configured workers, and a direct
    // records:write for anyone not revoked. The verdict is literal set membership.
    private sealed class WorkerGate(string[] workers) : IAuthorizationDecider
    {
        public List<AccessRequest> Asked { get; } = [];
        public HashSet<string> Revoked { get; } = new(StringComparer.Ordinal);

        public ValueTask<AuthorizationDecisionEvidence> DecideAsync(AccessRequest request, CancellationToken cancellationToken = default)
        {
            Asked.Add(request);
            var allowed = request.Operation == "records:complete-accepted"
                ? workers.Contains(request.Principal, StringComparer.Ordinal)
                : !Revoked.Contains(request.Principal);
            var refusal = allowed ? "None" : request.Operation == "records:complete-accepted" ? "NoGrant" : "NoEffectiveRole";
            return ValueTask.FromResult(new AuthorizationDecisionEvidence(request, allowed, refusal, allowed ? "grant:fixture@1" : "none", [], []));
        }
    }

    private sealed class FixedClock(DateTimeOffset at) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => at;
    }

    private sealed class InjectedFault : Exception { }

    // A host transaction that keys committed operations by the staged identity under a unique constraint and reads them back
    // from the same state; nothing is visible before commit.
    private sealed class AcceptedCommandPort : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader
    {
        private readonly Dictionary<string, RecordWriteCommittedOperation> _operations = new(StringComparer.Ordinal);

        public List<(KernelOperationIdentity Operation, RecordWriteRow Row, KernelAuditEvidence Audit)> Published { get; } = [];
        public List<KernelAuditEvidence> StagedAudits { get; } = [];
        public List<RecordWriteOperationKey> Lookups { get; } = [];
        public List<string> Events { get; } = [];
        public int Begun { get; private set; }
        public string? Fault { get; set; }
        public bool HideNextLookup { get; set; }

        public void Seed(RecordWriteOperationKey key, RecordWriteCommittedOperation committed) => _operations[key.IdempotencyKey] = committed;

        public ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
        {
            Lookups.Add(key);
            if (HideNextLookup)
            {
                HideNextLookup = false;
                return ValueTask.FromResult<RecordWriteCommittedOperation?>(null);
            }
            return ValueTask.FromResult(_operations.GetValueOrDefault(key.IdempotencyKey));
        }

        public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
        {
            Begun++;
            Events.Add("begin");
            return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
        }

        private sealed class Transaction(AcceptedCommandPort owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
        {
            private KernelOperationIdentity? _operation;
            private RecordWriteRow? _row;
            private KernelAuditEvidence? _audit;

            public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("operation");
                _operation = operation;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("record");
                _row = record;
                return ValueTask.CompletedTask;
            }

            public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
            {
                owner.Events.Add("audit");
                _audit = audit;
                owner.StagedAudits.Add(audit);
                return ValueTask.CompletedTask;
            }

            public ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("commit");
                if (owner.Fault == "commit") throw new InjectedFault();
                var operation = _operation!;
                if (owner._operations.TryGetValue(operation.IdempotencyKey, out var prior))
                    return ValueTask.FromResult(new RecordWriteCommit(prior.IntentDigest == operation.Fingerprint
                        ? RecordWriteCommitDisposition.Replayed : RecordWriteCommitDisposition.Conflict, prior.Receipt) { Stored = prior });
                var row = _row!;
                var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                    row.DefinitionDigest, row.Schema, row.RecordedAt);
                owner._operations.Add(operation.IdempotencyKey, new(row.IntentEncodingVersion, row.IntentDigest, receipt));
                owner.Published.Add((operation, row, _audit!));
                return ValueTask.FromResult(new RecordWriteCommit(RecordWriteCommitDisposition.Created, receipt));
            }

            public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
            {
                owner.Events.Add("rollback");
                return ValueTask.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                owner.Events.Add("dispose");
                return ValueTask.CompletedTask;
            }
        }
    }
}
