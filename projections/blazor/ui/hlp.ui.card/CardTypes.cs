namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>The surface treatment of a card: flat, raised, outlined, or elevated.</summary>
public enum CardAppearance
{
    /// <summary>Draws the card with no border or shadow.</summary>
    Flat,
    /// <summary>Draws the card with a light shadow lifting it off the page.</summary>
    Raised,
    /// <summary>Draws the card with a border and no shadow.</summary>
    Outlined,
    /// <summary>Draws the card with a pronounced shadow.</summary>
    Elevated
}
/// <summary>The inner padding of a card.</summary>
public enum CardPadding
{
    /// <summary>Removes padding so content runs to the card edge.</summary>
    None,
    /// <summary>Adds tight padding inside the card.</summary>
    Small,
    /// <summary>Adds standard padding inside the card.</summary>
    Medium,
    /// <summary>Adds generous padding inside the card.</summary>
    Large
}
/// <summary>Whether a card lays out its content vertically or horizontally.</summary>
public enum CardOrientation
{
    /// <summary>Stacks the card sections top to bottom.</summary>
    Vertical,
    /// <summary>Lays the card sections out side by side.</summary>
    Horizontal
}
internal sealed record HarborlineCardContext(CardPadding Padding, CardOrientation Orientation);
