namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>The rendered size of a numeric text box.</summary>
public enum NumericTextBoxSize
{
    /// <summary>Renders the compact numeric box for dense layouts.</summary>
    Small,
    /// <summary>Renders the standard numeric box size.</summary>
    Medium,
    /// <summary>Renders the large numeric box.</summary>
    Large
}
/// <summary>How a numeric text box is filled: solid, outline, or flat.</summary>
public enum NumericTextBoxFillMode
{
    /// <summary>Fills the numeric box with a solid background.</summary>
    Solid,
    /// <summary>Draws the numeric box as a border with a transparent fill.</summary>
    Outline,
    /// <summary>Renders the numeric box with no border and a subtle background.</summary>
    Flat
}
/// <summary>The corner radius of a numeric text box.</summary>
public enum NumericTextBoxRounded
{
    /// <summary>Gives the numeric box slightly rounded corners.</summary>
    Small,
    /// <summary>Gives the numeric box moderately rounded corners.</summary>
    Medium,
    /// <summary>Gives the numeric box strongly rounded corners.</summary>
    Large,
    /// <summary>Gives the numeric box fully rounded, pill-shaped ends.</summary>
    Full
}
