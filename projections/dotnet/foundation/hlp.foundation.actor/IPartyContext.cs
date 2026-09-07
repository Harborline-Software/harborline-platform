namespace Harborline.Foundation.Authorization;

/// <summary>Resolves the current authenticated principal to its server-derived Party identifier.</summary>
public interface IPartyContext
{
    /// <summary>
    /// Resolves the Party identifier for the current actor. No caller-controlled identity is
    /// accepted by this operation.
    /// </summary>
    /// <exception cref="PrincipalPartyResolutionException">
    /// The actor or tenant is unresolved, the tenant is a sentinel, or no Party is provisioned.
    /// </exception>
    ValueTask<Guid> GetCurrentPartyIdAsync(CancellationToken cancellationToken = default);
}
