using Harborline.Foundation.RuleAuthoring;
using Harborline.UIAdapters.Blazor.Components.RuleAuthoring;

namespace Harborline.UIAdapters.Blazor.Components.Layout;

public sealed record LayoutRuntimeDiagnostic(string Code, string Pointer);
public sealed record LayoutRuntimeBlock(string Id, string Kind, int Depth, string? Zone = null);
/// <summary>DES-0052 C1, layout-eng-26: the authority the platform returned with the surface. The lane derives nothing from it.</summary>
public sealed record LayoutRuntimeAuthority(bool CanSubmit);
// Authority: absent, the surface is read-only (no authority, no submit).
/// <summary>DES-0056 execution-runtime-ck-4: one execution-trace step, verbatim.</summary>
public sealed record LayoutExecutionTraceStep(int Ordinal, string Phase, string Status);
/// <summary>DES-0056 execution-runtime-ck-3, as the host's authorized read returned it; a null <paramref name="AccessDecisionId"/> is no recorded decision.</summary>
public sealed record LayoutRunReceipt(string RunId, string Status, string? AccessDecisionId, IReadOnlyList<LayoutExecutionTraceStep> Trace);
/// <summary>One of Access's four stages, as stored.</summary>
public sealed record LayoutAccessTraceStage(int Ordinal, string Stage, IReadOnlyList<string> Facts);
/// <summary>
/// What following the Access decision link yielded, classified by the platform (LayoutExecutionObservation):
/// <c>absent</c>, <c>missing</c>, <c>forbidden</c>, <c>malformed</c> or <c>valid</c>. The lane derives nothing.
/// </summary>
public sealed record LayoutAccessTrace(string Evidence, int? Version, IReadOnlyList<LayoutAccessTraceStage> Stages, string? DecidingGrant);
public sealed record LayoutRuntimePlan(
    string DefinitionId,
    string DefinitionVersionId,
    string Medium,
    IReadOnlyList<LayoutRuntimeBlock> Flow,
    IReadOnlyList<LayoutRuntimeBlock> StaticRegions,
    IReadOnlyList<LayoutRuntimeDiagnostic>? Diagnostics = null,
    LayoutRuntimeAuthority? Authority = null);

/// <summary>
/// One authored binding: the kind (record_field, query, measure, template or static) and the
/// name it resolves by. An empty <paramref name="Name"/> is the `needs a binding` state — the
/// block is preserved and rebound one at a time rather than deleted (layout-auth-31).
/// </summary>
public sealed record LayoutAuthoringBinding(string Kind, string Name);

/// <summary>A Rules named predicate's exact pin (rules-ck-22): nothing floats.</summary>
public sealed record LayoutAuthoringPredicatePin(string Name, string Version, string Digest);

/// <summary>
/// layout-ck-29: a guard holds exactly one of a Rules expression, compiled at publish, or a named
/// predicate by exact pin, resolved from the definition's pinned closure. Both fail closed.
/// </summary>
public sealed record LayoutAuthoringShowWhen(string? Expression = null, LayoutAuthoringPredicatePin? Predicate = null);

/// <summary>A named predicate a show_when guard may cite (layout-ck-29).</summary>
public sealed record LayoutAuthoringPredicate(string Label, LayoutAuthoringPredicatePin Pin);

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
    // The flow this block arranges its children in; admission requires one on a repeating block or any parent (T-724 ruling 41).
    string? Container = null,
    // layout-auth-19: the declared Records relationship this block observes; only the key is stored.
    string? RelatedRelationship = null,
    // layout-ck-29, layout-auth-20: the block's guard, holding exactly one form; absent, the block always shows.
    LayoutAuthoringShowWhen? ShowWhen = null,
    // The guided expression ShowWhen was lowered from; absent when the guard was written as raw text (T-724 ruling 39).
    FormulaExpr? ShowWhenGuide = null,
    // The capture properties a capture block narrows with (layout-ck-30).
    LayoutAuthoringCapture? Capture = null,
    // layout-auth-33: the selection this block opens with. Authored; the live selection is never stored.
    string? DefaultSelection = null,
    // layout-auth-34: the ids of the other blocks on this surface this block's selection filters.
    IReadOnlyList<string>? FilterTargets = null);

/// <summary>
/// What a capture block narrows (layout-ck-30): it may add a requirement and name registered
/// validation rules (layout-auth-21), never remove what Records declared.
/// </summary>
public sealed record LayoutAuthoringCapture(
    bool Required = false,
    IReadOnlyList<string>? ValidationRules = null,
    // layout-auth-22: the prompt this surface shows for the field, in its own context only.
    string? PromptOverride = null,
    // layout-bound-3: the registered field control this capture field uses; absent is the runtime's choice.
    string? Control = null);
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
    IReadOnlyList<LayoutAuthoringPageRun>? PageRuns = null,
    // layout-auth-35: the released surfaces a reader may drill through to from this one.
    IReadOnlyList<string>? DrillThroughTargets = null)
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
    IReadOnlyList<LayoutAuthoringOption>? ValidationRules = null,
    // DrillTargets: the released surfaces a drill-through may name (layout-auth-35).
    IReadOnlyList<LayoutAuthoringOption>? DrillTargets = null,
    // FieldControls: the field controls the host registers for capture fields (layout-bound-3).
    IReadOnlyList<LayoutAuthoringOption>? FieldControls = null,
    // ValueDomainFields: the record fields whose value domain picks their editor; they take no authored control (layout-bound-10).
    IReadOnlyList<string>? ValueDomainFields = null,
    // GuardReferences: the references a show_when guard may read, offered by the guided expression editor (layout-auth-20).
    IReadOnlyList<RulesPaletteItem>? GuardReferences = null,
    // Predicates: the named predicates a show_when guard may cite, each by its exact pin (layout-ck-29).
    IReadOnlyList<LayoutAuthoringPredicate>? Predicates = null);
