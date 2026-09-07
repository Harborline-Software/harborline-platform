using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S1 time-of-day + timezone/DST projection
/// (<see cref="ICalendarEventExpansionService.ExpandInstants"/>): each date-granular occurrence is
/// resolved to a real UTC <see cref="DateTimeOffset"/> by interpreting the event's wall-clock
/// <c>StartTime</c>/<c>EndTime</c> in its IANA <c>Timezone</c> on the occurrence date, with DST
/// applied. The hard case — a recurring 9 a.m. event across a spring-forward / fall-back boundary —
/// is the headline test.
/// </summary>
public sealed class CalendarEventInstantExpansionTests
{
    private static readonly TenantId Tenant = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private const string LA = "America/Los_Angeles";

    private static ICalendarEventExpansionService NewSut()
        => new CalendarEventExpansionService(new InMemoryRruleExpansionService());

    private static DateTimeOffset Utc(int y, int mo, int d, int h, int mi)
        => new(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc), TimeSpan.Zero);

    // ----------------------------------------------------------------
    // Time-of-day occurrences round-trip through the instant expansion
    // ----------------------------------------------------------------

    [Fact]
    public void TimedOccurrence_RoundTripsTimeOfDay_InUtc()
    {
        // A 9:00–9:30 single appointment in UTC. ExpandInstants must produce that exact instant pair
        // (a 30-min appointment slot, not just a date).
        var ev = CalendarEvent.Create(
            Tenant, "Intake", new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 10), Actor,
            timezone: "UTC",
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 2, 1, 0, 0), Utc(2026, 2, 28, 23, 59));

        var one = Assert.Single(occ);
        Assert.Equal(Utc(2026, 2, 10, 9, 0), one.StartUtc);
        Assert.Equal(Utc(2026, 2, 10, 9, 30), one.EndUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), one.EndUtc - one.StartUtc); // 30-min slot preserved
    }

    [Fact]
    public void RecurringTimedEvent_ExpandsToTimedInstants()
    {
        // Weekly Monday 9:00 (LA) — each occurrence is a real instant, not a date.
        var anchor = new DateOnly(2026, 1, 5); // Monday
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: LA,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 1, 1, 0, 0), Utc(2026, 1, 31, 23, 59));

        // 4 Mondays in Jan, all at 09:00 LA = 17:00Z (PST, UTC-8 in January).
        Assert.Equal(4, occ.Count);
        Assert.All(occ, o => Assert.Equal(17, o.StartUtc.Hour));
        Assert.All(occ, o => Assert.Equal(TimeSpan.FromMinutes(30), o.EndUtc - o.StartUtc));
    }

    // ----------------------------------------------------------------
    // DST boundary — the classic edge: a recurring 9 a.m. event keeps its
    // 9 a.m. WALL-CLOCK across spring-forward and fall-back; its UTC instant shifts an hour.
    // ----------------------------------------------------------------

    [Fact]
    public void Dst_SpringForward_RecurringNineAm_KeepsWallClock_UtcShiftsAnHour()
    {
        // Daily 9:00 (LA). US spring-forward 2026 = Sun 2026-03-08 (02:00 → 03:00, PST→PDT).
        // Before: 09:00 LA = 17:00Z (UTC-8 / PST). After: 09:00 LA = 16:00Z (UTC-7 / PDT).
        var ev = CalendarEvent.Create(
            Tenant, "Daily 9am", new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 6), Actor,
            rrule: "FREQ=DAILY", timezone: LA,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 0));

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 3, 6, 0, 0), Utc(2026, 3, 10, 23, 59));

        DateTimeOffset On(int day) => occ.Single(o => o.StartUtc.UtcDateTime.Date == new DateTime(2026, 3, day)).StartUtc;

        // Mar 7 (before): PST → 17:00Z. Mar 9 (after): PDT → 16:00Z. Same 9 a.m. wall-clock both days.
        Assert.Equal(Utc(2026, 3, 7, 17, 0), On(7));
        Assert.Equal(Utc(2026, 3, 9, 16, 0), On(9));
        // The instant shifted exactly one hour earlier in UTC across the spring-forward boundary.
        Assert.Equal(TimeSpan.FromHours(-1), On(9) - On(7) - TimeSpan.FromDays(2));
    }

    [Fact]
    public void Dst_FallBack_RecurringNineAm_KeepsWallClock_UtcShiftsAnHour()
    {
        // Daily 9:00 (LA). US fall-back 2026 = Sun 2026-11-01 (02:00 → 01:00, PDT→PST).
        // Before: 09:00 LA = 16:00Z (UTC-7 / PDT). After: 09:00 LA = 17:00Z (UTC-8 / PST).
        var ev = CalendarEvent.Create(
            Tenant, "Daily 9am", new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 30), Actor,
            rrule: "FREQ=DAILY", timezone: LA,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 0));

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 10, 30, 0, 0), Utc(2026, 11, 3, 23, 59));

        DateTimeOffset On(int month, int day)
            => occ.Single(o => o.StartUtc.UtcDateTime.Date == new DateTime(2026, month, day)).StartUtc;

        Assert.Equal(Utc(2026, 10, 31, 16, 0), On(10, 31)); // PDT
        Assert.Equal(Utc(2026, 11, 2, 17, 0), On(11, 2));   // PST
    }

    [Fact]
    public void Dst_SpringForwardGap_InvalidLocalTime_SnapsForward_DoesNotThrow()
    {
        // 02:30 on spring-forward day does NOT exist (the 02:00→03:00 hour is skipped). A 02:30
        // event must not throw — it snaps forward by the DST delta to 03:30 LA = 10:30Z (PDT, UTC-7).
        var ev = CalendarEvent.Create(
            Tenant, "Odd hour", new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), Actor,
            timezone: LA,
            startTime: new TimeOnly(2, 30), endTime: new TimeOnly(2, 30));

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 3, 8, 0, 0), Utc(2026, 3, 8, 23, 59));

        var one = Assert.Single(occ);
        // 03:30 PDT (UTC-7) = 10:30Z — the snapped-forward instant.
        Assert.Equal(Utc(2026, 3, 8, 10, 30), one.StartUtc);
    }

    // ----------------------------------------------------------------
    // Occurrence-level edits carry into the instant projection
    // ----------------------------------------------------------------

    [Fact]
    public void Override_RetimingOneOccurrence_AppliesInInstantExpansion()
    {
        // Weekly Monday 9:00 LA; the Jan 19 occurrence is re-timed to 8:00 (a one-off early start).
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: LA,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 1, 19),
                NewStart     = new DateOnly(2026, 1, 19),
                NewStartTime = new TimeOnly(8, 0),
                NewEndTime   = new TimeOnly(8, 30),
            },
            Actor);

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 1, 1, 0, 0), Utc(2026, 1, 31, 23, 59));

        var overridden = Assert.Single(occ, o => o.IsOverride);
        // 08:00 LA in January = 16:00Z (PST). The others are at 17:00Z (09:00 PST).
        Assert.Equal(16, overridden.StartUtc.Hour);
        Assert.All(occ.Where(o => !o.IsOverride), o => Assert.Equal(17, o.StartUtc.Hour));
    }

    [Fact]
    public void Exdate_DropsOccurrence_InInstantExpansion()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: LA, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 1, 1, 0, 0), Utc(2026, 1, 31, 23, 59));

        Assert.Equal(3, occ.Count); // 12th cancelled
        Assert.DoesNotContain(occ, o => o.StartUtc.UtcDateTime.Date == new DateTime(2026, 1, 12));
    }

    [Fact]
    public void CancelledWholeSeries_YieldsNoInstants()
    {
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: LA, startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));
        ev.Cancel(Actor);

        Assert.Empty(NewSut().ExpandInstants(ev, Utc(2026, 1, 1, 0, 0), Utc(2026, 1, 31, 23, 59)));
    }

    // ----------------------------------------------------------------
    // Backward-compat: an all-day (midnight) event still resolves cleanly
    // ----------------------------------------------------------------

    [Fact]
    public void AllDayEvent_DefaultsToMidnight_StillResolves()
    {
        // No times given → S0 all-day semantics (00:00). The instant is local-midnight in UTC.
        var ev = CalendarEvent.Create(
            Tenant, "Holiday", new DateOnly(2026, 7, 4), new DateOnly(2026, 7, 4), Actor, timezone: LA);

        var occ = NewSut().ExpandInstants(ev, Utc(2026, 7, 1, 0, 0), Utc(2026, 7, 31, 23, 59));

        var one = Assert.Single(occ);
        // Midnight Jul 4 LA (PDT, UTC-7) = 07:00Z.
        Assert.Equal(Utc(2026, 7, 4, 7, 0), one.StartUtc);
    }

    [Fact]
    public void Create_SingleDay_EndTimeBeforeStartTime_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CalendarEvent.Create(
                Tenant, "Bad times", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor,
                startTime: new TimeOnly(10, 0), endTime: new TimeOnly(9, 0)));
    }
}
