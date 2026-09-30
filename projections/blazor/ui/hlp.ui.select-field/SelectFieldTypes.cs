namespace Harborline.UIAdapters.Blazor.Components.Forms.Inputs;

/// <summary>The rendered size of a select field.</summary>
public enum SelectFieldSize
{
    /// <summary>Renders the compact select for dense layouts.</summary>
    Sm,
    /// <summary>Renders the standard select size.</summary>
    Md,
    /// <summary>Renders the large select.</summary>
    Lg
}
/// <summary>A choice in a select field: value, label and whether it is disabled.</summary>
public sealed record SelectOption(string Value, string Label, bool Disabled = false);
