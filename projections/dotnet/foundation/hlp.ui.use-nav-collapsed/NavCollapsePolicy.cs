namespace Harborline.Foundation.Responsive;

/// <summary>Inputs for one navigation-collapse controller.</summary>
public sealed record NavCollapseOptions(
    bool? Collapsed = null,
    bool DefaultCollapsed = false,
    int AutoCollapseBelow = 768,
    int OverlayBelow = 768);

/// <summary>Observable navigation-collapse state.</summary>
public sealed record NavCollapseSnapshot(bool Collapsed, bool IsOverlay, bool BaseCollapsed, bool UserOverrode);

/// <summary>Pure controller that preserves a user's base navigation preference across viewport changes.</summary>
public sealed class NavCollapseController
{
    private readonly Action<bool>? onCollapsedChange;
    private bool? controlledCollapsed;
    private bool baseCollapsed;
    private bool narrow;
    private bool overlay;

    /// <summary>Creates a controller from authored options and initial media-query matches.</summary>
    public NavCollapseController(
        NavCollapseOptions? options = null,
        bool initialNarrow = false,
        bool initialOverlay = false,
        Action<bool>? onCollapsedChange = null)
    {
        Options = options ?? new();
        controlledCollapsed = Options.Collapsed;
        baseCollapsed = Options.DefaultCollapsed;
        narrow = Options.AutoCollapseBelow > 0 && initialNarrow;
        overlay = Options.OverlayBelow > 0 && initialOverlay;
        this.onCollapsedChange = onCollapsedChange;
    }

    /// <summary>Authored options held for this controller lifetime.</summary>
    public NavCollapseOptions Options { get; }
    /// <summary>Whether the user explicitly selected a collapse state.</summary>
    public bool UserOverrode { get; private set; }
    /// <summary>Whether a host-controlled collapse value is authoritative.</summary>
    public bool IsControlled => controlledCollapsed.HasValue;
    /// <summary>Media query for the automatic-collapse threshold.</summary>
    public string AutoCollapseQuery => Query(Options.AutoCollapseBelow);
    /// <summary>Media query for the overlay threshold.</summary>
    public string OverlayQuery => Query(Options.OverlayBelow);
    /// <summary>Current immutable observable snapshot.</summary>
    public NavCollapseSnapshot Snapshot => new(EffectiveCollapsed, overlay, baseCollapsed, UserOverrode);

    private bool EffectiveCollapsed
    {
        get
        {
            var preference = controlledCollapsed ?? baseCollapsed;
            return UserOverrode ? preference : preference || narrow;
        }
    }

    /// <summary>Applies a new host-controlled collapse value.</summary>
    public void SetControlledValue(bool collapsed)
    {
        if (!IsControlled) throw new InvalidOperationException("controller-is-uncontrolled");
        controlledCollapsed = collapsed;
    }

    /// <summary>Records an explicit user request.</summary>
    public void SetCollapsed(bool collapsed)
    {
        UserOverrode = true;
        if (!IsControlled) baseCollapsed = collapsed;
        onCollapsedChange?.Invoke(collapsed);
    }

    /// <summary>Requests the inverse of the effective state.</summary>
    public void Toggle() => SetCollapsed(!EffectiveCollapsed);

    /// <summary>Applies current automatic-collapse and overlay query matches.</summary>
    public void UpdateViewport(bool isNarrow, bool isOverlay)
    {
        var nextNarrow = Options.AutoCollapseBelow > 0 && isNarrow;
        var crossedIntoNarrow = !narrow && nextNarrow;
        narrow = nextNarrow;
        overlay = Options.OverlayBelow > 0 && isOverlay;
        if (crossedIntoNarrow && IsControlled && controlledCollapsed == false && !UserOverrode)
            onCollapsedChange?.Invoke(true);
    }

    /// <summary>Builds the inclusive max-width query used by browser observers.</summary>
    public static string Query(int threshold) => $"(max-width: {Math.Max(0, threshold - 1)}px)";
}
