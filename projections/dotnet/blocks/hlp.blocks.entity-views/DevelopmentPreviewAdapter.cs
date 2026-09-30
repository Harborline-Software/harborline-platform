namespace Harborline.Blocks.EntityViews;

/// <summary>The deterministic development-only Harborline App preview tree.</summary>
public sealed class DevelopmentPreviewAdapter : IEntityReadStore, IConditionHistoryStore, IFormBindingSource
{
    private const string Building = "entity:preview/building-1";
    private const string Bedroom = "entity:preview/bedroom-1";
    private const string WaterHeater = "entity:preview/water-heater-1";
    private readonly InMemoryEntityViewsStore store;

    /// <summary>Seeds the fixed preview tree (building, bedroom, water heater, HVAC condenser) with the clock's current time; throws <see cref="EntityViewsException"/> with <see cref="EntityViewsCodes.ProductionPreviewForbidden"/> when the environment name is Production.</summary>
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

    /// <summary>Lists the seeded preview entities, optionally filtered to one type, ordered by display name.</summary>
    public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => store.ListEntitiesAsync(type);
    /// <summary>Returns the seeded preview entity, or null when the id is not in the preview tree.</summary>
    public ValueTask<EntityDetail?> GetEntityAsync(string id) => store.GetEntityAsync(id);
    /// <summary>Returns the direct children of a preview container, or null when the container is unknown; a missing <paramref name="asOf"/> defaults to the current time.</summary>
    public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => store.GetTreeAsync(containerId, asOf);
    /// <summary>Adds a generated preview entity; throws for an unknown type or a blank display name.</summary>
    public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => store.CreateEntityAsync(body);
    /// <summary>Adds a generated preview edge between two seeded entities; throws for an unknown endpoint or a self-edge.</summary>
    public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => store.AddEdgeAsync(body);
    /// <summary>Returns the seeded condition assessments for the entity, or an empty history when it has none.</summary>
    public ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf) => store.GetConditionHistoryAsync(entityId, asOf);
    /// <summary>Returns the seeded submissions for the entity, or an empty list when it has none.</summary>
    public ValueTask<SubmissionList> GetSubmissionsAsync(string entityId) => store.GetSubmissionsAsync(entityId);
    /// <summary>Returns the seeded forms bound to the record type, or an empty list when none are bound.</summary>
    public ValueTask<IReadOnlyList<BoundFormDescriptor>> GetBoundFormsAsync(string recordType) => store.GetBoundFormsAsync(recordType);
    /// <summary>Returns the seeded submissions of the entity projected as submitted-instance descriptors.</summary>
    public ValueTask<IReadOnlyList<SubmittedInstanceDescriptor>> GetSubmittedInstancesAsync(string entityId) => store.GetSubmittedInstancesAsync(entityId);
}
