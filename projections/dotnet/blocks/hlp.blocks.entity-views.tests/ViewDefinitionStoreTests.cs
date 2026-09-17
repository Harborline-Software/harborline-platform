using System.Text.Json;

using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewDefinitionStoreTests
{
    [Fact(DisplayName = "published heads use semver and restore creates only a new draft revision")]
    public async Task PublishedHeadsUseSemverAndRestoreCreatesOnlyANewDraftRevision()
    {
        var store = new InMemoryViewDefinitionStore();
        await store.CreateDraftAsync(Definition("1.0.0"), Binding());
        await store.PublishAsync("tenant-a", "work.queue", "1.0.0");
        await store.CreateDraftAsync(Definition("1.1.0"), Binding());

        var restored = await store.RestoreAsDraftAsync(
            "tenant-a",
            "work.queue",
            sourceVersion: "1.0.0",
            draftVersion: "1.0.1");

        Assert.Equal(ViewDefinitionStatus.Draft, restored.Status);
        Assert.Equal("1.0.0", restored.RestoredFromVersion);
        Assert.Equal("1.0.1", restored.Definition.Version);
        Assert.Equal("1.0.0", (await store.ResolvePublishedHeadAsync("tenant-a", "work.queue"))?.Version);
        Assert.Equal(
            ["1.0.0:Published", "1.0.1:Draft", "1.1.0:Draft"],
            (await store.ListHistoryAsync("tenant-a", "work.queue"))
                .Select(revision => $"{revision.Definition.Version}:{revision.Status}"));
        Assert.Equal("text", restored.Definition.Parameters.Columns[0].Presentation);
        Assert.Equal("layout.table", restored.Binding.Kind);
    }

    [Fact(DisplayName = "personal views never enter a pack export")]
    public void PersonalViewsNeverEnterAPackExport()
    {
        var publicView = new ViewDefinitionRevision(
            Definition("1.0.0"),
            Binding(),
            ViewDefinitionStatus.Published);
        var personalView = new ViewDefinitionRevision(
            Definition("1.0.0") with
            {
                Envelope = Definition("1.0.0").Envelope with { Identity = "work.mine" },
                Ownership = ViewOwnershipTier.Personal,
            },
            Binding(),
            ViewDefinitionStatus.Published);

        var export = ViewDefinitionPackExporter.Export([personalView, publicView]);

        Assert.Collection(export, item =>
        {
            Assert.Equal("work.queue", item.Definition.Key);
            Assert.Equal("title", item.Binding.ShapeRoles[ViewShapeRole.Title]);
        });
    }

    [Fact(DisplayName = "the store snapshots every mutable definition and binding collection")]
    public async Task StoreSnapshotsEveryMutableDefinitionAndBindingCollection()
    {
        var columns = new List<ViewColumn> { new("title", 240) };
        var sorts = new List<ViewSort> { new("title", ViewSortDirection.Ascending) };
        var requirements = new List<ViewDefinitionRequirement> { new("records.query", "1.0.0") };
        var measureParameters = new Dictionary<string, string> { ["format"] = "integer" };
        var nestedFilters = new List<ViewFilter> { ViewFilter.Equal("state", "open") };
        var shapeRoles = new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" };
        var widgetParameters = new Dictionary<string, string> { ["measure"] = "work.count" };
        using var provenance = JsonDocument.Parse("{\"source\":\"authoring\"}");
        var definition = Definition("1.0.0") with
        {
            Envelope = Definition("1.0.0").Envelope with
            {
                Provenance = provenance.RootElement,
                Requires = requirements,
            },
            Parameters = new(
                columns,
                sorts,
                new ViewAllFilter(nestedFilters),
                null,
                new("work.count", measureParameters)),
        };
        var binding = Binding() with
        {
            ShapeRoles = shapeRoles,
            Widget = new("helm.counter", widgetParameters),
        };
        var store = new InMemoryViewDefinitionStore();

        var stored = await store.CreateDraftAsync(definition, binding);
        columns[0] = new("mutated", 1);
        sorts.Clear();
        requirements.Clear();
        measureParameters["format"] = "mutated";
        nestedFilters.Clear();
        shapeRoles[ViewShapeRole.Title] = "mutated";
        widgetParameters["measure"] = "mutated";

        Assert.Equal("title", stored.Definition.Parameters.Columns[0].Field);
        Assert.Single(stored.Definition.Parameters.Sort);
        Assert.Single(stored.Definition.Envelope.Requires);
        Assert.Equal("integer", stored.Definition.Parameters.Measure!.Parameters["format"]);
        Assert.Single(Assert.IsType<ViewAllFilter>(stored.Definition.Parameters.Filter).Filters);
        Assert.Equal("title", stored.Binding.ShapeRoles[ViewShapeRole.Title]);
        Assert.Equal("work.count", stored.Binding.Widget!.Parameters["measure"]);
        Assert.Equal("authoring", stored.Definition.Envelope.Provenance.GetProperty("source").GetString());
    }

    private static ViewDefinition Definition(string version) => new(
        Envelope: new(
            Identity: "work.queue",
            Version: version,
            Tenant: "tenant-a",
            CascadeLayer: ViewCascadeLayer.Tenant,
            Provenance: JsonSerializer.SerializeToElement(new { source = "authoring" }),
            Requires: [new("records.query", "1.0.0")]),
        SchemaVersion: 1,
        Title: "Work queue",
        RecordType: "work-item",
        Ownership: ViewOwnershipTier.Public,
        OpenPermission: "work:read",
        Parameters: new(
            Columns: [new("title", 240)],
            Sort: [new("title", ViewSortDirection.Ascending)],
            Filter: null,
            GroupBy: null,
            Measure: null));

    private static ViewBinding Binding() => new(
        "layout.table",
        new Dictionary<ViewShapeRole, string> { [ViewShapeRole.Title] = "title" });
}
