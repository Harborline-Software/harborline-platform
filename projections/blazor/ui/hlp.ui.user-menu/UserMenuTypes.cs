using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Shell;

public sealed record UserIdentity(string Name, string? Email = null, string? Role = null, string? AvatarUri = null);
public enum UserMenuEntryKind { Action, Link, Custom, Separator }
public enum UserMenuPlacement { Top, Bottom }
public enum UserMenuAlignment { InlineStart, Center, InlineEnd }
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
public sealed record UserMenuLabels(Func<string,string> Trigger, string Panel, string SignOut)
{
    public static UserMenuLabels English { get; } = new(name => $"Account menu for {name}", "Account menu", "Sign out");
}
