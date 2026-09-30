using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>One context menu entry: label, disabled and danger state and an optional icon.</summary>
public sealed record ContextMenuItem(
    string Id,
    string Label,
    bool Disabled = false,
    bool Danger = false,
    RenderFragment? Icon = null);

/// <summary>A run of context menu items shown together and separated from other groups.</summary>
public sealed record ContextMenuGroup(IReadOnlyList<ContextMenuItem> Items);

/// <summary>The chosen context menu entry, identified by item id.</summary>
public sealed record ContextMenuSelection(string ItemId);
