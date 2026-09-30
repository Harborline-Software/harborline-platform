using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

/// <summary>One item in the side navigation: label, icon, link, disabled state, trailing content and child items.</summary>
public sealed record SideNavNavigationItem(
    string Id,
    string Label,
    RenderFragment? Icon = null,
    bool Disabled = false,
    string? Href = null,
    RenderFragment? TrailingContent = null,
    IReadOnlyList<SideNavNavigationItem>? Children = null);

/// <summary>A group of side navigation items with an optional heading.</summary>
public sealed record SideNavNavigationGroup(string Id, IReadOnlyList<SideNavNavigationItem> Items, string? Label = null);

/// <summary>The side navigation state: which items are expanded, the active item, whether it is collapsed and the activation callback.</summary>
public sealed class SideNavState
{
    /// <summary>The ids of the items currently expanded.</summary>
    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
    /// <summary>Id of the active item, or null when none is active.</summary>
    public string? ActiveItemId { get; set; }
    /// <summary>Whether the side navigation is collapsed to icons only.</summary>
    public bool Collapsed { get; set; }
    /// <summary>Called when a navigation item is activated.</summary>
    public EventCallback<SideNavNavigationItem> ItemActivated { get; set; }

    /// <summary>Expands the parents of the active item so it is visible.</summary>
    public void ExpandActive(IEnumerable<SideNavNavigationItem> items)
    {
        foreach (var item in items)
        {
            var children = item.Children ?? [];
            if (Contains(children, ActiveItemId)) Expanded.Add(item.Id);
            ExpandActive(children);
        }
    }

    private static bool Contains(IEnumerable<SideNavNavigationItem> items, string? id) =>
        id is not null && items.Any(item => item.Id == id || Contains(item.Children ?? [], id));
}
