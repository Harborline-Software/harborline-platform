using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>
/// Registry-neutral promotion of the Layout reference store. Maintains its locked revision map,
/// detached snapshots and restore-as-draft lifecycle; adds stream fencing and exact replay.
/// This in-memory reference implementation does not claim restart durability.
/// </summary>
public sealed class InMemoryVersionedDefinitionStore : IVersionedDefinitionStore
{
    // Source: LayoutDefinitionStore.cs at e2f4bb6c (handoff/t620-layout-store-source).
    // Opaque immutable strings replace Layout's serialize/deserialize snapshot. Member admission
    // replaces LayoutDefinitionAdmission; no Layout type or grammar remains in the shared store.
    private readonly object _gate = new();
    private readonly IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> _admissions;
    private readonly Dictionary<(DefinitionKey Key, string VersionId), DefinitionRevision> _revisions = [];
    private readonly Dictionary<DefinitionKey, List<DefinitionRevision>> _history = [];
    private readonly Dictionary<(DefinitionKey Key, string RequestId), Replay> _requests = [];

    /// <summary>Creates a store with explicitly admitted registry validators, copied from host configuration.</summary>
    public InMemoryVersionedDefinitionStore(IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        if (admissions.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null))
            throw Refuse("definition.registry_unknown", "/registry");
        _admissions = new Dictionary<DefinitionKind, DefinitionAdmission>(admissions);
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
                        throw Refuse("definition.version_immutable", "/versionId");
                    if (!StringComparer.Ordinal.Equals(current.Document.Version, document.Version))
                        throw Refuse("definition.version_conflict", "/version");
                }
                return Snapshot(document, DefinitionStatus.Draft);
            }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        Require(versionId, "definition.version_id_required", "/versionId");
        return Apply(key, expectedRevision, requestId, Signature("publish", versionId),
            DefinitionAdmissionPhase.Publish, () => Find(key, versionId), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        Require(sourceVersionId, "definition.version_id_required", "/sourceVersionId");
        return Apply(key, expectedRevision, requestId,
            Signature("restore", new { sourceVersionId, draftVersionId, draftVersion }),
            DefinitionAdmissionPhase.Author, () =>
            {
                var source = Find(key, sourceVersionId);
                if (source.Status != DefinitionStatus.Published)
                    throw Refuse("definition.published_version_required", "/sourceVersionId");
                if (_revisions.ContainsKey((key, draftVersionId)))
                    throw Refuse("definition.version_conflict", "/versionId");
                return Snapshot(source.Document with { VersionId = draftVersionId, Version = draftVersion },
                    DefinitionStatus.Draft) with { RestoredFromVersionId = sourceVersionId };
            }, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        lock (_gate)
            return ValueTask.FromResult<IReadOnlyList<DefinitionRevision>>(
                _history.TryGetValue(key, out var history) ? history.ToArray() : []);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        lock (_gate)
            return ValueTask.FromResult(_revisions.Values
                .Where(revision => revision.Document.Key == key && revision.Status == DefinitionStatus.Published)
                .OrderByDescending(revision => DefinitionSemanticVersion.Parse(revision.Document.Version))
                .FirstOrDefault());
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(binding);
        ValidateKey(binding.Key);
        Require(binding.VersionId, "definition.version_id_required", "/versionId");
        lock (_gate)
            return ValueTask.FromResult(_revisions.TryGetValue((binding.Key, binding.VersionId), out var revision)
                && revision.Status == DefinitionStatus.Published ? revision : null);
    }

    private ValueTask<DefinitionRevision> Apply(DefinitionKey key, long expectedRevision, string requestId,
        string signature, DefinitionAdmissionPhase phase, Func<DefinitionRevision> prepare,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        Require(requestId, "definition.request_id_required", "/requestId");
        if (expectedRevision < 0) throw Refuse("definition.revision_conflict", "/expectedRevision");
        DefinitionRevision candidate;
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature);
            if (replay is not null) return ValueTask.FromResult(replay);
            candidate = prepare();
        }

        // Member code runs outside the store lock. The second fence protects this snapshot
        // against concurrent edits and against a validator that re-enters a mutation method.
        Validate(candidate.Document, phase);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature);
            if (replay is not null) return ValueTask.FromResult(replay);
            if (phase == DefinitionAdmissionPhase.Publish)
            {
                var version = DefinitionSemanticVersion.Parse(candidate.Document.Version);
                if (_revisions.Values.Any(item => item.Document.Key == key
                    && item.Status == DefinitionStatus.Published
                    && item.Document.VersionId != candidate.Document.VersionId
                    && DefinitionSemanticVersion.Parse(item.Document.Version).CompareTo(version) == 0))
                    throw Refuse("definition.version_conflict", "/version");
                if (candidate.Status == DefinitionStatus.Published)
                {
                    _requests.Add((key, requestId), new(expectedRevision, signature, candidate));
                    return ValueTask.FromResult(candidate);
                }
                candidate = candidate with { Status = DefinitionStatus.Published };
            }
            var appended = candidate with { Revision = checked(expectedRevision + 1) };
            if (!_history.TryGetValue(key, out var history)) _history[key] = history = [];
            history.Add(appended);
            _revisions[(key, appended.Document.VersionId)] = appended;
            _requests.Add((key, requestId), new(expectedRevision, signature, appended));
            return ValueTask.FromResult(appended);
        }
    }

    private DefinitionRevision? ReplayOrFence(DefinitionKey key, long expectedRevision,
        string requestId, string signature)
    {
        if (_requests.TryGetValue((key, requestId), out var replay))
        {
            if (replay.ExpectedRevision != expectedRevision || !StringComparer.Ordinal.Equals(replay.Signature, signature))
                throw Refuse("definition.replay_conflict", "/requestId");
            return replay.Result;
        }
        long currentRevision = _history.TryGetValue(key, out var history) ? history[^1].Revision : 0;
        if (currentRevision != expectedRevision)
            throw Refuse("definition.revision_conflict", "/expectedRevision");
        return null;
    }

    private void Validate(DefinitionDocument document, DefinitionAdmissionPhase phase)
    {
        ValidateKey(document.Key);
        Require(document.VersionId, "definition.version_id_required", "/versionId");
        if (!DefinitionSemanticVersion.TryParse(document.Version, out _))
            throw Refuse("definition.version_invalid", "/version");
        try { using var parsed = JsonDocument.Parse(document.BodyJson); }
        catch (JsonException) { throw Refuse("definition.body_invalid", "/body"); }
        catch (ArgumentNullException) { throw Refuse("definition.body_invalid", "/body"); }
        var refusals = _admissions[document.Key.Kind](document, phase);
        if (refusals is null) throw Refuse("definition.admission_invalid", "/body");
        if (refusals.Count > 0) throw new DefinitionRefusalException(refusals);
    }

    private void ValidateKey(DefinitionKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enum.IsDefined(key.Kind) || !_admissions.ContainsKey(key.Kind))
            throw Refuse("definition.registry_unknown", "/registry");
        Require(key.Tenant, "definition.tenant_required", "/tenant");
        Require(key.DefinitionId, "definition.id_required", "/definitionId");
    }

    private DefinitionRevision Find(DefinitionKey key, string versionId)
        => _revisions.TryGetValue((key, versionId), out var source) ? source
            : throw Refuse("definition.not_found", "/versionId");

    private static DefinitionRevision Snapshot(DefinitionDocument document, DefinitionStatus status)
        => new(document, 0, status, document.BodyJson is null ? "" :
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document.BodyJson))));

    private static string Signature<T>(string operation, T payload)
        => JsonSerializer.Serialize(new { operation, payload });

    private static void Require(string? value, string code, string pointer)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Refuse(code, pointer);
    }

    private static DefinitionRefusalException Refuse(string code, string pointer) => new([new(code, pointer)]);
    private sealed record Replay(long ExpectedRevision, string Signature, DefinitionRevision Result);
}
