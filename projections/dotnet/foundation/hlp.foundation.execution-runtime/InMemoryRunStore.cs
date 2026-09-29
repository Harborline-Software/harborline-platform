using System.Collections.Concurrent;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>Single-process reference implementation of <see cref="IRunStore"/> for tests and hosts without durable storage.</summary>
public sealed class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<(TenantId Tenant, RunId Id), RunRecord> _runs = new();

    /// <inheritdoc />
    public ValueTask CreateAsync(RunRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_runs.TryAdd((record.TenantId, record.Id), record))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunDuplicate, $"Run '{record.Id}' is already stored.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<RunRecord?> GetAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_runs.GetValueOrDefault((tenantId, id)));
    }

    /// <inheritdoc />
    public ValueTask UpdateAsync(RunRecord record, long expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();
        var key = (record.TenantId, record.Id);
        if (!_runs.TryGetValue(key, out var current))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunUnknown, $"Run '{record.Id}' is not stored.");
        }

        if (current.Version != expectedVersion || record.Version != expectedVersion + 1 || !_runs.TryUpdate(key, record, current))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunConcurrencyConflict,
                $"Run '{record.Id}' moved past version {expectedVersion}.");
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<RunRecord>> ListByStatusAsync(
        TenantId tenantId,
        RunStatus status,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RunRecord> matches = _runs.Values
            .Where(run => run.TenantId == tenantId && run.Status == status)
            .OrderBy(run => run.CreatedUtc)
            .ThenBy(run => run.Id.ToString(), StringComparer.Ordinal)
            .ToList();
        return ValueTask.FromResult(matches);
    }
}
