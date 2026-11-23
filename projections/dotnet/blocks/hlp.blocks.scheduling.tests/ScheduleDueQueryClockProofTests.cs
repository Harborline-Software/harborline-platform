using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleDueQueryClockProofTests
{
    private static DateTimeOffset I(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private sealed class CountingClock : TimeProvider
    {
        private readonly DateTimeOffset[] instants = [I("2026-07-01T10:00:00Z"), I("2026-07-01T14:00:00Z"), I("2026-07-01T18:00:00Z"), I("2026-07-01T22:00:00Z")];
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow() => instants[Reads++];
    }
    private static ScheduleDefinition Definition(ScheduleBasis? basis = null) =>
        new(null, "Inspection", new(ScheduleStartKind.Form, "inspection"), Basis: basis,
            DueDate: basis is null ? "2026-07-01T12:00:00Z" : null, Tolerance: new("PT1H", "PT1H"));
    private static DueOccurrenceRequest Request(ScheduleDefinition definition) =>
        new(new("series", "asset", definition, Timezone: "UTC"), I("2026-07-01T00:00:00Z"), I("2026-07-02T00:00:00Z"), [], []);
    private static RruleDueQueueQueryService Service(CountingClock clock) => new(new InMemoryRruleExpansionService(), new KernelClock(clock));

    [Fact(DisplayName = "scheduling-eng-14 scheduling-run-1 query-clock reads once and refreshes")]
    public void QueryReadsOnceAndRefreshes()
    {
        var clock = new CountingClock();
        var service = Service(clock);
        var request = Request(Definition());
        var first = Assert.Single(service.Query(request));
        Assert.Equal(1, clock.Reads);
        Assert.Equal("series/lone", first.OccurrenceId);
        Assert.Equal(I("2026-07-01T12:00:00Z"), first.Due);
        Assert.Equal(OccurrenceClassification.Pending, first.Classification);
        Assert.False(first.IsDue);
        Assert.Equal(TimeSpan.Zero, first.Overdue);
        Assert.Equal(new DateTime(2026, 7, 1, 10, 0, 0), first.AsOfLocal);
    }

    [Fact(DisplayName = "scheduling-eng-14 scheduling-run-1 query-clock refreshes on the second query")]
    public void SecondQueryUsesFreshInstant()
    {
        var clock = new CountingClock();
        var service = Service(clock);
        var request = Request(Definition());
        service.Query(request);
        var second = Assert.Single(service.Query(request));
        Assert.Equal(OccurrenceClassification.Late, second.Classification);
        Assert.True(second.IsDue);
        Assert.Equal(TimeSpan.FromHours(2), second.Overdue);
        Assert.Equal(new DateTime(2026, 7, 1, 14, 0, 0), second.AsOfLocal);
        Assert.Equal(2, clock.Reads);
    }

    [Fact(DisplayName = "scheduling-eng-14 scheduling-run-1 query-clock dispatches completion basis")]
    public void CompletionDispatchSucceeds()
    {
        var clock = new CountingClock();
        var request = Request(Definition(new CompletionOffsetBasis("PT1H")));
        request = request with { Series = request.Series with { FirstDue = I("2026-07-01T12:00:00Z") } };
        IReadOnlyList<SchedulingOccurrence>? result = null;
        var exception = Record.Exception(() => result = Service(clock).Query(request));
        Assert.Null(exception);
        var occurrence = Assert.Single(result!);
        Assert.Equal("series/completion/first", occurrence.OccurrenceId);
        Assert.Equal(I("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal(1, clock.Reads);
    }

    [Fact(DisplayName = "scheduling-eng-14 scheduling-run-1 query-clock validates before reading")]
    public void InvalidRequestsDoNotReadClock()
    {
        var clock = new CountingClock();
        var service = Service(clock);
        var request = Request(Definition());
        Assert.Throws<ArgumentException>(() => service.Query(request with { From = I("2026-07-02T00:00:00Z"), To = I("2026-07-01T00:00:00Z") }));
        Assert.Equal(0, clock.Reads);
        var invalid = Request(Definition(new CompletionOffsetBasis("PT1H")) with { DueDate = "2026-07-01T12:00:00Z" });
        invalid = invalid with { Series = invalid.Series with { FirstDue = I("2026-07-01T12:00:00Z") } };
        Assert.Equal("scheduling.definition.due_date_invalid", Assert.Throws<OccurrenceRefusalException>(() => service.Query(invalid)).Code);
        Assert.Equal(0, clock.Reads);

        var correctedRange = request with { From = I("2026-07-01T00:00:00Z"), To = I("2026-07-02T00:00:00Z") };
        Assert.Equal("series/lone", Assert.Single(service.Query(correctedRange)).OccurrenceId);
        Assert.Equal(1, clock.Reads);

        var correctedDueDate = invalid with { Series = invalid.Series with { Definition = invalid.Series.Definition with { DueDate = null } } };
        Assert.Equal("series/completion/first", Assert.Single(service.Query(correctedDueDate)).OccurrenceId);
        Assert.Equal(2, clock.Reads);
    }
}
