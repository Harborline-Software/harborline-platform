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

/// <summary>Text shown by the notification center: title, filters, actions, empty message and dismiss text.</summary>
public sealed record NotificationCenterLabels(
    string Title,
    string All,
    string Confirm,
    string Deny,
    string MarkAllRead,
    string ViewAll,
    string Empty,
    Func<string, string> Dismiss,
    string Unread)
{
    /// <summary>The default English notification center text.</summary>
    public static NotificationCenterLabels English { get; } = new(
        "Notifications", "All", "Confirm", "Deny", "Mark all read", "View all", "No notifications",
        title => $"Dismiss: {title}", "Unread");
}
