using System.Buffers;
using System.Globalization;
using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Engine.Projection;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>
/// The readings of ADR-0110 (accepted 2026-10-12) that this package implements, isolated here so each is one change
/// (revision 16). The Records side holds its own (<c>Harborline.Blocks.BuilderDefinitions.AcceptedRecordCommandReadings</c>).
/// </summary>
public static class FormAcceptedCommandReadings
{
    /// <summary>
    /// Open item 11 and decision 7, <i>reading implemented, accepted 2026-10-12</i>: a submission becomes a durably
    /// accepted Records command only when the published head targets a Record Type whose creation gate is
    /// <c>form_only</c>, whose submit gate is the create gate (ADR-0043), so creation authorization is decided once, at
    /// acceptance. A <c>role</c> target's submission stays an ordinary projection: its record is created through the direct
    /// Records create, decided at commit against current authority (ADR-0100 decision 3), and adds no creation gate (D1).
    /// </summary>
    public static bool Qualifies(FormDefinition head, Harborline.Foundation.Forms.RecordTypeCreationGate? creationGate)
    {
        ArgumentNullException.ThrowIfNull(head);
        return head.RecordTypeId is not null && creationGate == Harborline.Foundation.Forms.RecordTypeCreationGate.FormOnly;
    }

    /// <summary>
    /// Decision 1, <i>interpretation, accepted 2026-10-12</i>: the acceptance decision is ordered against every concurrent
    /// revocation by deciding the submit gate inside the acceptance transaction, after the store's exclusion scope is opened
    /// and before it commits (the existing prepared-transaction contract: preparation reads inside the transaction). A
    /// revocation that commits first is seen and refuses the acceptance; one that commits afterwards falls under decision 3
    /// and does not cancel the command. The ordering holds in production only when the host's store scope contends on the
    /// fence its revocations take (open item 2, a W10 obligation). False would decide the gate before the transaction, as an
    /// ordinary submit does.
    /// <para><b>The decider contract this reading imposes (review M5; a W10 acceptance criterion):</b> the host's
    /// submit-gate decider runs while the store's exclusion scope is held. It must make <b>no writes</b> and open <b>no
    /// transaction of its own</b> (a nested <c>KernelTransactionBoundary</c> execution is refused and the submit denied); it
    /// must read current grants on the acceptance transaction's own connection, with no cache, so a revocation committed
    /// first is seen; and it must be fast. Platform enforces what its contracts allow: a call back into a built-in submission
    /// store from that flow is refused at once with <c>form.engine.store-reentrant</c> (<see cref="FormEngineStoreReentrancyException"/>)
    /// instead of deadlocking, and a decision or a mapped-body digest (<see cref="IFormAcceptedRecordBodies"/>, review R2-L3)
    /// that exceeds <see cref="FormEngineOptions.AcceptanceDecisionTimeout"/> is abandoned with
    /// <c>form.engine.resource-bound</c> and the scope released. A W10 store must refuse re-entry the same way, and a W10
    /// decider over SQLite or PostgreSQL must not wait on the write lock its own acceptance holds. The other host code that
    /// runs inside the scope and predates revision 16 (field protection, reuse resolution, rule evaluation) is not bounded by
    /// this option; the same obligations (no writes, no own transaction, no store re-entry, fast) bind it, a W10 acceptance
    /// criterion.</para>
    /// </summary>
    public static bool DecideAcceptanceInsideTransaction => true;

    /// <summary>
    /// Open item 3, <i>reading implemented</i>: the operation identity of a form-originated command is server-derived from the
    /// committed form instance id, never the client's idempotency key, so two forms submitted with one key by one person never
    /// share a Records operation, and a retry of one submit (which replays the same instance) keeps it.
    /// </summary>
    public static string OperationIdFor(EntityId instanceId) => instanceId.ToString();
}

/// <summary>
/// The host's mapping of a form's accepted values to the Records body its accepted command will create (review M4, plan
/// W6). At acceptance the engine asks for the digest of that body, computed with the Records operation's own intent encoder
/// (<c>RecordWriteIntent</c>, <c>records-write-intent/1</c> over the record type, no pin and the body), and binds it into the
/// command; the Records completion recomputes the digest from the body it is given and refuses a mismatch with
/// <c>records.command.binding_mismatch</c> before materializing. The host's execution must map with the same deterministic
/// mapping. A form_only submit with no such port is refused (<c>form.engine.provider-unavailable</c>) before the gate is
/// asked. The engine takes it as a constructor argument, or else from a projection sink that implements it. It runs while
/// the acceptance holds the store's exclusion scope, so it is held to <see cref="FormEngineOptions.AcceptanceDecisionTimeout"/>
/// (review R2-L3; past it the submit is refused <c>form.engine.resource-bound</c>) and must not write or call back into the
/// submission store (refused <c>form.engine.store-reentrant</c>).
/// </summary>
public interface IFormAcceptedRecordBodies
{
    /// <summary>Maps the accepted values to the Records body and returns that body's digest and its encoding.</summary>
    ValueTask<FormAcceptedRecordBodyDigest> DigestAsync(FormAcceptedRecordBodyRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One accepted payload to map: the tenant, the published revision, the target Record Type and the accepted values.</summary>
/// <param name="Tenant">The submission tenant.</param>
/// <param name="Definition">The effective published revision the values were accepted under.</param>
/// <param name="RecordTypeId">The Record Type the command creates.</param>
/// <param name="AcceptedCandidate">The accepted values, before protection; the port must not retain or log them.</param>
public sealed record FormAcceptedRecordBodyRequest(TenantId Tenant, FormDefinition Definition, string RecordTypeId, JsonDocument AcceptedCandidate);

/// <summary>The digest of a mapped Records body and the encoding it was computed under.</summary>
/// <param name="EncodingVersion">The intent encoding version, <c>records-write-intent/1</c> today.</param>
/// <param name="Digest">The lowercase hexadecimal digest.</param>
public sealed record FormAcceptedRecordBodyDigest(string EncodingVersion, string Digest);

/// <summary>
/// Review M3: the one acceptance-evidence predicate, applied by the engine at acceptance and before execution and mirrored
/// by the Records completion. It binds the stored snapshot to this command's own acceptance decision, not to any allowed
/// decision of the submitter: the snapshot must decode, name the command's tenant and submitter (top level and act stage),
/// record kind <see cref="AcceptanceKind"/>, act <see cref="FormSubmitGateAuthority.Operation"/> and target
/// <c>form/{formId}@{version}</c> of the command's own form revision, the verdict <c>allowed</c>, and an <c>at:</c> instant
/// exactly equal to the command's acceptance decision instant, which is at or before acceptance. A host decider whose
/// evidence spells the act or target differently is refused at acceptance (fail closed; the spellings are open item 4).
/// </summary>
public static class FormAcceptedCommandEvidence
{
    /// <summary>The evidence kind of a gate decision (the default kind of <see cref="AuthorizationDecisionEvidence"/>).</summary>
    public const string AcceptanceKind = "Gate";

    /// <summary>True when <paramref name="snapshot"/> is exactly this command's acceptance decision, as stated above.</summary>
    public static bool IsAcceptance(ReadOnlySpan<byte> snapshot, string tenant, string submitter, string formRevision,
        DateTimeOffset decidedAt, DateTimeOffset acceptedAt)
    {
        if (AuthoritySnapshotEncoding.TryDecode(snapshot) is not { } decoded || decidedAt > acceptedAt
            || !StringComparer.Ordinal.Equals(decoded.Tenant, tenant)
            || !StringComparer.Ordinal.Equals(decoded.Principal, submitter))
            return false;
        return StringComparer.Ordinal.Equals(ActFact(decoded, "kind:"), AcceptanceKind)
            && StringComparer.Ordinal.Equals(ActFact(decoded, "act:"), FormSubmitGateAuthority.Operation)
            && StringComparer.Ordinal.Equals(ActFact(decoded, "target:"), FormSubmitGateAuthority.RecordKind + "/" + formRevision)
            && StringComparer.Ordinal.Equals(ActFact(decoded, "principal:"), submitter)
            && StringComparer.Ordinal.Equals(ActFact(decoded, "tenant:"), tenant)
            && StringComparer.Ordinal.Equals(AuthoritySnapshotEncoding.Fact(decoded, AuthorizationDecisionEvidence.VerdictStage, "verdict:"), "allowed")
            && ActFact(decoded, "at:") is { } at
            && DateTimeOffset.TryParseExact(at, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recorded)
            && recorded == decidedAt;
    }

    private static string? ActFact(AuthorizationTraceSnapshot snapshot, string prefix) =>
        AuthoritySnapshotEncoding.Fact(snapshot, AuthorizationDecisionEvidence.ActStage, prefix);
}

/// <summary>
/// Review M1 and R2-M1: the audit evidence of a terminal or quarantining accepted-command transition, through the existing
/// audit contract (<see cref="KernelAuditEvidence"/>, stored by the submission store as one more audit envelope of the
/// submission, in the same atomic transition). The payload names identifiers and the outcome only, never a value.
/// </summary>
/// <remarks>Review R2-L5: every persisted spelling is one named constant here, accepted 2026-10-12 under open items 4
/// and 7; renaming one after audits exist is a migration.</remarks>
internal static class FormAcceptedCommandAudit
{
    // Payload operations (open item 4, accepted 2026-10-12).
    internal const string CancelledOperation = "accepted-command-cancelled";
    internal const string RefusedOperation = "accepted-command-refused";
    internal const string QuarantinedOperation = "accepted-command-quarantined";
    internal const string ResolvedOperation = "accepted-command-resolved";

    // Payload keys (open item 4, accepted 2026-10-12).
    internal const string OperationKey = "op";
    internal const string CommandIdKey = "command_id";
    internal const string OperationIdKey = "operation_id";
    internal const string RecordTypeIdKey = "record_type_id";
    internal const string CodeKey = "code";
    internal const string AfterClaimKey = "after_claim";

    // A cancel of a pending command (afterClaim false), or the abandonment of a quarantined one (afterClaim true).
    internal static KernelAuditEvidence Cancelled(string commandId, string actorId, DateTimeOffset at, byte[] authoritySnapshot, bool afterClaim = false) =>
        new(Guid.NewGuid().ToString("N"), actorId, at,
            Payload(CancelledOperation, commandId, null, null, FormAcceptedCommandAuthority.Cancelled, afterClaim ? true : (bool?)null))
        {
            AuthoritySnapshot = authoritySnapshot,
        };

    internal static KernelAuditEvidence Refused(FormAcceptedRecordCommand command, FormAcceptedCommandState stateBefore, string workerId,
        DateTimeOffset at, string code) =>
        new(Guid.NewGuid().ToString("N"), workerId, at,
            Payload(RefusedOperation, command.CommandId, command.OperationId, command.RecordTypeId, code, stateBefore == FormAcceptedCommandState.Claimed));

    // Review R2-M1: always after a claim; the interrupted run may have committed its Records effect.
    internal static KernelAuditEvidence Quarantined(FormAcceptedRecordCommand command, string workerId, DateTimeOffset at, string code) =>
        new(Guid.NewGuid().ToString("N"), workerId, at,
            Payload(QuarantinedOperation, command.CommandId, command.OperationId, command.RecordTypeId, code, true));

    // Review R3-L1: a quarantined command completed because the host's receipt lookup found its effect committed; the code
    // is the reason it was quarantined with.
    internal static KernelAuditEvidence Resolved(FormAcceptedRecordCommand command, string actorId, DateTimeOffset at) =>
        new(Guid.NewGuid().ToString("N"), actorId, at,
            Payload(ResolvedOperation, command.CommandId, command.OperationId, command.RecordTypeId,
                command.RefusalCode ?? FormAcceptedCommandAuthority.BindingMismatch, true));

    private static byte[] Payload(string operation, string commandId, string? operationId, string? recordTypeId, string code, bool? afterClaim)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(OperationKey, operation);
            writer.WriteString(CommandIdKey, commandId);
            if (operationId is not null) writer.WriteString(OperationIdKey, operationId);
            if (recordTypeId is not null) writer.WriteString(RecordTypeIdKey, recordTypeId);
            writer.WriteString(CodeKey, code);
            if (afterClaim is { } claimed) writer.WriteBoolean(AfterClaimKey, claimed);
            writer.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>
/// The ruled cancellation permission and the one request a cancel is decided on (the owner's cancellation rule,
/// 2026-10-10; ADR-0110 decision 5). Platform calculates no verdict.
/// </summary>
/// <remarks>
/// <para><c>records.command.cancel</c> is a dedicated permission, granted explicitly to the intended tenant administrators
/// and never automatically to submitters. A conforming host adapter allows it only on such an explicit grant in the request's
/// tenant. Its spelling as the act is open item 4, and which grant makes an intended tenant administrator is open item 5
/// (both accepted 2026-10-12); the spellings below are the ruled name and the record kind.</para>
/// </remarks>
public static class FormAcceptedCommandAuthority
{
    /// <summary>The ruled cancellation permission, asked as the act.</summary>
    public const string CancelAct = "records.command.cancel";

    /// <summary>The access record kind naming one accepted command (open item 4, accepted 2026-10-12).</summary>
    public const string RecordKind = "accepted-command";

    /// <summary>The stable code a cancel that lost the fence is answered with.</summary>
    public const string CancelTooLate = "records.command.cancel_too_late";

    /// <summary>The code of a won or replayed cancel (terminal-state name provisional, open item 7).</summary>
    public const string Cancelled = "records.command.cancelled";

    /// <summary>The code of a cancel naming no accepted command of the tenant (provisional, open item 7).</summary>
    public const string NotFound = "records.command.not_found";

    /// <summary>The terminal refusal of a command whose stored binding does not hold (provisional, open item 7).</summary>
    public const string BindingMismatch = "records.command.binding_mismatch";

    /// <summary>The terminal refusal of a command without valid persisted acceptance evidence (provisional, open item 7).</summary>
    public const string NotAccepted = "records.command.not_accepted";

    /// <summary>The pending code of a command whose configured worker is its own submitter; it is never executed.</summary>
    public const string WorkerIsSubmitter = "form.engine.accepted-command.worker-is-submitter";

    /// <summary>Builds the request a cancel is decided on: the canceller, the exact tenant and the exact command.</summary>
    public static AccessRequest CancelRequest(FormAcceptedCommandCancelScope scope, string commandId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var tenant = scope.Tenant.Value;
        return new AccessRequest(CancelAct, scope.ActorId, tenant,
            new AccessRecord(tenant, RecordKind, commandId, new Dictionary<string, System.Text.Json.Nodes.JsonNode?>()), at);
    }
}

/// <summary>The ambient scope of one cancel, from the host's authenticated context and never from a request body.</summary>
/// <param name="Tenant">The tenant the cancel is asked in.</param>
/// <param name="ActorId">The authenticated principal asking to cancel.</param>
public sealed record FormAcceptedCommandCancelScope(TenantId Tenant, string ActorId);

/// <summary>How one cancel ended.</summary>
public enum FormAcceptedCommandCancelStatus
{
    /// <summary>The cancel holds the fence: the command is cancelled and its effect has not committed and cannot commit.</summary>
    Cancelled,
    /// <summary>Execution crossed the fence first; <c>records.command.cancel_too_late</c>.</summary>
    TooLate,
    /// <summary>The tenant holds no such accepted command.</summary>
    NotFound,
    /// <summary>The gate refused the cancel permission; nothing was read or changed.</summary>
    Denied,
}

/// <summary>The outcome of one cancel.</summary>
/// <param name="Status">How the cancel ended.</param>
/// <param name="Code">The stable code: the gate's refusal reason when denied, otherwise one of the
/// <see cref="FormAcceptedCommandAuthority"/> codes.</param>
/// <param name="Replayed">True when the command was already cancelled (an idempotent retry).</param>
/// <param name="Decision">The gate's own evidence for this cancel, for the host's trace plumbing; on a won cancel the same
/// evidence was committed with the cancellation as its authority snapshot. It carries role-binding facts, so a host must
/// not serialize it to clients (D6).</param>
public sealed record FormAcceptedCommandCancelResult(
    FormAcceptedCommandCancelStatus Status,
    string Code,
    bool Replayed,
    AuthorizationDecisionEvidence? Decision);

/// <summary>What the host's Records receipt lookup established about one accepted command's effect (review R2-M1).</summary>
public enum FormAcceptedCommandEffect
{
    /// <summary>Nothing was established; the command is treated as possibly committed (fail closed).</summary>
    Unknown,
    /// <summary>The Records effect of the command's original operation committed.</summary>
    Committed,
    /// <summary>The abandonment condition of DES-0029 section 9 holds: the effect did not commit and cannot still commit.</summary>
    Absent,
}

/// <summary>One quarantined command whose Records effect the host is asked about.</summary>
/// <param name="Tenant">The command's tenant.</param>
/// <param name="Command">The quarantined command: its id, original operation id, Record Type and claiming worker.</param>
public sealed record FormAcceptedCommandEffectQuery(TenantId Tenant, FormAcceptedRecordCommand Command);

/// <summary>
/// Review R2-M1 (DES-0029 section 9, owner ruling 123): the host's Records receipt lookup for a quarantined command, the only
/// way Platform learns that a claimed command's abandonment condition holds. A conforming host (W10) answers
/// <see cref="FormAcceptedCommandEffect.Absent"/> only when the Records store holds no materialization of the command's
/// original operation (keyed as <c>RecordWriteOperation</c> keys an accepted command, or by
/// <c>RecordWriteRow.AcceptedCommandId</c>) <b>and</b> no execution of it can still commit: every
/// <see cref="Projection.IFormProjectionSink.DeliverAcceptedAsync"/> call for it has returned or thrown only after its
/// Records transaction ended, and Platform never executes a quarantined command again. Anything less is
/// <see cref="FormAcceptedCommandEffect.Unknown"/>. It answers <see cref="FormAcceptedCommandEffect.Committed"/> only when
/// that materialization exists; <see cref="FormAcceptedCommandResolution"/> then completes the command (review R3-L1).
/// Platform supplies no implementation; without one a quarantined command is never cancelled or resolved (fail closed).
/// </summary>
public interface IFormAcceptedCommandEffects
{
    /// <summary>Establishes what the command's Records effect is; a failure is thrown and the caller's cancel is retryable.</summary>
    ValueTask<FormAcceptedCommandEffect> FindAsync(FormAcceptedCommandEffectQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// The cancellation lifecycle of durably accepted Records commands (the owner's cancellation rule, ADR-0110 decision 5).
/// A cancel is decided against the canceller's current authority for the exact tenant and command, through the host's sole
/// decider, before anything is read; it then contends with execution on the store's one durable fence
/// (<see cref="IFormAcceptedCommandStore"/>), so cancel and execution cannot both succeed. Retries are idempotent. An
/// infrastructure failure is thrown as <see cref="FormEngineProviderUnavailableException"/> and leaves the command pending
/// and the cancel retryable; it is never a terminal refusal. Amending or deleting a record after completion is a separate
/// authorized action, not a cancel.
/// </summary>
/// <remarks>
/// Every Platform dispatch and recovery consumer of accepted commands honours <c>cancelled</c> at the claim (the engine's
/// submit-time execution and <see cref="IFormEngine.RecoverProjectionsAsync"/>, both through one execution path), which is
/// the precondition the rule sets before cancellation is enabled. Enabling it for a tenant is a host decision (W10); host
/// enablement stays held.
/// <para>Review R2-M1: a <see cref="FormAcceptedCommandState.Quarantined"/> command is claimed, so a cancel is too late
/// unless the abandonment condition of DES-0029 section 9 holds. The cancel then asks the host's
/// <see cref="IFormAcceptedCommandEffects"/>; only <see cref="FormAcceptedCommandEffect.Absent"/> lets it cancel the command,
/// audited as after the claim. Without the port, or on any other answer, it is <c>records.command.cancel_too_late</c>.</para>
/// </remarks>
public sealed class FormAcceptedCommandCancellation
{
    private readonly IFormSubmissionTransactionStore _submissions;
    private readonly AccessProvider _access;
    private readonly TimeProvider _clock;
    private readonly IFormAcceptedCommandEffects? _effects;

    /// <summary>Creates the lifecycle over the submission store that holds the accepted commands and the host's decider;
    /// <paramref name="effects"/> is the host's Records receipt lookup for quarantined commands (none: never abandoned).</summary>
    public FormAcceptedCommandCancellation(IFormSubmissionTransactionStore submissions, AccessProvider access, TimeProvider clock,
        IFormAcceptedCommandEffects? effects = null)
    {
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _effects = effects;
    }

    /// <summary>Cancels the tenant's accepted command <paramref name="commandId"/> if execution has not claimed it.</summary>
    public async ValueTask<FormAcceptedCommandCancelResult> CancelAsync(FormAcceptedCommandCancelScope scope, string commandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.ActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (scope.Tenant == default) throw new ArgumentException("A cancel needs its tenant.", nameof(scope));

        var at = _clock.GetUtcNow();
        var check = await _access.CheckWithEvidenceAsync(FormAcceptedCommandAuthority.CancelRequest(scope, commandId, at), cancellationToken)
            .ConfigureAwait(false);
        if (!check.Check.Allowed) return new(FormAcceptedCommandCancelStatus.Denied, check.Check.Reason, false, check.Evidence);
        var evidence = check.Evidence ?? throw new InvalidOperationException("The gate allowed without its evidence.");

        if (_submissions is not IFormAcceptedCommandStore commands) throw new FormEngineProviderUnavailableException();
        FormAcceptedCommandTransition? transition;
        try
        {
            // Review M1: the cancellation is audited through the existing audit contract, atomically with the transition.
            transition = await commands.CancelAcceptedCommandAsync(scope.Tenant, commandId,
                FormAcceptedCommandAudit.Cancelled(commandId, scope.ActorId, at, AuthoritySnapshotEncoding.Encode(evidence)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw new FormEngineProviderUnavailableException(failure); }

        if (transition is null) return new(FormAcceptedCommandCancelStatus.NotFound, FormAcceptedCommandAuthority.NotFound, false, evidence);
        if (transition.Command.State == FormAcceptedCommandState.Quarantined)
            transition = await AbandonAsync(commands, scope, transition.Command, at, evidence, cancellationToken).ConfigureAwait(false) ?? transition;
        if (transition.Command.State == FormAcceptedCommandState.Cancelled)
            return new(FormAcceptedCommandCancelStatus.Cancelled, FormAcceptedCommandAuthority.Cancelled, !transition.Changed, evidence);
        return new(FormAcceptedCommandCancelStatus.TooLate, FormAcceptedCommandAuthority.CancelTooLate, false, evidence);
    }

    // Review R2-M1: a quarantined command is cancelled only once the host establishes that its effect did not commit and
    // cannot still commit. Returns the abandonment, or null when the condition was not established (the cancel is too late).
    private async ValueTask<FormAcceptedCommandTransition?> AbandonAsync(IFormAcceptedCommandStore commands, FormAcceptedCommandCancelScope scope,
        FormAcceptedRecordCommand command, DateTimeOffset at, AuthorizationDecisionEvidence evidence, CancellationToken cancellationToken)
    {
        if (_effects is null) return null;
        try
        {
            var effect = await _effects.FindAsync(new(scope.Tenant, command), cancellationToken).ConfigureAwait(false);
            if (effect != FormAcceptedCommandEffect.Absent) return null;
            return await commands.AbandonQuarantinedCommandAsync(scope.Tenant, command.CommandId,
                FormAcceptedCommandAudit.Cancelled(command.CommandId, scope.ActorId, at, AuthoritySnapshotEncoding.Encode(evidence), afterClaim: true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception failure) { throw new FormEngineProviderUnavailableException(failure); }
    }
}

/// <summary>How one resolution of a quarantined command ended (review R3-L1).</summary>
public enum FormAcceptedCommandResolveStatus
{
    /// <summary>The receipt lookup found the effect committed; the command is completed, audited.</summary>
    Completed,
    /// <summary>The lookup did not establish a committed effect; the command stays quarantined and nothing changed.</summary>
    Unresolved,
    /// <summary>The command is not quarantined (pending, claimed, or already settled); nothing was asked or changed.</summary>
    NotQuarantined,
    /// <summary>The tenant holds no such accepted command.</summary>
    NotFound,
}

/// <summary>The outcome of one resolution.</summary>
/// <param name="Status">How the resolution ended.</param>
/// <param name="Effect">What the host's receipt lookup answered; <see cref="FormAcceptedCommandEffect.Unknown"/> when it was
/// not asked.</param>
public sealed record FormAcceptedCommandResolveResult(FormAcceptedCommandResolveStatus Status, FormAcceptedCommandEffect Effect);

/// <summary>
/// Review R3-L1: the one way a <see cref="FormAcceptedCommandState.Quarantined"/> command leaves quarantine as completed.
/// It asks the host's Records receipt lookup (<see cref="IFormAcceptedCommandEffects"/>) and, only when it answers
/// <see cref="FormAcceptedCommandEffect.Committed"/>, completes the command through
/// <see cref="IFormAcceptedCommandStore.ResolveQuarantinedCommandAsync"/> with an audit (op
/// <c>accepted-command-resolved</c>, <c>after_claim: true</c>) committed in the same transition. Any other answer changes
/// nothing; an absent effect is resolved by a cancel (<see cref="FormAcceptedCommandCancellation"/>), never here. The host
/// runs it from its operator or recovery job context as <c>workerId</c> (host configuration, like the
/// execution worker); it creates no Records effect, so no permission is decided (a reading, accepted 2026-10-12).
/// An infrastructure failure throws <see cref="FormEngineProviderUnavailableException"/> and changes nothing.
/// </summary>
/// <param name="submissions">The submission store that holds the accepted commands.</param>
/// <param name="effects">The host's Records receipt lookup.</param>
/// <param name="clock">The clock the audit instant is read from.</param>
/// <param name="workerId">The configured identity the resolution is audited as.</param>
public sealed class FormAcceptedCommandResolution(
    IFormSubmissionTransactionStore submissions,
    IFormAcceptedCommandEffects effects,
    TimeProvider clock,
    string workerId)
{
    private readonly IFormSubmissionTransactionStore _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
    private readonly IFormAcceptedCommandEffects _effects = effects ?? throw new ArgumentNullException(nameof(effects));
    private readonly TimeProvider _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly string _workerId = string.IsNullOrWhiteSpace(workerId) ? throw new ArgumentException("A worker identity is required.", nameof(workerId)) : workerId;

    /// <summary>Completes the tenant's quarantined command <paramref name="commandId"/> if its effect is found committed.</summary>
    public async ValueTask<FormAcceptedCommandResolveResult> ResolveAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        if (tenant == default) throw new ArgumentException("A resolution needs its tenant.", nameof(tenant));
        if (_submissions is not IFormAcceptedCommandStore commands) throw new FormEngineProviderUnavailableException();
        try
        {
            var command = await commands.GetAcceptedCommandAsync(tenant, commandId, cancellationToken).ConfigureAwait(false);
            if (command is null) return new(FormAcceptedCommandResolveStatus.NotFound, FormAcceptedCommandEffect.Unknown);
            if (command.State != FormAcceptedCommandState.Quarantined)
                return new(FormAcceptedCommandResolveStatus.NotQuarantined, FormAcceptedCommandEffect.Unknown);
            var effect = await _effects.FindAsync(new(tenant, command), cancellationToken).ConfigureAwait(false);
            if (effect != FormAcceptedCommandEffect.Committed) return new(FormAcceptedCommandResolveStatus.Unresolved, effect);
            var resolved = await commands.ResolveQuarantinedCommandAsync(tenant, commandId,
                FormAcceptedCommandAudit.Resolved(command, _workerId, _clock.GetUtcNow()), cancellationToken).ConfigureAwait(false);
            return resolved is { Changed: true, Command.State: FormAcceptedCommandState.Completed }
                ? new(FormAcceptedCommandResolveStatus.Completed, effect)
                : new(FormAcceptedCommandResolveStatus.NotQuarantined, effect);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormEngineException) { throw; }
        catch (Exception failure) { throw new FormEngineProviderUnavailableException(failure); }
    }
}

/// <summary>How one execution attempt of an accepted command ended.</summary>
internal enum FormAcceptedCommandRun
{
    /// <summary>The Records create committed or replayed; the command is completed.</summary>
    Completed,
    /// <summary>The command was refused terminally.</summary>
    Refused,
    /// <summary>Nothing settled; the command stays pending and retryable.</summary>
    Pending,
    /// <summary>The claim found the command cancelled or already settled; nothing was executed.</summary>
    Settled,
    /// <summary>Review R2-M1: the claimed command was quarantined, audited and non-terminal; it is never leased again.</summary>
    Quarantined,
}

/// <summary>
/// The one execution path of an accepted command (plan W7), shared by the engine's submit-time execution and recovery:
/// no impersonation, runtime validation of every stored binding, the durable claim before any effect, then the host's
/// execution as the configured worker.
/// </summary>
internal sealed class FormAcceptedCommandExecution(
    IFormSubmissionTransactionStore submissions,
    IFormProjectionSink projections,
    Harborline.Foundation.Forms.IFormDefinitionStore definitions,
    TimeProvider clock,
    int bindingRetryLimit)
{
    internal async ValueTask<FormAcceptedCommandRun> RunAsync(FormProjectionEnvelope row, string workerId, CancellationToken cancellationToken)
    {
        var command = row.AcceptedCommand ?? throw new InvalidOperationException("The row carries no accepted command.");
        if (submissions is not IFormAcceptedCommandStore commands)
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
        // ADR-0110 decision 4: the worker never acts as the submitter. A misconfiguration leaves the command pending.
        if (StringComparer.Ordinal.Equals(workerId, row.ActorId))
        {
            await RetryAsync(row, FormAcceptedCommandAuthority.WorkerIsSubmitter).ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }

        string? mismatch;
        try
        {
            mismatch = await BindingRefusalAsync(row, command, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(row).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
        if (mismatch is not null)
        {
            // A pending command never executed, so its refusal is terminal at once. A claimed one is never executed with a
            // broken binding either, and never made terminal, because its interrupted run may have committed its Records
            // effect (DES-0029 section 9, owner ruling 123; review R2-M1): each such run is one binding failure (review R2-L1:
            // only binding failures count), and the run that reaches the limit quarantines it, audited and non-terminal.
            if (command.State == FormAcceptedCommandState.Pending)
                return await RefuseAsync(commands, row, command, workerId, mismatch).ConfigureAwait(false);
            if (command.BindingFailures + 1 >= bindingRetryLimit)
                return await QuarantineAsync(commands, row, command, workerId, mismatch).ConfigureAwait(false);
            return await BindingFailureAsync(commands, row, command, mismatch).ConfigureAwait(false);
        }

        FormAcceptedCommandTransition? claim;
        try
        {
            claim = await commands.ClaimAcceptedCommandAsync(command.CommandId, workerId, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(row).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
        // Decision 5: every consumer honours cancelled. Only a command this worker (or an interrupted earlier claim) holds
        // is executed; a cancelled, completed or refused one is not.
        if (claim is not { Command.State: FormAcceptedCommandState.Claimed } held)
        {
            await ReleaseAsync(row).ConfigureAwait(false);
            return FormAcceptedCommandRun.Settled;
        }

        try
        {
            var delivered = await projections.DeliverAcceptedAsync(
                new(workerId, row with { AcceptedCommand = held.Command }, held.Command), cancellationToken).ConfigureAwait(false);
            // Review R2-M1: a Records refusal is terminal only on the run that made the claim, when no earlier run can have
            // committed the effect; after a resumed claim (an interrupted earlier run) the command is quarantined instead.
            if (delivered.TerminalRefusal is { } refusal)
                return held.Changed
                    ? await RefuseAsync(commands, row, held.Command, workerId, refusal).ConfigureAwait(false)
                    : await QuarantineAsync(commands, row, held.Command, workerId, refusal).ConfigureAwait(false);
            await submissions.CompleteProjectionAsync(row.OutboxId, delivered.Skips, cancellationToken).ConfigureAwait(false);
            return FormAcceptedCommandRun.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseAsync(row).ConfigureAwait(false);
            throw;
        }
        catch
        {
            // DES-0029 section 9: an interrupted execution stays pending and claimed; recovery executes it again and the
            // Records operation settles it idempotently on the original operation.
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
    }

    // D3, plan W7: every stored binding is validated again before any effect. The row and the command must agree with each
    // other, with the stored submission (instance, submitter, Party, form revision, schema, target, payload fingerprint and
    // the exact protected values) and with the form revision's own target, and the acceptance snapshot must be the
    // submitter's allowed decision in the row's tenant, decided at or before acceptance. Returns the refusal code, or null
    // when the binding holds; a read failure is thrown and leaves the command pending. Review R2-L1: a form revision that is
    // not found is such a read failure (it may be transient), never a binding mismatch.
    private async ValueTask<string?> BindingRefusalAsync(FormProjectionEnvelope row, FormAcceptedRecordCommand command, CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(command.CommandId, row.OutboxId)
            || !StringComparer.Ordinal.Equals(command.OperationId, FormAcceptedCommandReadings.OperationIdFor(row.InstanceId))
            || !StringComparer.Ordinal.Equals(command.RecordTypeId, row.RecordTypeId)
            || command.AcceptedAt != row.SubmittedAt
            || row.SchemaRef is null)
            return FormAcceptedCommandAuthority.BindingMismatch;
        var submission = await submissions.GetAsync(row.Tenant, row.InstanceId, cancellationToken).ConfigureAwait(false);
        if (submission is null
            || !StringComparer.Ordinal.Equals(submission.ActorId, row.ActorId)
            || submission.PartyId != row.PartyId
            || submission.FormId != row.FormId
            || submission.DefinitionVersion != row.DefinitionVersion
            || submission.SchemaRef != row.SchemaRef
            || !StringComparer.Ordinal.Equals(submission.RecordTypeId, command.RecordTypeId)
            || !StringComparer.Ordinal.Equals(submission.RequestFingerprint, command.PayloadFingerprint)
            || submission.SubmittedAt != command.AcceptedAt
            || !submission.ProtectedAcceptedCandidate.Span.SequenceEqual(row.ProtectedAcceptedValues.Span))
            return FormAcceptedCommandAuthority.BindingMismatch;
        // Review M3, after the row is known to bind the stored submission: the evidence must be this command's own acceptance
        // decision for the submission's own form revision.
        if (command.AcceptanceDecidedAt is not { } decidedAt
            || string.IsNullOrWhiteSpace(command.AcceptanceAuditId)
            || string.IsNullOrWhiteSpace(command.BodyDigestEncoding)
            || string.IsNullOrWhiteSpace(command.BodyDigest)
            || !FormAcceptedCommandEvidence.IsAcceptance(command.AcceptanceSnapshot.Span, submission.Tenant.Value, submission.ActorId,
                FormSubmitGateAuthority.RecordId(submission.FormId, submission.DefinitionVersion), decidedAt, command.AcceptedAt))
            return FormAcceptedCommandAuthority.NotAccepted;

        var revision = await definitions.GetAsync(row.Tenant, row.FormId, row.DefinitionVersion, cancellationToken).ConfigureAwait(false);
        return StringComparer.Ordinal.Equals(revision.RecordTypeId, command.RecordTypeId) && revision.SchemaRef == row.SchemaRef
            ? null
            : FormAcceptedCommandAuthority.BindingMismatch;
    }

    private async ValueTask<FormAcceptedCommandRun> RefuseAsync(IFormAcceptedCommandStore commands, FormProjectionEnvelope row,
        FormAcceptedRecordCommand command, string workerId, string code)
    {
        try
        {
            // Review L2: refuse only the state this run saw; review M1: with its audit, atomically.
            var refused = await commands.RefuseAcceptedCommandAsync(command.CommandId, command.State, code,
                FormAcceptedCommandAudit.Refused(command, command.State, workerId, clock.GetUtcNow(), code), CancellationToken.None)
                .ConfigureAwait(false);
            if (refused is { Command.State: FormAcceptedCommandState.Refused }) return FormAcceptedCommandRun.Refused;
            await ReleaseAsync(row).ConfigureAwait(false);
            return FormAcceptedCommandRun.Settled;
        }
        catch
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
    }

    // Review R2-M1: an audited, non-terminal quarantine of a claimed command; only the state this run saw (Claimed) moves.
    private async ValueTask<FormAcceptedCommandRun> QuarantineAsync(IFormAcceptedCommandStore commands, FormProjectionEnvelope row,
        FormAcceptedRecordCommand command, string workerId, string code)
    {
        try
        {
            var quarantined = await commands.QuarantineAcceptedCommandAsync(command.CommandId, code,
                FormAcceptedCommandAudit.Quarantined(command, workerId, clock.GetUtcNow(), code), CancellationToken.None).ConfigureAwait(false);
            if (quarantined is { Command.State: FormAcceptedCommandState.Quarantined }) return FormAcceptedCommandRun.Quarantined;
            await ReleaseAsync(row).ConfigureAwait(false);
            return FormAcceptedCommandRun.Settled;
        }
        catch
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
            return FormAcceptedCommandRun.Pending;
        }
    }

    // Review R2-L1: one binding failure of a claimed command, counted on the command; the row stays pending.
    private async ValueTask<FormAcceptedCommandRun> BindingFailureAsync(IFormAcceptedCommandStore commands, FormProjectionEnvelope row,
        FormAcceptedRecordCommand command, string code)
    {
        try
        {
            var recorded = await commands.RecordBindingFailureAsync(command.CommandId, code, CancellationToken.None).ConfigureAwait(false);
            if (recorded is not { Changed: true }) await ReleaseAsync(row).ConfigureAwait(false);
        }
        catch
        {
            await RetryAsync(row, "form.engine.projection-pending").ConfigureAwait(false);
        }
        return FormAcceptedCommandRun.Pending;
    }

    private async ValueTask RetryAsync(FormProjectionEnvelope row, string code)
    {
        try { await submissions.RetryProjectionAsync(row.OutboxId, code, CancellationToken.None).ConfigureAwait(false); }
        catch { await ReleaseAsync(row).ConfigureAwait(false); }
    }

    private async ValueTask ReleaseAsync(FormProjectionEnvelope row)
    {
        try { await submissions.ReleaseProjectionLeaseAsync(row.OutboxId, CancellationToken.None).ConfigureAwait(false); }
        catch { }
    }
}
