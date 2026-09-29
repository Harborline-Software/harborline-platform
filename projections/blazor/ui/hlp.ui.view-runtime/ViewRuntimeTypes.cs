namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

/// <summary>The ids of the built-in view kinds.</summary>
public static class ViewKindIds
{
/// <summary>Id of the table view kind.</summary>
    public const string Table = "layout.table";
}

/// <summary>A field a view shows: its id and optional label.</summary>
public sealed record ViewDefinitionField(string Id, string? Label = null);
/// <summary>The parameters of a render plan: the fields the view shows.</summary>
public sealed record ViewRenderPlanParameters(IReadOnlyList<ViewDefinitionField>? Fields = null);
/// <summary>An action a view offers on its rows: id and label.</summary>
public sealed record ViewRuntimeAction(string Id, string Label);
/// <summary>What a render plan binds to: the view kind, its parameters and the row actions.</summary>
public sealed record ViewRenderPlanBindings(string? ViewKind = null, ViewRenderPlanParameters? Parameters = null, IReadOnlyList<ViewRuntimeAction>? Actions = null);
/// <summary>A view render plan: definition hash, id and version, pack, definition kind and bindings.</summary>
public sealed record ViewRenderPlan(
    string DefinitionHash,
    string DefinitionId,
    string DefinitionVersion,
    string PackKey,
    string PackVersion,
    string DefinitionKind,
    ViewRenderPlanBindings Bindings);
/// <summary>One row of view data: its id and values by field.</summary>
public sealed record ViewRuntimeRow(string Id, IReadOnlyDictionary<string, object?> Values);
