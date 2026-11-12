namespace Harborline.Foundation.Scheduling;

/// <summary>A complete expansion cannot be determined within the producer's work budget.</summary>
public sealed class RruleExpansionBudgetExceededException(
    string recurrenceId, DateOnly anchor, DateOnly requestedRangeStart, DateOnly requestedRangeEnd,
    int candidateLimit, int candidatesExamined, DateOnly? lastEvaluatedOccurrence, RecurrenceBound bound)
    : InvalidOperationException("scheduling.occurrence.expansion_budget_exceeded")
{
    /// <summary>The stable refusal code.</summary>
    public string Code => "scheduling.occurrence.expansion_budget_exceeded";
    /// <summary>The authored rule.</summary>
    public string RecurrenceId { get; } = recurrenceId;
    /// <summary>The original DTSTART.</summary>
    public DateOnly Anchor { get; } = anchor;
    /// <summary>The requested lower bound.</summary>
    public DateOnly RequestedRangeStart { get; } = requestedRangeStart;
    /// <summary>The requested upper bound, including relevant override originals.</summary>
    public DateOnly RequestedRangeEnd { get; } = requestedRangeEnd;
    /// <summary>The allowed work units: recurrence periods plus matching dates.</summary>
    public int CandidateLimit { get; } = candidateLimit;
    /// <summary>Work units attempted, including the first unit past the limit.</summary>
    public int CandidatesExamined { get; } = candidatesExamined;
    /// <summary>The most recent matched date, if any.</summary>
    public DateOnly? LastEvaluatedOccurrence { get; } = lastEvaluatedOccurrence;
    /// <summary>The authored COUNT or UNTIL bound.</summary>
    public RecurrenceBound Bound { get; } = bound;
}
