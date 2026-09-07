namespace Harborline.Foundation.Authorization;

/// <summary>Stable fail-closed reasons for current-principal Party resolution.</summary>
public enum PrincipalPartyResolutionFailure
{
    /// <summary>No non-blank authenticated user identifier is available.</summary>
    NoAuthenticatedPrincipal,

    /// <summary>No tenant was resolved for the current authentication scope.</summary>
    TenantUnresolved,

    /// <summary>The resolved tenant is a default or system sentinel.</summary>
    TenantSentinel,

    /// <summary>No Party is provisioned for the exact tenant/user pair.</summary>
    PartyNotProvisioned,
}
