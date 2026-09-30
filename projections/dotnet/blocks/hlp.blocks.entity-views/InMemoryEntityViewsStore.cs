namespace Harborline.Blocks.EntityViews;

/// <summary>In-memory implementation of the entity read, condition-history and form-binding ports, seeded at construction and mutated only by create-entity and add-edge; not thread-safe.</summary>
public sealed class InMemoryEntityViewsStore : IEntityReadStore, IConditionHistoryStore, IFormBindingSource
{
    private readonly Dictionary<string, EntityDetail> entities;
    private readonly List<EdgeSummary> edges;
    private readonly Dictionary<string, ConditionHistory> conditions;
    private readonly Dictionary<string, SubmissionList> submissions;
    private readonly Dictionary<string, IReadOnlyList<BoundFormDescriptor>> bindings;
    private readonly HashSet<string> knownTypes;
    private readonly TimeProvider clock;
    private int nextEntity;
    private int nextEdge;

    /// <summary>Indexes the seed data by id; throws <see cref="EntityViewsException"/> with <see cref="EntityViewsCodes.UnknownEntity"/> when a seeded containment edge names an unknown endpoint. Known types default to those of the seeded entities.</summary>
    public InMemoryEntityViewsStore(
        IEnumerable<EntityDetail> entities,
        IEnumerable<EdgeSummary>? edges,
        IEnumerable<ConditionHistory>? conditions,
        IEnumerable<SubmissionList>? submissions,
        IEnumerable<KeyValuePair<string, IReadOnlyList<BoundFormDescriptor>>>? bindings,
        TimeProvider clock,
        IEnumerable<string>? knownTypes = null)
    {
        this.entities = entities.ToDictionary(entity => entity.Id);
        this.edges = edges?.ToList() ?? [];
        this.conditions = conditions?.ToDictionary(history => history.Entity) ?? [];
        this.submissions = submissions?.ToDictionary(list => list.Entity) ?? [];
        this.bindings = bindings?.ToDictionary(binding => binding.Key, binding => binding.Value) ?? [];
        this.knownTypes = new HashSet<string>(knownTypes ?? this.entities.Values.Select(entity => entity.Type), StringComparer.Ordinal);
        this.clock = clock;
        foreach (var edge in this.edges.Where(edge => edge.Kind == EdgeKind.Contains))
        {
            if (!this.entities.TryGetValue(edge.From, out var from) || !this.entities.TryGetValue(edge.To, out var to))
                throw new EntityViewsException(EntityViewsCodes.UnknownEntity, "A seeded containment edge endpoint is unknown.");
            this.entities[edge.To] = to with { ContainerId = edge.From, Path = [.. from.Path, edge.From] };
        }
    }

    /// <summary>Returns entity summaries, filtered to <paramref name="type"/> when it is not null, ordered by display name (ordinal).</summary>
    public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type)
    {
        IReadOnlyList<EntitySummary> result = entities.Values
            .Where(entity => type is null || entity.Type == type)
            .Select(ToSummary)
            .OrderBy(entity => entity.DisplayName, StringComparer.Ordinal)
            .ToArray();
        return ValueTask.FromResult(result);
    }

    /// <summary>Returns the entity, or null when the id is unknown.</summary>
    public ValueTask<EntityDetail?> GetEntityAsync(string id) => ValueTask.FromResult(entities.GetValueOrDefault(id));

    /// <summary>Returns the entities directly contained by the container plus the container's own path, or null when the container is unknown; <paramref name="asOf"/> defaults to the clock's current time and does not filter children.</summary>
    public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf)
    {
        if (!entities.TryGetValue(containerId, out var container)) return ValueTask.FromResult<TreeView?>(null);
        var children = entities.Values.Where(entity => entity.ContainerId == containerId).Select(ToSummary).ToArray();
        return ValueTask.FromResult<TreeView?>(new(containerId, asOf ?? Now(), container.Path, children));
    }

    /// <summary>Creates a top-level entity with a generated id; throws <see cref="EntityViewsCodes.UnknownType"/> for an unregistered type and <see cref="EntityViewsCodes.InvalidEntity"/> for a blank display name.</summary>
    public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body)
    {
        if (!knownTypes.Contains(body.Type)) throw new EntityViewsException(EntityViewsCodes.UnknownType, "Entity type is not known.");
        if (string.IsNullOrWhiteSpace(body.DisplayName)) throw new EntityViewsException(EntityViewsCodes.InvalidEntity, "Display name is required.");
        var id = $"entity:preview/generated-{++nextEntity}";
        var detail = new EntityDetail(id, body.Type, body.DisplayName, body.ScanKey, Now(), null, null, [], null);
        entities.Add(id, detail);
        return ValueTask.FromResult(detail);
    }

    /// <summary>Records an edge; throws <see cref="EntityViewsCodes.UnknownEntity"/> when either endpoint is unknown and <see cref="EntityViewsCodes.InvalidEdge"/> for a self-edge. A Contains edge also re-parents the target and extends its path.</summary>
    public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body)
    {
        if (!entities.TryGetValue(body.From, out var from) || !entities.TryGetValue(body.To, out var to))
            throw new EntityViewsException(EntityViewsCodes.UnknownEntity, "An edge endpoint is unknown.");
        if (body.From == body.To) throw new EntityViewsException(EntityViewsCodes.InvalidEdge, "An entity cannot relate to itself.");
        var edge = new EdgeSummary($"edge:preview/generated-{++nextEdge}", body.Kind, body.From, body.To, Now());
        edges.Add(edge);
        if (body.Kind == EdgeKind.Contains)
            entities[body.To] = to with { ContainerId = body.From, Path = [.. from.Path, body.From] };
        return ValueTask.FromResult(edge);
    }

    /// <summary>Returns the entity's assessments, or an empty history when it has none; a non-null <paramref name="asOf"/> keeps only assessments observed at or before it (ordinal string comparison of the ISO-8601 timestamps).</summary>
    public ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf)
    {
        var history = conditions.GetValueOrDefault(entityId) ?? new ConditionHistory(entityId, []);
        if (asOf is not null) history = history with { History = history.History.Where(item => string.CompareOrdinal(item.ObservedAt, asOf) <= 0).ToArray() };
        return ValueTask.FromResult(history);
    }

    /// <summary>Returns the entity's submissions, or an empty list when it has none.</summary>
    public ValueTask<SubmissionList> GetSubmissionsAsync(string entityId) =>
        ValueTask.FromResult(submissions.GetValueOrDefault(entityId) ?? new SubmissionList(entityId, []));

    /// <summary>Returns the forms bound to the record type, or an empty list when none are bound.</summary>
    public ValueTask<IReadOnlyList<BoundFormDescriptor>> GetBoundFormsAsync(string recordType) =>
        ValueTask.FromResult(bindings.GetValueOrDefault(recordType) ?? (IReadOnlyList<BoundFormDescriptor>)[]);

    /// <summary>Projects the entity's submissions into submitted-instance descriptors; empty when it has none.</summary>
    public async ValueTask<IReadOnlyList<SubmittedInstanceDescriptor>> GetSubmittedInstancesAsync(string entityId)
    {
        var list = await GetSubmissionsAsync(entityId).ConfigureAwait(false);
        return list.Submissions.Select(item => new SubmittedInstanceDescriptor(item.InstanceId, item.FormId, item.SubmittedAt, item.AssessorRef)).ToArray();
    }

    private string Now() => clock.GetUtcNow().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    private static EntitySummary ToSummary(EntityDetail entity) => new(entity.Id, entity.Type, entity.DisplayName, entity.ScanKey, entity.CreatedAt, entity.RetiredAt);
}
