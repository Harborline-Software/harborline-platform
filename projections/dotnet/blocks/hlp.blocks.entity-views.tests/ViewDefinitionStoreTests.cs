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
        await store.CreateDraftAsync(Definition("1.0.0"));
        await store.PublishAsync("tenant-a", "work.queue", "1.0.0");
        await store.CreateDraftAsync(Definition("1.1.0"));

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
    }

    [Fact(DisplayName = "personal views never enter a pack export")]
    public void PersonalViewsNeverEnterAPackExport()
    {
        var publicView = new ViewDefinitionRevision(
            Definition("1.0.0"),
            ViewDefinitionStatus.Published);
        var personalView = new ViewDefinitionRevision(
            Definition("1.0.0") with
            {
                Envelope = Definition("1.0.0").Envelope with { Identity = "work.mine" },
                Ownership = ViewOwnershipTier.Personal,
            },
            ViewDefinitionStatus.Published);

        var export = ViewDefinitionPackExporter.Export([personalView, publicView]);

        Assert.Collection(export, item => Assert.Equal("work.queue", item.Key));
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
}
