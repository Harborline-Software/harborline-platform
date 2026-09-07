namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;
public enum BadgeVariant { Default, Secondary, Info, Success, Warning, Danger }
public enum BadgeSize { Sm, Md }
public enum BadgeAppearance { Solid, Subtle, Outline }
public enum BadgeShape { Rounded, Pill, Square }
public enum BadgeFillMode { Solid, Outline, Flat }
public enum BadgeRounded { None, Sm, Md, Lg, Full }
public enum BadgeThemeColor { Primary, Secondary, Tertiary, Info, Success, Warning, Error, Inverse }
public enum BadgePosition { Edge, Outside, Inside }
public enum BadgeHorizontal { Start, End }
public enum BadgeVertical { Top, Bottom }
public sealed record BadgeAlign(BadgeHorizontal Horizontal, BadgeVertical Vertical);
