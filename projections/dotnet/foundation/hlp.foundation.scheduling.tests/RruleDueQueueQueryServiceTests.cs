using Microsoft.Extensions.DependencyInjection;
using Harborline.Foundation.Scheduling;
using Harborline.Foundation.Scheduling.DependencyInjection;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class RruleDueQueueQueryServiceTests
{
    private static readonly DateOnly FrozenToday = new(2026, 7, 20);

    private static IDueQueueQueryService CreateSut()
        => new RruleDueQueueQueryService(new InMemoryRruleExpansionService());

    [Fact]
    public void Query_ExpandsOnlyActiveSchedules_AndDiffsCompletionHistory()
    {
        var active = Schedule(
            subjectRef: "asset:boiler-7",
            scheduleRef: "schedule:quarterly-service",
            rrule: "FREQ=DAILY;COUNT=4",
            startsOn: new DateOnly(2026, 7, 18));
        var inactive = Schedule(
            subjectRef: "party:inactive-subject",
            scheduleRef: "schedule:inactive",
            rrule: "FREQ=DAILY",
            startsOn: new DateOnly(2026, 7, 18),
            isActive: false);

        var result = CreateSut().Query(
            [active, inactive],
            [
                new DueQueueCompletion(
                    active.SubjectRef,
                    active.ScheduleRef,
                    new DateOnly(2026, 7, 19)),
            ],
            windowStart: new DateOnly(2026, 7, 18),
            windowEnd: new DateOnly(2026, 7, 24),
            asOf: FrozenToday);

        Assert.Collection(
            result,
            item => AssertItem(item, active, new DateOnly(2026, 7, 18), overdueDays: 2),
            item => AssertItem(item, active, new DateOnly(2026, 7, 20), overdueDays: 0),
            item => AssertItem(item, active, new DateOnly(2026, 7, 21), overdueDays: 0));
        Assert.DoesNotContain(result, item => item.ScheduleRef == inactive.ScheduleRef);
    }

    [Fact]
    public void Query_FrozenClock_OrdersMostOverdueFirst_ThenUpcomingByDueDate()
    {
        var schedules = new[]
        {
            Schedule("asset:late", "schedule:late", "FREQ=DAILY;COUNT=1", new DateOnly(2026, 7, 15)),
            Schedule("asset:today", "schedule:today", "FREQ=DAILY;COUNT=1", FrozenToday),
            Schedule("asset:future-b", "schedule:future-b", "FREQ=DAILY;COUNT=1", new DateOnly(2026, 7, 23)),
            Schedule("asset:future-a", "schedule:future-a", "FREQ=DAILY;COUNT=1", new DateOnly(2026, 7, 22)),
        };

        var result = CreateSut().Query(
            schedules,
            [],
            windowStart: new DateOnly(2026, 7, 1),
            windowEnd: new DateOnly(2026, 7, 31),
            asOf: FrozenToday);

        Assert.Equal(
            ["schedule:late", "schedule:today", "schedule:future-a", "schedule:future-b"],
            result.Select(static item => item.ScheduleRef));
        Assert.Equal([5, 0, 0, 0], result.Select(static item => item.OverdueDays));
    }

    [Fact]
    public void Query_CompletionIdentityIncludesSubjectScheduleAndDueDate()
    {
        var first = Schedule(
            "asset:first",
            "schedule:shared",
            "FREQ=DAILY;COUNT=1",
            FrozenToday);
        var second = Schedule(
            "asset:second",
            "schedule:shared",
            "FREQ=DAILY;COUNT=1",
            FrozenToday);

        var result = CreateSut().Query(
            [first, second],
            [new DueQueueCompletion(first.SubjectRef, first.ScheduleRef, FrozenToday)],
            windowStart: FrozenToday,
            windowEnd: FrozenToday,
            asOf: FrozenToday);

        var remaining = Assert.Single(result);
        Assert.Equal(second.SubjectRef, remaining.SubjectRef);
    }

    [Fact]
    public void Query_RespectsInclusiveWindowAndScheduleEnd()
    {
        var schedule = Schedule(
            "asset:bounded",
            "schedule:bounded",
            "FREQ=DAILY",
            new DateOnly(2026, 7, 1),
            endsOn: new DateOnly(2026, 7, 18));

        var result = CreateSut().Query(
            [schedule],
            [],
            windowStart: new DateOnly(2026, 7, 17),
            windowEnd: new DateOnly(2026, 7, 25),
            asOf: FrozenToday);

        Assert.Equal(
            [new DateOnly(2026, 7, 17), new DateOnly(2026, 7, 18)],
            result.Select(static item => item.DueDate));
    }

    [Fact]
    public void Query_RejectsAnInvertedWindow()
    {
        Assert.Throws<ArgumentException>(() => CreateSut().Query(
            [],
            [],
            windowStart: new DateOnly(2026, 7, 2),
            windowEnd: new DateOnly(2026, 7, 1),
            asOf: FrozenToday));
    }

    [Fact]
    public void AddFoundationScheduling_RegistersDueQueueQuery()
    {
        var services = new ServiceCollection();

        services.AddFoundationScheduling();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<RruleDueQueueQueryService>(
            provider.GetRequiredService<IDueQueueQueryService>());
    }

    private static DueQueueSchedule Schedule(
        string subjectRef,
        string scheduleRef,
        string rrule,
        DateOnly startsOn,
        DateOnly? endsOn = null,
        bool isActive = true)
        => new(
            subjectRef,
            scheduleRef,
            rrule,
            startsOn,
            endsOn,
            Timezone: "UTC",
            IsActive: isActive);

    private static void AssertItem(
        DueQueueItem item,
        DueQueueSchedule schedule,
        DateOnly dueDate,
        int overdueDays)
    {
        Assert.Equal(schedule.SubjectRef, item.SubjectRef);
        Assert.Equal(schedule.ScheduleRef, item.ScheduleRef);
        Assert.Equal(dueDate, item.DueDate);
        Assert.Equal(overdueDays, item.OverdueDays);
    }
}

