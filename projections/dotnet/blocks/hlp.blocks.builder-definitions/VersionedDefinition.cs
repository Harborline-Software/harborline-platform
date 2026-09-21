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
}

/// <summary>A stable, localizable refusal at an RFC 6901 pointer.</summary>
public sealed record DefinitionRefusal(string Code, string Pointer);

/// <summary>
/// Pure member admission. It returns refusals without changing source or performing persistence.
/// Publication validates the same immutable snapshot that the store conditionally commits.
/// </summary>
public delegate IReadOnlyList<DefinitionRefusal> DefinitionAdmission(
    DefinitionDocument document, DefinitionAdmissionPhase phase);

/// <summary>A refused operation. No history or published head changed.</summary>
public sealed class DefinitionRefusalException : Exception
{
    /// <summary>Captures a detached refusal list.</summary>
    public DefinitionRefusalException(IEnumerable<DefinitionRefusal> refusals)
        : base("definition.refused") => Refusals = Array.AsReadOnly(refusals.ToArray());

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
