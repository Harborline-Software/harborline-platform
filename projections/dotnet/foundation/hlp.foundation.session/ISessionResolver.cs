using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Session;

/// <summary>Resolves a stored session into a live tenant-bound actor context.</summary>
public interface ISessionResolver
{
    /// <summary>
    /// Returns a live actor or null for absent, malformed, expired, inactive, sentinel, or
    /// cross-tenant sessions. Roles must be derived by the authenticated host.
    /// </summary>
    ValueTask<SessionActorContext?> ResolveAsync(
        string sessionId,
        ITenantContext tenantContext,
        IReadOnlyList<string> roles,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken = default);
}
