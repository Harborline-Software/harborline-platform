namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A stable definition identity inside an exact tenant and registry namespace.</summary>
public sealed record DefinitionKey(string Tenant, DefinitionKind Kind, string DefinitionId);

/// <summary>
/// Provider-neutral source. VersionId is an opaque immutable publication identity; Version is
/// its semantic-version label. BodyJson is preserved verbatim and never repaired by the store.
/// Member adapters own typed decoding and consistency between this metadata and their source.
/// </summary>
public sealed record DefinitionDocument(DefinitionKey Key, string VersionId, string Version, string BodyJson);

/// <summary>A consumer pin. Both the definition identity and immutable version identity are required.</summary>
public sealed record DefinitionBinding(DefinitionKey Key, string VersionId);

/// <summary>An observed immutable published head and the caller's diagnostic pointer.</summary>
public sealed record DefinitionPublishedHeadCondition(DefinitionKey Key, long Revision, string Pointer);

/// <summary>The state recorded by an append-only lifecycle event.</summary>
public enum DefinitionStatus
{
    /// <summary>An editable source snapshot, never returned by production resolution.</summary>
    Draft,
    /// <summary>An immutable source available to production consumers.</summary>
    Published,
}

/// <summary>A snapshot and its monotonically increasing definition-stream revision.</summary>
public sealed record DefinitionRevision(
    DefinitionDocument Document,
    long Revision,
    DefinitionStatus Status,
    string Digest,
    string? RestoredFromVersionId = null);

/// <summary>
/// Pure member admission. It returns refusals without changing source or performing persistence.
/// Publication validates the same immutable snapshot that the store conditionally commits.
/// </summary>
public delegate IReadOnlyList<DefinitionRefusal> DefinitionAdmission(
    DefinitionDocument document, DefinitionAdmissionPhase phase);

/// <summary>
/// One registry-neutral revision store. Mutations require the expected per-definition stream
/// revision (zero for creation) and a nonempty replay identity. Replays match the complete original
/// operation, fence and payload. Durable implementations must commit history, head and replay
/// result in one conditional transaction. Validators are trusted composition, never author input.
/// </summary>
public interface IVersionedDefinitionStore
{
    /// <summary>Lists detached stream keys in an exact namespace, including drafts, by ordinal id.</summary>
    ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>Appends an admitted draft snapshot without changing any published version.</summary>
    ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>Re-admits and immutably publishes an existing draft.</summary>
    ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes only if every observed published head still matches, atomically with the source fence
    /// and commit. Conditions are part of replay identity. Exact replays return before current-head checks.
    /// Implementations without atomic support refuse nonempty conditions; checking then publishing is unsafe.
    /// </summary>
    ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
        string requestId, IReadOnlyList<DefinitionPublishedHeadCondition> conditions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions.Count != 0)
            throw new DefinitionRefusalException(DefinitionAdmissionPhase.Publish,
                [new("definition.atomic_publish_unsupported", "/conditions")]);
        return PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);
    }

    /// <summary>
    /// Returns an already committed publication from the original operation's replay record, without
    /// replacing its stored target conditions or resolving those targets again. A missing request returns null; an id
    /// reused for a different operation, version or source fence refuses replay conflict. Implementations
    /// supporting guarded publication must provide this lookup from the same transaction's replay record.
    /// </summary>
    ValueTask<DefinitionRevision?> GetPublicationReplayAsync(DefinitionKey key, string versionId,
        long expectedRevision, string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DefinitionRevision?>(null);
    }

    /// <summary>Copies a published body into a new draft identity and semantic version.</summary>
    ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
        string draftVersionId, string draftVersion, long expectedRevision, string requestId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns append-only lifecycle history in stream revision order.</summary>
    ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the highest published semantic version, excluding every draft.</summary>
    ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves an exact immutable consumer pin. An absent or draft version returns null.</summary>
    ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding,
        CancellationToken cancellationToken = default);
}
