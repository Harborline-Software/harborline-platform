namespace Harborline.Foundation.NotificationDelivery;

/// <summary>
/// A closed transport identifier. Release 1 admits only the on-node inbox
/// (<c>notification-delivery-ck-1</c>).
/// </summary>
public readonly record struct DeliveryChannelKey
{
    private DeliveryChannelKey(string value) => Value = value;

    /// <summary>The recipient's on-node inbox transport.</summary>
    public static DeliveryChannelKey Inbox { get; } = new("inbox");

    /// <summary>The stable transport name.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>The amount of a rendered notification a transport is permitted to carry.</summary>
public enum ChannelContentClass
{
    /// <summary>The on-node transport carries the complete rendered notification.</summary>
    OnNodeFull = 0,
    /// <summary>An off-node transport carries its tier, definition title, subject display name and link only.</summary>
    LinkOnly = 1,
}

/// <summary>
/// Code-registered transport capabilities. Secret requirements and retry profiles are names rather
/// than values, so a binding can never become a credential store.
/// </summary>
public sealed record DeliveryChannel(
    DeliveryChannelKey Key,
    IReadOnlySet<string> RequiredSecretReferenceNames,
    IReadOnlySet<string> AllowedRetryProfiles,
    ChannelContentClass ContentClass);

/// <summary>
/// Tenant configuration for a registered channel. It can identify secrets held by the tenant, but
/// never stores their values (<c>notification-delivery-ck-2</c>).
/// </summary>
public sealed record ChannelBinding
{
    /// <summary>
    /// Creates a binding after ensuring its channel exists and every supplied secret reference is
    /// declared by that channel.
    /// </summary>
    public ChannelBinding(
        string tenant,
        DeliveryChannelKey channel,
        bool enabled,
        string? senderIdentity,
        IReadOnlyList<string> secretReferenceIds)
    {
        ArgumentNullException.ThrowIfNull(secretReferenceIds);
        var registered = DeliveryChannels.Require(channel);
        if (secretReferenceIds.Count != 0 && registered.RequiredSecretReferenceNames.Count == 0)
        {
            throw new NotificationDeliveryRefusedException(
                NotificationDeliveryRefusals.SecretReferenceNotAllowed,
                $"Channel '{channel}' declares no secret references.");
        }

        Tenant = tenant;
        Channel = channel;
        Enabled = enabled;
        SenderIdentity = senderIdentity;
        SecretReferenceIds = secretReferenceIds.ToArray();
    }

    /// <summary>The tenant that owns this non-portable channel configuration.</summary>
    public string Tenant { get; }

    /// <summary>The registered transport this configuration enables or disables.</summary>
    public DeliveryChannelKey Channel { get; }

    /// <summary>Whether the tenant admits this transport for routing.</summary>
    public bool Enabled { get; }

    /// <summary>The transport's configured sender identity, when that transport uses one.</summary>
    public string? SenderIdentity { get; }

    /// <summary>Tenant-held secret reference identifiers; this collection never contains secret values.</summary>
    public IReadOnlyList<string> SecretReferenceIds { get; }
}

/// <summary>Raised when a notification-delivery boundary refuses invalid channel configuration.</summary>
public sealed class NotificationDeliveryRefusedException : InvalidOperationException
{
    /// <summary>Creates a refusal with a stable machine-readable code and an operator-readable explanation.</summary>
    public NotificationDeliveryRefusedException(string code, string message)
        : base(message) => Code = code;

    /// <summary>The stable refusal code.</summary>
    public string Code { get; }
}

/// <summary>Stable refusal codes emitted by the channels slice.</summary>
public static class NotificationDeliveryRefusals
{
    /// <summary>The caller named a transport that this release does not register.</summary>
    public const string ChannelUnknown = "notification_delivery.channel_unknown";

    /// <summary>The binding supplied secret references to a transport that declares none.</summary>
    public const string SecretReferenceNotAllowed = "notification_delivery.secret_reference_not_allowed";

    /// <summary>The caller supplied no usable value for an inbox-entry field that the inbox must render.</summary>
    public const string InboxEntryInvalid = "notification_delivery.inbox_entry_invalid";

    /// <summary>The caller tried to update an entry that does not belong to the current person's inbox.</summary>
    public const string InboxEntryUnknown = "notification_delivery.inbox_entry_unknown";
}

/// <summary>The code registry for delivery transports; a new transport registers here, not a second delivery path.</summary>
public static class DeliveryChannels
{
    private static readonly DeliveryChannel Inbox = new(
        DeliveryChannelKey.Inbox,
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        ChannelContentClass.OnNodeFull);

    /// <summary>The mandatory privacy class with which a future off-node channel must begin.</summary>
    public static ChannelContentClass ExternalDefaultContentClass => ChannelContentClass.LinkOnly;

    /// <summary>Returns the registered channel or refuses a channel key unavailable in this release.</summary>
    public static DeliveryChannel Require(DeliveryChannelKey key) =>
        key == DeliveryChannelKey.Inbox
            ? Inbox
            : throw new NotificationDeliveryRefusedException(
                NotificationDeliveryRefusals.ChannelUnknown,
                $"Channel '{key}' is not registered.");
}
