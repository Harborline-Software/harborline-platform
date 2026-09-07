namespace Harborline.UIAdapters.Blazor.Components.Layout;

public enum HarborlineWindowState { Default, Minimized, Maximized }
public sealed record WindowPosition(double Top, double Left);
public sealed record WindowSize(double Width, double Height);
public sealed record WindowBounds(double? Top = null, double? Left = null, double? Right = null, double? Bottom = null);
