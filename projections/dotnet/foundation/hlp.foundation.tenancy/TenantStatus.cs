using System.Text.Json.Serialization;

namespace Harborline.Foundation.MultiTenancy;

/// <summary>Lifecycle status carried by resolved tenant metadata.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TenantStatus
{
    /// <summary>The tenant is serving traffic.</summary>
    Active = 0,

    /// <summary>The tenant is temporarily not servicing requests.</summary>
    Suspended = 1,

    /// <summary>The tenant is being deactivated while its data is retained.</summary>
    Decommissioning = 2,

    /// <summary>The tenant is deactivated and available only to separately authorized historical reads.</summary>
    Archived = 3,
}
