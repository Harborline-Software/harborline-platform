namespace Harborline.Blocks.EntityViews;

/// <summary>The three ambient-tenant ports consumed by entity views.</summary>
public sealed record EntityViewsTenantScope(
    IEntityReadStore Entities, IConditionHistoryStore History, ITypeCatalogStore Types,
    IConditionCaptureService ConditionCapture, ISubmissionLinkingService SubmissionLinking);

/// <summary>Creates isolated tenant scopes over shared seeds and tenant-local overlays.</summary>
public sealed class EntityViewsTenantScopes
{
    private readonly IReadOnlyList<EntityDetail> entitySeeds;
    private readonly IReadOnlyList<EdgeSummary> edgeSeeds;
    private readonly IReadOnlyList<ConditionHistory> conditionSeeds;
    private readonly IReadOnlyList<SubmissionList> submissionSeeds;
    private readonly IReadOnlyList<KeyValuePair<string, IReadOnlyList<BoundFormDescriptor>>> bindingSeeds;
    private readonly IReadOnlyList<string> knownTypes;
    private readonly TimeProvider clock;
    private readonly InMemoryTypeCatalogStore types;
    private readonly Func<string, ValueTask<string?>> caseResolver;
    private readonly Dictionary<string, EntityViewsTenantScope> scopes = new(StringComparer.Ordinal);
    private readonly object sync = new();

    /// <summary>Captures the shared seeds, type catalog, clock and case resolver that every tenant scope is built from; no tenant state is created until <see cref="ForTenant"/>.</summary>
    public EntityViewsTenantScopes(
        IEnumerable<EntityDetail> entities,
        IEnumerable<EdgeSummary>? edges,
        IEnumerable<ConditionHistory>? conditions,
        IEnumerable<SubmissionList>? submissions,
        IEnumerable<KeyValuePair<string, IReadOnlyList<BoundFormDescriptor>>>? bindings,
        IEnumerable<string>? knownTypes,
        InMemoryTypeCatalogStore types,
        TimeProvider clock,
        Func<string, ValueTask<string?>> caseResolver)
    {
        entitySeeds = entities.ToArray();
        edgeSeeds = edges?.ToArray() ?? [];
        conditionSeeds = conditions?.ToArray() ?? [];
        submissionSeeds = submissions?.ToArray() ?? [];
        bindingSeeds = bindings?.ToArray() ?? [];
        this.knownTypes = knownTypes?.ToArray() ?? entitySeeds.Select(entity => entity.Type).Distinct(StringComparer.Ordinal).ToArray();
        this.types = types;
        this.clock = clock;
        this.caseResolver = caseResolver;
    }

    /// <summary>Returns the tenant's scope, creating it on first use and returning the same instance afterwards; throws for a null or blank tenant id. Each tenant gets its own entity store and overlays, so captured conditions and linked submissions never cross tenants.</summary>
    public EntityViewsTenantScope ForTenant(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        lock (sync)
        {
            if (scopes.TryGetValue(tenantId, out var existing)) return existing;
            var waveA = new InMemoryEntityViewsStore(
                entitySeeds, edgeSeeds, conditionSeeds, submissionSeeds, bindingSeeds, clock, knownTypes);
            var scoped = new TenantArtifactStore(waveA);
            var result = new EntityViewsTenantScope(
                scoped, scoped, types.ForTenant(tenantId),
                new ConditionCapture(scoped, scoped), new SubmissionLinking(scoped, scoped, caseResolver));
            scopes.Add(tenantId, result);
            return result;
        }
    }

    private sealed class TenantArtifactStore(InMemoryEntityViewsStore inner) :
        IEntityReadStore, IConditionHistoryStore, IEntityViewsArtifactWriter
    {
        private readonly Dictionary<string, List<ConditionAssessment>> captured = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SubmissionSummary>> linked = new(StringComparer.Ordinal);

        /// <summary>Lists entities from the tenant's seeded store.</summary>
        public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => inner.ListEntitiesAsync(type);
        /// <summary>Returns the entity from the tenant's seeded store, or null when unknown.</summary>
        public ValueTask<EntityDetail?> GetEntityAsync(string id) => inner.GetEntityAsync(id);
        /// <summary>Returns the container's tree from the tenant's seeded store, or null when the container is unknown.</summary>
        public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => inner.GetTreeAsync(containerId, asOf);
        /// <summary>Creates an entity in the tenant's seeded store; failures are those of <see cref="InMemoryEntityViewsStore"/>.</summary>
        public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => inner.CreateEntityAsync(body);
        /// <summary>Adds an edge in the tenant's seeded store; failures are those of <see cref="InMemoryEntityViewsStore"/>.</summary>
        public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => inner.AddEdgeAsync(body);

        /// <summary>Returns the seeded history plus this tenant's captured assessments, ordered by observation time (ordinal); a non-null <paramref name="asOf"/> drops later assessments.</summary>
        public async ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf)
        {
            var seed = await inner.GetConditionHistoryAsync(entityId, asOf).ConfigureAwait(false);
            var overlay = captured.GetValueOrDefault(entityId) ?? [];
            var visible = asOf is null ? overlay : overlay.Where(item => string.CompareOrdinal(item.ObservedAt, asOf) <= 0).ToList();
            return new(entityId, seed.History.Concat(visible).OrderBy(item => item.ObservedAt, StringComparer.Ordinal).ToArray());
        }

        /// <summary>Returns the seeded submissions followed by the submissions linked in this tenant.</summary>
        public async ValueTask<SubmissionList> GetSubmissionsAsync(string entityId)
        {
            var seed = await inner.GetSubmissionsAsync(entityId).ConfigureAwait(false);
            return new(entityId, seed.Submissions.Concat(linked.GetValueOrDefault(entityId) ?? []).ToArray());
        }

        /// <summary>Stores the assessment in this tenant's overlay under its entity; the seeded history is untouched.</summary>
        public ValueTask AddConditionAsync(ConditionAssessment assessment)
        {
            if (!captured.TryGetValue(assessment.Entity, out var items)) captured.Add(assessment.Entity, items = []);
            items.Add(assessment);
            return ValueTask.CompletedTask;
        }

        /// <summary>Stores the submission in this tenant's overlay under the entity; the seeded list is untouched.</summary>
        public ValueTask AddSubmissionAsync(string entityId, SubmissionSummary submission)
        {
            if (!linked.TryGetValue(entityId, out var items)) linked.Add(entityId, items = []);
            items.Add(submission);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Narrow write port owned by the Wave B projection services.</summary>
public interface IEntityViewsArtifactWriter
{
    /// <summary>Persists a captured condition assessment for its entity.</summary>
    ValueTask AddConditionAsync(ConditionAssessment assessment);
    /// <summary>Persists a submission linked to the entity.</summary>
    ValueTask AddSubmissionAsync(string entityId, SubmissionSummary submission);
}
