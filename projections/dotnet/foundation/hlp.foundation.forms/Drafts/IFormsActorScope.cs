using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Drafts;

/// <summary>The tenant, Party, and actor derived atomically from one authenticated principal.</summary>
public sealed record FormsActorScope(TenantId Tenant, Guid PartyId, string ActorId);

/// <summary>
/// Fail-closed current-actor seam for Forms state. Hosts may replace the default authenticated
/// adapter, but callers cannot inject tenant and Party through independently mutable contexts.
/// </summary>
public interface IFormsActorScope
{
    /// <summary>
    /// Returns the current tenant, Party and actor, or throws <see cref="Harborline.Foundation.Authorization.PrincipalPartyResolutionException"/>
    /// when there is no authenticated principal, the tenant is unresolved or the system sentinel, or no Party maps to the principal.
    /// </summary>
    ValueTask<FormsActorScope> GetRequiredAsync(CancellationToken cancellationToken = default);
}
