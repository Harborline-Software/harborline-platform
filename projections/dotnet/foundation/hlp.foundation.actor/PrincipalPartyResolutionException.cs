using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Authorization;

/// <summary>Indicates that current-principal Party resolution failed closed.</summary>
public sealed class PrincipalPartyResolutionException : Exception
{
    private PrincipalPartyResolutionException(
        PrincipalPartyResolutionFailure failure,
        string message)
        : base(message)
    {
        Failure = failure;
    }

    /// <summary>The stable failure reason suitable for host-level error mapping.</summary>
    public PrincipalPartyResolutionFailure Failure { get; }

    /// <summary>Creates an unauthenticated-principal failure.</summary>
    public static PrincipalPartyResolutionException NoAuthenticatedPrincipal() =>
        new(
            PrincipalPartyResolutionFailure.NoAuthenticatedPrincipal,
            "Cannot resolve a PartyId because no authenticated principal is present.");

    /// <summary>Creates an unresolved-tenant failure.</summary>
    public static PrincipalPartyResolutionException TenantUnresolved() =>
        new(
            PrincipalPartyResolutionFailure.TenantUnresolved,
            "Cannot resolve a PartyId because no tenant is resolved for the current principal.");

    /// <summary>Creates a default/system tenant failure.</summary>
    public static PrincipalPartyResolutionException TenantSentinel() =>
        new(
            PrincipalPartyResolutionFailure.TenantSentinel,
            "Cannot resolve a PartyId for a default or system tenant sentinel.");

    /// <summary>Creates an unprovisioned-principal failure without exposing identifiers.</summary>
    public static PrincipalPartyResolutionException NoPartyForPrincipal(
        string userId,
        TenantId tenantId) =>
        new(
            PrincipalPartyResolutionFailure.PartyNotProvisioned,
            "The authenticated principal is not provisioned with a Party in the resolved tenant.");
}
