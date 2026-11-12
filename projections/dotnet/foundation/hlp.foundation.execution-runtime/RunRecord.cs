using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>Why an attempt failed, as the engine classifies it.</summary>
/// <param name="Code">A stable, non-secret failure code the engine owns.</param>
/// <param name="Retryable">False when another attempt cannot help; the run then dead-letters at once.</param>
public sealed record RunFailure(string Code, bool Retryable);

/// <summary>One attempt in a run's history.</summary>
/// <param name="Number">1-based attempt number.</param>
/// <param name="StartedUtc">When the attempt began.</param>
/// <param name="EndedUtc">When it ended, or null while it runs.</param>
/// <param name="Failure">Why it failed, or null when it succeeded, was cancelled or still runs.</param>
public sealed record RunAttempt(int Number, DateTimeOffset StartedUtc, DateTimeOffset? EndedUtc, RunFailure? Failure);

/// <summary>Why a run dead-lettered.</summary>
public enum DeadLetterReason
{
    /// <summary>Every attempt the retry profile allows failed. Wire name <c>retries-exhausted</c>.</summary>
    RetriesExhausted = 0,

    /// <summary>The engine classified the failure as one another attempt cannot fix. Wire name <c>not-retryable</c>.</summary>
    NotRetryable = 1,
}

/// <summary>The dead-letter entry a run carries once it reaches <see cref="RunStatus.DeadLettered"/>.</summary>
/// <param name="Reason">Why it dead-lettered.</param>
/// <param name="LastFailure">The failure of the final attempt.</param>
/// <param name="DeadLetteredUtc">When it dead-lettered.</param>
public sealed record DeadLetter(DeadLetterReason Reason, RunFailure LastFailure, DateTimeOffset DeadLetteredUtc);

/// <summary>
/// The substrate's durable, tenant-scoped record of one run: identity, status, attempts, retry profile,
/// dead-letter and the run that caused it. It holds no engine state; an engine keeps its position,
/// iteration counter and payload in its own store, keyed by <see cref="Id"/>.
/// </summary>
public sealed record RunRecord
{
    /// <summary>The run's identity, kind included.</summary>
    public required RunId Id { get; init; }

    /// <summary>The tenant the run belongs to.</summary>
    public required TenantId TenantId { get; init; }

    /// <summary>The run's status in the closed vocabulary.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>The retry profile resolved at start from the capability's allowed set.</summary>
    public required RetryProfileName RetryProfile { get; init; }

    /// <summary>The run that caused this one, correlated and never merged.</summary>
    public RunId? CausedBy { get; init; }

    /// <summary>Every attempt made, in order.</summary>
    public IReadOnlyList<RunAttempt> Attempts { get; init; } = [];

    /// <summary>Pending parallel waits; waiting leaves a started run running.</summary>
    public IReadOnlyList<RunWait> Waits { get; init; } = [];

    /// <summary>When an awaiting-retry run's next attempt is due.</summary>
    public DateTimeOffset? NextAttemptDueUtc { get; init; }

    /// <summary>The dead-letter entry, set only when <see cref="Status"/> is <see cref="RunStatus.DeadLettered"/>.</summary>
    public DeadLetter? DeadLetter { get; init; }

    /// <summary>When the run was started.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>When the record last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The optimistic-concurrency version, 1 at creation and advanced by every stored change.</summary>
    public long Version { get; init; } = 1;
}
/// <summary>The closed reasons a running step waits.</summary>
public enum RunWaitReason
{
    /// <summary>Wire name human-task.</summary>
    HumanTask,
    /// <summary>Wire name timer.</summary>
    Timer,
    /// <summary>Wire name dependency.</summary>
    Dependency,
}

/// <summary>A pending wait. Fixed registration data survives holds, claims and restarts.</summary>
public sealed record RunWait
{
    /// <summary>Registers a wait, requiring a UTC wake-up time exactly for timers.</summary>
    public RunWait(string stepId, RunWaitReason reason, DateTimeOffset beganUtc, DateTimeOffset? wakeUpUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason));
        if (beganUtc.Offset != TimeSpan.Zero || wakeUpUtc?.Offset != TimeSpan.Zero && wakeUpUtc is not null
            || (reason == RunWaitReason.Timer) != wakeUpUtc.HasValue)
        {
            throw new ArgumentException("Wait times must be UTC and only a timer requires a wake-up time.");
        }
        StepId = stepId;
        Reason = reason;
        BeganUtc = beganUtc;
        WakeUpUtc = wakeUpUtc;
    }

    /// <summary>The engine's step identity.</summary>
    public string StepId { get; }
    /// <summary>Why the step is waiting.</summary>
    public RunWaitReason Reason { get; }
    /// <summary>When the wait was registered, in UTC.</summary>
    public DateTimeOffset BeganUtc { get; }
    /// <summary>The timer's fixed absolute UTC wake-up time.</summary>
    public DateTimeOffset? WakeUpUtc { get; }
    /// <summary>A held wait is ineligible for dispatch.</summary>
    public bool IsHeld { get; private init; }
    /// <summary>The stable node id holding the timer claim.</summary>
    public string? ClaimDispatcherId { get; internal init; }
    /// <summary>When the timer claim expires.</summary>
    public DateTimeOffset? ClaimExpiresUtc { get; internal init; }
    /// <summary>Holds this wait without moving its wake-up time.</summary>
    public RunWait Hold() => this with { IsHeld = true };
    /// <summary>Releases this wait without moving its wake-up time.</summary>
    public RunWait Release() => this with { IsHeld = false };
}
