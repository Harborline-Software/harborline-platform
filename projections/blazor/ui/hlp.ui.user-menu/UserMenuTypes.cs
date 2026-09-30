using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Shell;

/// <summary>The signed-in user shown in the user menu: name, email, role and avatar address.</summary>
public sealed record UserIdentity(string Name, string? Email = null, string? Role = null, string? AvatarUri = null);
/// <summary>The kind of entry in a user menu: action, link, custom content, or separator.</summary>
public enum UserMenuEntryKind
{
    /// <summary>A menu item that runs a callback when chosen.</summary>
    Action,
    /// <summary>A menu item that navigates to a URL.</summary>
    Link,
    /// <summary>A menu entry that renders caller-supplied content.</summary>
    Custom,
    /// <summary>A divider line between groups of entries.</summary>
    Separator
}
/// <summary>Whether the user menu opens above or below its trigger.</summary>
public enum UserMenuPlacement
{
    /// <summary>Opens the menu above its trigger.</summary>
    Top,
    /// <summary>Opens the menu below its trigger.</summary>
    Bottom
}
/// <summary>How the user menu aligns to its trigger along the inline axis.</summary>
public enum UserMenuAlignment
{
    /// <summary>Aligns the menu with the inline start edge of its trigger.</summary>
    InlineStart,
    /// <summary>Centers the menu on its trigger.</summary>
    Center,
    /// <summary>Aligns the menu with the inline end edge of its trigger.</summary>
    InlineEnd
}
/// <summary>One entry in the user menu: kind, label, subtitle, icon, destination or action, and whether it closes the menu.</summary>
public sealed record UserMenuEntry(
    string Id,
    UserMenuEntryKind Kind,
    string? Label = null,
    string? Subtitle = null,
    RenderFragment? Icon = null,
    string? Destination = null,
    Func<Task>? ActivateAsync = null,
    RenderFragment? Content = null,
    bool CloseOnSelect = true,
    bool Disabled = false);
/// <summary>Text for the user menu: the trigger label built from the user name, the panel label and the sign-out label.</summary>
public sealed record UserMenuLabels(Func<string,string> Trigger, string Panel, string SignOut)
{
    /// <summary>The default English user menu text.</summary>
    public static UserMenuLabels English { get; } = new(name => $"Account menu for {name}", "Account menu", "Sign out");
}
