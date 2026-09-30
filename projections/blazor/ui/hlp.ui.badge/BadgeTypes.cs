namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;
/// <summary>The semantic color role of a badge.</summary>
public enum BadgeVariant
{
    /// <summary>Uses the standard neutral badge color.</summary>
    Default,
    /// <summary>Uses the muted secondary badge color.</summary>
    Secondary,
    /// <summary>Colors the badge as informational.</summary>
    Info,
    /// <summary>Colors the badge as a success state.</summary>
    Success,
    /// <summary>Colors the badge as a warning.</summary>
    Warning,
    /// <summary>Colors the badge as a danger or error state.</summary>
    Danger
}
/// <summary>The rendered size of a badge.</summary>
public enum BadgeSize
{
    /// <summary>Renders the compact badge for dense layouts.</summary>
    Sm,
    /// <summary>Renders the standard badge size.</summary>
    Md
}
/// <summary>Whether a badge is filled, tinted, or outlined.</summary>
public enum BadgeAppearance
{
    /// <summary>Fills the badge with a solid color.</summary>
    Solid,
    /// <summary>Draws the badge with a soft tinted fill.</summary>
    Subtle,
    /// <summary>Draws the badge as a border with no fill.</summary>
    Outline
}
/// <summary>The corner shape of a badge.</summary>
public enum BadgeShape
{
    /// <summary>Gives the badge softly rounded corners.</summary>
    Rounded,
    /// <summary>Gives the badge fully rounded, pill-shaped ends.</summary>
    Pill,
    /// <summary>Gives the badge square corners.</summary>
    Square
}
/// <summary>How a badge is filled: solid, outline, or flat.</summary>
public enum BadgeFillMode
{
    /// <summary>Fills the badge with a solid background.</summary>
    Solid,
    /// <summary>Draws the badge as a border with a transparent fill.</summary>
    Outline,
    /// <summary>Renders the badge with no border and a subtle background.</summary>
    Flat
}
/// <summary>The corner radius of a badge.</summary>
public enum BadgeRounded
{
    /// <summary>Gives the badge square corners.</summary>
    None,
    /// <summary>Gives the badge slightly rounded corners.</summary>
    Sm,
    /// <summary>Gives the badge moderately rounded corners.</summary>
    Md,
    /// <summary>Gives the badge strongly rounded corners.</summary>
    Lg,
    /// <summary>Gives the badge fully rounded, pill-shaped ends.</summary>
    Full
}
/// <summary>The theme color a badge is drawn in.</summary>
public enum BadgeThemeColor
{
    /// <summary>Colors the badge with the primary brand color.</summary>
    Primary,
    /// <summary>Colors the badge with the secondary brand color.</summary>
    Secondary,
    /// <summary>Colors the badge with the tertiary accent color.</summary>
    Tertiary,
    /// <summary>Colors the badge as informational.</summary>
    Info,
    /// <summary>Colors the badge as a success state.</summary>
    Success,
    /// <summary>Colors the badge as a warning.</summary>
    Warning,
    /// <summary>Colors the badge as an error.</summary>
    Error,
    /// <summary>Colors the badge for use on dark or inverted surfaces.</summary>
    Inverse
}
/// <summary>How a badge sits against the edge of its anchor: straddling it, outside it, or inside it.</summary>
public enum BadgePosition
{
    /// <summary>Centers the badge on the edge of its anchor.</summary>
    Edge,
    /// <summary>Places the badge fully outside its anchor.</summary>
    Outside,
    /// <summary>Places the badge inside the bounds of its anchor.</summary>
    Inside
}
/// <summary>Where a badge sits horizontally relative to its anchor.</summary>
public enum BadgeHorizontal
{
    /// <summary>Places the badge at the inline start side of its anchor.</summary>
    Start,
    /// <summary>Places the badge at the inline end side of its anchor.</summary>
    End
}
/// <summary>Where a badge sits vertically relative to its anchor.</summary>
public enum BadgeVertical
{
    /// <summary>Places the badge above its anchor.</summary>
    Top,
    /// <summary>Places the badge below its anchor.</summary>
    Bottom
}
/// <summary>Where a badge sits on its host: the horizontal and vertical alignment.</summary>
public sealed record BadgeAlign(BadgeHorizontal Horizontal, BadgeVertical Vertical);
