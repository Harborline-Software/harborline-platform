using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>One substrate dispatcher for due retries and timer waits, scoped to a host's tenants.</summary>
public sealed class DueRunDispatcher
{
    private readonly RunKindRegistry _registry;
    private readonly IRunStore _store;
    private readonly TimeProvider _clock;
    private readonly RunLifecycle _lifecycle;
    private readonly string _dispatcherId;
    private readonly TimeSpan _lease;
    private readonly HashSet<TenantId> _started = [];
    private readonly SemaphoreSlim _pass = new(1);

    /// <summary>Uses a stable node id across restarts and a positive timer lease (default five minutes).</summary>
    public DueRunDispatcher(RunKindRegistry registry, IRunStore store, TimeProvider clock, string dispatcherId, TimeSpan? lease = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatcherId);
        _dispatcherId = dispatcherId;
        _lease = lease ?? TimeSpan.FromMinutes(5);
        if (_lease <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lease));
        _lifecycle = new RunLifecycle(registry, store, clock);
    }

    /// <summary>
    /// Runs one pass, returning due run ids with no handler without claiming them. Handler failures propagate;
    /// retries stay running and timers become eligible after their lease. Hosts schedule passes, never engines.
    /// </summary>
    public async ValueTask<IReadOnlyList<RunId>> DispatchAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId.Value);
        await _pass.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_started.Contains(tenantId))
            {
                await ClearOwnClaimsAsync(tenantId, cancellationToken).ConfigureAwait(false);
                _started.Add(tenantId);
            }
            var missing = new List<RunId>();
            var retries = await _store.ListByStatusAsync(tenantId, RunStatus.AwaitingRetry, cancellationToken).ConfigureAwait(false);
            foreach (var run in retries)
            {
                if (run.NextAttemptDueUtc is not { } due || due > _clock.GetUtcNow()) continue;
                var handler = _registry.Registrations.FirstOrDefault(registration => registration.Kind == run.Id.Kind)?.Handler;
                if (handler is null) { missing.Add(run.Id); continue; }
                var claimed = await TryBeginAttemptAsync(tenantId, run.Id, cancellationToken).ConfigureAwait(false);
                if (claimed is null) continue;
                await handler(claimed, null, cancellationToken).ConfigureAwait(false);
            }
            var running = await _store.ListByStatusAsync(tenantId, RunStatus.Running, cancellationToken).ConfigureAwait(false);
            foreach (var candidate in running)
            {
                foreach (var step in candidate.Waits.Select(wait => wait.StepId))
                {
                    var run = await _store.GetAsync(tenantId, candidate.Id, cancellationToken).ConfigureAwait(false);
                    if (run is null || run.Status != RunStatus.Running) break;
                    var now = _clock.GetUtcNow();
                    var wait = run.Waits.FirstOrDefault(wait => wait.StepId == step);
                    if (wait is null || wait.Reason != RunWaitReason.Timer || wait.IsHeld
                        || wait.WakeUpUtc > now || wait.ClaimExpiresUtc > now) continue;
                    var handler = _registry.Registrations.FirstOrDefault(registration => registration.Kind == run.Id.Kind)?.Handler;
                    if (handler is null) { if (!missing.Contains(run.Id)) missing.Add(run.Id); continue; }
                    var claimed = run with
                    {
                        Waits = run.Waits.Select(item => item == wait ? item with { ClaimDispatcherId = _dispatcherId, ClaimExpiresUtc = now + _lease } : item).ToArray(),
                        UpdatedUtc = now,
                        Version = run.Version + 1,
                    };
                    try
                    {
                        await _store.UpdateAsync(claimed, run.Version, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ExecutionRuntimeRefusedException ex) when (ex.Code == ExecutionRuntimeRefusals.RunConcurrencyConflict)
                    {
                        continue;
                    }
                    await handler(claimed, step, cancellationToken).ConfigureAwait(false);
                }
            }
            return missing;
        }
        finally { _pass.Release(); }
    }

    private async ValueTask<RunRecord?> TryBeginAttemptAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken)
    {
        try
        {
            return await _lifecycle.BeginAttemptAsync(tenantId, id, cancellationToken).ConfigureAwait(false);
        }
        catch (ExecutionRuntimeRefusedException ex) when (ex.Code is ExecutionRuntimeRefusals.RunConcurrencyConflict
            or ExecutionRuntimeRefusals.TransitionIllegal or ExecutionRuntimeRefusals.RetryNotDue)
        {
            return null;
        }
    }

    private async ValueTask ClearOwnClaimsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        // Retry a conflicted cleanup so startup cannot leave an own claim stranded until lease expiry.
        foreach (var candidate in await _store.ListByStatusAsync(tenantId, RunStatus.Running, cancellationToken).ConfigureAwait(false))
        {
            while (true)
            {
                var run = await _store.GetAsync(tenantId, candidate.Id, cancellationToken).ConfigureAwait(false);
                if (run is null || !run.Waits.Any(wait => wait.Reason == RunWaitReason.Timer && wait.ClaimDispatcherId == _dispatcherId)) break;
                var next = run with
                {
                    Waits = run.Waits.Select(wait => wait.Reason == RunWaitReason.Timer && wait.ClaimDispatcherId == _dispatcherId
                        ? wait with { ClaimDispatcherId = null, ClaimExpiresUtc = null } : wait).ToArray(),
                    UpdatedUtc = _clock.GetUtcNow(),
                    Version = run.Version + 1,
                };
                try
                {
                    await _store.UpdateAsync(next, run.Version, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (ExecutionRuntimeRefusedException ex) when (ex.Code == ExecutionRuntimeRefusals.RunConcurrencyConflict) { }
            }
        }
    }
}
