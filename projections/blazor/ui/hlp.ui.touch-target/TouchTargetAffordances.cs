namespace Harborline.UIAdapters.Blazor.Accessibility;

/// <summary>Closed hit-area strategies equivalent to the retained React affordances.</summary>
public enum TouchTargetStrategy
{
    /// <summary>Expands the existing element's box to meet the touch target.</summary>
    GrowBox,
    /// <summary>Uses an overlay while preserving the existing positioning context.</summary>
    OverlayUsingExistingPositionContext,
    /// <summary>Uses an overlay that establishes its own positioning context.</summary>
    OverlayEstablishingPositionContext,
}

/// <summary>CSS class mapping for the Harborline 44 CSS-pixel touch target policy.</summary>
public static class TouchTargetAffordances
{
/// <summary>Smallest touch target size, in CSS pixels.</summary>
    public const int MinimumCssPixels = 44;
/// <summary>Class that grows the box of an element to the minimum touch target size.</summary>
    public const string GrowBox = "hl-touch-target";
/// <summary>Class that adds an overlay to enlarge the hit area, for elements that already position their children.</summary>
    public const string OverlayUsingExistingPositionContext = "hl-touch-target-overlay";
/// <summary>Class that adds an overlay to enlarge the hit area and makes the element a positioning context.</summary>
    public const string OverlayEstablishingPositionContext = "hl-touch-target-overlay hl-touch-target-overlay--positioned";

/// <summary>Returns the CSS classes for a touch target strategy.</summary>
    public static string For(TouchTargetStrategy strategy) => strategy switch
    {
        TouchTargetStrategy.GrowBox => GrowBox,
        TouchTargetStrategy.OverlayUsingExistingPositionContext => OverlayUsingExistingPositionContext,
        TouchTargetStrategy.OverlayEstablishingPositionContext => OverlayEstablishingPositionContext,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };
}
