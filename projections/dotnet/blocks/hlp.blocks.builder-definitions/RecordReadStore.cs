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
