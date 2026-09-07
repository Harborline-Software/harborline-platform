namespace Harborline.UIAdapters.Blazor.Components.Layout;

public abstract record DockNode;
public sealed record DockPane(IReadOnlyList<PackPanelDeclaration> Panels, IReadOnlyList<double> Fractions) : DockNode;
public sealed record DockSplit(string Orientation, double Ratio, DockNode First, DockNode Second) : DockNode;
public sealed record DockPanelContainer(PackPanelDeclaration Panel, string Kind);

public sealed record DockLayout(IReadOnlyList<PackPanelDeclaration> OpenPanels, bool Spread, DockNode? Root, DockNode? RenderRoot, IReadOnlyList<DockPanelContainer> Containers, ShellBreakpoint Breakpoint, ShellBreakpoint PlacementClass, int PaneCapacity, double ShellInlineSize, double RailInlineSize, bool SpreadUnavailable, string? SpreadUnavailableReason)
{
    public const int PaneMinimumWidth = 300;
    public int MinimumPaneWidth => PaneMinimumWidth;
    public IReadOnlyList<DockPane> Panes => Flatten(Root);

    /// <summary>
    /// chrome-spec: the dock shrinks before the body pane keeps its floor, so the ceiling on a dock width is
    /// the row minus the rail minus that floor. The clamp lives HERE, in the model, and the stylesheet only
    /// paints <c>flex:0 0 var(--hl-app-shell-dock-size)</c> - whatever width the model hands the aside is the
    /// width the box takes, so the dock's inline end never leaves the content row. Below PaneMinimumWidth
    /// there is no dock to size: the newest-opened panel is a sheet, and pane CAPACITY is derived from this same
    /// ceiling (<see cref="Capacity"/>) so the two can never disagree. The shell inline size is the MEASURED width
    /// of the shell box in both lanes - never the breakpoint class width, which is up to 340px away from it.
    /// </summary>
    public static double WidthCeiling(double shellInlineSize, double railInlineSize, double contentFloor = ShellChromeContract.ContentFloor)
        => shellInlineSize - railInlineSize - contentFloor;

    public static double ClampWidth(double requested, double shellInlineSize, double railInlineSize, double contentFloor = ShellChromeContract.ContentFloor)
        => Math.Max(PaneMinimumWidth, Math.Min(Math.Round(requested, MidpointRounding.AwayFromZero), WidthCeiling(shellInlineSize, railInlineSize, contentFloor)));

    /// <summary>How many panes the row can hold beside the rail and the content floor. Same ceiling as the clamp.</summary>
    public static int Capacity(double shellInlineSize, double railInlineSize, double contentFloor = ShellChromeContract.ContentFloor)
        => (int)Math.Max(0, Math.Floor(WidthCeiling(shellInlineSize, railInlineSize, contentFloor) / PaneMinimumWidth));

    public static DockLayout Create(IReadOnlyList<PackPanelDeclaration> panels, bool spread, ShellBreakpoint breakpoint = ShellBreakpoint.Large, double shellInlineSize = double.PositiveInfinity, double railInlineSize = 0, bool spreadUnavailable = false, string? spreadUnavailableReason = null)
    {
        var depth = spread ? 1 : 2;
        // Placement is constant inside a breakpoint class (adaptation-v1 classPlacementCases), so capacity is
        // counted from the CLASS width - the narrowest row in the class. The class that governs PLACEMENT is the
        // one the MEASURED container falls in, never the viewport's: capacity and the width clamp then read ONE
        // width, so a narrow scene inside a wide viewport cannot dock a pane its own box cannot seat. The
        // `breakpoint` argument stays the media-query (viewport) class and drives sheet CHROME only - bottom
        // sheet in compact, side sheet elsewhere. The rail is subtracted in both, because it takes the row's
        // space before the dock does.
        var placementClass = double.IsPositiveInfinity(shellInlineSize) ? breakpoint : ShellChromeContract.Breakpoint((int)shellInlineSize);
        var classWidth = placementClass switch { ShellBreakpoint.ExtraLarge => 1600, ShellBreakpoint.Large => 1200, ShellBreakpoint.Expanded => 840, ShellBreakpoint.Medium => 600, _ => 0 };
        var capacity = double.IsPositiveInfinity(shellInlineSize) ? int.MaxValue : Capacity(classWidth, railInlineSize);
        var paneCapacity = capacity;
        var dockedCount = capacity == int.MaxValue ? panels.Count : Math.Min(panels.Count, capacity * depth);
        var docked = panels.Take(dockedCount).ToArray();
        var sheetKind = breakpoint == ShellBreakpoint.Compact ? "bottom-sheet" : "side-sheet";
        return new([.. panels], spread, Build(docked, depth), Build(panels, depth), panels.Select((panel, index) => new DockPanelContainer(panel, index < dockedCount ? "docked" : sheetKind)).ToArray(), breakpoint, placementClass, paneCapacity, shellInlineSize, railInlineSize, spreadUnavailable, spreadUnavailableReason);
    }

    public DockLayout Open(PackPanelDeclaration panel) => OpenPanels.Any(open => open.Id == panel.Id) ? this : Create([.. OpenPanels, panel], Spread, Breakpoint, ShellInlineSize, RailInlineSize, SpreadUnavailable, SpreadUnavailableReason);
    public DockLayout Close(string panelId) => Create(OpenPanels.Where(panel => panel.Id != panelId).ToArray(), Spread, Breakpoint, ShellInlineSize, RailInlineSize, SpreadUnavailable, SpreadUnavailableReason);
    public DockLayout WithSpread(bool spread) => Create(OpenPanels, spread, Breakpoint, ShellInlineSize, RailInlineSize, SpreadUnavailable, SpreadUnavailableReason);

    private static DockNode? Build(IReadOnlyList<PackPanelDeclaration> panels, int depth)
    {
        DockNode? root = null;
        foreach (var items in panels.Chunk(depth)) { var pane = new DockPane(items, items.Select(_ => 1d / items.Length).ToArray()); root = root is null ? pane : new DockSplit("horizontal", .5, root, pane); }
        return root;
    }

    private static IReadOnlyList<DockPane> Flatten(DockNode? node) => node switch
    {
        null => [],
        DockPane pane => [pane],
        DockSplit split => [.. Flatten(split.First), .. Flatten(split.Second)],
        _ => throw new InvalidOperationException("unknown-dock-node")
    };

    public static double NodeMinimum(DockNode node, bool vertical, IReadOnlyList<DockPanelContainer> containers)
    {
        if(node is DockPane pane){var panels=pane.Panels.Where(p=>containers.Any(c=>c.Panel.Id==p.Id&&c.Kind=="docked")).ToArray();return vertical?(panels.Length>0?PaneMinimumWidth:0):panels.Sum(ShellChromeContract.PanelMinimumHeight);}
        var split=(DockSplit)node;var first=NodeMinimum(split.First,vertical,containers);var second=NodeMinimum(split.Second,vertical,containers);
        return (split.Orientation=="horizontal")==vertical?first+second:Math.Max(first,second);
    }
    public static DockNode ReplaceNode(DockNode root, DockNode target, DockNode replacement)=>ReferenceEquals(root,target)?replacement:root is DockSplit split?split with{First=ReplaceNode(split.First,target,replacement),Second=ReplaceNode(split.Second,target,replacement)}:root;
}
