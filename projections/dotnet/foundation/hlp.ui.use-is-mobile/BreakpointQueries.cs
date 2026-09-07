namespace Harborline.Foundation.Responsive;

/// <summary>application-authored responsive query identity shared by browser adapters.</summary>
public static class BreakpointQueries
{
    /// <summary>Phone-width structural collapse query.</summary>
    public const string Phone = "(max-width: 767px)";
    /// <summary>Persistent rail query requiring both usable width and height. One constant:
    /// bound to <see cref="FormFactorQueries.MasterDetailRail"/> (ticket 154) so the two names
    /// can never carry two thresholds.</summary>
    public const string CanShowRail = FormFactorQueries.MasterDetailRail;
    /// <summary>Held width-only dock exception retained for Harborline App compatibility.</summary>
    public const string Dock = "(min-width: 1280px)";
}

/// <summary>Fail-closed observed breakpoint snapshot.</summary>
public sealed record ResponsiveBreakpointState(bool IsMobile = false, bool CanShowRail = false);
