namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>The heading element level used for a page title.</summary>
public enum PageHeadingLevel
{
    /// <summary>Renders the page title as an h1 heading.</summary>
    H1,
    /// <summary>Renders the page title as an h2 heading.</summary>
    H2,
    /// <summary>Renders the page title as an h3 heading.</summary>
    H3
}
/// <summary>The padding around a page body.</summary>
public enum PageBodyPadding
{
    /// <summary>Removes padding so the body runs to the page edge.</summary>
    None,
    /// <summary>Adds tight padding around the page body.</summary>
    Sm,
    /// <summary>Adds standard padding around the page body.</summary>
    Md,
    /// <summary>Adds generous padding around the page body.</summary>
    Lg
}
