namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>One notification: id, title, timestamp, kind, read state and optional body, preview and actor.</summary>
public sealed record NotificationCenterItem(string Id, string Title, string Timestamp, string Kind, bool Read = false, string? Body = null, string? Preview = null, string? Actor = null);
/// <summary>A group of notifications shown under one heading: the kind it collects and its label.</summary>
public sealed record NotificationGroup(string Kind, string Label);
/// <summary>Notification totals: pending, unread and which count the badge shows.</summary>
public sealed record NotificationCounts(int Pending, int Unread, NotificationBadgeMode Mode);
/// <summary>What the notification center's badge counts: unread items or pending decisions.</summary>
public enum NotificationBadgeMode
{
    /// <summary>Counts all unread notifications on the bell badge.</summary>
    Unread,
    /// <summary>Counts only notifications awaiting a decision, with a dot when only other unread items exist.</summary>
    Decisions
}

/// <summary>Text shown by the notification center: title, filters, actions, empty message and dismiss text. Every field
/// is optional: a null or blank field (blank meaning only ECMA-262 white space and line terminators) comes from the
/// locale catalog, and any other field is kept exactly. Override one label with
/// <c>new NotificationCenterLabels(Confirm: "Approve")</c> or <c>new NotificationCenterLabels { Confirm = "Approve" }</c>.</summary>
public sealed record NotificationCenterLabels(
    string? Title = null,
    string? All = null,
    string? Confirm = null,
    string? Deny = null,
    string? MarkAllRead = null,
    string? ViewAll = null,
    string? Empty = null,
    Func<string, string>? Dismiss = null,
    string? Unread = null,
    string? GroupLabel = null)
{
    /// <summary>The default English notification center text. Leaving <c>Labels</c> unset (this instance) takes every label
    /// from the locale catalog. Any other record is caller text field by field: a copy such as <c>English with { ... }</c>
    /// has every field but <c>GroupLabel</c> set, so those are kept exactly, English included, and the unset
    /// <c>GroupLabel</c> comes from the catalog; to override one label, set only that field on a new record.</summary>
    public static NotificationCenterLabels English { get; } = new(
        "Notifications", "All", "Confirm", "Deny", "Mark all read", "View all", "No notifications",
        title => $"Dismiss: {title}", "Unread");
}
