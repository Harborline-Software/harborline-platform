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

/// <summary>
/// One authored binding: the kind (record_field, query, measure, template or static) and the
/// name it resolves by. An empty <paramref name="Name"/> is the `needs a binding` state — the
/// block is preserved and rebound one at a time rather than deleted (layout-auth-31).
/// </summary>
public sealed record LayoutAuthoringBinding(string Kind, string Name);

public sealed record LayoutAuthoringBlock(
    string Id,
    string Kind,
    LayoutAuthoringBinding? Binding = null,
    string? ParentId = null,
    string? Zone = null,
    string? Intent = null,
    string? Width = null,
    string? Height = null,
    string? AlignSelf = null,
    string? StaticRegion = null,
    string? WidgetId = null,
    bool BreakBefore = false,
    bool AvoidPageBreak = false,
    // layout-auth-18: the block repeats its children once per row of its collection binding.
    bool Repeating = false,
    // layout-auth-19: the declared Records relationship this block observes; only the key is stored.
    string? RelatedRelationship = null,
    // layout-auth-20: the block's guard, a Rules expression the shared engine evaluates fail-closed.
    string? ShowWhen = null,
    // The capture properties a capture block narrows with (layout-ck-30).
    LayoutAuthoringCapture? Capture = null,
    // layout-auth-33: the selection this block opens with. Authored; the live selection is never stored.
    string? DefaultSelection = null);

/// <summary>
/// What a capture block narrows (layout-ck-30): it may add a requirement and name registered
/// validation rules (layout-auth-21), never remove what Records declared.
/// </summary>
public sealed record LayoutAuthoringCapture(
    bool Required = false,
    IReadOnlyList<string>? ValidationRules = null,
    // layout-auth-22: the prompt this surface shows for the field, in its own context only.
    string? PromptOverride = null);
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
    IReadOnlyList<LayoutAuthoringOption>? HelmWidgets = null,
    // Bindables: the names offered per binding kind; `static` is authored on the block.
    IReadOnlyDictionary<string, IReadOnlyList<LayoutAuthoringOption>>? Bindables = null,
    // Relationships: the Records relationships declared on the surface's record type, by key (layout-auth-19).
    IReadOnlyList<LayoutAuthoringOption>? Relationships = null,
    // RequiredFields: the record fields Records declares required; a capture block cannot drop them (layout-auth-21).
    IReadOnlyList<string>? RequiredFields = null,
    // ValidationRules: the registered validation rules a capture block may name (layout-auth-21, layout-bound-8).
    IReadOnlyList<LayoutAuthoringOption>? ValidationRules = null);
