using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Registry-neutral promotion of the Layout reference store. Maintains its locked revision map,
/// detached snapshots and restore-as-draft lifecycle; adds stream fencing and exact replay, and stores
/// each publication's Definition Dependencies under the same lock that commits it.
/// This in-memory reference implementation does not claim restart durability.
/// </summary>
public sealed class InMemoryVersionedDefinitionStore : IVersionedDefinitionStore
{
    // Source: LayoutDefinitionStore.cs at e2f4bb6c (handoff/t620-layout-store-source).
    // Opaque immutable strings replace Layout's serialize/deserialize snapshot. Member admission
    // replaces LayoutDefinitionAdmission; no Layout type or grammar remains in the shared store.
    private readonly object _gate = new();
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Key listing has no admission of its own; every current caller (RuleDefinitionCatalog's
        // List/Load composition) is authoring-surface, so Author is the explicit stage here.
        ValidateNamespace(tenant, kind, DefinitionAdmissionPhase.Author);
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyList<DefinitionKey>>(_history.Keys
                .Where(key => key.Tenant == tenant && key.Kind == kind)
                .OrderBy(key => key.DefinitionId, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The kinds whose publish is checked at the commit by their dependency derivation, declared once. Admitting one without
    /// its derivation refuses construction (ADR 0071 decisions 1 and 4). Views refuses an incompatible shape role there
    /// (T-1177). Records joins under T-1198.
    /// </summary>
    private static readonly DefinitionKind[] DerivationRequired = [DefinitionKind.Views];

    private readonly IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> _admissions;
    private readonly IReadOnlyDictionary<DefinitionKind, DefinitionDependencyDerivation> _derivations;
    private readonly Dictionary<(DefinitionKey Key, string VersionId), DefinitionRevision> _revisions = [];
    private readonly Dictionary<DefinitionKey, List<DefinitionRevision>> _history = [];
    private readonly Dictionary<(DefinitionKey Key, string RequestId), Replay> _requests = [];
    private readonly Dictionary<(DefinitionKey Key, string VersionId), IReadOnlyList<DefinitionDependency>> _dependencies = [];

    /// <summary>Creates a store with explicitly admitted registry validators, copied from host configuration.</summary>
    // Kept as its own overload, not folded into an optional parameter, so binaries compiled against it still bind.
    public InMemoryVersionedDefinitionStore(IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions)
        : this(admissions, null)
    {
    }

    /// <summary>
    /// Creates a store with explicitly admitted registry validators, copied from host configuration, and the dependency
    /// derivations of the registries that store Definition Dependencies. Each derivation's registry must also be admitted.
    /// </summary>
    public InMemoryVersionedDefinitionStore(IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions,
        IReadOnlyDictionary<DefinitionKind, DefinitionDependencyDerivation>? dependencies)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        // Construction-time validation, before any operation exists to name a stage. Author is the
        // earliest lifecycle stage and matches host wiring, which always runs during authoring setup.
        if (admissions.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null))
            throw Refuse("definition.registry_unknown", "/registry", DefinitionAdmissionPhase.Author);
        if (dependencies?.Any(pair => !admissions.ContainsKey(pair.Key) || pair.Value is null) == true)
            throw Refuse("definition.registry_unknown", "/dependencies", DefinitionAdmissionPhase.Author);
        // ADR 0071 decisions 1 and 4: a kind whose publish is checked by its derivation cannot be admitted without it.
        if (admissions.Keys.Any(kind => DerivationRequired.Contains(kind) && dependencies?.ContainsKey(kind) != true))
            throw Refuse("definition.registry_unknown", "/dependencies", DefinitionAdmissionPhase.Author);
        _admissions = new Dictionary<DefinitionKind, DefinitionAdmission>(admissions);
        _derivations = new Dictionary<DefinitionKind, DefinitionDependencyDerivation>(
            dependencies ?? new Dictionary<DefinitionKind, DefinitionDependencyDerivation>());
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Apply(document.Key, expectedRevision, requestId, Signature("draft", document),
            DefinitionAdmissionPhase.Author, () =>
            {
                if (_revisions.TryGetValue((document.Key, document.VersionId), out var current))
                {
                    if (current.Status == DefinitionStatus.Published)
                        throw Refuse("definition.version_immutable", "/versionId", DefinitionAdmissionPhase.Author);
                    if (!StringComparer.Ordinal.Equals(current.Document.Version, document.Version))
                        throw Refuse("definition.version_conflict", "/version", DefinitionAdmissionPhase.Author);
                }
                return Snapshot(document, DefinitionStatus.Draft);
            }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        Require(versionId, "definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Publish);
        return Apply(key, expectedRevision, requestId, Signature("publish", versionId),
            DefinitionAdmissionPhase.Publish, () => Find(key, versionId, DefinitionAdmissionPhase.Publish), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, IReadOnlyList<DefinitionPublishedHeadCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        Require(versionId, "definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Publish);
        if (conditions.Count == 0) return PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        var snapshot = conditions.OrderBy(condition => condition.Key.Tenant, StringComparer.Ordinal)
            .ThenBy(condition => condition.Key.Kind).ThenBy(condition => condition.Key.DefinitionId, StringComparer.Ordinal)
            .ThenBy(condition => condition.Pointer, StringComparer.Ordinal).ThenBy(condition => condition.Revision).ToArray();
        foreach (var condition in snapshot)
        {
            ValidateKey(condition.Key, DefinitionAdmissionPhase.Publish);
            if (condition.Revision <= 0)
                throw Refuse("definition.published_head_conflict", condition.Pointer, DefinitionAdmissionPhase.Publish);
        }
        return Apply(key, expectedRevision, requestId, Signature("publish", new { versionId, conditions = snapshot }),
            DefinitionAdmissionPhase.Publish, () => Find(key, versionId, DefinitionAdmissionPhase.Publish),
            cancellationToken, snapshot);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision?> GetPublicationReplayAsync(DefinitionKey key, string versionId,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key, DefinitionAdmissionPhase.Publish);
        Require(versionId, "definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Publish);
        Require(requestId, "definition.request_id_required", "/requestId", DefinitionAdmissionPhase.Publish);
        if (expectedRevision < 0)
            throw Refuse("definition.revision_conflict", "/expectedRevision", DefinitionAdmissionPhase.Publish);
        lock (_gate)
        {
            if (!_requests.TryGetValue((key, requestId), out var replay))
                return ValueTask.FromResult<DefinitionRevision?>(null);
            if (replay.ExpectedRevision != expectedRevision
                || !StringComparer.Ordinal.Equals(replay.PublicationVersionId, versionId))
                throw Refuse("definition.replay_conflict", "/requestId", DefinitionAdmissionPhase.Publish);
            return ValueTask.FromResult<DefinitionRevision?>(replay.Result);
        }
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        Require(sourceVersionId, "definition.version_id_required", "/sourceVersionId", DefinitionAdmissionPhase.Author);
        return Apply(key, expectedRevision, requestId,
            Signature("restore", new { sourceVersionId, draftVersionId, draftVersion }),
            DefinitionAdmissionPhase.Author, () =>
            {
                var source = Find(key, sourceVersionId, DefinitionAdmissionPhase.Author);
                if (source.Status != DefinitionStatus.Published)
                    throw Refuse("definition.published_version_required", "/sourceVersionId", DefinitionAdmissionPhase.Author);
                if (_revisions.ContainsKey((key, draftVersionId)))
                    throw Refuse("definition.version_conflict", "/versionId", DefinitionAdmissionPhase.Author);
                return Snapshot(source.Document with { VersionId = draftVersionId, Version = draftVersion },
                    DefinitionStatus.Draft) with { RestoredFromVersionId = sourceVersionId };
            }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Plain history read, outside any admission. Every current caller composes authoring views
        // (RuleDefinitionCatalog Load/LoadVersion/Resolve), so Author is the explicit stage.
        ValidateKey(key, DefinitionAdmissionPhase.Author);
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyList<DefinitionRevision>>(
                _history.TryGetValue(key, out var history) ? history.ToArray() : []);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Same reasoning as ListHistoryAsync: a plain read whose current callers are all authoring
        // composition (LoadAsync's published-head fallback, ResolveAsync's Latest policy).
        ValidateKey(key, DefinitionAdmissionPhase.Author);
        lock (_gate)
            return ValueTask.FromResult(Head(key));
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(binding);
        // Unlike the other plain reads, this one binds an immutable *published* pin — the shape used
        // by release materialization and future install-time resolution — so Publish is the explicit
        // stage rather than Author.
        ValidateKey(binding.Key, DefinitionAdmissionPhase.Publish);
        Require(binding.VersionId, "definition.version_id_required", "/versionId", DefinitionAdmissionPhase.Publish);
        lock (_gate)
            return ValueTask.FromResult(_revisions.TryGetValue((binding.Key, binding.VersionId), out var revision)
                && revision.Status == DefinitionStatus.Published ? revision : null);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionDependencies> ReadDependenciesAsync(string tenant, string definitionType, string definitionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Require(tenant, "definition.tenant_required", "/tenant", DefinitionAdmissionPhase.Author);
        Require(definitionType, "definition.type_required", "/definitionType", DefinitionAdmissionPhase.Author);
        Require(definitionId, "definition.id_required", "/definitionId", DefinitionAdmissionPhase.Author);
        if (_derivations.Count == 0)
            throw Refuse("definition.dependencies_unsupported", "/dependencies", DefinitionAdmissionPhase.Author);
        lock (_gate)
        {
            // Only each definition's published head counts, so a later published version replaces the edges of an
            // earlier one, and an edge appears in Dependents exactly when its source still depends on the definition.
            var edges = _history.Keys
                .Where(key => key.Tenant == tenant)
                .OrderBy(key => key.DefinitionId, StringComparer.Ordinal).ThenBy(key => key.Kind)
                .SelectMany(key => Head(key) is { } head && _dependencies.TryGetValue((key, head.Document.VersionId), out var stored)
                    ? stored : [])
                .ToArray();
            bool Names(CatalogueDefinitionReference end) => end.DefinitionType == definitionType && end.DefinitionId == definitionId;
            return ValueTask.FromResult(new DefinitionDependencies(
                edges.Where(edge => Names(edge.FromDefinitionId)).ToArray(),
                edges.Where(edge => Names(edge.ToDefinitionId)).ToArray()));
        }
    }

    // The highest published semantic version of a key; the caller holds the gate.
    private DefinitionRevision? Head(DefinitionKey key) => _revisions.Values
        .Where(revision => revision.Document.Key == key && revision.Status == DefinitionStatus.Published)
        .OrderByDescending(revision => DefinitionSemanticVersion.Parse(revision.Document.Version))
        .FirstOrDefault();

    private ValueTask<DefinitionRevision> Apply(DefinitionKey key, long expectedRevision, string requestId,
        string signature, DefinitionAdmissionPhase phase, Func<DefinitionRevision> prepare,
        CancellationToken cancellationToken, IReadOnlyList<DefinitionPublishedHeadCondition>? conditions = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key, phase);
        Require(requestId, "definition.request_id_required", "/requestId", phase);
        if (expectedRevision < 0) throw Refuse("definition.revision_conflict", "/expectedRevision", phase);
        DefinitionRevision candidate;
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature, phase);
            if (replay is not null) return ValueTask.FromResult(replay);
            candidate = prepare();
        }

        // Member code runs outside the store lock. The second fence protects this snapshot
        // against concurrent edits and against a validator that re-enters a mutation method.
        Validate(candidate.Document, phase);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature, phase);
            if (replay is not null) return ValueTask.FromResult(replay);
            if (conditions is not null)
            {
                var changed = conditions.Where(condition => Head(condition.Key)?.Revision != condition.Revision)
                    .Select(condition => new DefinitionRefusal("definition.published_head_conflict", condition.Pointer)).ToArray();
                if (changed.Length > 0) throw new DefinitionRefusalException(phase, changed);
            }
            IReadOnlyList<DefinitionDependency>? dependencies = null;
            if (phase == DefinitionAdmissionPhase.Publish)
            {
                var version = DefinitionSemanticVersion.Parse(candidate.Document.Version);
                if (_revisions.Values.Any(item => item.Document.Key == key
                    && item.Status == DefinitionStatus.Published
                    && item.Document.VersionId != candidate.Document.VersionId
                    && DefinitionSemanticVersion.Parse(item.Document.Version).CompareTo(version) == 0))
                    throw Refuse("definition.version_conflict", "/version", phase);
                if (candidate.Status == DefinitionStatus.Published)
                {
                    _requests.Add((key, requestId), new(expectedRevision, signature, candidate, candidate.Document.VersionId));
                    return ValueTask.FromResult(candidate);
                }
                candidate = candidate with { Status = DefinitionStatus.Published };
                // The derivation sees exactly the heads this commit is fenced against and is handed no store, so it cannot
                // re-enter a mutation. A refusal or fault from it leaves nothing committed.
                if (_derivations.TryGetValue(key.Kind, out var derive))
                    dependencies = derive(candidate.Document, Head)?.ToArray()
                        ?? throw Refuse("definition.admission_invalid", "/dependencies", phase);
            }
            var appended = candidate with { Revision = checked(expectedRevision + 1) };
            if (!_history.TryGetValue(key, out var history)) _history[key] = history = [];
            history.Add(appended);
            _revisions[(key, appended.Document.VersionId)] = appended;
            if (dependencies is not null) _dependencies[(key, appended.Document.VersionId)] = dependencies;
            _requests.Add((key, requestId), new(expectedRevision, signature, appended,
                phase == DefinitionAdmissionPhase.Publish ? appended.Document.VersionId : null));
            return ValueTask.FromResult(appended);
        }
    }

    private DefinitionRevision? ReplayOrFence(DefinitionKey key, long expectedRevision,
        string requestId, string signature, DefinitionAdmissionPhase phase)
    {
        if (_requests.TryGetValue((key, requestId), out var replay))
        {
            if (replay.ExpectedRevision != expectedRevision || !StringComparer.Ordinal.Equals(replay.Signature, signature))
                throw Refuse("definition.replay_conflict", "/requestId", phase);
            return replay.Result;
        }
        long currentRevision = _history.TryGetValue(key, out var history) ? history[^1].Revision : 0;
        if (currentRevision != expectedRevision)
            throw Refuse("definition.revision_conflict", "/expectedRevision", phase);
        return null;
    }

    private void Validate(DefinitionDocument document, DefinitionAdmissionPhase phase)
    {
        ValidateKey(document.Key, phase);
        Require(document.VersionId, "definition.version_id_required", "/versionId", phase);
        if (!DefinitionSemanticVersion.TryParse(document.Version, out _))
            throw Refuse("definition.version_invalid", "/version", phase);
        try { using var parsed = JsonDocument.Parse(document.BodyJson); }
        catch (JsonException) { throw Refuse("definition.body_invalid", "/body", phase); }
        catch (ArgumentNullException) { throw Refuse("definition.body_invalid", "/body", phase); }
        var refusals = _admissions[document.Key.Kind](document, phase);
        if (refusals is null) throw Refuse("definition.admission_invalid", "/body", phase);
        if (refusals.Count > 0) throw new DefinitionRefusalException(phase, refusals);
    }

    private void ValidateKey(DefinitionKey key, DefinitionAdmissionPhase stage)
    {
        ArgumentNullException.ThrowIfNull(key);
        ValidateNamespace(key.Tenant, key.Kind, stage);
        Require(key.DefinitionId, "definition.id_required", "/definitionId", stage);
    }

    private void ValidateNamespace(string tenant, DefinitionKind kind, DefinitionAdmissionPhase stage)
    {
        if (!Enum.IsDefined(kind) || !_admissions.ContainsKey(kind))
            throw Refuse("definition.registry_unknown", "/registry", stage);
        Require(tenant, "definition.tenant_required", "/tenant", stage);
    }

    private DefinitionRevision Find(DefinitionKey key, string versionId, DefinitionAdmissionPhase stage)
        => _revisions.TryGetValue((key, versionId), out var source) ? source
            : throw Refuse("definition.not_found", "/versionId", stage);

    private static DefinitionRevision Snapshot(DefinitionDocument document, DefinitionStatus status)
        => new(document, 0, status, document.BodyJson is null ? "" :
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.BodyJson))));

    private static string Signature<T>(string operation, T payload)
        => JsonSerializer.Serialize(new { operation, payload });

    private static void Require(string? value, string code, string pointer, DefinitionAdmissionPhase stage)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Refuse(code, pointer, stage);
    }

    private static DefinitionRefusalException Refuse(string code, string pointer, DefinitionAdmissionPhase stage)
        => new(stage, [new(code, pointer)]);
    private sealed record Replay(long ExpectedRevision, string Signature, DefinitionRevision Result, string? PublicationVersionId);
}
