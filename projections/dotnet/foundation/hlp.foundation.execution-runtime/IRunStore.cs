using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The store port for run records. Every read and write is tenant-scoped; an engine's own store is never
/// read through it, and it is never a back door into one.
/// </summary>
public interface IRunStore
{
    /// <summary>Stores a new record, refusing an identity already stored for the tenant.</summary>
    ValueTask CreateAsync(RunRecord record, CancellationToken cancellationToken = default);

    /// <summary>The tenant's record for an identity, or null when absent.</summary>
    ValueTask<RunRecord?> GetAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the record stored at <paramref name="expectedVersion"/> with <paramref name="record"/>, which must
    /// carry the next version; refuses when the stored version has moved.
    /// </summary>
    ValueTask UpdateAsync(RunRecord record, long expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>The tenant's records in a status, ordered by creation time then identity.</summary>
    ValueTask<IReadOnlyList<RunRecord>> ListByStatusAsync(
        TenantId tenantId,
        RunStatus status,
        CancellationToken cancellationToken = default);
}
