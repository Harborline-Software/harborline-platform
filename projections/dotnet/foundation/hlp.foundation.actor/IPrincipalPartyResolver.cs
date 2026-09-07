using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Authorization;

/// <summary>Host-owned lookup from an exact tenant/user pair to a server-side Party identifier.</summary>
public interface IPrincipalPartyResolver
{
    /// <summary>
    /// Returns the mapped Party identifier, or <see langword="null"/> when the principal is not
    /// provisioned in the specified tenant.
    /// </summary>
    ValueTask<Guid?> ResolveAsync(
        string userId,
        TenantId tenantId,
        CancellationToken cancellationToken = default);
}
