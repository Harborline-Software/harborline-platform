using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S3 <b>free/busy</b> query — THE query that makes booking and coverage work:
/// <c>free = availability windows − ALL occupancy (Bookable + Blocking + Tentative)</c>. A Blocking
/// lunch removes the slot; a Bookable appointment consumes availability; free slots are correct across
/// a window.
/// </summary>
public sealed class FreeBusyTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly TenantId Globex = new("globex");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");

    private sealed record Sut(
        IResourceAvailabilityStore Availability,
        ICalendarEventStore Events,
        IFreeBusyService FreeBusy);

    private static Sut NewSut()
    {
        var rrule = new InMemoryRruleExpansionService();
        var expansion = new CalendarEventExpansionService(rrule);
        var availStore = new InMemoryResourceAvailabilityStore();
        var availExpansion = new AvailabilityExpansionService(rrule);
        var eventStore = new InMemoryCalendarEventStore();
        var freeBusy = new FreeBusyService(availStore, availExpansion, eventStore, expansion);
        return new Sut(availStore, eventStore, freeBusy);
    }

    /// <summary>"Dr. Smith 9-5 on 2026-03-04" (UTC), a single-day window — the bookable supply.</summary>
    private static ResourceAvailability NineToFiveOn(DateOnly day, ParticipantRef resource, string tz = "UTC")
        => ResourceAvailability.Create(Acme, resource, tz)
            .AddWindow(AvailabilityWindow.Create(day, new TimeOnly(9, 0), new TimeOnly(17, 0)));

    private static CalendarEvent TimedEvent(
        TenantId tenant, ParticipantRef resource, string title,
        DateOnly day, TimeOnly start, TimeOnly end, Occupancy occupancy, string tz = "UTC")
    {
        var ev = CalendarEvent.Create(tenant, title, day, day, Actor,
            timezone: tz, startTime: start, endTime: end, occupancy: occupancy);
        ev.SetResource(resource, Actor);
        return ev;
    }

    private static DateTimeOffset Utc(DateOnly day, int hour) =>
        new(day.Year, day.Month, day.Day, hour, 0, 0, TimeSpan.Zero);

    // ----------------------------------------------------------------
    // Availability with no occupancy = the whole window is free
    // ----------------------------------------------------------------

    [Fact]
    public async Task EmptyOccupancy_FreeSlotIsTheWholeAvailabilityWindow()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        var slot = Assert.Single(fb.FreeSlots);
        Assert.Equal(Utc(day, 9), slot.StartUtc);
        Assert.Equal(Utc(day, 17), slot.EndUtc);
        Assert.Empty(fb.BusyIntervals);
    }

    // ----------------------------------------------------------------
    // A Blocking lunch removes the slot (busy-but-not-bookable)
    // ----------------------------------------------------------------

    [Fact]
    public async Task BlockingLunch_RemovesTheSlot_SplittingFreeIntoMorningAndAfternoon()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        // The doctor's lunch 12-13 = a Blocking event (busy, NOT bookable).
        var lunch = TimedEvent(Acme, Doctor, "Lunch", day, new TimeOnly(12, 0), new TimeOnly(13, 0), Occupancy.Blocking);
        await sut.Events.SaveAsync(lunch);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // Free = [9-12] and [13-17]; busy = [12-13].
        Assert.Equal(2, fb.FreeSlots.Count);
        Assert.Equal(Utc(day, 9), fb.FreeSlots[0].StartUtc);
        Assert.Equal(Utc(day, 12), fb.FreeSlots[0].EndUtc);
        Assert.Equal(Utc(day, 13), fb.FreeSlots[1].StartUtc);
        Assert.Equal(Utc(day, 17), fb.FreeSlots[1].EndUtc);

        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 12), busy.StartUtc);
        Assert.Equal(Utc(day, 13), busy.EndUtc);
    }

    // ----------------------------------------------------------------
    // A Bookable appointment consumes availability
    // ----------------------------------------------------------------

    [Fact]
    public async Task BookableAppointment_ConsumesAvailability()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        // A booked appointment 10-11 = a Bookable event consuming availability.
        var appt = TimedEvent(Acme, Doctor, "Patient appt", day, new TimeOnly(10, 0), new TimeOnly(11, 0), Occupancy.Bookable);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // Free = [9-10] and [11-17].
        Assert.Equal(2, fb.FreeSlots.Count);
        Assert.Equal(Utc(day, 9), fb.FreeSlots[0].StartUtc);
        Assert.Equal(Utc(day, 10), fb.FreeSlots[0].EndUtc);
        Assert.Equal(Utc(day, 11), fb.FreeSlots[1].StartUtc);
        Assert.Equal(Utc(day, 17), fb.FreeSlots[1].EndUtc);
    }

    // ----------------------------------------------------------------
    // Both classes are busy: a Bookable appt AND a Blocking lunch both subtract
    // ----------------------------------------------------------------

    [Fact]
    public async Task FreeBusy_SubtractsBothBookableAndBlocking()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        await sut.Events.SaveAsync(TimedEvent(Acme, Doctor, "Appt", day, new TimeOnly(10, 0), new TimeOnly(11, 0), Occupancy.Bookable));
        await sut.Events.SaveAsync(TimedEvent(Acme, Doctor, "Lunch", day, new TimeOnly(12, 0), new TimeOnly(13, 0), Occupancy.Blocking));

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // Free = [9-10], [11-12], [13-17]; busy = [10-11], [12-13].
        Assert.Equal(3, fb.FreeSlots.Count);
        Assert.Equal((Utc(day, 9), Utc(day, 10)), (fb.FreeSlots[0].StartUtc, fb.FreeSlots[0].EndUtc));
        Assert.Equal((Utc(day, 11), Utc(day, 12)), (fb.FreeSlots[1].StartUtc, fb.FreeSlots[1].EndUtc));
        Assert.Equal((Utc(day, 13), Utc(day, 17)), (fb.FreeSlots[2].StartUtc, fb.FreeSlots[2].EndUtc));
        Assert.Equal(2, fb.BusyIntervals.Count);
    }

    [Fact]
    public async Task TentativeHold_AlsoCountsAsBusy()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        await sut.Events.SaveAsync(TimedEvent(Acme, Doctor, "Tentative hold", day, new TimeOnly(14, 0), new TimeOnly(15, 0), Occupancy.Tentative));

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // A tentative hold blocks a competing booking — busy = [14-15].
        Assert.Equal(Utc(day, 14), Assert.Single(fb.BusyIntervals).StartUtc);
        Assert.DoesNotContain(fb.FreeSlots, f => f.Overlaps(TimeInterval.Of(Utc(day, 14), Utc(day, 15))));
    }

    // ----------------------------------------------------------------
    // No availability = no free slots (supply must exist first)
    // ----------------------------------------------------------------

    [Fact]
    public async Task NoAvailability_YieldsNoFreeSlots_EvenWithNoOccupancy()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        // No availability saved.

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        Assert.Empty(fb.FreeSlots);
        Assert.Empty(fb.BusyIntervals);
    }

    // ----------------------------------------------------------------
    // A cancelled appointment does not consume availability
    // ----------------------------------------------------------------

    [Fact]
    public async Task CancelledEvent_DoesNotConsumeAvailability()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor));

        var appt = TimedEvent(Acme, Doctor, "Cancelled appt", day, new TimeOnly(10, 0), new TimeOnly(11, 0), Occupancy.Bookable);
        appt.Cancel(Actor);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // The cancelled appt does not subtract — the whole 9-17 is free.
        var slot = Assert.Single(fb.FreeSlots);
        Assert.Equal(Utc(day, 9), slot.StartUtc);
        Assert.Equal(Utc(day, 17), slot.EndUtc);
    }

    // ----------------------------------------------------------------
    // Cross-tenant isolation: another tenant's occupancy/availability never leaks
    // ----------------------------------------------------------------

    [Fact]
    public async Task FreeBusy_IsTenantScoped()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, Doctor)); // Acme

        // A Globex event on the same resource ref + slot — must NOT subtract from Acme's free/busy.
        var globexAppt = TimedEvent(Globex, Doctor, "Globex appt", day, new TimeOnly(10, 0), new TimeOnly(11, 0), Occupancy.Bookable);
        await sut.Events.SaveAsync(globexAppt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // Globex's appt is invisible to Acme — the whole 9-17 stays free.
        var slot = Assert.Single(fb.FreeSlots);
        Assert.Equal(Utc(day, 9), slot.StartUtc);
        Assert.Equal(Utc(day, 17), slot.EndUtc);
    }

    // ----------------------------------------------------------------
    // An Asset resource (a room) is a first-class free/busy subject too
    // ----------------------------------------------------------------

    [Fact]
    public async Task FreeBusy_WorksForAnAssetResource()
    {
        var sut = NewSut();
        var room = ParticipantRef.Asset("asset-room-1");
        var day = new DateOnly(2026, 3, 4);
        await sut.Availability.SaveAsync(NineToFiveOn(day, room));

        await sut.Events.SaveAsync(TimedEvent(Acme, room, "Room meeting", day, new TimeOnly(9, 0), new TimeOnly(10, 0), Occupancy.Bookable));

        var fb = await sut.FreeBusy.FreeBusy(Acme, room, Utc(day, 0), Utc(day, 23));

        var slot = Assert.Single(fb.FreeSlots);
        Assert.Equal(Utc(day, 10), slot.StartUtc);
        Assert.Equal(Utc(day, 17), slot.EndUtc);
    }
}
