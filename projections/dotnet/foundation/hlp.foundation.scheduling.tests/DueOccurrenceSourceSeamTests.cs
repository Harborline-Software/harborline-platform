using Harborline.Foundation.Scheduling;

using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

/// <summary>
/// The source-agnostic due-occurrence seam (user ruling 2026-08-16; migration ticket 086):
/// additive proofs that the RRULE expander is merely the FIRST source behind the seam and a
/// second source composes through the SAME port with the pinned queue ordering preserved.
/// These are seam tests, not pinned rows.
/// </summary>
public sealed class DueOccurrenceSourceSeamTests
{
    private sealed class StaticSource(params DueQueueItem[] items) : IDueOccurrenceSource
    {
        public IReadOnlyList<DueQueueItem> DeriveDue(
            IEnumerable<DueQueueCompletion> completionHistory,
            DateOnly windowStart,
            DateOnly windowEnd,
            DateOnly asOf) => items;
    }

    [Fact(DisplayName = "seam: the RRULE source yields exactly the pinned due-queue query result")]
    public void RruleSourceMatchesPinnedQuery()
    {
        var query = new RruleDueQueueQueryService(new InMemoryRruleExpansionService());
        var schedules = new[]
        {
            new DueQueueSchedule("subject:a", "schedule:daily", "FREQ=DAILY", new DateOnly(2026, 7, 1), null, "UTC", true),
        };
        var completions = new[] { new DueQueueCompletion("subject:a", "schedule:daily", new DateOnly(2026, 7, 1)) };
        var window = (Start: new DateOnly(2026, 7, 1), End: new DateOnly(2026, 7, 3));
        var asOf = new DateOnly(2026, 7, 3);

        var direct = query.Query(schedules, completions, window.Start, window.End, asOf);
        var viaSeam = new RruleDueOccurrenceSource(query, schedules)
            .DeriveDue(completions, window.Start, window.End, asOf);

        Assert.Equal(direct, viaSeam);
    }

    [Fact(DisplayName = "seam: a second source composes through the same port with pinned ordering preserved")]
    public void CompositeMergesSecondSourceWithPinnedOrdering()
    {
        var query = new RruleDueQueueQueryService(new InMemoryRruleExpansionService());
        var rrule = new RruleDueOccurrenceSource(query, new[]
        {
            new DueQueueSchedule("subject:a", "schedule:daily", "FREQ=DAILY", new DateOnly(2026, 7, 2), null, "UTC", true),
        });
        // A stand-in for the ticket-086 relative-chain generator: same item shape, no RRULE.
        var chainStandIn = new StaticSource(
            new DueQueueItem("subject:b", "chain:dose-2", new DateOnly(2026, 7, 1), 2),
            new DueQueueItem("subject:b", "chain:follow-up", new DateOnly(2026, 7, 4), 0));

        var merged = new CompositeDueOccurrenceSource([rrule, chainStandIn])
            .DeriveDue([], new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 3));

        Assert.Contains(merged, item => item.ScheduleRef == "schedule:daily");
        Assert.Contains(merged, item => item.ScheduleRef == "chain:dose-2");
        // Pinned ordering across the MERGED set: most overdue first, then upcoming by due date.
        Assert.Equal(merged.OrderByDescending(i => i.OverdueDays).ThenBy(i => i.DueDate).Select(i => i.ScheduleRef), merged.Select(i => i.ScheduleRef));
        Assert.Equal("chain:dose-2", merged[0].ScheduleRef);
    }

    [Fact(DisplayName = "seam: an empty composite derives an empty queue, never a failure")]
    public void EmptyCompositeDerivesEmpty()
    {
        var merged = new CompositeDueOccurrenceSource([])
            .DeriveDue([], new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 2), new DateOnly(2026, 7, 1));
        Assert.Empty(merged);
    }
}
