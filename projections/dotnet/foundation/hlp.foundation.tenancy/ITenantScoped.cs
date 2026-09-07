using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.MultiTenancy;

/// <summary>Marks a value as owned by one tenant.</summary>
public interface ITenantScoped
{
    /// <summary>The tenant that owns this value.</summary>
    TenantId TenantId { get; }
}

/// <summary>Marks a persisted value whose tenant must always be populated.</summary>
public interface IMustHaveTenant : ITenantScoped
{
}
