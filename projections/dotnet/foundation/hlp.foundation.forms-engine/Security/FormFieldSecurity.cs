using System.Text.Json;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Security;

public sealed record FormProtectionResult(
    byte[] ProtectedCandidate,
    IReadOnlySet<string> SensitiveFields);

public enum FormFieldReadDisposition
{
    Plaintext,
    Withheld,
    DecryptGranted,
}

public enum FormSensitiveReadAuditKind
{
    PolicySensitiveRead,
    DecryptOnRender,
}

public enum FormSensitiveReadAuditOutcome
{
    Granted,
    Denied,
    Withheld,
}

public sealed record FormSensitiveReadAuditEvent(
    string FieldName,
    FormSensitiveReadAuditKind Kind,
    FormSensitiveReadAuditOutcome Outcome,
    string? Permission = null,
    string? Purpose = null,
    string? Reason = null,
    string? DecryptCapabilityId = null);

/// <summary>
/// A single field-level projection decision. The engine constructs readable JSON only from
/// these decisions so an adapter cannot release ciphertext or unaudited decrypted values.
/// </summary>
public sealed record FormFieldReadDecision(
    string FieldName,
    FormFieldReadDisposition Disposition,
    JsonElement? ProjectedValue,
    bool IsSensitive,
    IReadOnlyList<FormSensitiveReadAuditEvent> RequiredAudits);

public sealed record FormReadableCandidate(IReadOnlyList<FormFieldReadDecision> Fields);

public static class FormEnginePermissions
{
    public const string DecryptSensitive = "forms:decrypt-sensitive";
    public const string DecryptOnRenderPurpose = "forms-decrypt-on-render";
}

public interface IFormFieldSecurity
{
    ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        Harborline.Foundation.Assets.Common.EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default);

    ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default);
}

/// <summary>Marker for an adapter that enforces classification and protection on writes.</summary>
public interface IFormGovernanceEnforcingFieldSecurity : IFormFieldSecurity;

public sealed record FormSensitiveReadAudit(
    Harborline.Foundation.Assets.Common.TenantId Tenant,
    Harborline.Foundation.Assets.Common.EntityId InstanceId,
    string ActorId,
    FormSensitiveReadAuditEvent Event,
    DateTimeOffset RecordedAt);

public interface IFormSensitiveReadAudit
{
    ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default);
}
