using Harborline.Blocks.BuilderDefinitions;

namespace Harborline.Blocks.LayoutRuntime;

/// <summary>One selected medium's fragmentation context.</summary>
public sealed record LayoutFragmentainer(string Kind);

/// <summary>One immutable block placement in the shared render plan.</summary>
public sealed record LayoutFlowBlock(
    string BlockId,
    string Kind,
    int Depth,
    string? Zone = null,
    bool BreakBefore = false,
    bool BreakAfter = false,
    LayoutBreakInside BreakInside = LayoutBreakInside.Auto,
    string? StaticRegion = null);

/// <summary>One deterministic ordered page fragment selected from an authored run and master.</summary>
public sealed record LayoutPageFragment(
    int Number,
    string RunId,
    string PageLayoutId,
    string PageMasterId,
    string Variant,
    IReadOnlyList<LayoutFlowBlock> Flow,
    IReadOnlyList<LayoutFlowBlock> StaticRegions);

/// <summary>Renderer-neutral flow and static-region plan for one Layout surface.</summary>
public sealed record LayoutRenderPlan(
    LayoutFragmentainer Fragmentainer,
    IReadOnlyList<LayoutFlowBlock> Flow,
    IReadOnlyList<LayoutFlowBlock> StaticRegions,
    IReadOnlyList<LayoutPageFragment>? PageFragments = null);

/// <summary>
/// Flows an admitted Layout tree once. Renderers consume the result and never change tree order,
/// choose a different fragmentation model, or promote persisted state.
/// </summary>
public sealed class LayoutRuntimeEngine
{
    /// <summary>Creates one medium-specific plan while preserving authored tree order.</summary>
    public LayoutRenderPlan Flow(LayoutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var flow = new List<LayoutFlowBlock>();
        var staticRegions = new List<LayoutFlowBlock>();
        foreach (var block in definition.Blocks ?? []) Append(block, 0, flow, staticRegions);
        var pageFragments = definition.Medium == LayoutMedium.Page
            ? Fragment(definition, flow, staticRegions)
            : null;
        return new(
            new(definition.Medium == LayoutMedium.Screen ? "screen" : "page"),
            flow.AsReadOnly(),
            staticRegions.AsReadOnly(),
            pageFragments);
    }

    private static IReadOnlyList<LayoutPageFragment> Fragment(
        LayoutDefinition definition,
        IReadOnlyList<LayoutFlowBlock> allFlow,
        IReadOnlyList<LayoutFlowBlock> allStatic)
    {
        var layouts = (definition.PageLayouts ?? []).ToDictionary(value => value.Id, StringComparer.Ordinal);
        var masters = (definition.PageMasters ?? []).ToDictionary(value => value.Id, StringComparer.Ordinal);
        var runs = definition.PageRuns ?? [];
        if (runs.Count == 0) return [];
        var fragments = new List<LayoutPageFragment>();
        foreach (var run in runs)
        {
            if (!layouts.ContainsKey(run.PageLayoutId) || !masters.TryGetValue(run.PageMasterId, out var master)) continue;
            var selected = allFlow.Where(block => (run.BlockIds ?? []).Contains(block.BlockId, StringComparer.Ordinal)).ToArray();
            var pages = new List<List<LayoutFlowBlock>> { new() };
            foreach (var block in selected)
            {
                if (block.BreakBefore && pages[^1].Count > 0) pages.Add(new());
                pages[^1].Add(block);
                if (block.BreakAfter) pages.Add(new());
            }
            foreach (var page in pages.Where(page => page.Count > 0))
            {
                var number = fragments.Count + 1;
                var variant = number == 1 ? ("first", master.First) : number % 2 == 0 ? ("left", master.Left) : ("right", master.Right);
                var regions = new[] { variant.Item2.LeftRegion, variant.Item2.CenterRegion, variant.Item2.RightRegion }
                    .Where(region => !string.IsNullOrWhiteSpace(region))
                    .SelectMany(region => allStatic.Where(block => string.Equals(block.StaticRegion, region, StringComparison.Ordinal)))
                    .ToArray();
                fragments.Add(new(number, run.Id, run.PageLayoutId, run.PageMasterId, variant.Item1, page.AsReadOnly(), regions));
            }
        }
        return fragments.AsReadOnly();
    }

    private static void Append(
        LayoutBlock block,
        int depth,
        ICollection<LayoutFlowBlock> flow,
        ICollection<LayoutFlowBlock> staticRegions)
    {
        var placement = new LayoutFlowBlock(
            block.Id,
            block.Kind,
            depth,
            block.Placement?.Zone,
            block.BreakBefore,
            block.BreakAfter,
            block.BreakInside,
            block.StaticRegion);
        if (block.FlowRole == LayoutFlowRole.Static) staticRegions.Add(placement);
        else flow.Add(placement);
        foreach (var child in block.Children ?? []) Append(child, depth + 1, flow, staticRegions);
    }
}
