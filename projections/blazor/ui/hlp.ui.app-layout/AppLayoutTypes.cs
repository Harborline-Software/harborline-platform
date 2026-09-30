namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>How the side navigation is presented: rail, overlay, hidden, or chosen automatically.</summary>
public enum SideNavMode
{
    /// <summary>Docks the side navigation as a persistent rail beside the content.</summary>
    Rail,
    /// <summary>Shows the side navigation as an overlay above the content instead of a rail.</summary>
    Overlay,
    /// <summary>Removes the side navigation from the layout.</summary>
    Hidden,
    /// <summary>Picks rail or overlay from the current viewport and rail capability.</summary>
    Auto
}
/// <summary>Which element scrolls the app content: the main region or the whole page.</summary>
public enum AppContentScroll
{
    /// <summary>Scrolls only the main content region, keeping the shell fixed.</summary>
    Main,
    /// <summary>Scrolls the whole page, header and content together.</summary>
    Page
}
