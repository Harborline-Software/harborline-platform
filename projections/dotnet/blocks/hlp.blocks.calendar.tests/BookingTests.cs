using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S3 <b>booking</b> path (Direction A): book a demand into a free slot
/// (create a Bookable event consuming availability) + enforce <b>no-double-book</b> on the resource.
/// A patient into a doctor's free slot.
/// </summary>
public sealed class BookingTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private static readonly ParticipantRef Patient = ParticipantRef.Party("party-patient");

    private sealed record Sut(
        IResourceAvailabilityStore Availability,
        ICalendarEventStore Events,
        IFreeBusyService FreeBusy,
        IBookingService Booking);

    private static Sut NewSut()
    {
        var rrule = new InMemoryRruleExpansionService();
        var expansion = new CalendarEventExpansionService(rrule);
        var availStore = new InMemoryResourceAvailabilityStore();
        var availExpansion = new AvailabilityExpansionService(rrule);
        var eventStore = new InMemoryCalendarEventStore();
        var freeBusy = new FreeBusyService(availStore, availExpansion, eventStore, expansion);
        var booking = new BookingService(
            freeBusy, availStore, availExpansion, eventStore, new DefaultPaddingPolicy(EventPadding.None));
        return new Sut(availStore, eventStore, freeBusy, booking);
    }

    private static readonly DateOnly Day = new(2026, 3, 4);

    private static DateTimeOffset Utc(int hour) => new(2026, 3, 4, hour, 0, 0, TimeSpan.Zero);

    private static async Task SeedNineToFive(Sut sut, ParticipantRef resource, string tz = "UTC")
        => await sut.Availability.SaveAsync(
            ResourceAvailability.Create(Acme, resource, tz)
                .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(17, 0))));

    // ----------------------------------------------------------------
    // Book into a free slot succeeds and creates a Bookable event
    // ----------------------------------------------------------------

    [Fact]
    public async Task Book_IntoAFreeSlot_Succeeds_AndCreatesABookableEvent()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        var outcome = await sut.Booking.Book(Acme, Doctor, "Checkup", Utc(10), Utc(11), Actor, attendee: Patient);

        Assert.True(outcome.Success);
        Assert.Null(outcome.RejectionReason);
        var ev = Assert.IsType<CalendarEvent>(outcome.Event);
        Assert.Equal(Occupancy.Bookable, ev.Occupancy);
        // The doctor is a Resource, the patient an Attendee.
        Assert.Contains(ev.Participations, p => p.Participant == Doctor && p.Role == ParticipationRole.Resource);
        Assert.Contains(ev.Participations, p => p.Participant == Patient && p.Role == ParticipationRole.Attendee);

        // The booked event is persisted and consumes that slot in subsequent free/busy.
        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(0), Utc(23));
        Assert.DoesNotContain(fb.FreeSlots, f => f.Contains(Utc(10)));
    }

    [Fact]
    public async Task Book_PersistsEvent_ThatReExpandsToTheSameUtcSlot()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor, tz: "America/Los_Angeles");

        // 17:30 UTC on 2026-03-04 = 09:30 PST — inside 9-17 LA-local availability.
        var startUtc = new DateTimeOffset(2026, 3, 4, 17, 30, 0, TimeSpan.Zero);
        var endUtc = new DateTimeOffset(2026, 3, 4, 18, 0, 0, TimeSpan.Zero);

        var outcome = await sut.Booking.Book(Acme, Doctor, "LA checkup", startUtc, endUtc, Actor);

        Assert.True(outcome.Success);
        // The persisted event re-expands to the exact same UTC instant it was booked at.
        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, startUtc, endUtc);
        Assert.Empty(fb.FreeSlots);   // the only-30-min window is fully consumed
    }

    // ----------------------------------------------------------------
    // No-double-book: a second booking on the same slot is rejected
    // ----------------------------------------------------------------

    [Fact]
    public async Task Book_TwiceOnTheSameSlot_RejectsTheSecond_SlotConflict()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        var first = await sut.Booking.Book(Acme, Doctor, "First", Utc(10), Utc(11), Actor);
        Assert.True(first.Success);

        var second = await sut.Booking.Book(Acme, Doctor, "Second", Utc(10), Utc(11), Actor);
        Assert.False(second.Success);
        Assert.Equal(BookingOutcome.SlotConflict, second.RejectionReason);
        Assert.Null(second.Event);
    }

    [Fact]
    public async Task Book_OverlappingAnExistingBooking_RejectsSlotConflict()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        await sut.Booking.Book(Acme, Doctor, "First", Utc(10), Utc(11), Actor);

        // 10:30-11:30 overlaps the 10-11 booking.
        var overlap = await sut.Booking.Book(
            Acme, Doctor, "Overlap",
            new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 4, 11, 30, 0, TimeSpan.Zero),
            Actor);

        Assert.False(overlap.Success);
        Assert.Equal(BookingOutcome.SlotConflict, overlap.RejectionReason);
    }

    [Fact]
    public async Task Book_AdjacentToAnExistingBooking_Succeeds()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        await sut.Booking.Book(Acme, Doctor, "First", Utc(10), Utc(11), Actor);

        // 11-12 is back-to-back with 10-11 (half-open) — not a conflict.
        var adjacent = await sut.Booking.Book(Acme, Doctor, "Adjacent", Utc(11), Utc(12), Actor);

        Assert.True(adjacent.Success);
    }

    [Fact]
    public async Task Book_AgainstABlockingLunch_RejectsSlotConflict()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        // The doctor's lunch is a Blocking event — busy, not bookable.
        var lunch = CalendarEvent.Create(Acme, "Lunch", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(13, 0), occupancy: Occupancy.Blocking);
        lunch.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(lunch);

        var outcome = await sut.Booking.Book(Acme, Doctor, "During lunch", Utc(12), Utc(13), Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.SlotConflict, outcome.RejectionReason);
    }

    // ----------------------------------------------------------------
    // Outside availability = NO_AVAILABILITY (distinct from a conflict)
    // ----------------------------------------------------------------

    [Fact]
    public async Task Book_OutsideAvailabilityWindow_RejectsNoAvailability()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        // 18-19 is after the 9-17 window — not available at all.
        var outcome = await sut.Booking.Book(Acme, Doctor, "After hours", Utc(18), Utc(19), Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.NoAvailability, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_WithNoAvailabilityRecord_RejectsNoAvailability()
    {
        var sut = NewSut();
        // No availability saved at all.

        var outcome = await sut.Booking.Book(Acme, Doctor, "Anytime", Utc(10), Utc(11), Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.NoAvailability, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_PartiallyOutsideAvailability_RejectsNoAvailability()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        // 16:30-17:30 straddles the 17:00 window end — not fully contained in availability.
        var outcome = await sut.Booking.Book(
            Acme, Doctor, "Straddle",
            new DateTimeOffset(2026, 3, 4, 16, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 4, 17, 30, 0, TimeSpan.Zero),
            Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.NoAvailability, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_InvertedSlot_RejectsSlotInverted()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        var outcome = await sut.Booking.Book(Acme, Doctor, "Backwards", Utc(11), Utc(10), Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.SlotInverted, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_CarriesAContextRef_WhenScheduledAgainstIsGiven()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        var ward = ContextRef.Of("floor", "icu-3");

        var outcome = await sut.Booking.Book(Acme, Doctor, "ICU round", Utc(10), Utc(11), Actor, scheduledAgainst: ward);

        Assert.True(outcome.Success);
        Assert.Equal(ward, outcome.Event!.ScheduledAgainst);
    }

    // ----------------------------------------------------------------
    // S1 — the booking gate enforces the subscribed shared-calendar layer.
    // A resource subscribed to a "clinic closed Wednesday" shared calendar
    // shows Wednesday busy in free/busy; a Wednesday booking MUST be rejected
    // (the booking gate composes the SAME shared-exception layer free/busy does).
    // ----------------------------------------------------------------

    private sealed record LayeredSut(
        IResourceAvailabilityStore Availability,
        ISharedCalendarStore SharedCalendars,
        ICalendarSubscriptionStore Subscriptions,
        IFreeBusyService FreeBusy,
        IBookingService Booking);

    private static LayeredSut NewLayeredSut()
    {
        var rrule = new InMemoryRruleExpansionService();
        var expansion = new CalendarEventExpansionService(rrule);
        var availStore = new InMemoryResourceAvailabilityStore();
        var availExpansion = new AvailabilityExpansionService(rrule);
        var sharedStore = new InMemorySharedCalendarStore();
        var subStore = new InMemoryCalendarSubscriptionStore();
        var resolver = new SharedCalendarResolver(subStore, sharedStore);
        var eventStore = new InMemoryCalendarEventStore();
        var freeBusy = new FreeBusyService(availStore, availExpansion, eventStore, expansion, resolver, visibilityPolicy: null);
        // The CALENDAR-LAYERS booking constructor — the booking gate resolves the SAME shared-calendar
        // exceptions free/busy does, so the view (free/busy) and the gate (Book) agree.
        var booking = new BookingService(
            freeBusy, availStore, availExpansion, eventStore, new DefaultPaddingPolicy(EventPadding.None), resolver);
        return new LayeredSut(availStore, sharedStore, subStore, freeBusy, booking);
    }

    [Fact]
    public async Task Book_OnASubscribedSharedHoliday_RejectsNoAvailability()
    {
        var sut = NewLayeredSut();
        var monday = new DateOnly(2026, 3, 2);          // a Monday — the recurring weekday anchor
        var wednesday = new DateOnly(2026, 3, 4);       // the clinic-closed holiday
        var tuesday = new DateOnly(2026, 3, 3);         // an open day (control)

        // Doctor available weekdays 9-5.
        await sut.Availability.SaveAsync(
            ResourceAvailability.Create(Acme, Doctor, "UTC")
                .AddWindow(AvailabilityWindow.Create(monday, new TimeOnly(9, 0), new TimeOnly(17, 0),
                    rrule: "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR")));

        // A clinic holiday calendar closes Wednesday; the Doctor subscribes to it.
        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(wednesday, wednesday, "Clinic closed Wednesday"));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        var wedStart = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
        var wedEnd = new DateTimeOffset(2026, 3, 4, 11, 0, 0, TimeSpan.Zero);

        // The VIEW: free/busy shows Wednesday removed (no free slot on the holiday).
        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, wedStart, wedEnd);
        Assert.Empty(fb.FreeSlots);

        // The GATE: a Wednesday booking is REJECTED — the gate matches the view (no silent admit).
        var wedOutcome = await sut.Booking.Book(Acme, Doctor, "Wed checkup", wedStart, wedEnd, Actor);
        Assert.False(wedOutcome.Success);
        Assert.Equal(BookingOutcome.NoAvailability, wedOutcome.RejectionReason);

        // Control: a Tuesday (non-holiday) booking on the SAME subscribed resource still succeeds —
        // the shared layer only removes the subscribed holiday, not every day.
        var tueStart = new DateTimeOffset(2026, 3, 3, 10, 0, 0, TimeSpan.Zero);
        var tueEnd = new DateTimeOffset(2026, 3, 3, 11, 0, 0, TimeSpan.Zero);
        var tueOutcome = await sut.Booking.Book(Acme, Doctor, "Tue checkup", tueStart, tueEnd, Actor);
        Assert.True(tueOutcome.Success);
        Assert.Equal(tuesday, tueOutcome.Event!.Start);
    }
}
