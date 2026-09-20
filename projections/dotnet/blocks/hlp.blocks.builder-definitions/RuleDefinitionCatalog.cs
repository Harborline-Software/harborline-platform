using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Registry;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A typed view of one shared definition revision.</summary>
public sealed record RuleDefinitionSnapshot(DefinitionRevision Revision, RuleDefinitionDocument Source);

/// <summary>The result of resolving a Rules policy against shared history.</summary>
public sealed record RuleDefinitionResolution(RuleResolutionStatus Status, RuleDefinitionSnapshot? Snapshot);

/// <summary>Composes Rules admission with the shared definition and archive stores.</summary>
public sealed class RuleDefinitionCatalog
{
    private readonly IVersionedDefinitionStore _store;
    private readonly IDefinitionLifecycleStore _lifecycle;

    /// <summary>Composes shared history and lifecycle state.</summary>
    public RuleDefinitionCatalog(IVersionedDefinitionStore store, IDefinitionLifecycleStore lifecycle)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(lifecycle);
        _store = store;
        _lifecycle = lifecycle;
    }

    /// <summary>Creates a new stream at revision zero using caller-supplied version identity.</summary>
    public ValueTask<DefinitionRevision> CreateJsonAsync(string json, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevision != 0) throw Refuse("definition.revision_conflict", "/expectedRevision");
        return SaveDraftJsonAsync(json, versionId, 0, requestId, cancellationToken);
    }

    /// <summary>Loads the latest open draft, or the shared published head when no draft remains.</summary>
    public async ValueTask<RuleDefinitionSnapshot?> LoadAsync(DefinitionKey key,
        CancellationToken cancellationToken = default)
    {
        RequireRules(key, cancellationToken);
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var current = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last())
            .Where(item => item.Status == DefinitionStatus.Draft)
            .OrderByDescending(item => item.Revision).FirstOrDefault()
            ?? await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false);
        return current is null ? null : Decode(current);
    }

    /// <summary>Lists detached typed source in exact tenant scope, ordered by name and identity.</summary>
    public async ValueTask<IReadOnlyList<RuleDefinitionSnapshot>> ListAsync(string tenant, bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var keys = await _store.ListKeysAsync(tenant, DefinitionKind.Rules, cancellationToken).ConfigureAwait(false);
        var rows = new List<RuleDefinitionSnapshot>();
        foreach (var key in keys)
        {
            if (!includeArchived && await _lifecycle.IsArchivedAsync(new(key.Tenant, key.Kind, key.DefinitionId),
                cancellationToken).ConfigureAwait(false)) continue;
            var snapshot = await LoadAsync(key, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null) rows.Add(snapshot);
        }
        return rows.OrderBy(row => row.Source.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Source.Envelope.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Copies exact source into a new identity and history with caller-supplied version metadata.</summary>
    public async ValueTask<DefinitionRevision> DuplicateAsync(DefinitionKey key, string sourceVersionId,
        string newId, string name, string versionId, string version, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        var original = await LoadVersionAsync(key, sourceVersionId, cancellationToken).ConfigureAwait(false)
            ?? throw Refuse("definition.not_found", "/sourceVersionId");
        var source = original.Source with
        {
            Name = name,
            Envelope = original.Source.Envelope with { Id = newId, Version = version },
        };
        return await CreateJsonAsync(RuleDefinitionCodec.SerializeCanonical(source), versionId,
            expectedRevision, requestId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Changes list visibility through the shared lifecycle without revoking published pins.</summary>
    public async ValueTask ArchiveAsync(DefinitionKey key, CancellationToken cancellationToken = default)
    {
        if (await LoadAsync(key, cancellationToken).ConfigureAwait(false) is null)
            throw Refuse("definition.not_found", "/definitionId");
        await _lifecycle.ArchiveAsync(new(key.Tenant, key.Kind, key.DefinitionId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Admits and saves authored Rules JSON as a shared draft.</summary>
    public ValueTask<DefinitionRevision> SaveDraftJsonAsync(string json, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = RequireSource(RuleIntentValidator.ValidateJson(json, RuleIntentPhase.Author));
        var envelope = source.Envelope;
        var document = new DefinitionDocument(new(envelope.Tenant, DefinitionKind.Rules, envelope.Id),
            versionId, envelope.Version, RuleDefinitionCodec.SerializeBody(source));
        return _store.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);
    }

    /// <summary>Publishes a fenced shared revision.</summary>
    public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default)
    {
        RequireRules(key, cancellationToken);
        return _store.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
    }

    /// <summary>Decodes an exact version from shared history.</summary>
    public async ValueTask<RuleDefinitionSnapshot?> LoadVersionAsync(DefinitionKey key, string versionId,
        CancellationToken cancellationToken = default)
    {
        RequireRules(key, cancellationToken);
        if (string.IsNullOrWhiteSpace(versionId)) throw Refuse("definition.version_id_required", "/versionId");
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);
        var revision = history.LastOrDefault(item => item.Document.VersionId == versionId);
        return revision is null ? null : Decode(revision);
    }

    /// <summary>Restores published body bytes under new shared metadata.</summary>
    public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default)
    {
        RequireRules(key, cancellationToken);
        return _store.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion,
            expectedRevision, requestId, cancellationToken);
    }

    /// <summary>Resolves a caller policy against shared history, independently of list visibility.</summary>
    public async ValueTask<RuleDefinitionResolution> ResolveAsync(DefinitionKey key, RuleVersionPolicy policy,
        RuleResolveScope scope, CancellationToken cancellationToken = default)
    {
        RequireRules(key, cancellationToken);
        RequirePolicy(policy);
        if (!Enum.IsDefined(scope)) throw Refuse(RuleDefinitionCodes.InvalidDocument, "/scope");
        var history = await _store.ListHistoryAsync(key, cancellationToken).ConfigureAwait(false);

        DefinitionRevision? selected;
        switch (policy.Kind)
        {
            case RuleVersionPolicyKind.Latest:
                selected = await _store.GetPublishedHeadAsync(key, cancellationToken).ConfigureAwait(false);
                break;
            case RuleVersionPolicyKind.Pinned:
                // Bind an exact admitted label to its immutable shared version identity. Prefer
                // publication if another draft has the same label; drafts cannot shadow a pin.
                var versions = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last());
                selected = versions.Where(item => item.Document.Version == policy.Version)
                    .OrderByDescending(item => item.Status == DefinitionStatus.Published)
                    .ThenByDescending(item => item.Revision).FirstOrDefault();
                if (selected?.Status == DefinitionStatus.Published)
                    selected = await _store.ResolvePublishedAsync(new(key, selected.Document.VersionId), cancellationToken)
                        .ConfigureAwait(false);
                break;
            case RuleVersionPolicyKind.Draft:
                if (scope == RuleResolveScope.Production) return new(RuleResolutionStatus.DraftRefused, null);
                selected = history.GroupBy(item => item.Document.VersionId).Select(group => group.Last())
                    .Where(item => item.Status == DefinitionStatus.Draft)
                    .OrderByDescending(item => item.Revision).FirstOrDefault();
                break;
            default: throw Refuse(RuleDefinitionCodes.InvalidVersionPolicy, "/versionPolicy/kind");
        }
        if (selected is null) return new(RuleResolutionStatus.NotFound, null);
        if (selected.Status == DefinitionStatus.Draft && scope == RuleResolveScope.Production)
            return new(RuleResolutionStatus.DraftRefused, null);
        return new(RuleResolutionStatus.Resolved, Decode(selected));
    }

    /// <summary>Validates reconstructed Rules source at the shared admission boundary.</summary>
    public static IReadOnlyList<DefinitionRefusal> Admit(DefinitionDocument document, DefinitionAdmissionPhase phase)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Key.Kind != DefinitionKind.Rules) return [new("definition.registry_unknown", "/registry")];
        if (!Enum.IsDefined(phase)) return [new(RuleDefinitionCodes.InvalidDocument, "/phase")];
        var result = ValidateSource(document, phase == DefinitionAdmissionPhase.Publish
            ? RuleIntentPhase.Publish : RuleIntentPhase.Author);
        return Refusals(result);
    }

    private static RuleIntentResult ValidateSource(DefinitionDocument document, RuleIntentPhase phase)
    {
        if (!DefinitionSemanticVersion.TryParse(document.Version, out _))
            return new(null, [new("definition.version_invalid", "/version", phase)]);
        var result = RuleIntentValidator.ValidateBodyJson(document.BodyJson, document.Key.DefinitionId,
            document.Key.Tenant, document.Version, phase);
        return result;
    }

    private static RuleDefinitionSnapshot Decode(DefinitionRevision revision)
        => new(revision, RequireSource(ValidateSource(revision.Document, RuleIntentPhase.Persisted)));

    private static RuleDefinitionDocument RequireSource(RuleIntentResult result)
        => result.IsValid ? result.Document! : throw new DefinitionRefusalException(Refusals(result));

    private static DefinitionRefusal[] Refusals(RuleIntentResult result)
        => result.Diagnostics.Select(item => new DefinitionRefusal(item.Code, item.Location)).ToArray();

    private static void RequirePolicy(RuleVersionPolicy policy)
    {
        if (!Enum.IsDefined(policy.Kind)) throw Refuse(RuleDefinitionCodes.InvalidVersionPolicy, "/versionPolicy/kind");
        if (policy.Kind == RuleVersionPolicyKind.Pinned && !DefinitionSemanticVersion.TryParse(policy.Version, out _))
            throw Refuse("definition.version_invalid", "/versionPolicy/version");
    }

    private static void RequireRules(DefinitionKey key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(key);
        if (key.Kind != DefinitionKind.Rules) throw Refuse("definition.registry_unknown", "/registry");
    }

    private static DefinitionRefusalException Refuse(string code, string pointer) => new([new(code, pointer)]);
}
