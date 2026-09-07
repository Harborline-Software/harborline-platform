using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Engine.Capabilities;

/// <summary>The coarse operations granted by a verified Forms bearer capability.</summary>
public enum FormCapabilityAction
{
    /// <summary>Render an existing or empty form view.</summary>
    Read,

    /// <summary>Validate or submit form work.</summary>
    Write,
}

/// <summary>
/// A verified capability. Roles are deliberately absent: authorization roles always come from
/// the current host-authenticated actor, never from bearer material.
/// </summary>
public sealed record VerifiedFormCapability(
    TenantId Tenant,
    string Subject,
    IReadOnlySet<FormCapabilityAction> Actions,
    DateTimeOffset ExpiresAt);

/// <summary>Supplies the request's Forms bearer without coupling Foundation to an HTTP stack.</summary>
public interface IFormCapabilityBearerProvider
{
    /// <summary>Returns the current request bearer, or <see langword="null"/> when absent.</summary>
    ValueTask<string?> GetBearerAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves macaroon root-key bytes for one opaque location.</summary>
public interface IFormCapabilityRootKeyProvider
{
    /// <summary>Returns a root key or <see langword="null"/> for an unknown location.</summary>
    ValueTask<ReadOnlyMemory<byte>?> GetRootKeyAsync(
        string location,
        CancellationToken cancellationToken = default);
}

/// <summary>Verifies a source-compatible Forms macaroon and returns only trusted claims.</summary>
public interface IFormCapabilityVerifier
{
    /// <summary>
    /// Verifies encoding, signature, strict caveat shape, actions, and expiry. Every denial uses
    /// one opaque exception that never includes bearer, key, identifier, location, or caveat data.
    /// </summary>
    ValueTask<VerifiedFormCapability> VerifyAsync(
        string bearer,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}

/// <summary>Mints source-compatible Forms macaroons for trusted host adapters.</summary>
public interface IFormCapabilityIssuer
{
    /// <summary>
    /// Issues a single-action capability. Embedded roles are retained only for wire compatibility.
    /// </summary>
    ValueTask<string> IssueAsync(
        TenantId tenant,
        string subject,
        IReadOnlyList<string> compatibilityRoles,
        IReadOnlyList<FormCapabilityAction> actions,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}

/// <summary>Opaque fail-closed outcome for every capability verification denial.</summary>
public sealed class FormCapabilityDeniedException()
    : Exception("The form capability was denied.")
{
    /// <summary>Stable code safe for host-level mapping and telemetry.</summary>
    public string Code => "form.engine.denied";
}
