using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class ExpansionBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UtcUntilCanIncludeTheFollowingLocalDate(bool visitor)
    {
        const string rule = "FREQ=DAILY;UNTIL=20261103T230000Z";
        var service = new InMemoryRruleExpansionService();
        var result = new List<DateOnly>();
        if (visitor)
            service.VisitZonedOccurrences(rule, new(2026, 11, 3), new(8, 0), "Asia/Tokyo", new(2026, 11, 4),
                new(2026, 11, 3), result.Add);
        else
            result.AddRange(service.ExpandZonedOccurrences(rule, new(2026, 11, 3), new(8, 0), "Asia/Tokyo",
                new(2026, 11, 4), 0, 0, new(2026, 11, 3)));
        Assert.Equal([new DateOnly(2026, 11, 3), new(2026, 11, 4)], result);
    }

    [Fact]
    public void BudgetCountsPeriodsAndDatesBeforeFilteringWithLiteralBoundaryAndFields()
    {
        var service = new InMemoryRruleExpansionService();
        var anchor = new DateOnly(2026, 1, 1);
        var result = new List<DateOnly>();
        // Three daily periods and three matched dates cost six units, even before the requested lower bound.
        service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
            new(2026, 1, 4), result.Add, 7);
        Assert.Equal([new DateOnly(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3)], result);
        result.Clear();
        service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
            new(2026, 1, 4), result.Add, 6);
        Assert.Equal(3, result.Count);
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() =>
            service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
                new(2026, 1, 4), _ => { }, 5));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal("FREQ=DAILY;COUNT=3", refusal.RecurrenceId);
        Assert.Equal(new DateOnly(2026, 1, 1), refusal.Anchor);
        Assert.Equal(new DateOnly(2026, 1, 4), refusal.RequestedRangeStart);
        Assert.Equal(new DateOnly(2026, 1, 10), refusal.RequestedRangeEnd);
        Assert.Equal(5, refusal.CandidateLimit);
        Assert.Equal(6, refusal.CandidatesExamined);
        Assert.Equal(new DateOnly(2026, 1, 2), refusal.LastEvaluatedOccurrence);
        Assert.Equal(RecurrenceBound.Count, refusal.Bound);
    }

    [Fact]
    public void EmptyPeriodsConsumeBudgetAndUntilEndsTheWalk()
    {
        var service = new InMemoryRruleExpansionService();
        var result = new List<DateOnly>();
        const string never = "FREQ=MONTHLY;BYDAY=5MO;BYMONTHDAY=1";
        service.VisitZonedOccurrences(never, new(2026, 1, 1), new(9, 0), "UTC", new(2026, 3, 31),
            new(2026, 1, 1), result.Add, 3);
        Assert.Empty(result);
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() =>
            service.VisitZonedOccurrences(never, new(2026, 1, 1), new(9, 0), "UTC", new(2026, 4, 1),
                new(2026, 1, 1), result.Add, 3));
        Assert.Equal(4, refusal.CandidatesExamined);
        Assert.Null(refusal.LastEvaluatedOccurrence);
        Assert.Equal(RecurrenceBound.None, refusal.Bound);
        service.VisitZonedOccurrences(never + ";UNTIL=20260331", new(2026, 1, 1), new(9, 0), "UTC", new(9998, 1, 1),
            new(2026, 1, 1), result.Add, 3);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("FREQ=DAILY", 3)]
    [InlineData("FREQ=DAILY;COUNT=2", 0)]
    public void OldAnchorAndCountRemainIndependentOfTheQueryLowerBound(string rule, int expected)
    {
        var result = new InMemoryRruleExpansionService().ExpandZonedOccurrences(rule, new(2018, 1, 1), new(9, 0),
            "UTC", new(2026, 1, 3), 0, 0, new(2026, 1, 1));
        Assert.Equal(expected, result.Count);
        if (expected != 0)
            Assert.Equal([new DateOnly(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3)], result);
    }
}
