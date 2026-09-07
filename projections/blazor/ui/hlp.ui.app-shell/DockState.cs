using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

/// <summary>Serialisable dock node. Mirror of the React projection's DockNodeSnapshot wire shape.</summary>
public sealed record DockNodeSnapshot
{
    [property: JsonPropertyName("pane"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Pane { get; init; }
    [property: JsonPropertyName("fractions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<double>? Fractions { get; init; }
    [property: JsonPropertyName("orientation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Orientation { get; init; }
    [property: JsonPropertyName("ratio"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Ratio { get; init; }
    [property: JsonPropertyName("first"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DockNodeSnapshot? First { get; init; }
    [property: JsonPropertyName("second"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DockNodeSnapshot? Second { get; init; }
}

/// <summary>Serialisable per-workspace dock state. The HOST persists it; the shell never touches storage.</summary>
public sealed record DockStateSnapshot
{
    [property: JsonPropertyName("version")] public int Version { get; init; } = 1;
    [property: JsonPropertyName("openPanelIds")] public IReadOnlyList<string> OpenPanelIds { get; init; } = [];
    [property: JsonPropertyName("tree")] public DockNodeSnapshot? Tree { get; init; }
    [property: JsonPropertyName("widths")] public IReadOnlyDictionary<string, double> Widths { get; init; } = new Dictionary<string, double>();
}

public sealed record RestoredDockState(IReadOnlyList<string> OpenPanelIds, DockNode? Root, IReadOnlyDictionary<string, double> Widths);

public static class DockState
{
    public static DockStateSnapshot Serialize(IReadOnlyList<string> openPanelIds, DockNode? root, IReadOnlyDictionary<string, double> widths)
        => new() { Version = 1, OpenPanelIds = [.. openPanelIds], Tree = root is null ? null : Node(root), Widths = widths.ToDictionary(entry => entry.Key, entry => entry.Value) };

    private static DockNodeSnapshot Node(DockNode node) => node switch
    {
        DockPane pane => new DockNodeSnapshot { Pane = pane.Panels.Select(panel => panel.Id).ToArray(), Fractions = [.. pane.Fractions] },
        DockSplit split => new DockNodeSnapshot { Orientation = split.Orientation, Ratio = split.Ratio, First = Node(split.First), Second = Node(split.Second) },
        _ => throw new InvalidOperationException("unknown-dock-node")
    };

    /// <summary>Total: a malformed or stale snapshot yields null or a pruned state, never a throw.</summary>
    public static RestoredDockState? Read(JsonElement? value, IReadOnlyList<PackPanelDeclaration> panels)
    {
        if (value is not { ValueKind: JsonValueKind.Object } state) return null;
        if (!state.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var revision) || revision != 1) return null;
        var declared = panels.GroupBy(panel => panel.Id).ToDictionary(group => group.Key, group => group.First());
        var openPanelIds = state.TryGetProperty("openPanelIds", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String && declared.ContainsKey(id.GetString()!)).Select(id => id.GetString()!).ToArray()
            : [];
        var widths = new Dictionary<string, double>();
        if (state.TryGetProperty("widths", out var declaredWidths) && declaredWidths.ValueKind == JsonValueKind.Object)
            foreach (var entry in declaredWidths.EnumerateObject())
                if (declared.ContainsKey(entry.Name) && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetDouble(out var width) && double.IsFinite(width))
                    widths[entry.Name] = Math.Max(DockLayout.PaneMinimumWidth, Math.Round(width, MidpointRounding.AwayFromZero)); // half-up, like the React lane's Math.round: a persisted 640.5 restores as 641 in both
        var open = openPanelIds.Distinct().ToDictionary(id => id, id => declared[id]);
        return new(openPanelIds, state.TryGetProperty("tree", out var tree) ? ReadNode(tree, open) : null, widths);
    }

    private static double Fraction(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var part) && double.IsFinite(part) ? Math.Clamp(part, 0, 1) : .5;

    private static DockNode? ReadNode(JsonElement value, IReadOnlyDictionary<string, PackPanelDeclaration> known)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        if (value.TryGetProperty("pane", out var pane) && pane.ValueKind == JsonValueKind.Array)
        {
            var panels = pane.EnumerateArray().Where(id => id.ValueKind == JsonValueKind.String && known.ContainsKey(id.GetString()!)).Select(id => known[id.GetString()!]).ToArray();
            if (panels.Length == 0) return null;
            var declared = value.TryGetProperty("fractions", out var raw) && raw.ValueKind == JsonValueKind.Array ? raw.EnumerateArray().ToArray() : [];
            var fractions = panels.Select((_, index) => index < declared.Length ? Fraction(declared[index]) : .5).ToArray();
            var total = fractions.Sum();
            return new DockPane(panels, total > 0 ? fractions.Select(part => part / total).ToArray() : panels.Select(_ => 1d / panels.Length).ToArray());
        }
        var first = value.TryGetProperty("first", out var start) ? ReadNode(start, known) : null;
        var second = value.TryGetProperty("second", out var end) ? ReadNode(end, known) : null;
        if (first is null || second is null) return first ?? second;
        var orientation = value.TryGetProperty("orientation", out var axis) && axis.ValueKind == JsonValueKind.String && axis.GetString() == "vertical" ? "vertical" : "horizontal";
        return new DockSplit(orientation, value.TryGetProperty("ratio", out var ratio) ? Fraction(ratio) : .5, first, second);
    }
}
