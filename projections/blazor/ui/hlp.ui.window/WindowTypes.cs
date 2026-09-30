namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Whether a window is at its default size, minimized, or maximized.</summary>
public enum HarborlineWindowState
{
    /// <summary>Shows the window at its normal size and position.</summary>
    Default,
    /// <summary>Collapses the window out of the way.</summary>
    Minimized,
    /// <summary>Expands the window to fill its container.</summary>
    Maximized
}
/// <summary>A window position: distance from the top and from the left.</summary>
public sealed record WindowPosition(double Top, double Left);
/// <summary>A window width and height.</summary>
public sealed record WindowSize(double Width, double Height);
/// <summary>Window bounds as optional top, left, right and bottom offsets.</summary>
public sealed record WindowBounds(double? Top = null, double? Left = null, double? Right = null, double? Bottom = null);
