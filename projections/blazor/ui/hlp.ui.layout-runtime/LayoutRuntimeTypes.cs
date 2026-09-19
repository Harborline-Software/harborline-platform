namespace Harborline.UIAdapters.Blazor.Components.Layout;

public sealed record LayoutRuntimeDiagnostic(string Code, string Pointer);
public sealed record LayoutRuntimeBlock(string Id, string Kind, int Depth, string? Zone = null);
public sealed record LayoutRuntimePlan(
    string DefinitionId,
    string DefinitionVersionId,
    string Medium,
    IReadOnlyList<LayoutRuntimeBlock> Flow,
    IReadOnlyList<LayoutRuntimeBlock> StaticRegions,
    IReadOnlyList<LayoutRuntimeDiagnostic>? Diagnostics = null);

public sealed record LayoutAuthoringBlock(
    string Id,
    string Kind,
    string? ParentId = null,
    string? Zone = null,
    string? Intent = null,
    string? Width = null,
    string? Height = null,
    string? AlignSelf = null,
    string? StaticRegion = null,
    string? WidgetId = null,
    bool BreakBefore = false,
    bool AvoidPageBreak = false);
public sealed record LayoutAuthoringPageRun(string Id, string PageLayoutId, string PageMasterId);
public sealed record LayoutAuthoringDraft(
    string Name,
    string Medium,
    string? CollapseBelow,
    IReadOnlyList<LayoutAuthoringBlock> Blocks,
    string? DefaultIntent = null,
    string? ContainerFlow = null,
    int? Gap = null,
    string? Density = null,
    IReadOnlyList<LayoutAuthoringPageRun>? PageRuns = null)
{
    public static LayoutAuthoringDraft Empty { get; } = new("", "screen", null, []);
}
public sealed record LayoutAuthoringOption(string Id, string Label);
public sealed record LayoutAuthoringCatalogue(
    IReadOnlyList<LayoutAuthoringOption> BlockKinds,
    IReadOnlyList<string> Zones,
    IReadOnlyList<LayoutAuthoringOption>? PageLayouts = null,
    IReadOnlyList<LayoutAuthoringOption>? PageMasters = null,
    IReadOnlyList<string>? StaticRegions = null,
    IReadOnlyList<LayoutAuthoringOption>? HelmWidgets = null);
