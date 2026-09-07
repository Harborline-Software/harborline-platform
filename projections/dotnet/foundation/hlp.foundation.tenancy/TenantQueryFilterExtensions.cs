using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.MultiTenancy;

/// <summary>Fail-closed tenant predicates for query paths without an automatic provider filter.</summary>
public static class TenantQueryFilterExtensions
{
    /// <summary>Filters by the resolved ambient tenant.</summary>
    /// <exception cref="InvalidOperationException">No tenant is resolved.</exception>
    public static IQueryable<T> WhereTenant<T>(
        this IQueryable<T> query,
        ITenantContext tenantContext)
        where T : IMustHaveTenant
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(tenantContext);
        if (tenantContext.Tenant is null)
        {
            throw new InvalidOperationException(
                "ITenantContext has no resolved tenant. A tenant-scoped query cannot execute without an active tenant.");
        }

        return query.WhereTenant(tenantContext.Tenant.Id);
    }

    /// <summary>Filters by an explicit non-sentinel tenant identifier.</summary>
    /// <exception cref="ArgumentException">The tenant is default or a system sentinel.</exception>
    public static IQueryable<T> WhereTenant<T>(
        this IQueryable<T> query,
        TenantId tenantId)
        where T : IMustHaveTenant
    {
        ArgumentNullException.ThrowIfNull(query);
        if (tenantId.IsSystemSentinel)
        {
            throw new ArgumentException(
                "WhereTenant rejects default and system-sentinel tenant identifiers.",
                nameof(tenantId));
        }

        return query.Where(entity => entity.TenantId == tenantId);
    }
}
