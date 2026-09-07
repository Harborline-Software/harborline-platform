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

        public ValueTask<IReadOnlyList<EntitySummary>> ListEntitiesAsync(string? type) => inner.ListEntitiesAsync(type);
        public ValueTask<EntityDetail?> GetEntityAsync(string id) => inner.GetEntityAsync(id);
        public ValueTask<TreeView?> GetTreeAsync(string containerId, string? asOf) => inner.GetTreeAsync(containerId, asOf);
        public ValueTask<EntityDetail> CreateEntityAsync(CreateEntityBody body) => inner.CreateEntityAsync(body);
        public ValueTask<EdgeSummary> AddEdgeAsync(AddEdgeBody body) => inner.AddEdgeAsync(body);

        public async ValueTask<ConditionHistory> GetConditionHistoryAsync(string entityId, string? asOf)
        {
            var seed = await inner.GetConditionHistoryAsync(entityId, asOf).ConfigureAwait(false);
            var overlay = captured.GetValueOrDefault(entityId) ?? [];
            var visible = asOf is null ? overlay : overlay.Where(item => string.CompareOrdinal(item.ObservedAt, asOf) <= 0).ToList();
            return new(entityId, seed.History.Concat(visible).OrderBy(item => item.ObservedAt, StringComparer.Ordinal).ToArray());
        }

        public async ValueTask<SubmissionList> GetSubmissionsAsync(string entityId)
        {
            var seed = await inner.GetSubmissionsAsync(entityId).ConfigureAwait(false);
            return new(entityId, seed.Submissions.Concat(linked.GetValueOrDefault(entityId) ?? []).ToArray());
        }

        public ValueTask AddConditionAsync(ConditionAssessment assessment)
        {
            if (!captured.TryGetValue(assessment.Entity, out var items)) captured.Add(assessment.Entity, items = []);
            items.Add(assessment);
            return ValueTask.CompletedTask;
        }

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
    ValueTask AddConditionAsync(ConditionAssessment assessment);
    ValueTask AddSubmissionAsync(string entityId, SubmissionSummary submission);
}
