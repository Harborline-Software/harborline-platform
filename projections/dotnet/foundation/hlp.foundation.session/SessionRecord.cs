using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Session;

/// <summary>Authoritative server-side state for one opaque session id.</summary>
public sealed record SessionRecord
{
    /// <summary>Opaque high-entropy session identifier carried by the client.</summary>
    public required string SessionId { get; init; }

    /// <summary>Stable authenticated user identifier.</summary>
    public required string UserId { get; init; }

    /// <summary>Tenant to which the session is bound.</summary>
    public required TenantId TenantId { get; init; }

    /// <summary>Session issuance instant.</summary>
    public required DateTimeOffset IssuedUtc { get; init; }

    /// <summary>Non-sliding absolute expiry instant.</summary>
    public required DateTimeOffset AbsoluteExpiryUtc { get; init; }

    /// <summary>Last accepted authenticated activity instant.</summary>
    public required DateTimeOffset LastSeenUtc { get; init; }

    /// <summary>Provider-neutral establishment reason.</summary>
    public required SessionEstablishmentReason Reason { get; init; }

    /// <summary>Returns a new record whose activity instant is advanced.</summary>
    public SessionRecord Touch(DateTimeOffset now)
    {
        if (now < LastSeenUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Session activity cannot move backward.");
        }

        return this with { LastSeenUtc = now };
    }

    /// <summary>Returns true after either the absolute or sliding-idle lifetime.</summary>
    public bool IsExpired(DateTimeOffset now, TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout), "Idle timeout must be positive.");
        }

        return now > AbsoluteExpiryUtc || now - LastSeenUtc > idleTimeout;
    }

    internal void ValidateForStorage()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(UserId);
        if (TenantId.IsSystemSentinel)
        {
            throw new ArgumentException("A session requires a non-sentinel tenant.", nameof(TenantId));
        }

        if (AbsoluteExpiryUtc <= IssuedUtc)
        {
            throw new ArgumentException("Absolute expiry must be after session issuance.", nameof(AbsoluteExpiryUtc));
        }

        if (LastSeenUtc < IssuedUtc || LastSeenUtc > AbsoluteExpiryUtc)
        {
            throw new ArgumentException("Last-seen time must fall within the session lifetime.", nameof(LastSeenUtc));
        }

        if (!Enum.IsDefined(Reason))
        {
            throw new ArgumentOutOfRangeException(nameof(Reason));
        }
    }
}
