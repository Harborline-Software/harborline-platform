namespace Harborline.Foundation.MultiTenancy;

/// <summary>The tenant resolved for the current request, job, or operation.</summary>
public interface ITenantContext
{
    /// <summary>The resolved tenant metadata, or null when no tenant was resolved.</summary>
    TenantMetadata? Tenant { get; }

    /// <summary>True when a tenant has been resolved.</summary>
    bool IsResolved => Tenant is not null;
}
