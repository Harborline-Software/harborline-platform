using Harborline.UIAdapters.Blazor.Components.DataDisplay;

var empty = ViewAuthoringDraft.Empty;
var catalogue = new ViewAuthoringCatalogue(
    [new("asset", "Asset")],
    [new(ViewKindIds.Table, "Table")],
    [new("name", "Name")],
    [new("asset.count", "Asset count")],
    [new("metric", "Metric")],
    [new("record.open", "Open record")]);

if (empty.Columns.Count != 0 || catalogue.ViewKinds.Single().Id != ViewKindIds.Table)
    throw new InvalidOperationException("packed Blazor authoring contract drifted");

Console.WriteLine($"VIEWS_BLAZOR_AUTHORING_PASS:{{\"editor\":\"{typeof(HarborlineViewAuthoringEditor).Name}\",\"authoredFields\":13}}");
