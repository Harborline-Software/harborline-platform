using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Session;

/// <summary>Canonical fail-closed implementation of <see cref="ISessionResolver"/>.</summary>
public sealed class SessionResolver : ISessionResolver
{
    private readonly ISessionStore _store;

    /// <summary>Creates a resolver over the host-selected authoritative store.</summary>
    public SessionResolver(ISessionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public async ValueTask<SessionActorContext?> ResolveAsync(
        string sessionId,
        ITenantContext tenantContext,
        IReadOnlyList<string> roles,
        DateTimeOffset now,
        TimeSpan idleTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(roles);
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be positive.");
        }

        var tenant = tenantContext.Tenant;
        if (string.IsNullOrWhiteSpace(sessionId)
            || tenant is null
            || tenant.Id.IsSystemSentinel
            || tenant.Status != TenantStatus.Active)
        {
            return null;
        }

        var record = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.TenantId != tenant.Id)
        {
            return null;
        }

        if (record.IsExpired(now, idleTimeout))
        {
            await _store.RemoveAsync(sessionId, cancellationToken).ConfigureAwait(false);
            return null;
        }

        var touched = await _store.TouchAsync(sessionId, now, cancellationToken).ConfigureAwait(false);
        if (touched is null)
        {
            return null;
        }

        return new SessionActorContext
        {
            SessionId = touched.SessionId,
            UserId = touched.UserId,
            Roles = roles.ToArray(),
            ResolvedTenant = tenant,
        };
    }
}
