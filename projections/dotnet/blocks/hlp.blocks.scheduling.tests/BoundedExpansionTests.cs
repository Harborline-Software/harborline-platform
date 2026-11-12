using System.Diagnostics;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class BoundedExpansionTests
{
    private static DueOccurrenceRequest Request(string rule = "FREQ=DAILY") => new(
        new("series", "asset", new(null, "Inspection", new(ScheduleStartKind.Form, "inspection"),
            Basis: new FixedCalendarBasis(rule)), new(2026, 1, 1, 9, 0, 0), "UTC"),
        new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 2, 23, 59, 59, TimeSpan.Zero), [], []);

    private static IReadOnlyList<SchedulingOccurrence> Query(DueOccurrenceRequest request, IRruleExpansionService producer) =>
        new RruleDueQueueQueryService(producer, new KernelClock(TimeProvider.System)).Query(request);

    [Fact]
    public void OverridePreprocessingExhaustsTheSharedBudgetBeforeCallingProducer()
    {
        var request = Request("FREQ=DAILY;COUNT=2");
        request = request with { Series = request.Series with { Overrides = Enumerable.Range(0, 50001).ToDictionary(
            day => new DateTime(2026, 1, 1, 9, 0, 0).AddDays(day), _ => new DateTime(2026, 1, 2, 11, 0, 0)) } };
        var producer = new CountingProducer();
        var refusal = Assert.Throws<OccurrenceRefusalException>(() => Query(request, producer));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal(0, producer.Calls);
        Assert.Equal("FREQ=DAILY;COUNT=2", refusal.Data["RecurrenceId"]);
        Assert.Equal(new DateOnly(2026, 1, 1), refusal.Data["Anchor"]);
        Assert.Equal(new DateOnly(2026, 1, 1), refusal.Data["RequestedRangeStart"]);
        Assert.Equal(new DateOnly(2026, 1, 3), refusal.Data["RequestedRangeEnd"]);
        Assert.Equal(50000, refusal.Data["CandidateLimit"]);
        Assert.Equal(50001, refusal.Data["CandidatesExamined"]);
        Assert.Null(refusal.Data["LastEvaluatedOccurrence"]);
        Assert.Equal(RecurrenceBound.Count, refusal.Data["Bound"]);
    }

    [Fact]
    public void InvalidOverridesAndProducerWalkConsumeTheSameBudget()
    {
        var request = Request();
        request = request with { Series = request.Series with { Overrides = Enumerable.Range(0, 49986).ToDictionary(
            day => new DateTime(2026, 1, 1, 8, 0, 0).AddDays(day), _ => new DateTime(2026, 1, 4, 11, 0, 0)) } };
        var producer = new CountingProducer();
        var refusal = Assert.Throws<OccurrenceRefusalException>(() => Query(request, producer));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal(1, producer.Calls);
        Assert.Equal(50000, refusal.Data["CandidateLimit"]);
        Assert.Equal(50001, refusal.Data["CandidatesExamined"]);
        Assert.Equal(new DateOnly(2026, 1, 2), refusal.Data["LastEvaluatedOccurrence"]);
    }

    [Fact]
    public void RuleTextSpendsBudgetBeforeAdmissionAllocatesSelectorLists()
    {
        var request = Request("FREQ=YEARLY;BYMONTH=" + string.Join(',', Enumerable.Repeat("1", 25001)));
        var producer = new CountingProducer();
        var refusal = Assert.Throws<OccurrenceRefusalException>(() => Query(request, producer));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal(0, producer.Calls);
        Assert.Equal(50000, refusal.Data["CandidateLimit"]);
        Assert.Equal(50001, refusal.Data["CandidatesExamined"]);
        Assert.Null(refusal.Data["LastEvaluatedOccurrence"]);
        Assert.Null(refusal.Data["Bound"]);
    }

    [Theory]
    [InlineData("FREQ=WEEKLY;BYDAY=MO", "2026-01-06T09:00:00", 0)]
    [InlineData("FREQ=DAILY;COUNT=3", "2026-01-10T09:00:00", 1)]
    [InlineData("FREQ=DAILY;UNTIL=20260103", "2026-01-10T09:00:00", 1)]
    public void CorrectlyTimedOverrideMustNameAnInstanceOfTheRule(string rule, string original, int expected)
    {
        var request = Request(rule);
        request = request with { Series = request.Series with { Overrides = new Dictionary<DateTime, DateTime>
        {
            [DateTime.Parse(original, System.Globalization.CultureInfo.InvariantCulture)] = new(2026, 1, 2, 11, 0, 0),
        } } };
        var producer = new CountingProducer();
        var result = Query(request, producer);
        Assert.Equal(1, producer.Calls);
        Assert.Equal(expected, result.Count);
        if (expected == 1) Assert.Equal(new DateTime(2026, 1, 2, 9, 0, 0), result[0].RecurrenceLocal);
    }

    [Fact]
    public void WidestWindowIsEmptyPromptlyAndOneDayWiderRefusesBeforeExpansion()
    {
        var request = Request("FREQ=MONTHLY;BYDAY=5MO;BYMONTHDAY=1");
        request = request with { To = request.From.AddDays(3659) };
        var producer = new CountingProducer();
        var watch = Stopwatch.StartNew();
        Assert.Empty(Query(request, producer));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal(1, producer.Calls);
        var refusal = Assert.Throws<OccurrenceRefusalException>(() => Query(request with { To = request.To.AddDays(1) }, producer));
        Assert.Equal("scheduling.occurrence.window_maximum_exceeded", refusal.Code);
        Assert.Equal(1, producer.Calls);
    }

    [Fact]
    public void FutureOriginalMovedIntoWindowUsesOneWalkAndKeepsIdentity()
    {
        var request = Request();
        request = request with { Series = request.Series with { Overrides = new Dictionary<DateTime, DateTime>
        {
            [new(2026, 1, 10, 9, 0, 0)] = new(2026, 1, 2, 11, 0, 0),
            [new(2026, 1, 11, 8, 0, 0)] = new(2026, 1, 2, 12, 0, 0),
        } } };
        var producer = new CountingProducer();
        var result = Query(request, producer);
        Assert.Equal(1, producer.Calls);
        Assert.Equal(new DateOnly(2026, 1, 10), producer.End);
        Assert.Equal([new DateTime(2026, 1, 2, 9, 0, 0), new(2026, 1, 10, 9, 0, 0)], result.Select(o => o.RecurrenceLocal));
        Assert.Equal("series/2026-01-10T09%3A00%3A00.0000000", result[1].OccurrenceId);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 11, 0, 0, TimeSpan.Zero), result[1].Due);
    }

    [Fact]
    public void OverrideMovedOutDoesNotExtendWalk()
    {
        var request = Request();
        request = request with { Series = request.Series with { Overrides = new Dictionary<DateTime, DateTime>
        {
            [new(9998, 1, 10, 9, 0, 0)] = new(2026, 1, 4, 11, 0, 0),
        } } };
        var producer = new CountingProducer();
        Assert.Single(Query(request, producer));
        Assert.Equal(1, producer.Calls);
        Assert.Equal(new DateOnly(2026, 1, 3), producer.End);
    }

    [Fact]
    public void FarFutureOriginalMovedInRefusesWithBudgetCode()
    {
        var request = Request();
        request = request with { Series = request.Series with { Overrides = new Dictionary<DateTime, DateTime>
        {
            [new(9998, 1, 10, 9, 0, 0)] = new(2026, 1, 2, 11, 0, 0),
        } } };
        var refusal = Assert.Throws<OccurrenceRefusalException>(() => Query(request, new InMemoryRruleExpansionService()));
        Assert.Equal("scheduling.occurrence.expansion_budget_exceeded", refusal.Code);
        Assert.Equal(50000, refusal.Data["CandidateLimit"]);
        Assert.Equal(50001, refusal.Data["CandidatesExamined"]);
    }

    private sealed class CountingProducer : IRruleExpansionService
    {
        private readonly InMemoryRruleExpansionService inner = new();
        public int Calls { get; private set; }
        public DateOnly? End { get; private set; }
        public void VisitZonedOccurrences(string rrule, DateOnly start, TimeOnly startTime, string timezone,
            DateOnly end, DateOnly requestedRangeStart, Action<DateOnly> visit, int candidateLimit = 50000,
            RruleExpansionBudget? budget = null)
        {
            Calls++;
            End = end;
            inner.VisitZonedOccurrences(rrule, start, startTime, timezone, end, requestedRangeStart, visit, candidateLimit, budget);
        }
        public IReadOnlyList<DateOnly> ExpandOccurrences(string rrule, DateOnly start, DateOnly? end, int lookaheadDays,
            int leadDays, DateOnly today, string timezone) => throw new NotSupportedException();
        public IReadOnlyList<DateOnly> ExpandZonedOccurrences(string rrule, DateOnly start, TimeOnly startTime,
            string timezone, DateOnly? end, int lookaheadDays, int leadDays, DateOnly today)
        {
            Calls++;
            End = end;
            return inner.ExpandZonedOccurrences(rrule, start, startTime, timezone, end, lookaheadDays, leadDays, today);
        }
    }
}
