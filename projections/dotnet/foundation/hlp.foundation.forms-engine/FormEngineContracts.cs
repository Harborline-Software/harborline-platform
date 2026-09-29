using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine;

/// <inheritdoc />
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

/// <inheritdoc />
public sealed record FormExecutionScope(
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    IReadOnlyList<string> Roles,
    RoleVocabulary? RoleVocabulary = null,
    HeldRoleSet? HeldRoles = null);

/// <inheritdoc />
public interface IFormExecutionContextProvider
{
    /// <inheritdoc />
    ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// DES-0016 forms-eng-2 — the host decides whether the submitter satisfies the form's own submit gate
/// (role, standing or capability, forms-ck-4). A form that declares a gate with no port is refused.
/// </summary>
public interface IFormSubmitGateAccess
{
    /// <inheritdoc />
    ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, Harborline.Foundation.Forms.Models.FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed record FormSubmitRequest(
    FormDefinitionId FormId,
    JsonDocument Candidate,
    string IdempotencyKey,
    string? CaseReference = null);

/// <inheritdoc />
public enum FormProjectionStatus
{
    /// <summary>Projection delivery remains outstanding.</summary>
    Pending,
    /// <summary>Projection delivery completed.</summary>
    Complete
}

/// <inheritdoc />
public sealed record FormProjectionSkip(string Reason, string FieldPointer, string? Target = null);

/// <inheritdoc />
public sealed record FormSubmitReceipt(
    EntityId InstanceId,
    DateTimeOffset SubmittedAt,
    FormProjectionStatus ProjectionStatus,
    IReadOnlyList<FormProjectionSkip> ProjectionSkips);

/// <inheritdoc />
public sealed record FormProjectionRecoveryResult(int Attempted, int Completed, int Pending);

/// <inheritdoc />
public interface IFormEngine
{
    /// <inheritdoc />
    ValueTask<FormView> RenderAsync(FormDefinitionId formId, EntityId? instanceId, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<ValidationResult> ValidateAsync(FormDefinitionId formId, JsonDocument candidate, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<FormProjectionRecoveryResult> RecoverProjectionsAsync(int maximumDeliveries, CancellationToken cancellationToken = default);
}
