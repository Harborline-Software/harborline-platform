namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>The tenant-scoped committed-row reader over the same store the Records write path commits to.</summary>
/// <remarks>
/// This storage port grants no read authority. Only committed rows are visible, and tenant and record id are matched
/// ordinally, without trimming or case folding. Null means a miss only; corruption and operational failures propagate.
/// The current-read operation detaches body bytes before using or returning them. The real journal adapter is integrated
/// with that operation by its existing owner; this port does not introduce another journal parser or storage format.
/// PublicationVersionId is the immutable identity on bound rows and null on genuine legacy rows; adapters preserve it
/// without inferring it from Version. A journal validates every entry's local evidence before returning any row.
/// </remarks>
public interface IRecordReadStore
{
    /// <summary>Returns the exact committed row or null when this tenant has no record under this id.</summary>
    /// <param name="tenant">The host-bound tenant, compared exactly.</param>
    /// <param name="recordId">The opaque record identity, not a write operation id.</param>
    /// <param name="cancellationToken">Cancellation for the store lookup.</param>
    ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default);
}

/// <summary>The result of explicit effective selection.</summary>
public enum RecordTemporalLookupKind
{
    /// <summary>No exact logical identity.</summary>
    NotFound = 0,
    /// <summary>An ordinary row with no temporal provenance.</summary>
    NonTemporal = 1,
    /// <summary>A selected effective revision.</summary>
    Temporal = 2,
    /// <summary>A temporal identity has no covering revision.</summary>
    NoEffectiveRevision = 3,
}
/// <summary>Exactly the member named by Kind is populated.</summary>
/// <param name="Kind">Selection state.</param>
/// <param name="NonTemporal">Ordinary row only for NonTemporal.</param>
/// <param name="Temporal">Selected version only for Temporal.</param>
public sealed record RecordTemporalLookup(RecordTemporalLookupKind Kind, RecordWriteRow? NonTemporal, RecordTemporalVersion? Temporal);
/// <summary>Logical history provenance.</summary>
public enum RecordTemporalHistoryKind
{
    /// <summary>No identity.</summary>
    NotFound = 0,
    /// <summary>Ordinary identity without fabricated history.</summary>
    NonTemporal = 1,
    /// <summary>Complete nonempty causal sequence.</summary>
    Temporal = 2,
}
/// <summary>A complete detached causal sequence, or an empty non-temporal result.</summary>
/// <param name="Kind">Provenance state.</param>
/// <param name="Versions">Versions in causal order; empty unless Temporal.</param>
public sealed record RecordTemporalHistory(RecordTemporalHistoryKind Kind, IReadOnlyList<RecordTemporalVersion> Versions);
/// <summary>Stable same-journal target metadata without authority or publication lookup.</summary>
/// <param name="RecordTypeId">Immutable logical Record Type.</param>
/// <param name="IsTemporal">Whether this identity has temporal provenance.</param>
public sealed record RecordTemporalTarget(string RecordTypeId, bool IsTemporal);
/// <summary>Explicit bounded effective/history/target reads, all using the current-read load spine.</summary>
public interface IRecordTemporalReadStore : IRecordReadStore
{
    /// <summary>Validates the whole journal and selects by the declared effective instant.</summary>
    ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId,
        DateTimeOffset effectiveAt, CancellationToken cancellationToken = default);
    /// <summary>Returns complete detached causal history without inferring old-row dates.</summary>
    ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId,
        CancellationToken cancellationToken = default);
    /// <summary>Returns stable target provenance, with the same 16 MiB resource bound.</summary>
    ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId,
        CancellationToken cancellationToken = default);
}
