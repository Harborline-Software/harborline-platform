using System.Text.Json;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Security;

/// <inheritdoc />
public sealed record FormProtectionResult(
    byte[] ProtectedCandidate,
    IReadOnlySet<string> SensitiveFields);

/// <inheritdoc />
public enum FormFieldReadDisposition
{
    /// <summary>The field is returned as submitted.</summary>
    Plaintext,
    /// <summary>The field value is withheld.</summary>
    Withheld,
    /// <summary>The field is returned after capability-authorized decryption.</summary>
    DecryptGranted,
}

/// <inheritdoc />
public enum FormSensitiveReadAuditKind
{
    /// <summary>A policy-sensitive read occurred.</summary>
    PolicySensitiveRead,
    /// <summary>A decrypt operation occurred during render.</summary>
    DecryptOnRender,
}

/// <inheritdoc />
public enum FormSensitiveReadAuditOutcome
{
    /// <summary>The read was granted.</summary>
    Granted,
    /// <summary>The read was denied.</summary>
    Denied,
    /// <summary>The value was withheld.</summary>
    Withheld,
}

/// <inheritdoc />
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

/// <inheritdoc />
public sealed record FormReadableCandidate(IReadOnlyList<FormFieldReadDecision> Fields);

/// <inheritdoc />
public static class FormEnginePermissions
{
    /// <inheritdoc />
    public const string DecryptSensitive = "forms:decrypt-sensitive";
    /// <inheritdoc />
    public const string DecryptOnRenderPurpose = "forms-decrypt-on-render";
}

/// <inheritdoc />
public interface IFormFieldSecurity
{
    /// <inheritdoc />
    ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        Harborline.Foundation.Assets.Common.EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default);

    /// <inheritdoc />
    ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default);
}

/// <summary>Marker for an adapter that enforces classification and protection on writes.</summary>
public interface IFormGovernanceEnforcingFieldSecurity : IFormFieldSecurity;

/// <inheritdoc />
public sealed record FormSensitiveReadAudit(
    Harborline.Foundation.Assets.Common.TenantId Tenant,
    Harborline.Foundation.Assets.Common.EntityId InstanceId,
    string ActorId,
    FormSensitiveReadAuditEvent Event,
    DateTimeOffset RecordedAt);

/// <inheritdoc />
public interface IFormSensitiveReadAudit
{
    /// <inheritdoc />
    ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default);
}
