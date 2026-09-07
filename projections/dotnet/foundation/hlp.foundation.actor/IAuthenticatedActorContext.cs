using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Authorization;

/// <summary>
/// The user and tenant values produced by one validated authentication scope.
/// </summary>
/// <remarks>
/// Hosts implement one scoped adapter for this interface so party resolution cannot combine a
/// user from one principal with a tenant from another. Authorization policy is intentionally not
/// part of this context.
/// </remarks>
public interface IAuthenticatedActorContext : ICurrentUser, ITenantContext;
