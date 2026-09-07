namespace Harborline.UIAdapters.Blazor.Components.Layout;

public enum CardAppearance { Flat, Raised, Outlined, Elevated }
public enum CardPadding { None, Small, Medium, Large }
public enum CardOrientation { Vertical, Horizontal }
internal sealed record HarborlineCardContext(CardPadding Padding, CardOrientation Orientation);
