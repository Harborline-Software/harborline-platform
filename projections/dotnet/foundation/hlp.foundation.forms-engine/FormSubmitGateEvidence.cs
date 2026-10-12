using System.Text.Json;
using System.Text.Json.Nodes;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;
using Harborline.Foundation.Forms.Engine.Persistence;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>
/// The exact write a submit-gate decision is asked to authorize (decision D3). The engine builds it from the
/// server-resolved scope and the published head: tenant, submitter, form id and version, the immutable schema
/// reference, the record type target, the gate evaluated, the decision instant, and from the request only the operation
/// identity and the case reference, which the authorization then binds to the staged and stored write.
/// </summary>
/// <param name="Tenant">The tenant of the execution scope.</param>
/// <param name="SubmitterActorId">The authenticated actor of the execution scope.</param>
/// <param name="FormId">The published head's definition id.</param>
/// <param name="FormVersion">The published head's version.</param>
/// <param name="SchemaRef">The published head's content-addressed schema reference.</param>
/// <param name="RecordTypeId">The published head's record type target, or <see langword="null"/> for a form without one.</param>
/// <param name="OperationId">The submit's operation identity (its idempotency key), or <see langword="null"/> when
/// the gate is asked for a blank read rather than a write.</param>
/// <param name="DecidedAt">The instant the gate is decided at; the decider's evidence must carry exactly this instant.</param>
/// <param name="Gate">The published head's submit gate the decider was asked to evaluate (F11).</param>
/// <param name="CaseReference">The request's case reference, which reaches the projection row; null when none.</param>
public sealed record FormSubmitIntent(
    TenantId Tenant,
    string SubmitterActorId,
    FormDefinitionId FormId,
    SemanticVersion FormVersion,
    SchemaId SchemaRef,
    string? RecordTypeId,
    string? OperationId,
    DateTimeOffset DecidedAt,
    SubmitGate Gate,
    string? CaseReference);

/// <summary>One submit-gate question: the form's own gate, asked for one intended write.</summary>
/// <param name="Scope">The execution scope of the submitter.</param>
/// <param name="Definition">The published head whose gate is asked.</param>
/// <param name="Gate">The head's declared submit gate.</param>
/// <param name="Intent">The exact write the answer must authorize.</param>
public sealed record FormSubmitGateRequest(
    FormExecutionScope Scope,
    FormDefinition Definition,
    SubmitGate Gate,
    FormSubmitIntent Intent);

/// <summary>
/// A submit-gate answer. <c>Evidence</c> is the host decider's own evidence, unchanged; Forms never
/// constructs it. The engine refuses an answer whose intent, verdict or evidence does not bind the asked intent.
/// </summary>
/// <param name="Intent">The intent this answer was decided for; it must equal the asked intent.</param>
/// <param name="Satisfied">Whether the submitter meets the gate.</param>
/// <param name="Evidence">The decider's evidence, or <see langword="null"/> from a port that returns only a verdict.</param>
public sealed record FormSubmitGateDecision(
    FormSubmitIntent Intent,
    bool Satisfied,
    AuthorizationDecisionEvidence? Evidence);

/// <summary>
/// The submit-gate evidence bound to the exact write it authorized (D3): the decided intent, the instance the write
/// committed as, and the canonical fingerprint of the accepted payload. Returned on the receipt and never persisted by
/// this revision; the stored submission keeps the bound schema reference and target for recovery (W7/W8). It carries
/// the payload fingerprint and role-binding facts, so a host must not serialize it to clients (D6 disclosure).
/// </summary>
/// <param name="Intent">The intent the gate decided.</param>
/// <param name="InstanceId">The committed (or replayed) form instance.</param>
/// <param name="PayloadFingerprint">The canonical accepted-candidate fingerprint committed with the instance.</param>
/// <param name="Evidence">The decider's evidence for <c>Intent</c>.</param>
public sealed record FormSubmitAuthorization(
    FormSubmitIntent Intent,
    EntityId InstanceId,
    string PayloadFingerprint,
    AuthorizationDecisionEvidence Evidence)
{
    /// <summary>
    /// Runtime binding check inside the submit transaction: the staged commit and its kernel operation must be the
    /// write the evidence was decided for, and carry the fingerprint this authorization names. Any mismatch denies,
    /// and the transaction rolls back with nothing persisted.
    /// </summary>
    internal void RequireBinds(FormSubmissionCommit commit, KernelOperationIdentity operation)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ArgumentNullException.ThrowIfNull(operation);
        var bound = BindsCommit(commit)
            && StringComparer.Ordinal.Equals(operation.IdempotencyKey, Intent.OperationId)
            && StringComparer.Ordinal.Equals(operation.CommandId, InstanceId.ToString())
            && StringComparer.Ordinal.Equals(operation.Fingerprint, PayloadFingerprint);
        if (!bound) throw new FormEngineDeniedException();
    }

    /// <summary>
    /// True when <paramref name="commit"/>, staged or stored, is exactly the write this authorization names: operation
    /// id, instance, tenant, submitter, form id and version, schema reference, record type target, case reference and
    /// payload fingerprint on every row, with evidence that binds the intent. An idempotent replay attaches an
    /// authorization only when the stored commit passes this check (F1).
    /// </summary>
    internal bool BindsCommit(FormSubmissionCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var submission = commit.Submission;
        var projection = commit.Projection;
        return Intent.OperationId is { } operationId
            && StringComparer.Ordinal.Equals(commit.IdempotencyKey, operationId)
            && submission.InstanceId == InstanceId
            && submission.Tenant == Intent.Tenant
            && StringComparer.Ordinal.Equals(submission.ActorId, Intent.SubmitterActorId)
            && submission.FormId == Intent.FormId
            && submission.DefinitionVersion == Intent.FormVersion
            && StringComparer.Ordinal.Equals(submission.RequestFingerprint, PayloadFingerprint)
            && submission.SchemaRef == Intent.SchemaRef
            && StringComparer.Ordinal.Equals(submission.RecordTypeId, Intent.RecordTypeId)
            && commit.Audit.InstanceId == InstanceId
            && commit.Audit.Tenant == Intent.Tenant
            && StringComparer.Ordinal.Equals(commit.Audit.ActorId, Intent.SubmitterActorId)
            && projection.InstanceId == InstanceId
            && projection.Tenant == Intent.Tenant
            && StringComparer.Ordinal.Equals(projection.ActorId, Intent.SubmitterActorId)
            && projection.FormId == Intent.FormId
            && projection.DefinitionVersion == Intent.FormVersion
            && projection.SchemaRef == Intent.SchemaRef
            && StringComparer.Ordinal.Equals(projection.RecordTypeId, Intent.RecordTypeId)
            && StringComparer.Ordinal.Equals(projection.CaseReference, Intent.CaseReference)
            && commit.Receipt.InstanceId == InstanceId
            && FormSubmitGateAuthority.Binds(Evidence, Intent);
    }
}

/// <summary>
/// The one request shape a form's submit gate is decided on, and the check that the decider's evidence answers
/// exactly that request (D3). DES-0016 section 6 defines no act spelling; it forbids only a generic form-submit grant
/// or a per-form capability. The act below is not a grant: it asks the host decider to evaluate the form's own declared
/// gate, which travels in the record fields, so the host adapter must evaluate that field and never a grant on the act.
/// The spellings are provisional until the owner ratifies them with the host decider adapter (W10).
/// </summary>
public static class FormSubmitGateAuthority
{
    /// <summary>The operation every submit-gate decision is asked under; the gate arm travels in the record fields.</summary>
    public const string Operation = "forms:submit-gate";

    /// <summary>The access record kind that names a form revision.</summary>
    public const string RecordKind = "form";

    /// <summary>The access record field carrying the form's declared submit gate as JSON.</summary>
    public const string SubmitGateField = "submit_gate";

    /// <summary>The access record field carrying the form's record type target, or JSON null.</summary>
    public const string RecordTypeField = "record_type_id";

    /// <summary>The access record id naming one immutable form revision: <c>{formId}@{version}</c>.</summary>
    public static string RecordId(FormDefinitionId formId, SemanticVersion version) => $"{formId.Value}@{version}";

    /// <summary>Builds the access request the gate is decided on, at the intent's instant, for the intent's submitter.</summary>
    public static AccessRequest Request(FormSubmitGateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var intent = request.Intent;
        var tenant = intent.Tenant.Value;
        var fields = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            [SubmitGateField] = JsonSerializer.SerializeToNode(intent.Gate),
            [RecordTypeField] = intent.RecordTypeId is null ? null : JsonValue.Create(intent.RecordTypeId),
        };
        return new AccessRequest(
            Operation,
            intent.SubmitterActorId,
            tenant,
            new AccessRecord(tenant, RecordKind, RecordId(intent.FormId, intent.FormVersion), fields),
            intent.DecidedAt);
    }

    /// <summary>
    /// True when <paramref name="evidence"/> is the decider's answer to <see cref="Request"/> for exactly
    /// <paramref name="intent"/>: same operation, submitter, tenant, form revision and instant.
    /// </summary>
    public static bool Binds(AuthorizationDecisionEvidence evidence, FormSubmitIntent intent)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(intent);
        return StringComparer.Ordinal.Equals(evidence.Operation, Operation)
            && StringComparer.Ordinal.Equals(evidence.Principal, intent.SubmitterActorId)
            && StringComparer.Ordinal.Equals(evidence.Tenant, intent.Tenant.Value)
            && StringComparer.Ordinal.Equals(evidence.RecordKind, RecordKind)
            && StringComparer.Ordinal.Equals(evidence.RecordId, RecordId(intent.FormId, intent.FormVersion))
            && evidence.At == intent.DecidedAt;
    }
}

/// <summary>
/// Decision D3 and plan W5: an engine or other reserved principal never submits a form, whatever route or token
/// produced the scope. The reserved namespaces are those the API host's engine principals use (plan A4): the exact id
/// <c>system</c> and the prefixes <c>sys.</c>, <c>system:</c> and <c>installer:</c>, compared without regard to case.
/// </summary>
public static class ReservedFormPrincipals
{
    private static readonly string[] Prefixes = ["sys.", "system:", "installer:"];

    /// <summary>True when <paramref name="actorId"/> names a reserved engine or installer principal.</summary>
    public static bool IsReserved(string actorId)
    {
        ArgumentNullException.ThrowIfNull(actorId);
        return string.Equals(actorId, "system", StringComparison.OrdinalIgnoreCase)
            || Prefixes.Any(prefix => actorId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Platform submit-gate port over the host's sole decider (C1): every decision goes through
/// <see cref="AccessProvider.CheckWithEvidenceAsync"/> once, and the decider's evidence is returned unchanged.
/// </summary>
public sealed class AccessProviderFormSubmitGateAccess : IFormSubmitGateAccess
{
    private readonly AccessProvider _access;
    private readonly TimeProvider _clock;

    /// <summary>Creates the port over the host's access provider and clock.</summary>
    public AccessProviderFormSubmitGateAccess(AccessProvider access, TimeProvider clock)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async ValueTask<bool> SatisfiesAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        SubmitGate gate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(gate);
        var intent = new FormSubmitIntent(scope.Tenant, scope.ActorId, definition.Id, definition.Version,
            definition.SchemaRef, definition.RecordTypeId, null, _clock.GetUtcNow(), gate, null);
        return (await DecideAsync(new(scope, definition, gate, intent), cancellationToken).ConfigureAwait(false)).Satisfied;
    }

    /// <inheritdoc />
    public async ValueTask<FormSubmitGateDecision> DecideAsync(
        FormSubmitGateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var check = await _access.CheckWithEvidenceAsync(FormSubmitGateAuthority.Request(request), cancellationToken)
            .ConfigureAwait(false);
        return new(request.Intent, check.Check.Allowed && check.Evidence is not null, check.Evidence);
    }
}
