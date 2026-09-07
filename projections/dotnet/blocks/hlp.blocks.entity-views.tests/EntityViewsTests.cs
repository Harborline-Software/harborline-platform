using Harborline.Blocks.EntityViews;

using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class EntityViewsTests
{
    private const string Building = "entity:preview/building-1";
    private const string Bedroom = "entity:preview/bedroom-1";
    private const string WaterHeater = "entity:preview/water-heater-1";
    private static readonly DateTimeOffset FixedInstant = DateTimeOffset.Parse("2026-07-16T00:00:00.000Z");

    [Fact(DisplayName = "lists every seeded entity")]
    public async Task ListsEverySeededEntity()
    {
        var entities = await Preview().ListEntitiesAsync(null);
        Assert.Contains(entities, entity => entity.Id == Building);
        Assert.Contains(entities, entity => entity.Id == WaterHeater);
        Assert.Equal(entities.OrderBy(entity => entity.DisplayName, StringComparer.Ordinal), entities);
    }

    [Fact(DisplayName = "narrows the list by type")]
    public async Task NarrowsTheListByType()
    {
        var entities = await Preview().ListEntitiesAsync("water-heater");
        Assert.Collection(entities, entity => Assert.Equal(WaterHeater, entity.Id));
    }

    [Fact(DisplayName = "returns an entity detail with its container + breadcrumb path")]
    public async Task ReturnsDetailWithContainerAndPath()
    {
        var detail = Assert.IsType<EntityDetail>(await Preview().GetEntityAsync(WaterHeater));
        Assert.Equal(Bedroom, detail.ContainerId);
        Assert.Equal([Building, Bedroom], detail.Path);
        Assert.Equal(new FormRef("water-heater.props", "1.0.0"), detail.PropertyForm);
    }

    [Fact(DisplayName = "returns null for an unknown entity")]
    public async Task ReturnsNullForUnknownEntity() => Assert.Null(await Preview().GetEntityAsync("entity:preview/does-not-exist"));

    [Fact(DisplayName = "the containment tree lists a container's direct children + breadcrumb")]
    public async Task TreeListsDirectChildrenAndBreadcrumb()
    {
        var tree = Assert.IsType<TreeView>(await Preview().GetTreeAsync(Bedroom, null));
        Assert.Equal([Building], tree.Path);
        Assert.Equal(["entity:preview/hvac-1", WaterHeater], tree.Children.Select(child => child.Id).Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "the condition history round-trips a seeded assessment with provenance")]
    public async Task ConditionRoundTripsProvenance()
    {
        var item = Assert.Single((await Preview().GetConditionHistoryAsync(WaterHeater, null)).History);
        Assert.Equal(4, item.Grade);
        Assert.Equal(5, item.ScaleMax);
        Assert.Equal("plumbing.inspection", item.SourceForm);
    }

    [Fact(DisplayName = "a newly created entity is contained by a `contains` edge")]
    public async Task CreatedEntityCanBeContained()
    {
        var store = Preview();
        var created = await store.CreateEntityAsync(new("water-heater", "Attic heater", null));
        Assert.Null(created.ContainerId);
        var edge = await store.AddEdgeAsync(new(EdgeKind.Contains, Bedroom, created.Id));
        Assert.Equal(EdgeKind.Contains, edge.Kind);
        Assert.Equal(Bedroom, (await store.GetEntityAsync(created.Id))?.ContainerId);
        Assert.Contains((await store.GetTreeAsync(Bedroom, null))!.Children, child => child.Id == created.Id);
    }

    [Fact(DisplayName = "lists all entities and encodes an optional type filter")]
    public async Task ListsAllEntitiesAndUsesTypedOptionalFilter() => await NarrowsTheListByType();

    [Fact(DisplayName = "gets entity detail and returns null only for a 404")]
    public async Task EntityDetailUses404OnlyNullInvariant()
    {
        Assert.NotNull(await Preview().GetEntityAsync(WaterHeater));
        Assert.Null(await Preview().GetEntityAsync("missing"));
        var exception = await Assert.ThrowsAsync<EntityViewsException>(async () => await new ThrowingStore().GetEntityAsync("failure"));
        Assert.Equal(EntityViewsCodes.StoreUnavailable, exception.Code);
    }

    [Fact(DisplayName = "creates an entity with authenticated JSON and surfaces a typed status message")]
    public async Task CreateSurfacesTypedStatus()
    {
        var store = Preview();
        var created = await store.CreateEntityAsync(new("boiler", "Main boiler", "boiler-1"));
        Assert.Equal(new CreateEntityBody("boiler", "Main boiler", "boiler-1"), new CreateEntityBody(created.Type, created.DisplayName, created.ScanKey));
        var exception = await Assert.ThrowsAsync<EntityViewsException>(async () => await store.CreateEntityAsync(new("unknown", "Thing", null)));
        Assert.Equal(EntityViewsCodes.UnknownType, exception.Code);
    }

    [Fact(DisplayName = "gets an as-of tree and returns null only for a 404")]
    public async Task TreeUsesAsOfAnd404OnlyNullInvariant()
    {
        const string asOf = "2026-01-02T03:04:05.000Z";
        Assert.Equal(asOf, (await Preview().GetTreeAsync(Building, asOf))?.AsOf);
        Assert.Null(await Preview().GetTreeAsync("missing", asOf));
        var exception = await Assert.ThrowsAsync<EntityViewsException>(async () => await new ThrowingStore().GetTreeAsync(Building, asOf));
        Assert.Equal(EntityViewsCodes.StoreUnavailable, exception.Code);
    }

    [Fact(DisplayName = "gets condition history and submissions from their entity subroutes")]
    public async Task GetsConditionAndSubmissions()
    {
        var store = Preview();
        Assert.Single((await store.GetConditionHistoryAsync(WaterHeater, null)).History);
        Assert.Single((await store.GetSubmissionsAsync(WaterHeater)).Submissions);
        Assert.Empty((await store.GetConditionHistoryAsync("missing", null)).History);
        Assert.Empty((await store.GetSubmissionsAsync("missing")).Submissions);
    }

    [Fact(DisplayName = "adds an authenticated JSON edge and surfaces a typed status message")]
    public async Task AddEdgeSurfacesTypedStatus()
    {
        var store = Preview();
        var created = await store.CreateEntityAsync(new("boiler", "Main boiler", "boiler-1"));
        var edge = await store.AddEdgeAsync(new(EdgeKind.Contains, Building, created.Id));
        Assert.Equal(new AddEdgeBody(EdgeKind.Contains, Building, created.Id), new AddEdgeBody(edge.Kind, edge.From, edge.To));
        var exception = await Assert.ThrowsAsync<EntityViewsException>(async () => await store.AddEdgeAsync(new(EdgeKind.Contains, Building, "missing")));
        Assert.Equal(EntityViewsCodes.UnknownEntity, exception.Code);
    }

    [Fact(DisplayName = "serves every read shape without a network call when no node is available")]
    public async Task PreviewServesEveryReadShape()
    {
        var store = Preview();
        Assert.NotEmpty(await store.ListEntitiesAsync(null));
        Assert.NotNull(await store.GetEntityAsync(WaterHeater));
        Assert.NotNull(await store.GetTreeAsync(Building, null));
        Assert.NotEmpty((await store.GetConditionHistoryAsync(WaterHeater, null)).History);
        Assert.NotEmpty((await store.GetSubmissionsAsync(WaterHeater)).Submissions);
    }

    [Fact(DisplayName = "serves preview writes without a network call and keeps the clock deterministic")]
    public async Task PreviewWritesUseDeterministicClock()
    {
        var store = Preview();
        var created = await store.CreateEntityAsync(new("boiler", "Preview boiler", null));
        var edge = await store.AddEdgeAsync(new(EdgeKind.Contains, Bedroom, created.Id));
        Assert.Equal("2026-07-16T00:00:00.000Z", created.CreatedAt);
        Assert.Equal(created.CreatedAt, edge.EffectiveFrom);
    }

    [Fact(DisplayName = "never renders a raw entity id in the breadcrumb, even for an unresolved ancestor")]
    public async Task NeverReturnsRawIdAsBreadcrumbLabel()
    {
        const string missing = "entity:vanished-ancestor";
        var labels = await new BreadcrumbResolver(Preview()).ResolveAsync([missing, Building]);
        Assert.Equal(BreadcrumbResolver.UnresolvedLabel, labels[0].Label);
        Assert.DoesNotContain(missing, labels.Select(label => label.Label));
        Assert.Equal("Harborline House", labels[1].Label);
    }

    [Fact(DisplayName = "lists the type's bound forms and filling one navigates with ?form=&into= + recordName state")]
    public async Task BoundFormsNavigateIntoRecord()
    {
        var forms = await Preview().GetBoundFormsAsync("water-heater");
        Assert.Contains(forms, form => form.Definition == "plumbing.inspection");
        Assert.Contains(forms, form => form.Definition == "water-heater.props");
        var target = new ViewNavigationTargets().FillForm("plumbing.inspection", "entity:acme/heater-1", "Heater A");
        Assert.Equal("/forms?form=plumbing.inspection&into=entity%3Aacme%2Fheater-1", target.Uri);
        Assert.Equal("Heater A", target.RecordName);
    }

    [Fact(DisplayName = "lists submitted forms and opening one navigates to the read-only bound view (?instance=)")]
    public async Task SubmittedFormsNavigateToInstance()
    {
        var submitted = Assert.Single(await Preview().GetSubmittedInstancesAsync(WaterHeater));
        Assert.Equal("/forms?form=plumbing.inspection&instance=forminst%3Apreview%2Fwh-inspection-1", new ViewNavigationTargets().ViewSubmission(submitted.FormId, submitted.InstanceId));
    }

    [Fact(DisplayName = "shows the empty state when no forms are bound to the record type")]
    public async Task NoBindingsReturnsExplicitEmptyResult() => Assert.Empty(await Preview().GetBoundFormsAsync("note"));

    [Fact]
    public void ProductionPreviewFailsClosed()
    {
        var exception = Assert.Throws<EntityViewsException>(() => new DevelopmentPreviewAdapter("Production", Clock()));
        Assert.Equal(EntityViewsCodes.ProductionPreviewForbidden, exception.Code);
    }

    private static DevelopmentPreviewAdapter Preview() => new("Development", Clock());
    private static TimeProvider Clock() => new FixedTimeProvider(FixedInstant);

    private sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    private sealed class ThrowingStore : IEntityReadStore
    {
        public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => throw Failure();
        public ValueTask<EntityDetail?> GetEntityAsync(string id) => throw Failure();
        public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => throw Failure();
        public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => throw Failure();
        public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => throw Failure();
        private static EntityViewsException Failure() => new(EntityViewsCodes.StoreUnavailable, "Store unavailable.");
    }
}
