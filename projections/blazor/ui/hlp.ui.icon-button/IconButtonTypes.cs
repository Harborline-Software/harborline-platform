namespace Harborline.UIAdapters.Blazor.Components.Buttons;

/// <summary>The visual style of an icon button, from default to destructive.</summary>
public enum IconButtonAppearance
{
    /// <summary>Uses the standard icon button styling.</summary>
    Default,
    /// <summary>Draws the icon button with no background until hovered.</summary>
    Ghost,
    /// <summary>Draws the icon button with a border and no fill.</summary>
    Outline,
    /// <summary>Styles the icon button as a destructive action.</summary>
    Destructive
}
/// <summary>The rendered size of an icon button.</summary>
public enum IconButtonSize
{
    /// <summary>Renders the compact icon button for dense layouts.</summary>
    Small,
    /// <summary>Renders the standard icon button size.</summary>
    Medium,
    /// <summary>Renders the large icon button.</summary>
    Large,
    /// <summary>Renders the icon button with a touch-sized hit target.</summary>
    Touch
}
