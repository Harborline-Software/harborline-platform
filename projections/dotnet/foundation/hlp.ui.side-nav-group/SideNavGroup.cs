namespace Harborline.Foundation.Navigation;

/// <summary>Immutable framework-neutral side-navigation item.</summary>
public sealed record SideNavItem
{
    /// <summary>Creates one validated navigation item.</summary>
    public SideNavItem(string id, string label, string? href = null, bool disabled = false, IReadOnlyList<SideNavItem>? children = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("invalid-side-nav-identity", nameof(id));
        Id = id; Label = label ?? throw new ArgumentNullException(nameof(label)); Href = href; Disabled = disabled; Children = children?.ToArray() ?? [];
    }
    /// <summary>Stable item identity.</summary>
    public string Id { get; }
    /// <summary>Caller-localized visible label.</summary>
    public string Label { get; }
    /// <summary>Optional host route.</summary>
    public string? Href { get; }
    /// <summary>Whether activation is unavailable.</summary>
    public bool Disabled { get; }
    /// <summary>Ordered immutable child items.</summary>
    public IReadOnlyList<SideNavItem> Children { get; }
}

/// <summary>Immutable framework-neutral side-navigation group.</summary>
public sealed record SideNavGroup
{
    /// <summary>Creates one validated navigation group.</summary>
    public SideNavGroup(string id, IReadOnlyList<SideNavItem> items, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("invalid-side-nav-identity", nameof(id));
        Id = id; Label = label; Items = items?.ToArray() ?? throw new ArgumentNullException(nameof(items));
    }
    /// <summary>Stable group identity.</summary>
    public string Id { get; }
    /// <summary>Optional caller-localized group label.</summary>
    public string? Label { get; }
    /// <summary>Ordered immutable group items.</summary>
    public IReadOnlyList<SideNavItem> Items { get; }
}
