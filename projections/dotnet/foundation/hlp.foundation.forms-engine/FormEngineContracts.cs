using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>Identifies the operation whose authorization context is required by the engine.</summary>
public enum FormEngineAction
{
    /// <summary>Reads a form.</summary>
    Read,
    /// <summary>Validates a candidate.</summary>
    Validate,
    /// <summary>Submits a candidate.</summary>
    Submit,
    /// <summary>Recovers undelivered projections.</summary>
    RecoverProjections
}

/// <summary>Captures the tenant, party, actor, and role context used for one engine operation.</summary>
public sealed record FormExecutionScope(
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    IReadOnlyList<string> Roles,
    RoleVocabulary? RoleVocabulary = null,
    HeldRoleSet? HeldRoles = null);

/// <summary>Supplies the required execution scope for a requested engine action.</summary>
public interface IFormExecutionContextProvider
{
    /// <summary>Returns the tenant, party, actor and role scope for the action, failing when none is established.</summary>
    ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// DES-0016 forms-eng-2 — the host decides whether the submitter satisfies the form's own submit gate
/// (role, standing or capability, forms-ck-4). A form that declares a gate with no port is refused.
/// </summary>
public interface IFormSubmitGateAccess
{
    /// <summary>Returns whether the submitter in the scope meets the form's submit gate.</summary>
    ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, Harborline.Foundation.Forms.Models.FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default);
}

/// <summary>Carries an idempotent candidate submission and its tenant-visible correlation data.</summary>
public sealed record FormSubmitRequest(
    FormDefinitionId FormId,
    JsonDocument Candidate,
    string IdempotencyKey,
    string? CaseReference = null);

/// <summary>Describes whether the post-submit projection is still pending or has completed.</summary>
public enum FormProjectionStatus
{
    /// <summary>Projection delivery remains outstanding.</summary>
    Pending,
    /// <summary>Projection delivery completed.</summary>
    Complete
}

/// <summary>Records a projection item that could not be delivered and why.</summary>
public sealed record FormProjectionSkip(string Reason, string FieldPointer, string? Target = null);

/// <summary>Returns the created or replayed instance together with projection delivery outcomes.</summary>
public sealed record FormSubmitReceipt(
    EntityId InstanceId,
    DateTimeOffset SubmittedAt,
    FormProjectionStatus ProjectionStatus,
    IReadOnlyList<FormProjectionSkip> ProjectionSkips);

/// <summary>Reports the number of projection deliveries attempted, completed, and left pending.</summary>
public sealed record FormProjectionRecoveryResult(int Attempted, int Completed, int Pending);

/// <summary>Exposes the public render, validation, submission, and projection-recovery operations.</summary>
public interface IFormEngine
{
    /// <summary>Renders the published form, prefilled and readable-projected when an instance is given.</summary>
    ValueTask<FormView> RenderAsync(FormDefinitionId formId, EntityId? instanceId, CancellationToken cancellationToken = default);
    /// <summary>Validates a candidate against the effective form definition without persisting it.</summary>
    ValueTask<ValidationResult> ValidateAsync(FormDefinitionId formId, JsonDocument candidate, CancellationToken cancellationToken = default);
    /// <summary>Validates, protects and commits the candidate once per idempotency key, then delivers its projection.</summary>
    ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default);
    /// <summary>Redelivers up to the given number of pending projections left by earlier submissions.</summary>
    ValueTask<FormProjectionRecoveryResult> RecoverProjectionsAsync(int maximumDeliveries, CancellationToken cancellationToken = default);
}
