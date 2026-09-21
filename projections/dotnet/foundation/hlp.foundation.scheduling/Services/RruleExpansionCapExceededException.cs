using System.Globalization;

namespace Harborline.Foundation.Scheduling;

/// <summary>
/// Whether a recurrence rule carries its own end, and which part supplies it.
/// </summary>
public enum RecurrenceBound
{
    /// <summary>The rule runs forever; only the caller's horizon bounds the walk.</summary>
    None,

    /// <summary>The rule ends after <c>COUNT</c> candidate occurrences from the anchor.</summary>
    Count,

    /// <summary>The rule ends on the <c>UNTIL</c> date.</summary>
    Until,
}

/// <summary>
/// The expansion reached its candidate limit before it could determine the result for the
/// requested range, so it refuses rather than returning a partial list.
/// </summary>
/// <remarks>
/// <para>
/// Per DES-0057 §10 ruling 3 (2026-09-20) an expansion may return occurrences <b>only</b> when
/// every occurrence relevant to the requested range has been evaluated. A truncated list is a
/// false success: a caller cannot tell "this recurrence has no occurrences in the requested
/// window" from "the walker stopped before it reached that window". A flag would leave the API
/// fail-open — a caller that omits the check gets exactly the old behaviour — so the cap is a
/// control-flow boundary instead, matching the producer's standing rule that an authored
/// restriction is never silently discarded.
/// </para>
/// <para>
/// A valid long recurrence is not malformed input. The answers to a genuine overflow are to
/// measure the worst case and raise the limit, narrow the requested range, or add a windowed
/// expansion — never to let the result misreport.
/// </para>
/// </remarks>
public sealed class RruleExpansionCapExceededException : Exception
{
    /// <summary>
    /// Constructs the refusal from the walk state at the moment the limit was reached.
    /// </summary>
    public RruleExpansionCapExceededException(
        string recurrenceId,
        DateOnly anchor,
        DateOnly requestedRangeStart,
        DateOnly requestedRangeEnd,
        int candidateLimit,
        int candidatesExamined,
        DateOnly? lastEvaluatedOccurrence,
        RecurrenceBound bound)
        : base(
            $"RRULE '{recurrenceId}' anchored {anchor:yyyy-MM-dd} reached the {candidateLimit}-occurrence "
            + $"candidate limit before the requested range {requestedRangeStart:yyyy-MM-dd}..{requestedRangeEnd:yyyy-MM-dd} "
            + $"was fully evaluated ({candidatesExamined} candidates examined, last evaluated occurrence "
            + $"{(lastEvaluatedOccurrence is { } last ? last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none")}, rule bound: {bound}). "
            + "A partial expansion is not returned because a caller cannot distinguish it from a complete one.")
    {
        RecurrenceId = recurrenceId;
        Anchor = anchor;
        RequestedRangeStart = requestedRangeStart;
        RequestedRangeEnd = requestedRangeEnd;
        CandidateLimit = candidateLimit;
        CandidatesExamined = candidatesExamined;
        LastEvaluatedOccurrence = lastEvaluatedOccurrence;
        Bound = bound;
    }

    /// <summary>
    /// The recurrence that could not be evaluated. This boundary takes no caller-side identifier,
    /// so a recurrence's identity here is its rule string — which is also what a reader needs to
    /// diagnose the rule half of the failure.
    /// </summary>
    public string RecurrenceId { get; }

    /// <summary>The recurrence anchor (DTSTART) the walk started from.</summary>
    public DateOnly Anchor { get; }

    /// <summary>The earliest date the caller asked to be told about (<c>today + leadDays</c>).</summary>
    public DateOnly RequestedRangeStart { get; }

    /// <summary>
    /// The last date the caller asked to be told about: the explicit end or
    /// <c>today + lookaheadDays</c>, whichever the rule's <c>UNTIL</c> did not already lower.
    /// </summary>
    public DateOnly RequestedRangeEnd { get; }

    /// <summary>The hard cap on returned occurrences that the walk reached.</summary>
    public int CandidateLimit { get; }

    /// <summary>
    /// Candidate occurrences matched from the anchor before the limit was reached, including
    /// those the lead filter excluded from the result.
    /// </summary>
    public int CandidatesExamined { get; }

    /// <summary>
    /// The last date that matched the rule before the walk stopped, or <see langword="null"/>
    /// if none matched.
    /// </summary>
    public DateOnly? LastEvaluatedOccurrence { get; }

    /// <summary>Whether the rule ends through <c>COUNT</c>, <c>UNTIL</c>, or neither.</summary>
    public RecurrenceBound Bound { get; }
}
