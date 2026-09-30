namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>Which directions the user may drag-resize a text area.</summary>
public enum TextAreaResize
{
    /// <summary>Prevents the user from resizing the text area.</summary>
    None,
    /// <summary>Lets the user drag to change only the height.</summary>
    Vertical,
    /// <summary>Lets the user drag to change only the width.</summary>
    Horizontal,
    /// <summary>Lets the user drag to change width and height.</summary>
    Both
}
/// <summary>The rendered size of a text area.</summary>
public enum TextAreaSize
{
    /// <summary>Renders the compact text area for dense layouts.</summary>
    Small,
    /// <summary>Renders the standard text area size.</summary>
    Medium,
    /// <summary>Renders the large text area.</summary>
    Large
}
/// <summary>How a text area is filled: solid, outline, or flat.</summary>
public enum TextAreaFillMode
{
    /// <summary>Fills the text area with a solid background.</summary>
    Solid,
    /// <summary>Draws the text area as a border with a transparent fill.</summary>
    Outline,
    /// <summary>Renders the text area with no border and a subtle background.</summary>
    Flat
}
/// <summary>The corner radius of a text area.</summary>
public enum TextAreaRounded
{
    /// <summary>Gives the text area slightly rounded corners.</summary>
    Small,
    /// <summary>Gives the text area moderately rounded corners.</summary>
    Medium,
    /// <summary>Gives the text area strongly rounded corners.</summary>
    Large,
    /// <summary>Gives the text area fully rounded, pill-shaped ends.</summary>
    Full
}
