namespace Harborline.Foundation.ExecutionRuntime;

/// <summary>
/// The closed run status vocabulary every engine's run is read through (DES-0056
/// <c>execution-runtime-ck-1</c>). An engine's own terminal detail (a plan's <c>proven</c>, an exchange's
/// census) is engine state; the substrate status is one of these.
/// </summary>
public enum RunStatus
{
    /// <summary>Started under a kind, no attempt begun. Wire name <c>pending</c>.</summary>
    Pending = 0,

    /// <summary>An attempt is in progress. Wire name <c>running</c>.</summary>
    Running = 1,

    /// <summary>An attempt failed retryably and the profile allows another; waiting until it is due. Wire name <c>awaiting-retry</c>.</summary>
    AwaitingRetry = 2,

    /// <summary>Terminal: an attempt succeeded. Wire name <c>succeeded</c>.</summary>
    Succeeded = 3,

    /// <summary>Terminal: the retry profile is exhausted or the failure is not retryable; the one dead-letter path. Wire name <c>dead-lettered</c>.</summary>
    DeadLettered = 4,

    /// <summary>Terminal: withdrawn before it succeeded or dead-lettered. Wire name <c>cancelled</c>.</summary>
    Cancelled = 5,
}

/// <summary>The legal status transitions, and the wire names of the vocabulary.</summary>
public static class RunStatusTransitions
{
    private static readonly Dictionary<RunStatus, RunStatus[]> Legal = new Dictionary<RunStatus, RunStatus[]>
    {
        [RunStatus.Pending] = [RunStatus.Running, RunStatus.Cancelled],
        [RunStatus.Running] = [RunStatus.Succeeded, RunStatus.AwaitingRetry, RunStatus.DeadLettered, RunStatus.Cancelled],
        [RunStatus.AwaitingRetry] = [RunStatus.Running, RunStatus.Cancelled],
        [RunStatus.Succeeded] = [],
        [RunStatus.DeadLettered] = [],
        [RunStatus.Cancelled] = [],
    };

    /// <summary>True when the table allows <paramref name="from"/> to become <paramref name="to"/>.</summary>
    public static bool IsLegal(RunStatus from, RunStatus to) =>
        Legal.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Refuses a transition the table does not allow, including any value outside the vocabulary.</summary>
    public static void Require(RunStatus from, RunStatus to)
    {
        if (!IsLegal(from, to))
        {
            throw new ExecutionRuntimeRefusedException(
                ExecutionRuntimeRefusals.TransitionIllegal,
                $"A run cannot move from '{WireName(from)}' to '{WireName(to)}'.");
        }
    }

    /// <summary>True for the three statuses no transition leaves.</summary>
    public static bool IsTerminal(RunStatus status) =>
        Legal.TryGetValue(status, out var targets) && targets.Length == 0;

    /// <summary>The wire name of a status, or the raw number for a value outside the vocabulary.</summary>
    public static string WireName(RunStatus status) => status switch
    {
        RunStatus.Pending => "pending",
        RunStatus.Running => "running",
        RunStatus.AwaitingRetry => "awaiting-retry",
        RunStatus.Succeeded => "succeeded",
        RunStatus.DeadLettered => "dead-lettered",
        RunStatus.Cancelled => "cancelled",
        _ => ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
