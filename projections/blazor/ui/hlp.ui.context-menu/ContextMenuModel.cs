using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

public sealed record ContextMenuItem(
    string Id,
    string Label,
    bool Disabled = false,
    bool Danger = false,
    RenderFragment? Icon = null);

public sealed record ContextMenuGroup(IReadOnlyList<ContextMenuItem> Items);

public sealed record ContextMenuSelection(string ItemId);
