namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>The rendered size of a text input.</summary>
public enum InputSize
{
    /// <summary>Renders the compact input for dense layouts.</summary>
    Sm,
    /// <summary>Renders the standard input size.</summary>
    Md,
    /// <summary>Renders the large input.</summary>
    Lg,
    /// <summary>Renders the compact input for dense layouts.</summary>
    Small,
    /// <summary>Renders the standard input size.</summary>
    Medium,
    /// <summary>Renders the large input.</summary>
    Large
}
/// <summary>How a text input is filled: solid, outline, or flat.</summary>
public enum InputFillMode
{
    /// <summary>Fills the input with a solid background.</summary>
    Solid,
    /// <summary>Draws the input as a border with a transparent fill.</summary>
    Outline,
    /// <summary>Renders the input with no border and a subtle background.</summary>
    Flat
}
/// <summary>The corner radius of a text input.</summary>
public enum InputRounded
{
    /// <summary>Gives the input slightly rounded corners.</summary>
    Small,
    /// <summary>Gives the input moderately rounded corners.</summary>
    Medium,
    /// <summary>Gives the input strongly rounded corners.</summary>
    Large,
    /// <summary>Gives the input fully rounded, pill-shaped ends.</summary>
    Full
}
