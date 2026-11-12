using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class ExpansionBudgetTests
{
    [Fact]
    public void RepeatedSelectorsRemainOneDateWithinTheExactInputAndWalkBudget()
    {
        var result = new List<DateOnly>();
        new InMemoryRruleExpansionService().VisitZonedOccurrences("FREQ=YEARLY;BYMONTH=1,1", new(2026, 1, 1), new(9, 0),
            "UTC", new(2026, 1, 1), new(2026, 1, 1), result.Add, 25);
        Assert.Equal([new DateOnly(2026, 1, 1)], result);
    }

    [Fact]
    public void SharedBudgetCannotBeAttachedToADifferentExpansion()
    {
        var budget = new RruleExpansionBudget("FREQ=DAILY", new(2026, 1, 1), new(2026, 1, 1), new(2026, 1, 2));
        var refusal = Assert.Throws<ArgumentException>(() => new InMemoryRruleExpansionService().VisitZonedOccurrences(
            "FREQ=DAILY", new(2026, 1, 2), new(9, 0), "UTC", new(2026, 1, 2), new(2026, 1, 1), _ => { }, budget: budget));
        Assert.Equal("budget", refusal.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefaultListPathHasNoWorkBudget(bool zoned)
    {
        var service = new InMemoryRruleExpansionService();
        var result = zoned
            ? service.ExpandZonedOccurrences("FREQ=DAILY", new(2026, 1, 1), new(9, 0), "UTC", new(2130, 1, 3), 0, 0, new(2130, 1, 1))
            : service.ExpandOccurrences("FREQ=DAILY", new(2026, 1, 1), new(2130, 1, 3), 0, 0, new(2130, 1, 1), "UTC");
        Assert.Equal([new DateOnly(2130, 1, 1), new(2130, 1, 2), new(2130, 1, 3)], result);
        Assert.Throws<RruleExpansionBudgetExceededException>(() => service.VisitZonedOccurrences("FREQ=DAILY",
            new(2026, 1, 1), new(9, 0), "UTC", new(2130, 1, 3), new(2130, 1, 1), _ => { }, 50000));
    }

    [Theory]
    [InlineData(false, "Asia/Tokyo", "99991231T235959Z", 1)]
    [InlineData(true, "Asia/Tokyo", "99991231T235959Z", 1)]
    [InlineData(false, "America/Los_Angeles", "00010101T000000Z", 0)]
    [InlineData(true, "America/Los_Angeles", "00010101T000000Z", 0)]
    public void UntilOutsideLocalDateRangeDoesNotOverflow(bool visitor, string zone, string until, int expected)
    {
        var service = new InMemoryRruleExpansionService();
        var result = new List<DateOnly>();
        if (visitor)
            service.VisitZonedOccurrences("FREQ=DAILY;UNTIL=" + until, new(2026, 1, 1), new(9, 0), zone,
                new(2026, 1, 1), new(2026, 1, 1), result.Add);
        else
            result.AddRange(service.ExpandZonedOccurrences("FREQ=DAILY;UNTIL=" + until, new(2026, 1, 1), new(9, 0), zone,
                new(2026, 1, 1), 0, 0, new(2026, 1, 1)));
        Assert.Equal(expected, result.Count);
        if (expected == 1) Assert.Equal(new DateOnly(2026, 1, 1), result[0]);
    }

    [Fact]
    public void SelectorTextExhaustsBudgetBeforeAnyCandidateIsVisited()
    {
        var visits = 0;
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() =>
            new InMemoryRruleExpansionService().VisitZonedOccurrences(
                "FREQ=YEARLY;BYMONTH=" + string.Join(',', Enumerable.Repeat("1", 50)), new(2026, 1, 1), new(9, 0),
                "UTC", new(2026, 1, 1), new(2026, 1, 1), _ => visits++, 40));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal(0, visits);
        Assert.Equal(40, refusal.CandidateLimit);
        Assert.Equal(41, refusal.CandidatesExamined);
        Assert.Null(refusal.LastEvaluatedOccurrence);
        Assert.Null((RecurrenceBound?)refusal.Bound);
    }

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
        // Eighteen rule characters, three daily periods and three matched dates cost twenty-four units.
        service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
            new(2026, 1, 4), result.Add, 25);
        Assert.Equal([new DateOnly(2026, 1, 1), new(2026, 1, 2), new(2026, 1, 3)], result);
        result.Clear();
        service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
            new(2026, 1, 4), result.Add, 24);
        Assert.Equal(3, result.Count);
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() =>
            service.VisitZonedOccurrences("FREQ=DAILY;COUNT=3", anchor, new(9, 0), "UTC", new(2026, 1, 10),
                new(2026, 1, 4), _ => { }, 23));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal("FREQ=DAILY;COUNT=3", refusal.RecurrenceId);
        Assert.Equal(new DateOnly(2026, 1, 1), refusal.Anchor);
        Assert.Equal(new DateOnly(2026, 1, 4), refusal.RequestedRangeStart);
        Assert.Equal(new DateOnly(2026, 1, 10), refusal.RequestedRangeEnd);
        Assert.Equal(23, refusal.CandidateLimit);
        Assert.Equal(24, refusal.CandidatesExamined);
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
            new(2026, 1, 1), result.Add, 38);
        Assert.Empty(result);
        var refusal = Assert.Throws<RruleExpansionBudgetExceededException>(() =>
            service.VisitZonedOccurrences(never, new(2026, 1, 1), new(9, 0), "UTC", new(2026, 4, 1),
                new(2026, 1, 1), result.Add, 38));
        Assert.Equal(39, refusal.CandidatesExamined);
        Assert.Null(refusal.LastEvaluatedOccurrence);
        Assert.Equal(RecurrenceBound.None, refusal.Bound);
        service.VisitZonedOccurrences(never + ";UNTIL=20260331", new(2026, 1, 1), new(9, 0), "UTC", new(9998, 1, 1),
            new(2026, 1, 1), result.Add, 53);
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
