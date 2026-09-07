using System.Text.Json;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Forms;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine;

public enum FormEngineAction { Read, Validate, Submit, RecoverProjections }

public sealed record FormExecutionScope(
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    IReadOnlyList<string> Roles,
    RoleVocabulary? RoleVocabulary = null,
    HeldRoleSet? HeldRoles = null);

public interface IFormExecutionContextProvider
{
    ValueTask<FormExecutionScope> GetRequiredAsync(FormEngineAction action, CancellationToken cancellationToken = default);
}

public sealed record FormSubmitRequest(
    FormDefinitionId FormId,
    JsonDocument Candidate,
    string IdempotencyKey,
    string? CaseReference = null);

public enum FormProjectionStatus { Pending, Complete }

public sealed record FormProjectionSkip(string Reason, string FieldPointer, string? Target = null);

public sealed record FormSubmitReceipt(
    EntityId InstanceId,
    DateTimeOffset SubmittedAt,
    FormProjectionStatus ProjectionStatus,
    IReadOnlyList<FormProjectionSkip> ProjectionSkips);

public sealed record FormProjectionRecoveryResult(int Attempted, int Completed, int Pending);

public interface IFormEngine
{
    ValueTask<FormView> RenderAsync(FormDefinitionId formId, EntityId? instanceId, CancellationToken cancellationToken = default);
    ValueTask<ValidationResult> ValidateAsync(FormDefinitionId formId, JsonDocument candidate, CancellationToken cancellationToken = default);
    ValueTask<FormSubmitReceipt> SubmitAsync(FormSubmitRequest request, CancellationToken cancellationToken = default);
    ValueTask<FormProjectionRecoveryResult> RecoverProjectionsAsync(int maximumDeliveries, CancellationToken cancellationToken = default);
}
