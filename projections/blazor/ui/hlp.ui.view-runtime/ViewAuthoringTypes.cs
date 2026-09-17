namespace Harborline.UIAdapters.Blazor.Components.DataDisplay;

public sealed record ViewAuthoringOption(string Id, string Label);
public sealed record ViewAuthoringColumn(string Field, int Width, string Presentation);
public sealed record ViewAuthoringSort(string Field, string Direction);
public sealed record ViewAuthoringBinding(string Name, string Parameters);
public sealed record ViewAuthoringRowBehavior(string OpenAction, bool InlineEdit);

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
    public static ViewAuthoringDraft Empty { get; } = new(
        "", "", "", [], [], "", "", new Dictionary<string, string>
        {
            ["title"] = "", ["placedBy"] = "", ["groupedBy"] = "",
        }, new("", ""), new("", ""), new("", false), "standard", "public");
}

public sealed record ViewAuthoringCatalogue(
    IReadOnlyList<ViewAuthoringOption> RecordTypes,
    IReadOnlyList<ViewAuthoringOption> ViewKinds,
    IReadOnlyList<ViewAuthoringOption> Fields,
    IReadOnlyList<ViewAuthoringOption> Measures,
    IReadOnlyList<ViewAuthoringOption> Widgets,
    IReadOnlyList<ViewAuthoringOption> RowActions);
