namespace Harborline.Foundation.Scheduling;

/// <summary>
/// The SOURCE-AGNOSTIC due-occurrence seam (user ruling 2026-08-16, migration ticket 086).
/// A source derives incomplete due occurrences over an inclusive window from whatever
/// schedule kind it owns: the RRULE expander is the FIRST source behind this seam
/// (<see cref="RruleDueOccurrenceSource"/>); the commissioned ADR-0149-amendment
/// event-anchored relative-chain generator lands later as a SECOND source behind the SAME
/// port — no breaking change to consumers. The pinned <see cref="IDueQueueQueryService"/>
/// stays byte-faithful beneath this seam; this abstraction is additive.
/// </summary>
public interface IDueOccurrenceSource
{
    /// <summary>Derive incomplete due occurrences over the inclusive window, ordered
    /// overdue-first then by due date (the pinned queue ordering).</summary>
    IReadOnlyList<DueQueueItem> DeriveDue(
        IEnumerable<DueQueueCompletion> completionHistory,
        DateOnly windowStart,
        DateOnly windowEnd,
        DateOnly asOf);
}

/// <summary>The RRULE-backed source: the pinned due-queue query over the source's own
/// RRULE schedule definitions.</summary>
public sealed class RruleDueOccurrenceSource(
    IDueQueueQueryService query,
    IEnumerable<DueQueueSchedule> schedules) : IDueOccurrenceSource
{
    private readonly IReadOnlyList<DueQueueSchedule> schedules = schedules.ToArray();

    public IReadOnlyList<DueQueueItem> DeriveDue(
        IEnumerable<DueQueueCompletion> completionHistory,
        DateOnly windowStart,
        DateOnly windowEnd,
        DateOnly asOf)
        => query.Query(schedules, completionHistory, windowStart, windowEnd, asOf);
}

/// <summary>Merges due occurrences from every registered source and re-applies the pinned
/// queue ordering (most-overdue first, then upcoming by due date) across the merged set.</summary>
public sealed class CompositeDueOccurrenceSource(IEnumerable<IDueOccurrenceSource> sources) : IDueOccurrenceSource
{
    private readonly IReadOnlyList<IDueOccurrenceSource> sources = sources.ToArray();

    public IReadOnlyList<DueQueueItem> DeriveDue(
        IEnumerable<DueQueueCompletion> completionHistory,
        DateOnly windowStart,
        DateOnly windowEnd,
        DateOnly asOf)
    {
        var history = completionHistory.ToArray();
        return sources
            .SelectMany(source => source.DeriveDue(history, windowStart, windowEnd, asOf))
            .OrderByDescending(item => item.OverdueDays)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.SubjectRef, StringComparer.Ordinal)
            .ThenBy(item => item.ScheduleRef, StringComparer.Ordinal)
            .ToArray();
    }
}
