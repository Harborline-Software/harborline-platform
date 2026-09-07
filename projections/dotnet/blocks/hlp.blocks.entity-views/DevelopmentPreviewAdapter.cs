namespace Harborline.Blocks.EntityViews;

/// <summary>The deterministic development-only Harborline App preview tree.</summary>
public sealed class DevelopmentPreviewAdapter : IEntityReadStore, IConditionHistoryStore, IFormBindingSource
{
    private const string Building = "entity:preview/building-1";
    private const string Bedroom = "entity:preview/bedroom-1";
    private const string WaterHeater = "entity:preview/water-heater-1";
    private readonly InMemoryEntityViewsStore store;

    public DevelopmentPreviewAdapter(string environmentName, TimeProvider clock)
    {
        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            throw new EntityViewsException(EntityViewsCodes.ProductionPreviewForbidden, "The development preview store cannot run in Production.");

        var now = clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
        EntityDetail[] entities =
        [
            new(Building, "building", "Harborline House", null, now, null, null, [], null),
            new(Bedroom, "bedroom", "Primary bedroom", null, now, null, Building, [Building], null),
            new(WaterHeater, "water-heater", "Water heater — closet", null, now, null, Bedroom, [Building, Bedroom], new FormRef("water-heater.props", "1.0.0")),
            new("entity:preview/hvac-1", "hvac-condenser", "HVAC condenser — north side", null, now, null, Bedroom, [Building, Bedroom], null),
        ];
        ConditionHistory[] conditions =
        [
            new(WaterHeater,
            [
                new("cond:preview/1", WaterHeater, 4, 5, null, 0.8, now, null, "plumbing.inspection", "condition", null),
            ]),
        ];
        SubmissionList[] submissions =
        [
            new(WaterHeater,
            [
                new("forminst:preview/wh-inspection-1", "plumbing.inspection", now, null),
            ]),
        ];
        KeyValuePair<string, IReadOnlyList<BoundFormDescriptor>>[] bindings =
        [
            new("water-heater",
            [
                new("plumbing.inspection", "1.0.0", "plumbing"),
                new("water-heater.props", "1.0.0", "Property form"),
            ]),
        ];
        store = new InMemoryEntityViewsStore(
            entities,
            null,
            conditions,
            submissions,
            bindings,
            clock,
            ["building", "bedroom", "water-heater", "hvac-condenser", "boiler"]);
    }

    public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => store.ListEntitiesAsync(type);
    public ValueTask<EntityDetail?> GetEntityAsync(string id) => store.GetEntityAsync(id);
    public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => store.GetTreeAsync(containerId, asOf);
    public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => store.CreateEntityAsync(body);
    public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => store.AddEdgeAsync(body);
    public ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf) => store.GetConditionHistoryAsync(entityId, asOf);
    public ValueTask<SubmissionList> GetSubmissionsAsync(string entityId) => store.GetSubmissionsAsync(entityId);
    public ValueTask<IReadOnlyList<BoundFormDescriptor>> GetBoundFormsAsync(string recordType) => store.GetBoundFormsAsync(recordType);
    public ValueTask<IReadOnlyList<SubmittedInstanceDescriptor>> GetSubmittedInstancesAsync(string entityId) => store.GetSubmittedInstancesAsync(entityId);
}
