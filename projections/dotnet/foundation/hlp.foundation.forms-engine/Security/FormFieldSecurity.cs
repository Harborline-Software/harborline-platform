using System.Text.Json;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Security;

/// <summary>Contains the protected candidate and the fields whose values require sensitive handling.</summary>
public sealed record FormProtectionResult(
    byte[] ProtectedCandidate,
    IReadOnlySet<string> SensitiveFields);

/// <summary>Specifies whether a field is returned directly, withheld, or decrypted by capability.</summary>
public enum FormFieldReadDisposition
{
    /// <summary>The field is returned as submitted.</summary>
    Plaintext,
    /// <summary>The field value is withheld.</summary>
    Withheld,
    /// <summary>The field is returned after capability-authorized decryption.</summary>
    DecryptGranted,
}

/// <summary>Identifies the kind of sensitive-read event recorded by the engine.</summary>
public enum FormSensitiveReadAuditKind
{
    /// <summary>A policy-sensitive read occurred.</summary>
    PolicySensitiveRead,
    /// <summary>A decrypt operation occurred during render.</summary>
    DecryptOnRender,
}

/// <summary>Records whether a sensitive-read request was granted, denied, or withheld.</summary>
public enum FormSensitiveReadAuditOutcome
{
    /// <summary>The read was granted.</summary>
    Granted,
    /// <summary>The read was denied.</summary>
    Denied,
    /// <summary>The value was withheld.</summary>
    Withheld,
}

/// <summary>Describes one sensitive-read event and its optional authorization context.</summary>
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

/// <summary>Collects field decisions that determine the readable candidate projection.</summary>
public sealed record FormReadableCandidate(IReadOnlyList<FormFieldReadDecision> Fields);

/// <summary>Defines the permission and purpose identifiers used for sensitive field decryption.</summary>
public static class FormEnginePermissions
{
    /// <summary>Permission required to decrypt sensitive field values.</summary>
    public const string DecryptSensitive = "forms:decrypt-sensitive";
    /// <summary>Purpose value required when decryption occurs during rendering.</summary>
    public const string DecryptOnRenderPurpose = "forms-decrypt-on-render";
}

/// <summary>Protects accepted candidates and projects stored submissions into readable values.</summary>
public interface IFormFieldSecurity
{
    /// <summary>Protects the accepted candidate's sensitive fields for storage.</summary>
    ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        Harborline.Foundation.Assets.Common.EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default);

    /// <summary>Projects a stored submission into the values the caller may read.</summary>
    ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default);
}

/// <summary>Marker for an adapter that enforces classification and protection on writes.</summary>
public interface IFormGovernanceEnforcingFieldSecurity : IFormFieldSecurity;

/// <summary>Records a tenant-scoped sensitive-read audit event for one form instance.</summary>
public sealed record FormSensitiveReadAudit(
    Harborline.Foundation.Assets.Common.TenantId Tenant,
    Harborline.Foundation.Assets.Common.EntityId InstanceId,
    string ActorId,
    FormSensitiveReadAuditEvent Event,
    DateTimeOffset RecordedAt);

/// <summary>Appends sensitive-read audit records to the host's audit sink.</summary>
public interface IFormSensitiveReadAudit
{
    /// <summary>Appends the sensitive-read audit record to the host's audit sink.</summary>
    ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default);
}
