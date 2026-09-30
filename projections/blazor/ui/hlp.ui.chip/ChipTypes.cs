namespace Harborline.UIAdapters.Blazor.Components.Feedback;
/// <summary>How a chip is filled: solid, flat, or outline.</summary>
public enum ChipFillMode
{
    /// <summary>Fills the chip with a solid background.</summary>
    Solid,
    /// <summary>Renders the chip with no border and a subtle background.</summary>
    Flat,
    /// <summary>Draws the chip as a border with a transparent fill.</summary>
    Outline
}
/// <summary>The theme color a chip is drawn in.</summary>
public enum ChipThemeColor
{
    /// <summary>Uses the neutral base color for the chip.</summary>
    Base,
    /// <summary>Colors the chip with the primary brand color.</summary>
    Primary,
    /// <summary>Colors the chip with the secondary brand color.</summary>
    Secondary,
    /// <summary>Colors the chip with the tertiary accent color.</summary>
    Tertiary,
    /// <summary>Colors the chip as informational.</summary>
    Info,
    /// <summary>Colors the chip as a success state.</summary>
    Success,
    /// <summary>Colors the chip as a warning.</summary>
    Warning,
    /// <summary>Colors the chip as an error.</summary>
    Error
}
/// <summary>The corner radius of a chip.</summary>
public enum ChipRounded
{
    /// <summary>Gives the chip slightly rounded corners.</summary>
    Small,
    /// <summary>Gives the chip moderately rounded corners.</summary>
    Medium,
    /// <summary>Gives the chip strongly rounded corners.</summary>
    Large,
    /// <summary>Gives the chip fully rounded, pill-shaped ends.</summary>
    Full
}
