using Harborline.Foundation.Authorization;
using Harborline.Foundation.MultiTenancy;

namespace Harborline.Foundation.Session;

/// <summary>A live session projected into the narrow current actor/tenant boundary.</summary>
public sealed record SessionActorContext : IAuthenticatedActorContext
{
    /// <summary>The opaque session identifier used for audit correlation.</summary>
    public required string SessionId { get; init; }

    /// <inheritdoc />
    public required string UserId { get; init; }

    /// <inheritdoc />
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>The exact active tenant metadata resolved by the host.</summary>
    public required TenantMetadata ResolvedTenant { get; init; }

    /// <inheritdoc />
    public TenantMetadata? Tenant => ResolvedTenant;
}
