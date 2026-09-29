using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine;

/// <summary>Defines the supported form engine action values.</summary>
public enum FormEngineAction
{
    /// <summary>The read option.</summary>
    Read,
    /// <summary>The validate option.</summary>
    Validate,
    /// <summary>The submit option.</summary>
    Submit,
    /// <summary>The recover projections option.</summary>
    RecoverProjections
}

/// <summary>Represents the form execution scope contract used by this package.</summary>
public sealed record FormExecutionScope(
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    IReadOnlyList<string> Roles,
    RoleVocabulary? RoleVocabulary = null,
    HeldRoleSet? HeldRoles = null);

/// <summary>Represents the iform execution context provider contract used by this package.</summary>
public interface IFormExecutionContextProvider
{
    /// <summary>Executes the get required async contract.</summary>
    ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default);
}

/// <summary>
/// DES-0016 forms-eng-2 — the host decides whether the submitter satisfies the form's own submit gate
/// (role, standing or capability, forms-ck-4). A form that declares a gate with no port is refused.
/// </summary>
public interface IFormSubmitGateAccess
{
    /// <summary>Executes the satisfies async contract.</summary>
    ValueTask<bool> SatisfiesAsync(FormExecutionScope scope, Harborline.Foundation.Forms.Models.FormDefinition definition, SubmitGate gate, CancellationToken cancellationToken = default);
}

/// <summary>Represents the form submit request contract used by this package.</summary>
public sealed record FormSubmitRequest(
    FormDefinitionId FormId,
    JsonDocument Candidate,
    string IdempotencyKey,
    string? CaseReference = null);

/// <summary>Defines the supported form projection status values.</summary>
public enum FormProjectionStatus
{
    /// <summary>The pending option.</summary>
    Pending,
    /// <summary>The complete option.</summary>
    Complete
}

/// <summary>Represents the form projection skip contract used by this package.</summary>
/// <param name="Reason">The Reason value.</param>
/// <param name="FieldPointer">The FieldPointer value.</param>
/// <param name="Target">The Target value.</param>
public sealed record FormProjectionSkip(string Reason, string FieldPointer, string? Target = null);

/// <summary>Represents the form submit receipt contract used by this package.</summary>
public sealed record FormSubmitReceipt(
    EntityId InstanceId,
    DateTimeOffset SubmittedAt,
    FormProjectionStatus ProjectionStatus,
    IReadOnlyList<FormProjectionSkip> ProjectionSkips);

/// <summary>Represents the form projection recovery result contract used by this package.</summary>
/// <param name="Attempted">The Attempted value.</param>
/// <param name="Completed">The Completed value.</param>
/// <param name="Pending">The Pending value.</param>
public sealed record FormProjectionRecoveryResult(int Attempted, int Completed, int Pending);

/// <summary>Represents the iform engine contract used by this package.</summary>
public interface IFormEngine
{
    /// <summary>Executes the render async contract.</summary>
    ValueTask<FormView> RenderAsync(FormDefinitionId formId, EntityId? instanceId, CancellationToken cancellationToken = default);
    /// <summary>Executes the validate async contract.</summary>
    ValueTask<ValidationResult> ValidateAsync(FormDefinitionId formId, JsonDocument candidate, CancellationToken cancellationToken = default);
    /// <summary>Executes the submit async contract.</summary>
    ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default);
    /// <summary>Executes the recover projections async contract.</summary>
    ValueTask<FormProjectionRecoveryResult> RecoverProjectionsAsync(int maximumDeliveries, CancellationToken cancellationToken = default);
}
