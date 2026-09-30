namespace Harborline.UIAdapters.Blazor.Components.Feedback;
/// <summary>The rendered size of a spinner.</summary>
public enum SpinnerSize
{
    /// <summary>Renders the extra-small spinner for inline use.</summary>
    Xs,
    /// <summary>Renders the small spinner.</summary>
    Sm,
    /// <summary>Renders the standard spinner size.</summary>
    Md,
    /// <summary>Renders the large spinner.</summary>
    Lg
}
/// <summary>The theme color a spinner is drawn in.</summary>
public enum SpinnerThemeColor
{
    /// <summary>Colors the spinner with the primary brand color.</summary>
    Primary,
    /// <summary>Colors the spinner with the secondary brand color.</summary>
    Secondary,
    /// <summary>Colors the spinner with the tertiary accent color.</summary>
    Tertiary,
    /// <summary>Colors the spinner as informational.</summary>
    Info,
    /// <summary>Colors the spinner as a success state.</summary>
    Success,
    /// <summary>Colors the spinner as a warning.</summary>
    Warning,
    /// <summary>Colors the spinner as an error.</summary>
    Error,
    /// <summary>Colors the spinner for use on dark or inverted surfaces.</summary>
    Inverse
}
/// <summary>The animation style of a spinner.</summary>
public enum SpinnerType
{
    /// <summary>Draws a rotating ring.</summary>
    Ring,
    /// <summary>Draws converging elements that animate toward the center.</summary>
    Converging
}
