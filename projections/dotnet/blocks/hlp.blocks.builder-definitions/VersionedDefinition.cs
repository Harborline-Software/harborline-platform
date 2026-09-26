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

/// <summary>The admission boundary presented to a member's validator.</summary>
public enum DefinitionAdmissionPhase
{
    /// <summary>Draft creation, replacement or restoration.</summary>
    Author,
    /// <summary>Publication of an immutable version.</summary>
    Publish,
    /// <summary>Installation of released content against its pinned dependency closure.</summary>
    Install,
    /// <summary>Re-admission of persisted published content against this host before it renders (T-583 item 2).</summary>
    Render,
}

/// <summary>
/// A stable, localizable refusal at an RFC 6901 pointer. <paramref name="Target"/> names a fetchable definition the
/// refusal concerns, and is present only when revealing it is safe and authorized; otherwise it is omitted.
/// </summary>
public sealed record DefinitionRefusal(string Code, string Pointer, string? Target = null);

/// <summary>A pure admission verdict: the stage it ran at and every refusal, empty when admitted.</summary>
public sealed record DefinitionRefusalReport(DefinitionAdmissionPhase Stage, IReadOnlyList<DefinitionRefusal> Refusals);

/// <summary>
/// Pure member admission. It returns refusals without changing source or performing persistence.
/// Publication validates the same immutable snapshot that the store conditionally commits.
/// </summary>
public delegate IReadOnlyList<DefinitionRefusal> DefinitionAdmission(
    DefinitionDocument document, DefinitionAdmissionPhase phase);

/// <summary>A refused operation. No history or published head changed.</summary>
public sealed class DefinitionRefusalException : Exception
{
    /// <summary>Captures a detached refusal list and the stage that refused.</summary>
    public DefinitionRefusalException(DefinitionAdmissionPhase stage, IEnumerable<DefinitionRefusal> refusals)
        : base("definition.refused") => (Stage, Refusals) = (stage, Array.AsReadOnly(refusals.ToArray()));

    /// <summary>The admission stage that refused. There is no default: every refusal names its stage explicitly (T-724 ruling 79).</summary>
    public DefinitionAdmissionPhase Stage { get; }

    /// <summary>The coded, located reasons for refusal.</summary>
    public IReadOnlyList<DefinitionRefusal> Refusals { get; }
}

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
