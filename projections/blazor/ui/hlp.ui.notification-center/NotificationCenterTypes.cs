namespace Harborline.UIAdapters.Blazor.Components.Navigation;

public sealed record NotificationCenterItem(string Id, string Title, string Timestamp, string Kind, bool Read = false, string? Body = null, string? Preview = null, string? Actor = null);
public sealed record NotificationGroup(string Kind, string Label);
public sealed record NotificationCounts(int Pending, int Unread, NotificationBadgeMode Mode);
public enum NotificationBadgeMode { Unread, Decisions }

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
    public static NotificationCenterLabels English { get; } = new(
        "Notifications", "All", "Confirm", "Deny", "Mark all read", "View all", "No notifications",
        title => $"Dismiss: {title}", "Unread");
}
