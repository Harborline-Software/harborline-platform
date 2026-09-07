namespace Harborline.Foundation.Builder;

/// <summary>Host-localized text consumed by Layers Rail renderers.</summary>
public sealed record RailLabels(
    string LensesHeading,
    string OutlineHeading,
    string Insert,
    Func<string, string> ToggleLens,
    Func<string, string> ActivateLens,
    Func<int, string> PassiveCount,
    Func<string, string> Source,
    string Locked,
    string UnresolvedSource,
    string Empty,
    string CollapseNode,
    string ExpandNode,
    string RailRegion,
    Func<string, string> ViewingLens,
    string ExitLens,
    Func<int, string> LensShortcutHint);

/// <summary>Exact pinned English development defaults. Production hosts may supply a complete localized set.</summary>
public static class DefaultRailLabels
{
    /// <summary>The immutable English fallback label set.</summary>
    public static RailLabels Value { get; } = new(
        "Lenses", "Outline", "Insert",
        lens => $"Toggle {lens} lens",
        lens => $"Show {lens} lens on the canvas",
        count => $"+{count}",
        tier => tier,
        "Locked — inherited from a higher tier",
        "source: unknown",
        "Add your first field — press ⌘K or the + above.",
        "Collapse", "Expand", "Layers",
        lens => $"Viewing: {lens}",
        "Exit lens (back to Layout)",
        number => $"press {number}");
}
