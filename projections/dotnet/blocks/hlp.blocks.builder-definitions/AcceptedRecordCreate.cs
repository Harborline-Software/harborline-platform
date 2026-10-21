using System.Globalization;
using System.Text.Json.Nodes;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// The scope a host supplies when its worker completes durably accepted Records commands (ADR-0110 decision 4, plan W7/W8):
/// the tenant the worker acts in and the worker's own identity, from host configuration and never from a request or a
/// stored row. The worker is the actual executor; it never acts as the submitter.
/// </summary>
/// <param name="Tenant">The tenant the worker completes the command in; it must equal the command's tenant.</param>
/// <param name="WorkerId">The worker's own principal, decided by the gate for the bounded act
/// <see cref="AcceptedRecordCommandAuthorization.Act"/>; it must differ from the command's submitter.</param>
public sealed record RecordWorkerScope(string Tenant, string WorkerId);

/// <summary>
/// One durably accepted Records create (ADR-0110 decision 1, plan W6/W8): a create the submitter was authorized for at
/// acceptance, whose exact inputs, authority snapshot and delivery record were committed together in the accepting store
/// (the Forms submission store's outbox row), handed to <see cref="RecordWriteOperation.CompleteAcceptedAsync"/> by the
/// host's recovery worker. Every member is copied from that durable row and is immutable after acceptance.
/// </summary>
/// <param name="Tenant">The tenant the command was accepted in.</param>
/// <param name="CommandId">The accepted command's durable identity in the accepting store; the worker's bounded act is
/// decided on it, and the record's provenance links to it.</param>
/// <param name="SubmitterId">The submitter the acceptance decided for. Provenance only: it scopes the operation id, the audit
/// carries it as <see cref="Harborline.Kernel.Core.KernelAuditEvidence.OriginatorId"/>, and nothing is decided for it again.</param>
/// <param name="OperationId">The original operation identity, server-derived at acceptance; held to the same bound as a
/// direct create's (nonempty, at most 255 UTF-8 bytes, never trimmed).</param>
/// <param name="RecordTypeId">The Record Type the accepted command creates, bound into the submit intent and its evidence at
/// acceptance (K9, E3/E11).</param>
/// <param name="Body">The record fields the accepted payload maps to.</param>
/// <param name="BodyDigestEncoding">The encoding the acceptance digest was computed under; it must be
/// <see cref="RecordWriteIntent.EncodingVersion"/> (review M4).</param>
/// <param name="BodyDigest">The digest of the mapped body bound at acceptance (plan W6): <see cref="RecordWriteIntent.Digest"/>
/// of <see cref="RecordWriteIntent.Encode"/> over the Record Type, no pin and the body. The completion recomputes it from
/// <paramref name="Body"/> and refuses a mismatch before materializing.</param>
/// <param name="FormRevision">The accepted form revision, <c>{formId}@{version}</c>: the target the acceptance decision was
/// asked for (review M3).</param>
/// <param name="AcceptanceAuditId">The id of the acceptance audit committed with the command (review M3, L7).</param>
/// <param name="AcceptanceDecidedAt">The exact instant of the acceptance decision (review M3).</param>
/// <param name="AcceptedAt">The acceptance instant; the acceptance decision was made at or before it.</param>
/// <param name="AcceptanceSnapshot">The acceptance decision's own authority snapshot, as committed with the command, in the
/// <see cref="AuthoritySnapshotEncoding"/> grammar.</param>
public sealed record AcceptedRecordCreate(
    string Tenant,
    string CommandId,
    string SubmitterId,
    string OperationId,
    string RecordTypeId,
    JsonObject Body,
    string BodyDigestEncoding,
    string BodyDigest,
    string FormRevision,
    string AcceptanceAuditId,
    DateTimeOffset AcceptanceDecidedAt,
    DateTimeOffset AcceptedAt,
    ReadOnlyMemory<byte> AcceptanceSnapshot);

/// <summary>
/// The host decider contract for completing an accepted Records command (ADR-0110 decision 4, ruling K7, plan W8): the one
/// request <see cref="RecordWriteOperation.CompleteAcceptedAsync"/> sends, before any other stage, for the worker's own
/// bounded act. Platform calculates no verdict; it states the question.
/// </summary>
/// <remarks>
/// <para><b>What Platform sends:</b> operation <see cref="Act"/>, the worker as principal, the worker scope's tenant, an
/// <see cref="AccessRecord"/> of kind <see cref="RecordKind"/> whose id is the accepted command's id, whose
/// <see cref="AccessRecord.RecordTypeId"/> is the command's Record Type, and whose fields name the command's submitter
/// (<see cref="SubmitterField"/>) and operation id (<see cref="OperationIdField"/>), at the act instant.</para>
/// <para><b>What a conforming host adapter decides (W10):</b> it allows only its configured recovery worker, only for a
/// command its accepting store durably holds in that tenant, for that type, submitter and operation id, and only while
/// that command is claimed for execution; it never lets a tenant-wide or root grant (for example a scheduler's
/// <c>records:write@/</c>) satisfy this act. Engine identity alone authorizes no other tenant or type (ruling D3).</para>
/// <para><b>Open item 4, accepted as implemented 2026-10-12:</b> the spellings <see cref="Act"/> and
/// <see cref="RecordKind"/>. Each is one named constant and nothing else spells it. Host enablement (W10) stays
/// held.</para>
/// </remarks>
public static class AcceptedRecordCommandAuthorization
{
    /// <summary>The worker's bounded act (open item 4, accepted 2026-10-12).</summary>
    public const string Act = "records:complete-accepted";

    /// <summary>The access record kind naming one accepted command (open item 4, accepted 2026-10-12).</summary>
    public const string RecordKind = "accepted-command";

    /// <summary>The access record field carrying the command's submitter.</summary>
    public const string SubmitterField = "submitter";

    /// <summary>The access record field carrying the command's operation id.</summary>
    public const string OperationIdField = "operation_id";

    /// <summary>Builds the one request the worker's bounded act is decided on.</summary>
    public static AccessRequest Request(RecordWorkerScope worker, AcceptedRecordCreate command, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(worker);
        ArgumentNullException.ThrowIfNull(command);
        var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            [SubmitterField] = JsonValue.Create(command.SubmitterId),
            [OperationIdField] = JsonValue.Create(command.OperationId),
        };
        return new AccessRequest(Act, worker.WorkerId, worker.Tenant,
            new AccessRecord(worker.Tenant, RecordKind, command.CommandId, fields) { RecordTypeId = command.RecordTypeId }, at);
    }
}

/// <summary>
/// The readings of ADR-0110 (accepted 2026-10-12) that this package implements, isolated here so each is one change
/// (ADR-0110 decision 1's creation-authorization timing, decision 7 and open item 11; the Forms side holds the rest).
/// </summary>
public static class AcceptedRecordCommandReadings
{
    /// <summary>
    /// ADR-0110 decision 1, <i>interpretation, accepted 2026-10-12</i>: for an accepted command, creation authorization
    /// was decided once, at acceptance, by the form's submit gate (ADR-0043: the submit gate is the create gate), against
    /// the submitter's then-current authority. The completion decides only the worker's bounded act and does not re-decide
    /// the submitter (rulings D4, K2: a later ordinary revocation does not implicitly cancel). The one place this reading is
    /// applied is the gate request in <see cref="RecordWriteOperation.CompleteAcceptedAsync"/>; the other reading would add a
    /// second request there, for the submitter's <c>records:write</c> on the type, which is ADR-0100 decision 3's commit-time
    /// rule, and refuse a revoked submitter's command.
    /// </summary>
    public const string CreationAuthorizationDecided = "at-acceptance";

    /// <summary>
    /// Open item 11 and decision 7, <i>reading implemented, accepted 2026-10-12</i>: only a form-originated record create
    /// of a <c>form_only</c> Record Type is an accepted command. A bound revision whose creation gate is <c>engine</c>
    /// refuses <c>records.write.engine_only</c>; one that is now <c>role</c> refuses
    /// <c>records.command.creation_gate_changed</c>, because the acceptance decided the submit gate, not the role (fail
    /// closed rather than widening). A <c>role</c> type's form creates through the direct create as its submitter (D1),
    /// which stays ordinary work decided at commit (ADR-0100 decision 3).
    /// </summary>
    public static bool AdmitsCreationGate(RecordCreationGate gate) => gate == RecordCreationGate.FormOnly;

    /// <summary>
    /// The act of the decision that accepts a form-originated Records command: the Forms submit-gate operation
    /// (<c>FormSubmitGateAuthority.Operation</c> in forms-engine, which this package cannot reference). Open item 4,
    /// accepted as implemented 2026-10-12; one named constant.
    /// </summary>
    public const string AcceptanceAct = "forms:submit-gate";

    /// <summary>The evidence kind of that decision: a gate decision (open item 4, accepted 2026-10-12).</summary>
    public const string AcceptanceKind = "Gate";

    /// <summary>The access record kind of the accepted form revision the decision targets (open item 4, accepted 2026-10-12).</summary>
    public const string AcceptanceTargetKind = "form";

    /// <summary>
    /// Review M3: the acceptance evidence is bound to this command's own stored acceptance decision, not to any allowed
    /// decision of the submitter. The snapshot must decode in the shared grammar; name the command's tenant and submitter at
    /// the top level and in the act stage; record kind <see cref="AcceptanceKind"/>, act <see cref="AcceptanceAct"/> and
    /// target <c>form/{FormRevision}</c>; the verdict <c>allowed</c>; and an <c>at:</c> instant exactly equal to
    /// <see cref="AcceptedRecordCreate.AcceptanceDecidedAt"/>, which is at or before acceptance. The command must also name
    /// its acceptance audit and form revision. A host decider that spells the act or target otherwise is refused (fail
    /// closed; open item 4).
    /// <para><b>What bytes alone cannot prove</b> (escalated in the HANDOFF): that the accepting store holds this command
    /// and that the form revision targets this Record Type. Those facts live in the Forms store, which this package cannot
    /// read; a caller holding the worker identity and a genuine past acceptance snapshot of the same submitter, form revision
    /// and instant could still assemble a command. Closing that needs either a cross-module acceptance-ledger port (a new
    /// contract) or the W10 decider's check that the command is held and claimed.</para>
    /// </summary>
    public static bool IsAcceptanceEvidence(AcceptedRecordCreate command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.FormRevision) || string.IsNullOrWhiteSpace(command.AcceptanceAuditId)
            || command.AcceptanceDecidedAt > command.AcceptedAt
            || AuthoritySnapshotEncoding.TryDecode(command.AcceptanceSnapshot.Span) is not { } snapshot
            || !StringComparer.Ordinal.Equals(snapshot.Tenant, command.Tenant)
            || !StringComparer.Ordinal.Equals(snapshot.Principal, command.SubmitterId))
            return false;
        return StringComparer.Ordinal.Equals(ActFact(snapshot, "kind:"), AcceptanceKind)
            && StringComparer.Ordinal.Equals(ActFact(snapshot, "act:"), AcceptanceAct)
            && StringComparer.Ordinal.Equals(ActFact(snapshot, "target:"), AcceptanceTargetKind + "/" + command.FormRevision)
            && StringComparer.Ordinal.Equals(ActFact(snapshot, "principal:"), command.SubmitterId)
            && StringComparer.Ordinal.Equals(ActFact(snapshot, "tenant:"), command.Tenant)
            && StringComparer.Ordinal.Equals(AuthoritySnapshotEncoding.Fact(snapshot, AuthorizationDecisionEvidence.VerdictStage, "verdict:"), "allowed")
            && ActFact(snapshot, "at:") is { } at
            && DateTimeOffset.TryParseExact(at, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var decidedAt)
            && decidedAt == command.AcceptanceDecidedAt;
    }

    private static string? ActFact(AuthorizationTraceSnapshot snapshot, string prefix) =>
        AuthoritySnapshotEncoding.Fact(snapshot, AuthorizationDecisionEvidence.ActStage, prefix);
}

/// <summary>
/// How a host's recovery worker treats a completion outcome (ADR-0110 decisions 2 and 5, DES-0029 section 9): a committed
/// or replayed receipt completes the command; a refusal of the command itself (its binding, its evidence, its operation id,
/// its payload under the original operation, or an execution-time invariant such as the bound type or the schema) is a
/// terminal, audited refusal that is never retried; a refusal of the worker's own act (the authorize stage) and an
/// unreadable receipt for the accepted operation stay pending and retryable, because neither says anything about the
/// command and the unreadable receipt never authorizes another insertion. Infrastructure faults are exceptions and also stay
/// pending.
/// </summary>
public static class AcceptedRecordCreateOutcomes
{
    /// <summary>True when <paramref name="outcome"/> is a terminal refusal of the accepted command.</summary>
    public static bool IsTerminalRefusal(RecordWriteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Refusal is { } refusal
            && !StringComparer.Ordinal.Equals(refusal.Stage, "authorize")
            && !StringComparer.Ordinal.Equals(refusal.Code, "records.write.receipt_unsupported");
    }
}
