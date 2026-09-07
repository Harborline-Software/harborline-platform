using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Navigation;

public sealed record SideNavNavigationItem(
    string Id,
    string Label,
    RenderFragment? Icon = null,
    bool Disabled = false,
    string? Href = null,
    RenderFragment? TrailingContent = null,
    IReadOnlyList<SideNavNavigationItem>? Children = null);

public sealed record SideNavNavigationGroup(string Id, IReadOnlyList<SideNavNavigationItem> Items, string? Label = null);

public sealed class SideNavState
{
    public HashSet<string> Expanded { get; } = new(StringComparer.Ordinal);
    public string? ActiveItemId { get; set; }
    public bool Collapsed { get; set; }
    public EventCallback<SideNavNavigationItem> ItemActivated { get; set; }

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
