namespace Harborline.UIAdapters.Blazor.Accessibility;

/// <summary>Closed hit-area strategies equivalent to the retained React affordances.</summary>
public enum TouchTargetStrategy
{
    GrowBox,
    OverlayUsingExistingPositionContext,
    OverlayEstablishingPositionContext,
}

/// <summary>CSS class mapping for the Harborline 44 CSS-pixel touch target policy.</summary>
public static class TouchTargetAffordances
{
    public const int MinimumCssPixels = 44;
    public const string GrowBox = "hl-touch-target";
    public const string OverlayUsingExistingPositionContext = "hl-touch-target-overlay";
    public const string OverlayEstablishingPositionContext = "hl-touch-target-overlay hl-touch-target-overlay--positioned";

    public static string For(TouchTargetStrategy strategy) => strategy switch
    {
        TouchTargetStrategy.GrowBox => GrowBox,
        TouchTargetStrategy.OverlayUsingExistingPositionContext => OverlayUsingExistingPositionContext,
        TouchTargetStrategy.OverlayEstablishingPositionContext => OverlayEstablishingPositionContext,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };
}
