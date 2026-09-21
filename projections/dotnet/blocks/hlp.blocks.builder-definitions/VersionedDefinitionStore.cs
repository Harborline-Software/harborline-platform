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
    private readonly IReadOnlyDictionary<DefinitionKind, RegisteredAdmission> _admissions;
    private readonly Dictionary<(DefinitionKey Key, string VersionId), DefinitionRevision> _revisions = [];
    private readonly Dictionary<DefinitionKey, List<DefinitionRevision>> _history = [];
    private readonly Dictionary<(DefinitionKey Key, string RequestId), Replay> _requests = [];

    /// <summary>Creates a store with explicitly admitted registry validators, copied from host configuration.</summary>
    public InMemoryVersionedDefinitionStore(IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions)
        : this(Register(admissions)) { }

    /// <summary>Creates a store whose registered validators require explicit principal context.</summary>
    public static InMemoryVersionedDefinitionStore CreateAsync(
        IReadOnlyDictionary<DefinitionKind, DefinitionAsyncAdmission> admissions)
        => new(Register(admissions));

    /// <summary>Creates a store with disjoint synchronous and asynchronous registry validators.</summary>
    public InMemoryVersionedDefinitionStore(
        IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions,
        IReadOnlyDictionary<DefinitionKind, DefinitionAsyncAdmission> asyncAdmissions)
        : this(Register(admissions, asyncAdmissions)) { }

    private InMemoryVersionedDefinitionStore(IReadOnlyDictionary<DefinitionKind, RegisteredAdmission> admissions)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        _admissions = new Dictionary<DefinitionKind, RegisteredAdmission>(admissions);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
        => SaveDraftCoreAsync(document, expectedRevision, requestId, null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionPrincipalContext principalContext,
        DefinitionDocument document, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
        => SaveDraftCoreAsync(document, expectedRevision, requestId, principalContext,
            cancellationToken);

    private ValueTask<DefinitionRevision> SaveDraftCoreAsync(DefinitionDocument document, long expectedRevision,
        string requestId, DefinitionPrincipalContext? principalContext, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Apply(document.Key, expectedRevision, requestId,
            OperationSignature("draft", document, principalContext),
            DefinitionAdmissionPhase.Author, () =>
            {
                DefinitionRevision? prior = null;
                if (_revisions.TryGetValue((document.Key, document.VersionId), out var current))
                {
                    if (current.Status == DefinitionStatus.Published)
                        throw Refuse("definition.version_immutable", "/versionId");
                    if (!StringComparer.Ordinal.Equals(current.Document.Version, document.Version))
                        throw Refuse("definition.version_conflict", "/version");
                    prior = Detach(current);
                }
                return new(Snapshot(document, DefinitionStatus.Draft), prior);
            }, principalContext, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
        => PublishCoreAsync(key, versionId, expectedRevision, requestId, null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionPrincipalContext principalContext,
        DefinitionKey key, string versionId, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
        => PublishCoreAsync(key, versionId, expectedRevision, requestId, principalContext, cancellationToken);

    private ValueTask<DefinitionRevision> PublishCoreAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, DefinitionPrincipalContext? principalContext, CancellationToken cancellationToken)
    {
        Require(versionId, "definition.version_id_required", "/versionId");
        return Apply(key, expectedRevision, requestId,
            OperationSignature("publish", versionId, principalContext),
            DefinitionAdmissionPhase.Publish, () =>
            {
                var current = Find(key, versionId);
                return new(Detach(current), Detach(current));
            }, principalContext, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
        => RestoreAsDraftCoreAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision,
            requestId, null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionPrincipalContext principalContext,
        DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
        => RestoreAsDraftCoreAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision,
            requestId, principalContext, cancellationToken);

    private ValueTask<DefinitionRevision> RestoreAsDraftCoreAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        DefinitionPrincipalContext? principalContext, CancellationToken cancellationToken)
    {
        Require(sourceVersionId, "definition.version_id_required", "/sourceVersionId");
        return Apply(key, expectedRevision, requestId,
            OperationSignature("restore", new { sourceVersionId, draftVersionId, draftVersion }, principalContext),
            DefinitionAdmissionPhase.Author, () =>
            {
                var source = Find(key, sourceVersionId);
                if (source.Status != DefinitionStatus.Published)
                    throw Refuse("definition.published_version_required", "/sourceVersionId");
                if (_revisions.ContainsKey((key, draftVersionId)))
                    throw Refuse("definition.version_conflict", "/versionId");
                var candidate = Snapshot(
                    source.Document with { VersionId = draftVersionId, Version = draftVersion },
                    DefinitionStatus.Draft) with { RestoredFromVersionId = sourceVersionId };
                return new(candidate, null);
            }, principalContext, cancellationToken);
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

    private async ValueTask<DefinitionRevision> Apply(DefinitionKey key, long expectedRevision, string requestId,
        string signature, DefinitionAdmissionPhase phase, Func<PreparedAdmission> prepare,
        DefinitionPrincipalContext? principalContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateKey(key);
        ValidatePrincipalContext(key, principalContext);
        Require(requestId, "definition.request_id_required", "/requestId");
        if (expectedRevision < 0) throw Refuse("definition.revision_conflict", "/expectedRevision");
        PreparedAdmission prepared;
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature);
            if (replay is not null) return replay;
            prepared = prepare();
        }

        // Member code runs outside the store lock. The second fence protects this snapshot
        // against concurrent edits and against a validator that re-enters a mutation method.
        await ValidateAsync(prepared.Candidate, phase, prepared.PriorSameVersionRevision,
            principalContext, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var replay = ReplayOrFence(key, expectedRevision, requestId, signature);
            if (replay is not null) return replay;
            var candidate = prepared.Candidate;
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
                    return candidate;
                }
                candidate = candidate with { Status = DefinitionStatus.Published };
            }
            var appended = candidate with { Revision = checked(expectedRevision + 1) };
            if (!_history.TryGetValue(key, out var history)) _history[key] = history = [];
            history.Add(appended);
            _revisions[(key, appended.Document.VersionId)] = appended;
            _requests.Add((key, requestId), new(expectedRevision, signature, appended));
            return appended;
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

    private async ValueTask ValidateAsync(DefinitionRevision candidate, DefinitionAdmissionPhase phase,
        DefinitionRevision? priorSameVersionRevision, DefinitionPrincipalContext? principalContext,
        CancellationToken cancellationToken)
    {
        var document = candidate.Document;
        ValidateKey(document.Key);
        Require(document.VersionId, "definition.version_id_required", "/versionId");
        if (!DefinitionSemanticVersion.TryParse(document.Version, out _))
            throw Refuse("definition.version_invalid", "/version");
        try { using var parsed = JsonDocument.Parse(document.BodyJson); }
        catch (JsonException) { throw Refuse("definition.body_invalid", "/body"); }
        catch (ArgumentNullException) { throw Refuse("definition.body_invalid", "/body"); }
        var context = new DefinitionAdmissionContext(candidate, phase, priorSameVersionRevision, principalContext);
        var refusals = await _admissions[document.Key.Kind].Callback(context, cancellationToken)
            .ConfigureAwait(false);
        if (refusals is null) throw Refuse("definition.admission_invalid", "/body");
        if (refusals.Count > 0) throw new DefinitionRefusalException(refusals);
    }

    private void ValidatePrincipalContext(DefinitionKey key, DefinitionPrincipalContext? principalContext)
    {
        if (principalContext is not null)
        {
            Require(principalContext.Principal, "definition.principal_required", "/principal");
            return;
        }
        if (_admissions[key.Kind].RequiresPrincipal)
            throw Refuse("definition.principal_required", "/principal");
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

    private static DefinitionRevision Detach(DefinitionRevision revision)
        => revision with { Document = revision.Document with { Key = revision.Document.Key with { } } };

    private static string OperationSignature<T>(string operation, T payload,
        DefinitionPrincipalContext? principalContext)
        => principalContext is null
            ? Signature(operation, payload)
            : Signature(operation, new { payload, principal = principalContext.Principal });

    private static string Signature<T>(string operation, T payload)
        => JsonSerializer.Serialize(new { operation, payload });

    private static IReadOnlyDictionary<DefinitionKind, RegisteredAdmission> Register(
        IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        if (admissions.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null))
            throw Refuse("definition.registry_unknown", "/registry");
        return admissions.ToDictionary(pair => pair.Key, pair =>
            new RegisteredAdmission((context, _) =>
                ValueTask.FromResult(pair.Value(context.Candidate.Document, context.Phase)), false));
    }

    private static IReadOnlyDictionary<DefinitionKind, RegisteredAdmission> Register(
        IReadOnlyDictionary<DefinitionKind, DefinitionAsyncAdmission> admissions)
    {
        ArgumentNullException.ThrowIfNull(admissions);
        if (admissions.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null))
            throw Refuse("definition.registry_unknown", "/registry");
        return admissions.ToDictionary(pair => pair.Key, pair => new RegisteredAdmission(pair.Value, true));
    }

    private static IReadOnlyDictionary<DefinitionKind, RegisteredAdmission> Register(
        IReadOnlyDictionary<DefinitionKind, DefinitionAdmission> admissions,
        IReadOnlyDictionary<DefinitionKind, DefinitionAsyncAdmission> asyncAdmissions)
    {
        var registered = new Dictionary<DefinitionKind, RegisteredAdmission>(Register(admissions));
        foreach (var pair in Register(asyncAdmissions))
        {
            if (!registered.TryAdd(pair.Key, pair.Value))
                throw new ArgumentException("A registry can have only one admission callback.",
                    nameof(asyncAdmissions));
        }
        return registered;
    }

    private static void Require(string? value, string code, string pointer)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Refuse(code, pointer);
    }

    private static DefinitionRefusalException Refuse(string code, string pointer) => new([new(code, pointer)]);
    private sealed record PreparedAdmission(
        DefinitionRevision Candidate,
        DefinitionRevision? PriorSameVersionRevision);
    private sealed record RegisteredAdmission(DefinitionAsyncAdmission Callback, bool RequiresPrincipal);
    private sealed record Replay(long ExpectedRevision, string Signature, DefinitionRevision Result);
}
