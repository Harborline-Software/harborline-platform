using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class OccurrenceGenerationTests
{
    private static DateTimeOffset I(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static DateTime L(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture);
    private static ScheduleDefinition Definition(ScheduleBasis? basis = null, ScheduleWindow? window = null)
        => new(null, "Inspection", new(ScheduleStartKind.Form, "inspection"), Basis: basis, Window: window,
            DueDate: basis is null ? "2026-07-01T12:00:00Z" : null, Tolerance: new("PT1H", "PT1H"));
    private static DueOccurrenceRequest Request(ScheduleDefinition definition)
        => new(new("series", "asset", definition), I("2024-01-01T00:00:00Z"), I("2027-12-31T23:59:59Z"), [], []);
    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
    private static IReadOnlyList<SchedulingOccurrence> Query(DueOccurrenceRequest request, string now = "2026-07-01T20:00:00Z")
        => new RruleDueQueueQueryService(new InMemoryRruleExpansionService(), new KernelClock(new FixedClock(I(now)))).Query(request);

    [Fact(DisplayName = "scheduling-eng-3, scheduling-run-1: a lone due date is generated without a solver, window or void")]
    public void LoneDueDateHasOwnDuePointAndStableIdentity()
    {
        var request = Request(Definition());
        var occurrence = Assert.Single(Query(request));
        Assert.Equal(I("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal("series/lone", occurrence.OccurrenceId);
        Assert.Null(occurrence.Earliest);
        Assert.Null(occurrence.Latest);
        Assert.True(occurrence.IsDue);
        Assert.Equal(TimeSpan.FromHours(8), occurrence.Overdue);
        Assert.Equal(OccurrenceClassification.Late, occurrence.Classification);
        Assert.Equal(occurrence, Assert.Single(Query(request)));
    }

    // Literal instants instantiate the slice's Due/window/void table, independently of the generator.
    [Theory(DisplayName = "scheduling-eng-6, scheduling-eng-7: tolerance classifies on time, early and late independently of the window")]
    [InlineData("2026-07-01T12:30:00Z", OccurrenceClassification.OnTime)]
    [InlineData("2026-07-01T10:00:00Z", OccurrenceClassification.Early)]
    [InlineData("2026-07-01T14:00:00Z", OccurrenceClassification.Late)]
    [InlineData("2026-07-01T08:00:00Z", OccurrenceClassification.Void)]
    [InlineData("2026-07-01T16:00:00Z", OccurrenceClassification.Void)]
    [InlineData("2026-07-01T09:00:00Z", OccurrenceClassification.Early)]
    [InlineData("2026-07-01T15:00:00Z", OccurrenceClassification.Late)]
    public void CompletionClassificationUsesLiteralWindow(string completed, OccurrenceClassification expected)
    {
        var request = Request(Definition(window: new("2026-07-01T09:00:00Z", "2026-07-01T15:00:00Z")));
        request = request with { Completions = [new("done", "series", "series/lone", I(completed))] };
        var occurrence = Assert.Single(Query(request));
        Assert.Equal(I("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal(I("2026-07-01T09:00:00Z"), occurrence.Earliest);
        Assert.Equal(I("2026-07-01T15:00:00Z"), occurrence.Latest);
        Assert.Equal(expected, occurrence.Classification);
        Assert.False(occurrence.IsDue);
    }

    [Theory(DisplayName = "scheduling-eng-7: no completion is void only after latest, never merely after late tolerance")]
    [InlineData("2026-07-01T14:00:00Z", OccurrenceClassification.Late)]
    [InlineData("2026-07-01T15:00:00Z", OccurrenceClassification.Late)]
    [InlineData("2026-07-01T15:00:01Z", OccurrenceClassification.Void)]
    public void UncompletedWindowClosesAfterLatest(string now, OccurrenceClassification expected)
        => Assert.Equal(expected, Assert.Single(Query(Request(Definition(window: new("2026-07-01T09:00:00Z", "2026-07-01T15:00:00Z"))), now)).Classification);

    [Theory(DisplayName = "scheduling-ck-24, scheduling-eng-14: kernel instant is converted separately for Auckland and Honolulu")]
    [InlineData("Pacific/Auckland", "2026-07-02T08:00:00", "2026-07-01T21:00:00Z")]
    [InlineData("Pacific/Honolulu", "2026-07-01T10:00:00", "2026-07-01T19:00:00Z")]
    public void KernelClockAndSeriesZoneDecideDue(string zone, string localNow, string due)
    {
        var request = Request(Definition(new FixedCalendarBasis("FREQ=DAILY;COUNT=2")));
        request = request with { Series = request.Series with { LocalStart = L("2026-07-01T09:00:00"), Timezone = zone } };
        var occurrences = Query(request);
        Assert.All(occurrences, o => Assert.Equal(L(localNow), o.AsOfLocal));
        var occurrence = occurrences.Single(o => o.Due == I(due));
        Assert.Equal(zone == "Pacific/Honolulu", occurrence.IsDue);
        Assert.Equal(zone == "Pacific/Honolulu" ? TimeSpan.FromHours(1) : TimeSpan.Zero, occurrence.Overdue);
    }

    [Theory(DisplayName = "scheduling-ck-24: fixed calendar refuses a missing start or time zone")]
    [InlineData(null, "UTC")]
    [InlineData("2026-07-01T09:00:00", null)]
    [InlineData("2026-07-01T09:00:00", "unknown/zone")]
    public void MissingSeriesFactsRefuse(string? start, string? zone)
    {
        var request = Request(Definition(new FixedCalendarBasis("FREQ=DAILY")));
        request = request with { Series = request.Series with { LocalStart = start is null ? null : L(start), Timezone = zone } };
        Assert.Equal("scheduling.occurrence.series_start_and_zone_required", Assert.Throws<OccurrenceRefusalException>(() => Query(request)).Code);
    }

    [Fact(DisplayName = "scheduling-eng-4: EXDATE suppresses and RECURRENCE-ID moves an occurrence while retaining its local identity")]
    public void FixedCalendarOwnsExceptionsAndIdentity()
    {
        var request = Request(Definition(new FixedCalendarBasis("FREQ=DAILY;COUNT=3")));
        request = request with { Series = request.Series with
        {
            LocalStart = L("2026-07-01T09:00:00"), Timezone = "UTC",
            ExceptionDates = new HashSet<DateTime> { L("2026-07-02T09:00:00") },
            Overrides = new Dictionary<DateTime, DateTime> { [L("2026-07-03T09:00:00")] = L("2026-07-04T11:00:00") },
        } };
        var occurrences = Query(request);
        Assert.Equal([I("2026-07-01T09:00:00Z"), I("2026-07-04T11:00:00Z")], occurrences.Select(o => o.Due));
        Assert.Equal("series/2026-07-03T09%3A00%3A00.0000000", occurrences[1].OccurrenceId);
        Assert.Equal(occurrences.Select(o => o.OccurrenceId), Query(request).Select(o => o.OccurrenceId));
        var changedZone = Query(request with { Series = request.Series with { Timezone = "Pacific/Auckland" } });
        Assert.Equal(occurrences.Select(o => o.OccurrenceId), changedZone.Select(o => o.OccurrenceId));
        var clipped = Query(request with { From = I("2026-07-04T00:00:00Z"), To = I("2026-07-04T23:00:00Z") });
        Assert.Equal(occurrences[1].OccurrenceId, Assert.Single(clipped).OccurrenceId);
    }

    [Theory(DisplayName = "scheduling-ck-25: month and year durations remain calendar-exact at the series wall-clock time")]
    [InlineData("2026-01-31T12:00:00Z", "P1M", "2026-02-28T12:00:00Z", "2026-02-28T11:00:00Z", "2026-02-28T13:00:00Z")]
    [InlineData("2024-02-29T12:00:00Z", "P1Y", "2025-02-28T12:00:00Z", "2025-02-28T11:00:00Z", "2025-02-28T13:00:00Z")]
    [InlineData("2026-03-07T17:00:00Z", "P1D", "2026-03-08T16:00:00Z", "2026-03-08T15:00:00Z", "2026-03-08T17:00:00Z")]
    [InlineData("2026-03-07T17:00:00Z", "PT24H", "2026-03-08T17:00:00Z", "2026-03-08T16:00:00Z", "2026-03-08T18:00:00Z")]
    public void CalendarDurationHasLiteralExpectedInstant(string completed, string offset, string expected, string earliest, string latest)
    {
        var request = Request(Definition(new CompletionOffsetBasis(offset), new("-PT1H", "PT1H")));
        request = request with { Series = request.Series with { Timezone = "America/New_York" },
            Completions = [new("done", "series", "prior", I(completed))] };
        var occurrence = Assert.Single(Query(request, "2026-12-31T00:00:00Z"));
        Assert.Equal(I(expected), occurrence.Due);
        Assert.Equal(I(earliest), occurrence.Earliest);
        Assert.Equal(I(latest), occurrence.Latest);
        Assert.Equal("series/completion/done", occurrence.OccurrenceId);
        Assert.Equal(occurrence.OccurrenceId, Assert.Single(Query(request, "2026-12-31T00:00:00Z")).OccurrenceId);
    }

    [Fact]
    public void CalendarOffsetUsesTheCompletionOffsetWhenNoNamedZoneIsDeclared()
    {
        var request = Request(Definition(new CompletionOffsetBasis("P1M")));
        request = request with { Completions = [new("done", "series", "prior", I("2026-03-01T00:30:00+14:00"))] };
        Assert.Equal(I("2026-03-31T10:30:00Z"), Assert.Single(Query(request)).Due);
    }

    [Theory]
    [InlineData("-P1D", "P2D", "2026-07-01T12:30:00Z", OccurrenceClassification.OnTime)]
    [InlineData("-P1D", "P2D", "2026-07-01T10:00:00Z", OccurrenceClassification.Early)]
    [InlineData("-P1D", "P2D", "2026-07-01T14:00:00Z", OccurrenceClassification.Late)]
    [InlineData("-P1D", "P2D", "2026-06-29T12:00:00Z", OccurrenceClassification.Void)]
    public void RecurringWindowHasLiteralBoundsAndCompletionClassification(string from, string to, string completed, OccurrenceClassification expected)
    {
        var request = Request(Definition(new FixedCalendarBasis("FREQ=DAILY;COUNT=1"), new(from, to)));
        request = request with { Series = request.Series with { LocalStart = L("2026-07-01T12:00:00"), Timezone = "UTC" },
            Completions = [new("done", "series", "series/2026-07-01T12%3A00%3A00.0000000", I(completed))] };
        var occurrence = Assert.Single(Query(request));
        Assert.Equal(I("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal(I("2026-06-30T12:00:00Z"), occurrence.Earliest);
        Assert.Equal(I("2026-07-03T12:00:00Z"), occurrence.Latest);
        Assert.Equal(expected, occurrence.Classification);
    }

    [Fact]
    public void QueryRefusesGenerationInputsThatCouldOtherwiseBypassAdmission()
    {
        var request = Request(Definition(new FixedCalendarBasis("FREQ=DAILY")) with { DueDate = "2026-07-01T12:00:00Z" });
        Assert.Equal("scheduling.definition.due_date_invalid", Assert.Throws<OccurrenceRefusalException>(() => Query(request)).Code);
        var inverted = Request(Definition()) with { From = I("2027-01-01T00:00:00Z"), To = I("2026-01-01T00:00:00Z") };
        Assert.Throws<ArgumentException>(() => Query(inverted));
    }

    [Fact]
    public void CompletionOffsetRequiresFirstDueAndUsesItUntilACompletionExists()
    {
        var request = Request(Definition(new CompletionOffsetBasis("P1W")));
        Assert.Equal("scheduling.occurrence.first_due_required", Assert.Throws<OccurrenceRefusalException>(() => Query(request)).Code);
        request = request with { Series = request.Series with { FirstDue = I("2026-07-03T12:00:00Z") } };
        Assert.Equal(I("2026-07-03T12:00:00Z"), Assert.Single(Query(request)).Due);
        Assert.Equal("series/completion/first", Assert.Single(Query(request)).OccurrenceId);
    }

    [Fact]
    public void ThresholdCrossRecoverCrossKeepsOneOpenOccurrenceUntilCompletion()
    {
        var request = Request(Definition(new ThresholdBasis("hours", ThresholdCrossing.Above, 100)));
        Assert.Equal("scheduling.occurrence.reading_required", Assert.Throws<OccurrenceRefusalException>(() => Query(request)).Code);
        request = request with { Readings = [new("a", "series", "hours", I("2026-07-01T08:00:00Z"), 101),
            new("b", "series", "hours", I("2026-07-01T09:00:00Z"), 90), new("c", "series", "hours", I("2026-07-01T10:00:00Z"), 102)] };
        var first = Assert.Single(Query(request));
        Assert.Equal(I("2026-07-01T08:00:00Z"), first.Due);
        Assert.Equal("series/threshold/a/initial", first.OccurrenceId);
        Assert.Equal(first.OccurrenceId, Assert.Single(Query(request)).OccurrenceId);
        request = request with { Completions = [new("done", "series", first.OccurrenceId, I("2026-07-01T11:00:00Z"))] };
        var occurrences = Query(request);
        Assert.Equal([I("2026-07-01T08:00:00Z"), I("2026-07-01T11:00:00Z")], occurrences.Select(o => o.Due));
        Assert.Equal("series/threshold/c/done", occurrences[1].OccurrenceId);
        Assert.Equal(occurrences.Select(o => o.OccurrenceId), Query(request).Select(o => o.OccurrenceId));
    }

    [Fact]
    public void ThresholdCompletionWhileRecoveredWaitsForNextCrossing()
    {
        var request = Request(Definition(new ThresholdBasis("hours", ThresholdCrossing.Below, 100)));
        request = request with { Readings = [new("a", "series", "hours", I("2026-07-01T08:00:00Z"), 99),
            new("b", "series", "hours", I("2026-07-01T09:00:00Z"), 110), new("c", "series", "hours", I("2026-07-01T12:00:00Z"), 98)],
            Completions = [new("done", "series", "series/threshold/a/initial", I("2026-07-01T11:00:00Z"))] };
        Assert.Equal([I("2026-07-01T08:00:00Z"), I("2026-07-01T12:00:00Z")], Query(request).Select(o => o.Due));
    }
}
