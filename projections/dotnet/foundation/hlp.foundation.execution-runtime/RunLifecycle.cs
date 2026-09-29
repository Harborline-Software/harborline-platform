using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>A request to start a run of a registered kind.</summary>
/// <param name="Kind">The registered run kind.</param>
/// <param name="TenantId">The tenant the run belongs to.</param>
/// <param name="Capability">The retry profiles the capability being run allows, and its default.</param>
/// <param name="SelectedRetryProfile">The author's selection among the allowed profiles, or null for the default.</param>
/// <param name="CausedBy">The run that caused this one, correlated and never merged.</param>
public sealed record StartRun(
    RunKind Kind,
    TenantId TenantId,
    CapabilityRetryPolicy Capability,
    RetryProfileName? SelectedRetryProfile = null,
    RunId? CausedBy = null);

/// <summary>
/// The one place a run's status changes. Every engine starts, attempts, fails, succeeds and cancels its runs
/// here, so every run obeys one transition table, one retry rule and one dead-letter path
/// (DES-0056 <c>execution-runtime-ck-1</c>, <c>execution-runtime-ck-8</c>, <c>execution-runtime-cc-5</c>).
/// </summary>
public sealed class RunLifecycle
{
    private readonly RunKindRegistry _registry;
    private readonly IRunStore _store;
    private readonly TimeProvider _clock;

    /// <summary>Creates the lifecycle over a registry, a store and a clock.</summary>
    public RunLifecycle(RunKindRegistry registry, IRunStore store, TimeProvider clock)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Starts a <see cref="RunStatus.Pending"/> run under a fresh identity of the request's kind, refusing an
    /// unregistered kind, a retry profile the capability does not allow, and a <c>caused_by</c> that names no
    /// run the tenant holds.
    /// </summary>
    public async ValueTask<RunRecord> StartAsync(StartRun request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Capability);
        RequireTenant(request.TenantId);
        _registry.Require(request.Kind);
        var profile = request.Capability.Resolve(request.SelectedRetryProfile);

        if (request.CausedBy is { } cause
            && await _store.GetAsync(request.TenantId, cause, cancellationToken).ConfigureAwait(false) is null)
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.CausedByInvalid,
                $"caused_by names run '{cause}', which the tenant does not hold.");
        }

        var now = _clock.GetUtcNow();
        var record = new RunRecord
        {
            Id = RunId.New(request.Kind),
            TenantId = request.TenantId,
            Status = RunStatus.Pending,
            RetryProfile = profile.Name,
            CausedBy = request.CausedBy,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        await _store.CreateAsync(record, cancellationToken).ConfigureAwait(false);
        return record;
    }

    /// <summary>Begins the next attempt, refusing a retry that is not yet due.</summary>
    public ValueTask<RunRecord> BeginAttemptAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default) =>
        ChangeAsync(tenantId, id, (run, now) =>
        {
            if (run.Status == RunStatus.AwaitingRetry && run.NextAttemptDueUtc > now)
            {
                throw new ExecutionRuntimeRefusedException(
                    ExecutionRuntimeRefusals.RetryNotDue,
                    $"Run '{run.Id}' is not due for another attempt until {run.NextAttemptDueUtc:O}.");
            }

            RunStatusTransitions.Require(run.Status, RunStatus.Running);
            return run with
            {
                Status = RunStatus.Running,
                NextAttemptDueUtc = null,
                Attempts = [.. run.Attempts, new RunAttempt(run.Attempts.Count + 1, now, null, null)],
            };
        }, cancellationToken);

    /// <summary>Ends the running attempt as a success.</summary>
    public ValueTask<RunRecord> SucceedAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default) =>
        ChangeAsync(tenantId, id, (run, now) =>
        {
            RunStatusTransitions.Require(run.Status, RunStatus.Succeeded);
            return run with { Status = RunStatus.Succeeded, Attempts = EndLastAttempt(run, now, null) };
        }, cancellationToken);

    /// <summary>
    /// Ends the running attempt as a failure. The run awaits another attempt when the failure is retryable and
    /// its profile allows one; otherwise it dead-letters, the one path every engine's failed runs take.
    /// </summary>
    public ValueTask<RunRecord> FailAttemptAsync(
        TenantId tenantId,
        RunId id,
        RunFailure failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure.Code);
        return ChangeAsync(tenantId, id, (run, now) =>
        {
            // Only a running attempt can fail, and running is the only status dead-lettered is legal from;
            // awaiting-retry is legal from exactly the same one.
            RunStatusTransitions.Require(run.Status, RunStatus.DeadLettered);

            var profile = RetryProfiles.Get(run.RetryProfile);
            var attempts = EndLastAttempt(run, now, failure);
            if (failure.Retryable && profile.AllowsAnotherAttempt(attempts.Count))
            {
                return run with
                {
                    Status = RunStatus.AwaitingRetry,
                    Attempts = attempts,
                    NextAttemptDueUtc = now + profile.DelayAfter(attempts.Count),
                };
            }

            var reason = failure.Retryable ? DeadLetterReason.RetriesExhausted : DeadLetterReason.NotRetryable;
            return run with
            {
                Status = RunStatus.DeadLettered,
                Attempts = attempts,
                DeadLetter = new DeadLetter(reason, failure, now),
            };
        }, cancellationToken);
    }

    /// <summary>Cancels a run that has not reached a terminal status.</summary>
    public ValueTask<RunRecord> CancelAsync(TenantId tenantId, RunId id, CancellationToken cancellationToken = default) =>
        ChangeAsync(tenantId, id, (run, now) =>
        {
            RunStatusTransitions.Require(run.Status, RunStatus.Cancelled);
            var attempts = run.Status == RunStatus.Running ? EndLastAttempt(run, now, null) : run.Attempts;
            return run with { Status = RunStatus.Cancelled, Attempts = attempts, NextAttemptDueUtc = null };
        }, cancellationToken);

    /// <summary>The tenant's dead-lettered runs of every kind, read without touching any engine's store.</summary>
    public ValueTask<IReadOnlyList<RunRecord>> DeadLetteredAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        RequireTenant(tenantId);
        return _store.ListByStatusAsync(tenantId, RunStatus.DeadLettered, cancellationToken);
    }

    private async ValueTask<RunRecord> ChangeAsync(
        TenantId tenantId,
        RunId id,
        Func<RunRecord, DateTimeOffset, RunRecord> change,
        CancellationToken cancellationToken)
    {
        RequireTenant(tenantId);
        var current = await _store.GetAsync(tenantId, id, cancellationToken).ConfigureAwait(false)
            ?? throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.RunUnknown, $"The tenant holds no run '{id}'.");
        var now = _clock.GetUtcNow();
        var next = change(current, now) with { UpdatedUtc = now, Version = current.Version + 1 };
        await _store.UpdateAsync(next, current.Version, cancellationToken).ConfigureAwait(false);
        return next;
    }

    private static List<RunAttempt> EndLastAttempt(RunRecord run, DateTimeOffset now, RunFailure? failure)
    {
        var attempts = run.Attempts.ToList();
        attempts[^1] = attempts[^1] with { EndedUtc = now, Failure = failure };
        return attempts;
    }

    private static void RequireTenant(TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId.Value))
        {
            throw new ArgumentException("A run requires a tenant.", nameof(tenantId));
        }
    }
}
