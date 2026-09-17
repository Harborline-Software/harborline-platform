namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public sealed record ViewDefinitionField(string Id, string? Label = null);
public sealed record ViewRenderPlanParameters(IReadOnlyList<ViewDefinitionField>? Fields = null);
public sealed record ViewRuntimeAction(string Id, string Label);
public sealed record ViewRenderPlanBindings(string? ViewKind = null, ViewRenderPlanParameters? Parameters = null, IReadOnlyList<ViewRuntimeAction>? Actions = null);
public sealed record ViewRenderPlan(
    string DefinitionHash,
    string DefinitionId,
    string DefinitionVersion,
    string PackKey,
    string PackVersion,
    string DefinitionKind,
    ViewRenderPlanBindings Bindings);
public sealed record ViewRuntimeRow(string Id, IReadOnlyDictionary<string, object?> Values);
