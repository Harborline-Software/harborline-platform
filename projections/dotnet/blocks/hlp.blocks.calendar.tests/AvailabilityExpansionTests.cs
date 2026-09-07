using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S3 <b>availability windows</b> (the bookable supply): recurring windows
/// expand to UTC intervals via the shared RRULE expander + timezone resolution (DST applied);
/// whole-day exceptions suppress; the supply is the first operand of free/busy.
/// </summary>
public sealed class AvailabilityExpansionTests
{
    private static readonly TenantId Acme = new("acme");

    private static IAvailabilityExpansionService NewSut()
        => new AvailabilityExpansionService(new InMemoryRruleExpansionService());

    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");

    [Fact]
    public void RecurringWeekdayWindow_ExpandsToOneIntervalPerWeekday()
    {
        // "Dr. Smith Mon-Fri 9-5" in UTC.
        var avail = ResourceAvailability.Create(Acme, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(
                anchorDate: new DateOnly(2026, 3, 2),       // a Monday
                startTime:  new TimeOnly(9, 0),
                endTime:    new TimeOnly(17, 0),
                rrule:      "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR"));

        // One full week: Mon 2026-03-02 .. Sun 2026-03-08.
        var windowStart = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = new DateTimeOffset(2026, 3, 8, 23, 59, 59, TimeSpan.Zero);

        var intervals = NewSut().Expand(avail, windowStart, windowEnd);

        // 5 weekday windows (Mon-Fri), none on the weekend.
        Assert.Equal(5, intervals.Count);
        Assert.All(intervals, i => Assert.Equal(TimeSpan.FromHours(8), i.Duration));
        // First interval is Monday 09:00-17:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 9, 0, 0, TimeSpan.Zero), intervals[0].StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 2, 17, 0, 0, TimeSpan.Zero), intervals[0].EndUtc);
    }

    [Fact]
    public void SingleDayWindow_AppliesOnlyOnItsAnchorDate()
    {
        var avail = ResourceAvailability.Create(Acme, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(
                anchorDate: new DateOnly(2026, 3, 4),
                startTime:  new TimeOnly(10, 0),
                endTime:    new TimeOnly(12, 0)));    // no rrule → one-day window

        var intervals = NewSut().Expand(avail,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero));

        var only = Assert.Single(intervals);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero), only.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 12, 0, 0, TimeSpan.Zero), only.EndUtc);
    }

    [Fact]
    public void ExceptionDate_SuppressesTheWindowOnThatDay()
    {
        var avail = ResourceAvailability.Create(Acme, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(
                new DateOnly(2026, 3, 2), new TimeOnly(9, 0), new TimeOnly(17, 0),
                "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR"))
            .AddException(new DateOnly(2026, 3, 4));   // Wednesday off (a holiday)

        var intervals = NewSut().Expand(avail,
            new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 8, 23, 59, 59, TimeSpan.Zero));

        // Mon, Tue, Thu, Fri — Wednesday is suppressed.
        Assert.Equal(4, intervals.Count);
        Assert.DoesNotContain(intervals, i => i.StartUtc.Date == new DateTime(2026, 3, 4));
    }

    [Fact]
    public void Window_KeepsWallClock_AcrossSpringForwardDst()
    {
        // America/Los_Angeles springs forward 2026-03-08 02:00 → 03:00. A 9 a.m. window keeps its
        // 9 a.m. wall-clock; its UTC instant shifts from 17:00 (PST, -8) to 16:00 (PDT, -7).
        var avail = ResourceAvailability.Create(Acme, Doctor, "America/Los_Angeles")
            .AddWindow(AvailabilityWindow.Create(
                new DateOnly(2026, 3, 6), new TimeOnly(9, 0), new TimeOnly(17, 0),
                "FREQ=DAILY"));

        var intervals = NewSut().Expand(avail,
            new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 10, 23, 59, 59, TimeSpan.Zero));

        // 2026-03-06 (PST): 09:00 local = 17:00 UTC.
        var fri = Assert.Single(intervals, i => i.StartUtc.Date == new DateTime(2026, 3, 6));
        Assert.Equal(new DateTimeOffset(2026, 3, 6, 17, 0, 0, TimeSpan.Zero), fri.StartUtc);

        // 2026-03-09 (PDT, after spring-forward): 09:00 local = 16:00 UTC.
        var mon = Assert.Single(intervals, i => i.StartUtc.Date == new DateTime(2026, 3, 9));
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 16, 0, 0, TimeSpan.Zero), mon.StartUtc);
    }

    [Fact]
    public void NoWindows_YieldsNoIntervals()
    {
        var avail = ResourceAvailability.Create(Acme, Doctor, "UTC");
        var intervals = NewSut().Expand(avail,
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty(intervals);
    }

    [Fact]
    public void TwoNonAdjacentWindows_StayTwoIntervals()
    {
        // Morning + afternoon split (lunch gap in the availability itself): 9-12 and 13-17.
        var avail = ResourceAvailability.Create(Acme, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(new DateOnly(2026, 3, 4), new TimeOnly(9, 0), new TimeOnly(12, 0)))
            .AddWindow(AvailabilityWindow.Create(new DateOnly(2026, 3, 4), new TimeOnly(13, 0), new TimeOnly(17, 0)));

        var intervals = NewSut().Expand(avail,
            new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 4, 23, 59, 59, TimeSpan.Zero));

        Assert.Equal(2, intervals.Count);
    }

    [Fact]
    public void AvailabilityWindow_RejectsInvertedSpan()
    {
        Assert.Throws<ArgumentException>(() =>
            AvailabilityWindow.Create(new DateOnly(2026, 3, 4), new TimeOnly(17, 0), new TimeOnly(9, 0)));
    }
}
