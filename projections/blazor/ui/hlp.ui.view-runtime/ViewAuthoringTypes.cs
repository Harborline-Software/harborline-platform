namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

/// <summary>A selectable option in the view authoring form: id and label.</summary>
public sealed record ViewAuthoringOption(string Id, string Label);
/// <summary>A column in the view being authored: field, width and presentation.</summary>
public sealed record ViewAuthoringColumn(string Field, int Width, string Presentation);
/// <summary>A sort on a view: the field and the direction.</summary>
public sealed record ViewAuthoringSort(string Field, string Direction);
/// <summary>A named binding, such as a measure or widget, with its parameters.</summary>
public sealed record ViewAuthoringBinding(string Name, string Parameters);
/// <summary>What a row does in the view: its open action and whether it can be edited inline.</summary>
public sealed record ViewAuthoringRowBehavior(string OpenAction, bool InlineEdit);

/// <summary>The draft of a view being authored: record type, kind, columns, sorts, grouping, filter, bindings, density and ownership.</summary>
public sealed record ViewAuthoringDraft(
    string Name,
    string RecordType,
    string ViewKind,
    IReadOnlyList<ViewAuthoringColumn> Columns,
    IReadOnlyList<ViewAuthoringSort> Sorts,
    string GroupBy,
    string FilterPredicate,
    IReadOnlyDictionary<string, string> ShapeRoles,
    ViewAuthoringBinding Measure,
    ViewAuthoringBinding Widget,
    ViewAuthoringRowBehavior RowBehavior,
    string Density,
    string Ownership)
{
/// <summary>An empty draft with the standard density and public ownership.</summary>
    public static ViewAuthoringDraft Empty { get; } = new(
        "", "", "", [], [], "", "", new Dictionary<string, string>
        {
            ["title"] = "", ["placedBy"] = "", ["groupedBy"] = "",
        }, new("", ""), new("", ""), new("", false), "standard", "public");
}

/// <summary>The options the view authoring form offers: record types, view kinds, fields, measures, widgets and row actions.</summary>
public sealed record ViewAuthoringCatalogue(
    IReadOnlyList<ViewAuthoringOption> RecordTypes,
    IReadOnlyList<ViewAuthoringOption> ViewKinds,
    IReadOnlyList<ViewAuthoringOption> Fields,
    IReadOnlyList<ViewAuthoringOption> Measures,
    IReadOnlyList<ViewAuthoringOption> Widgets,
    IReadOnlyList<ViewAuthoringOption> RowActions);
