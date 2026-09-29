using System.Text.Json;
using Harborline.Foundation.Forms.Models;
using Harborline.Foundation.Forms.Engine.Persistence;

namespace Harborline.Foundation.Forms.Engine.Security;

/// <summary>Represents the form protection result contract used by this package.</summary>
public sealed record FormProtectionResult(
    byte[] ProtectedCandidate,
    IReadOnlySet<string> SensitiveFields);

/// <summary>Describes whether a field value is returned, withheld, or decrypted.</summary>
public enum FormFieldReadDisposition
{
    /// <summary>The plaintext option.</summary>
    Plaintext,
    /// <summary>The withheld option.</summary>
    Withheld,
    /// <summary>The decrypt granted option.</summary>
    DecryptGranted,
}

/// <summary>Identifies the sensitive-read policy event being audited.</summary>
public enum FormSensitiveReadAuditKind
{
    /// <summary>The policy sensitive read option.</summary>
    PolicySensitiveRead,
    /// <summary>The decrypt on render option.</summary>
    DecryptOnRender,
}

/// <summary>Describes the outcome of a sensitive-read authorization decision.</summary>
public enum FormSensitiveReadAuditOutcome
{
    /// <summary>The value was authorized for reading.</summary>
    Granted,
    /// <summary>The read was denied by policy.</summary>
    Denied,
    /// <summary>The value remained withheld from the caller.</summary>
    Withheld,
}

/// <summary>Represents the form sensitive read audit event contract used by this package.</summary>
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

/// <summary>Represents the form readable candidate contract used by this package.</summary>
/// <param name="Fields">The Fields value.</param>
public sealed record FormReadableCandidate(IReadOnlyList<FormFieldReadDecision> Fields);

/// <summary>Represents the form engine permissions contract used by this package.</summary>
public static class FormEnginePermissions
{
    /// <summary>Provides the decrypt sensitive associated with this value.</summary>
    public const string DecryptSensitive = "forms:decrypt-sensitive";
    /// <summary>Provides the decrypt on render purpose associated with this value.</summary>
    public const string DecryptOnRenderPurpose = "forms-decrypt-on-render";
}

/// <summary>Represents the iform field security contract used by this package.</summary>
public interface IFormFieldSecurity
{
    /// <summary>Executes the protect async contract.</summary>
    ValueTask<FormProtectionResult> ProtectAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        Harborline.Foundation.Assets.Common.EntityId instanceId,
        JsonDocument acceptedCandidate,
        CancellationToken cancellationToken = default);

    /// <summary>Executes the read async contract.</summary>
    ValueTask<FormReadableCandidate> ReadAsync(
        FormExecutionScope scope,
        FormDefinition definition,
        FormSubmissionRecord submission,
        CancellationToken cancellationToken = default);
}

/// <summary>Marker for an adapter that enforces classification and protection on writes.</summary>
public interface IFormGovernanceEnforcingFieldSecurity : IFormFieldSecurity;

/// <summary>Represents the form sensitive read audit contract used by this package.</summary>
public sealed record FormSensitiveReadAudit(
    Harborline.Foundation.Assets.Common.TenantId Tenant,
    Harborline.Foundation.Assets.Common.EntityId InstanceId,
    string ActorId,
    FormSensitiveReadAuditEvent Event,
    DateTimeOffset RecordedAt);

/// <summary>Represents the iform sensitive read audit contract used by this package.</summary>
public interface IFormSensitiveReadAudit
{
    /// <summary>Executes the append async contract.</summary>
    ValueTask AppendAsync(FormSensitiveReadAudit audit, CancellationToken cancellationToken = default);
}
