using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.MultiTenancy;

/// <summary>Identity-only metadata for one tenant resolved by a host.</summary>
public sealed record TenantMetadata
{
    /// <summary>Stable tenant identifier.</summary>
    public required TenantId Id { get; init; }

    /// <summary>Short routable tenant name.</summary>
    public required string Name { get; init; }

    /// <summary>Lifecycle status.</summary>
    public TenantStatus Status { get; init; } = TenantStatus.Active;

    /// <summary>Optional human-friendly display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional BCP-47 locale tag.</summary>
    public string? Locale { get; init; }

    /// <summary>Optional creation timestamp.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Host-specific extension metadata.</summary>
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
}
