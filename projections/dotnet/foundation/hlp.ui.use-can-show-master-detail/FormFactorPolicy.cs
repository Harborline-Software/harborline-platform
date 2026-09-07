namespace Harborline.Foundation.Responsive;

/// <summary>Closed form-factor modes used by Harborline App layout affordances.</summary>
public enum FormFactorMode
{
    /// <summary>Phone-width or short-landscape mode.</summary>
    Phone,
    /// <summary>Residual mode between phone and desktop.</summary>
    Tablet,
    /// <summary>Desktop-width mode.</summary>
    Desktop,
}

/// <summary>Viewport orientation signal.</summary>
public enum FormFactorOrientation
{
    /// <summary>Portrait orientation.</summary>
    Portrait,
    /// <summary>Landscape orientation.</summary>
    Landscape,
}

/// <summary>Viewport height classification.</summary>
public enum FormFactorHeightClass
{
    /// <summary>Height at or below the authored short-height threshold.</summary>
    Short,
    /// <summary>Height above the authored short-height threshold.</summary>
    Tall,
}

/// <summary>Raw media-query matches consumed by the pure form-factor resolver.
/// WARNING (ticket 154): <c>MasterDetailRail</c> is a NEW ninth signal, defaulted to
/// <see langword="false"/>. A host still observing only the original seven queries never
/// sets it, and its <see cref="ResolvedFormFactor.CanShowMasterDetail"/> is then false at
/// every viewport — permanently. Any host must also observe
/// <see cref="FormFactorQueries.MasterDetailRail"/> and pass the match through.</summary>
public sealed record FormFactorSignals(
    bool PhoneWidth = false,
    bool DesktopWidth = false,
    bool Landscape = false,
    bool ShortHeight = false,
    bool AnyCoarsePointer = false,
    bool AnyFinePointer = false,
    bool Hover = false,
    bool CanSplitBuilderPanes = false,
    bool MasterDetailRail = false);

/// <summary>Resolved responsive policy snapshot consumed by Blazor hosts.</summary>
public sealed record ResolvedFormFactor(
    FormFactorMode Mode,
    FormFactorOrientation Orientation,
    FormFactorHeightClass HeightClass,
    bool CanShowMasterDetail,
    bool TouchSizing,
    bool ShowHoverAffordance,
    bool CanSplitBuilderPanes);

/// <summary>Authored media-query identity for form-factor observers.</summary>
public static class FormFactorQueries
{
    /// <summary>Phone-width query.</summary>
    public const string Phone = "(max-width: 767px)";
    /// <summary>Desktop-width query.</summary>
    public const string Desktop = "(min-width: 1280px)";
    /// <summary>Landscape orientation query.</summary>
    public const string Landscape = "(orientation: landscape)";
    /// <summary>Short-height query.</summary>
    public const string ShortHeight = "(max-height: 500px)";
    /// <summary>Any coarse pointer is available.</summary>
    public const string AnyCoarsePointer = "(any-pointer: coarse)";
    /// <summary>Any fine pointer is available.</summary>
    public const string AnyFinePointer = "(any-pointer: fine)";
    /// <summary>Hover capability is available.</summary>
    public const string Hover = "(hover: hover)";
    /// <summary>Builder split-pane width query.</summary>
    public const string CanSplitBuilderPanes = "(min-width: 1024px)";

    /// <summary>The one master-detail rail decider (ticket 154): the capability policy and the
    /// detail panel's docked-versus-modal gate both derive from this single query.</summary>
    public const string MasterDetailRail = "(min-width: 768px) and (min-height: 600px)";
}

/// <summary>Pure capabilities-over-platform form-factor policy.</summary>
public static class FormFactorPolicy
{
    /// <summary>Resolves raw query matches into one immutable affordance snapshot.</summary>
    public static ResolvedFormFactor Resolve(FormFactorSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var shortLandscape = signals.Landscape && signals.ShortHeight;
        var mode = signals.PhoneWidth || shortLandscape
            ? FormFactorMode.Phone
            : signals.DesktopWidth ? FormFactorMode.Desktop : FormFactorMode.Tablet;
        return new(
            mode,
            signals.Landscape ? FormFactorOrientation.Landscape : FormFactorOrientation.Portrait,
            signals.ShortHeight ? FormFactorHeightClass.Short : FormFactorHeightClass.Tall,
            // Fail closed: master-detail needs the rail viewport the detail panel itself docks at,
            // not merely a non-phone mode (768x550 is tablet mode yet below the rail's 600px floor).
            mode != FormFactorMode.Phone && signals.MasterDetailRail,
            mode == FormFactorMode.Phone || signals.AnyCoarsePointer,
            signals.Hover || signals.AnyFinePointer,
            signals.CanSplitBuilderPanes);
    }
}
