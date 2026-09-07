using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the <b>padding / event time-footprint</b> slice — events are NOT pure [start,end]
/// blocks. An event has a <i>visible</i> (booked / shown) interval and an <i>occupied</i>
/// (resource-blocking) interval = <c>[Start − pre, End + post]</c>. <b>free/busy + no-double-book use
/// the OCCUPIED interval; the demand side sees the VISIBLE.</b> The padding MECHANISM (pre/post fields
/// + occupied-interval math) is built-in; the VALUES come from a configured
/// <see cref="IPaddingPolicy"/> (default at booking) + a per-event override. Default padding = 0
/// (backward-compatible). The occupied interval is computed by differencing absolute UTC instants —
/// the load-bearing S3 invariant — so a padded recurrence across a DST boundary stays correct.
/// </summary>
public sealed class PaddingTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private static readonly ParticipantRef Patient = ParticipantRef.Party("party-patient");

    private const string LA = "America/Los_Angeles";

    private sealed record Sut(
        IResourceAvailabilityStore Availability,
        ICalendarEventStore Events,
        IFreeBusyService FreeBusy,
        IBookingService Booking);

    private static Sut NewSut(EventPadding? defaultPadding = null)
    {
        var rrule = new InMemoryRruleExpansionService();
        var expansion = new CalendarEventExpansionService(rrule);
        var availStore = new InMemoryResourceAvailabilityStore();
        var availExpansion = new AvailabilityExpansionService(rrule);
        var eventStore = new InMemoryCalendarEventStore();
        var freeBusy = new FreeBusyService(availStore, availExpansion, eventStore, expansion);
        var policy = new DefaultPaddingPolicy(defaultPadding ?? EventPadding.None);
        var booking = new BookingService(freeBusy, availStore, availExpansion, eventStore, policy);
        return new Sut(availStore, eventStore, freeBusy, booking);
    }

    private static DateTimeOffset Utc(DateOnly day, int hour, int minute = 0)
        => new(day.Year, day.Month, day.Day, hour, minute, 0, TimeSpan.Zero);

    private static async Task SeedNineToFive(Sut sut, ParticipantRef resource, DateOnly day, string tz = "UTC")
        => await sut.Availability.SaveAsync(
            ResourceAvailability.Create(Acme, resource, tz)
                .AddWindow(AvailabilityWindow.Create(day, new TimeOnly(9, 0), new TimeOnly(17, 0))));

    // ================================================================
    // 1. The model — visible vs. occupied interval (pure unit)
    // ================================================================

    [Fact]
    public void OccupiedInterval_ExtendsTheVisibleInterval_ByPrePost()
    {
        // A 2:00–2:30 appointment with 5-min pre + 30-min post → occupied 1:55–3:00.
        var day = new DateOnly(2026, 3, 4);
        var instant = new OccurrenceInstant(
            CalendarEventId.NewId(),
            day,
            Utc(day, 14, 0),
            Utc(day, 14, 30),
            "Appt",
            IsOverride: false);

        var padding = EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

        var visible = instant.VisibleInterval;
        var occupied = instant.OccupiedInterval(padding);

        // Visible = exactly the booked slot.
        Assert.Equal(Utc(day, 14, 0), visible.StartUtc);
        Assert.Equal(Utc(day, 14, 30), visible.EndUtc);

        // Occupied = visible − pre … visible + post.
        Assert.Equal(Utc(day, 13, 55), occupied.StartUtc);
        Assert.Equal(Utc(day, 15, 0), occupied.EndUtc);
    }

    [Fact]
    public void OccupiedInterval_WithNonePadding_EqualsTheVisibleInterval()
    {
        var day = new DateOnly(2026, 3, 4);
        var instant = new OccurrenceInstant(
            CalendarEventId.NewId(), day, Utc(day, 14, 0), Utc(day, 14, 30), "Appt", false);

        var occupied = instant.OccupiedInterval(EventPadding.None);

        Assert.Equal(instant.VisibleInterval, occupied);
    }

    [Fact]
    public void EventPadding_NegativeDurations_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPadding.Of(TimeSpan.FromMinutes(-1), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(-1)));
    }

    // ================================================================
    // 2. free/busy uses the OCCUPIED interval (a padded appt blocks beyond its visible span)
    // ================================================================

    [Fact]
    public async Task FreeBusy_PaddedAppointment_BlocksBeyondItsVisibleSpan()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // A 14:00–14:30 appointment with 30-min post-padding → occupied 14:00–15:00.
        var appt = CalendarEvent.Create(Acme, "Padded appt", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(14, 30),
            occupancy: Occupancy.Bookable,
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        appt.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        // Busy span = the OCCUPIED interval (14:00–15:00), not the visible (14:00–14:30).
        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 14, 0), busy.StartUtc);
        Assert.Equal(Utc(day, 15, 0), busy.EndUtc);

        // The free afternoon resumes at 15:00 (post-padding consumed 14:30–15:00), not at 14:30.
        Assert.Contains(fb.FreeSlots, f => f.StartUtc == Utc(day, 15, 0) && f.EndUtc == Utc(day, 17, 0));
        Assert.DoesNotContain(fb.FreeSlots, f => f.Overlaps(TimeInterval.Of(Utc(day, 14, 30), Utc(day, 15, 0))));
    }

    [Fact]
    public async Task FreeBusy_PrePadding_BlocksBeforeTheVisibleStart()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // A 14:00–14:30 appointment with 10-min pre-padding → occupied 13:50–14:30.
        var appt = CalendarEvent.Create(Acme, "Pre-padded appt", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(14, 30),
            padding: EventPadding.Of(TimeSpan.FromMinutes(10), TimeSpan.Zero));
        appt.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 13, 50), busy.StartUtc);
        Assert.Equal(Utc(day, 14, 30), busy.EndUtc);
    }

    // ================================================================
    // 3. no-double-book uses OCCUPIED — booking into the padding is rejected
    // ================================================================

    [Fact]
    public async Task Book_IntoThePostPadding_OfAnExistingAppt_IsRejected_SlotConflict()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // Existing 14:00–14:30 appt with 30-min post-padding → occupied 14:00–15:00.
        var existing = CalendarEvent.Create(Acme, "First", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(14, 30),
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        existing.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(existing);

        // A 14:45 booking lands INSIDE the post-padding (14:30–15:00) → conflict, even though it is
        // past the VISIBLE end (14:30).
        var outcome = await sut.Booking.Book(Acme, Doctor, "Into padding", Utc(day, 14, 45), Utc(day, 15, 0), Actor);

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.SlotConflict, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_RightAfterThePostPadding_Succeeds()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // Existing 14:00–14:30 with 30-min post-padding → occupied ends 15:00.
        var existing = CalendarEvent.Create(Acme, "First", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(14, 30),
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        existing.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(existing);

        // 15:00 is back-to-back with the 15:00 occupied end (half-open) — not a conflict.
        var outcome = await sut.Booking.Book(Acme, Doctor, "After padding", Utc(day, 15, 0), Utc(day, 15, 30), Actor);

        Assert.True(outcome.Success);
    }

    [Fact]
    public async Task Book_WhoseOwnPrePadding_OverlapsAnEarlierAppt_IsRejected()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // An earlier unpadded appt 10:00–10:30.
        var earlier = CalendarEvent.Create(Acme, "Earlier", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(10, 0), endTime: new TimeOnly(10, 30));
        earlier.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(earlier);

        // A new 10:45–11:15 booking with 20-min PRE-padding → its occupied footprint starts at 10:25,
        // overlapping the earlier appt (ends 10:30). The candidate's OWN padding causes the conflict.
        var outcome = await sut.Booking.Book(
            Acme, Doctor, "Pre-padded", Utc(day, 10, 45), Utc(day, 11, 15), Actor,
            padding: EventPadding.Of(TimeSpan.FromMinutes(20), TimeSpan.Zero));

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.SlotConflict, outcome.RejectionReason);
    }

    [Fact]
    public async Task Book_PaddingPastClose_IsNotFalselyRejected_AsNoAvailabilityOrConflict()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // A 16:30–17:00 booking (visible slot inside 9–17) with 30-min post-padding → occupied ends
        // 17:30, PAST the availability close. The booking is still admitted: availability bounds what
        // can be BOOKED (the visible slot fits), and the doctor's post-visit documentation may spill
        // past close without a conflict (nothing else is occupying 17:00–17:30).
        var outcome = await sut.Booking.Book(
            Acme, Doctor, "Late with cleanup", Utc(day, 16, 30), Utc(day, 17, 0), Actor,
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));

        Assert.True(outcome.Success);
        Assert.Equal(EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)), outcome.Event!.Padding);
    }

    // ================================================================
    // 4. The padding policy — configured default + per-event override; default 0 = backward-compat
    // ================================================================

    [Fact]
    public async Task PaddingPolicy_ConfiguredDefault_IsAppliedAtBooking()
    {
        // A deployment configures a 5-min pre + 30-min post default.
        var policyDefault = EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
        var sut = NewSut(defaultPadding: policyDefault);
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        var outcome = await sut.Booking.Book(Acme, Doctor, "Default-padded", Utc(day, 10, 0), Utc(day, 10, 30), Actor);

        Assert.True(outcome.Success);
        // The booked event carries the policy default, with NO explicit override passed.
        Assert.Equal(policyDefault, outcome.Event!.Padding);

        // And it occupies its padded footprint: 09:55–11:00 (5 pre + 30 post around 10:00–10:30).
        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));
        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 9, 55), busy.StartUtc);
        Assert.Equal(Utc(day, 11, 0), busy.EndUtc);
    }

    [Fact]
    public async Task PaddingPolicy_PerEventOverride_WinsOverTheDefault()
    {
        // Policy default = 5 pre + 30 post, but this booking overrides to 0 pre + 10 post.
        var sut = NewSut(defaultPadding: EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)));
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        var overridePadding = EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(10));
        var outcome = await sut.Booking.Book(
            Acme, Doctor, "Overridden", Utc(day, 10, 0), Utc(day, 10, 30), Actor, padding: overridePadding);

        Assert.True(outcome.Success);
        Assert.Equal(overridePadding, outcome.Event!.Padding); // the override, not the default
    }

    [Fact]
    public async Task PaddingPolicy_DefaultZero_IsBackwardCompatible_OccupiedEqualsVisible()
    {
        // No configured default (= EventPadding.None). A booking occupies exactly its visible slot —
        // the pre-padding behavior.
        var sut = NewSut(); // default None
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        var outcome = await sut.Booking.Book(Acme, Doctor, "Unpadded", Utc(day, 10, 0), Utc(day, 11, 0), Actor);

        Assert.True(outcome.Success);
        Assert.Equal(EventPadding.None, outcome.Event!.Padding);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));
        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 10, 0), busy.StartUtc); // occupied == visible
        Assert.Equal(Utc(day, 11, 0), busy.EndUtc);
    }

    [Fact]
    public void SetPadding_PerEventOverride_UpdatesTheEnvelope()
    {
        var day = new DateOnly(2026, 3, 4);
        var ev = CalendarEvent.Create(Acme, "Appt", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(10, 0), endTime: new TimeOnly(10, 30));
        Assert.Equal(EventPadding.None, ev.Padding); // default

        var p = EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
        ev.SetPadding(p, Actor);

        Assert.Equal(p, ev.Padding);
    }

    // ================================================================
    // 5. UTC correctness — a padded recurrence across spring-forward holds
    // ================================================================

    [Fact]
    public async Task FreeBusy_PaddedRecurrence_AcrossSpringForward_StaysCorrectInUtc()
    {
        // A daily 9:00–9:30 (LA) appointment with 30-min post-padding → occupied is a 1-hour footprint
        // (09:00–09:30 visible + 30-min post = 09:00–10:00 LA wall-clock). Across US spring-forward
        // (2026-03-08), the LA wall-clock stays 09:00–10:00 but the UTC instant shifts by an hour.
        // Because padding is added to the UTC INSTANTS (not wall-clock), the occupied footprint is a
        // flat 60 real minutes on BOTH sides of the boundary.
        var sut = NewSut();
        var resource = Doctor;

        // Availability 9–11 LA each day so the padded footprint fits.
        await sut.Availability.SaveAsync(
            ResourceAvailability.Create(Acme, resource, LA)
                .AddWindow(AvailabilityWindow.Create(
                    new DateOnly(2026, 3, 6), new TimeOnly(9, 0), new TimeOnly(11, 0), "FREQ=DAILY")));

        var ev = CalendarEvent.Create(Acme, "Daily 9am", new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 6), Actor,
            rrule: "FREQ=DAILY", timezone: LA,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30),
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        ev.SetResource(resource, Actor);
        await sut.Events.SaveAsync(ev);

        // Query a wide UTC window spanning the boundary.
        var fb = await sut.FreeBusy.FreeBusy(Acme, resource,
            new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 11, 0, 0, 0, TimeSpan.Zero));

        // Every busy occupied footprint is exactly 1 hour (09:00–10:00 LA = 09:00 visible + 30 pre-vis
        // 09:30 visible-end + 30 post = a 60-min footprint), regardless of the DST shift.
        Assert.NotEmpty(fb.BusyIntervals);
        Assert.All(fb.BusyIntervals, b => Assert.Equal(TimeSpan.FromHours(1), b.Duration));

        // Before spring-forward (Mar 7, PST = UTC-8): 09:00 LA = 17:00Z, occupied 17:00–18:00Z.
        var mar7 = fb.BusyIntervals.Single(b => b.StartUtc.UtcDateTime.Date == new DateTime(2026, 3, 7));
        Assert.Equal(new DateTimeOffset(2026, 3, 7, 17, 0, 0, TimeSpan.Zero), mar7.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 7, 18, 0, 0, TimeSpan.Zero), mar7.EndUtc);

        // After spring-forward (Mar 9, PDT = UTC-7): 09:00 LA = 16:00Z, occupied 16:00–17:00Z.
        var mar9 = fb.BusyIntervals.Single(b => b.StartUtc.UtcDateTime.Date == new DateTime(2026, 3, 9));
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 16, 0, 0, TimeSpan.Zero), mar9.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 17, 0, 0, TimeSpan.Zero), mar9.EndUtc);
    }

    [Fact]
    public void OccupiedInterval_AcrossSpringForward_IsDifferencedInUtc_NotWallClock()
    {
        // The visible instants are already DST-resolved (Mar 9 09:00 LA = 16:00Z). Adding a flat 30-min
        // UTC post-padding keeps the footprint at 30 real minutes — NOT a wall-clock add that would be
        // wrong across the lost hour. This is the load-bearing invariant, asserted at the instant level.
        var mar9Start = new DateTimeOffset(2026, 3, 9, 16, 0, 0, TimeSpan.Zero); // 09:00 LA, PDT
        var mar9End = new DateTimeOffset(2026, 3, 9, 16, 30, 0, TimeSpan.Zero);  // 09:30 LA

        var instant = new OccurrenceInstant(CalendarEventId.NewId(), new DateOnly(2026, 3, 9), mar9Start, mar9End, "Appt", false);
        var occupied = instant.OccupiedInterval(EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)));

        // 16:00Z − 5min = 15:55Z; 16:30Z + 30min = 17:00Z. Pure UTC arithmetic.
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 15, 55, 0, TimeSpan.Zero), occupied.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 17, 0, 0, TimeSpan.Zero), occupied.EndUtc);
        Assert.Equal(TimeSpan.FromMinutes(65), occupied.Duration); // 30 visible + 5 pre + 30 post
    }

    // ================================================================
    // 6. Coverage shift-turnover — incoming pre-padding overlaps outgoing post-padding (falls out free)
    // ================================================================

    [Fact]
    public async Task CoverageTurnover_IncomingPrePadding_OverlapsOutgoingPostPadding()
    {
        // Two back-to-back shifts on a station (an Asset) modeled as Blocking events. The OUTGOING
        // shift 06:00–14:00 has a 30-min post-padding (handoff/documentation) → occupied to 14:30. The
        // INCOMING shift 14:00–22:00 has a 30-min pre-padding (handoff) → occupied from 13:30. Their
        // occupied footprints OVERLAP 13:30–14:30 — the turnover window where BOTH are present. The
        // overlap falls out of the occupied-interval math for free; the calendar exposes it, a coverage
        // overlay would read higher coverage there.
        var sut = NewSut();
        var station = ParticipantRef.Asset("asset-icu-station-3");
        var day = new DateOnly(2026, 6, 1);

        var outgoing = CalendarEvent.Create(Acme, "Day shift", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(6, 0), endTime: new TimeOnly(14, 0),
            occupancy: Occupancy.Blocking,
            padding: EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));
        outgoing.SetResource(station, Actor);

        var incoming = CalendarEvent.Create(Acme, "Evening shift", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(22, 0),
            occupancy: Occupancy.Blocking,
            padding: EventPadding.Of(TimeSpan.FromMinutes(30), TimeSpan.Zero));
        incoming.SetResource(station, Actor);

        await sut.Events.SaveAsync(outgoing);
        await sut.Events.SaveAsync(incoming);

        // Read the raw occupied footprints over the turnover window. Both shifts cover the overlap, so
        // the merged occupancy is continuous 05:xx … but the per-event footprints overlap at 13:30–14:30.
        var occupied = await sut.FreeBusy.OccupiedIntervals(
            Acme, station, Utc(day, 13, 0), Utc(day, 15, 0));

        // The merged occupancy across the turnover window is continuous (no gap) — the incoming shift's
        // pre-padding (from 13:30) overlaps the outgoing shift's post-padding (to 14:30).
        var merged = Assert.Single(occupied);
        Assert.Equal(Utc(day, 13, 0), merged.StartUtc);  // clipped to the query window
        Assert.Equal(Utc(day, 15, 0), merged.EndUtc);

        // Assert the per-shift occupied footprints overlap (the turnover) at the instant level — this is
        // the "turnover overlap falls out for free" property.
        var outInstant = new OccurrenceInstant(outgoing.Id, day, Utc(day, 6, 0), Utc(day, 14, 0), "Day", false);
        var inInstant = new OccurrenceInstant(incoming.Id, day, Utc(day, 14, 0), Utc(day, 22, 0), "Evening", false);
        var outOccupied = outInstant.OccupiedInterval(outgoing.Padding);   // 06:00–14:30
        var inOccupied = inInstant.OccupiedInterval(incoming.Padding);     // 13:30–22:00

        Assert.True(outOccupied.Overlaps(inOccupied), "incoming pre-padding should overlap outgoing post-padding");
        Assert.Equal(Utc(day, 13, 30), inOccupied.StartUtc);
        Assert.Equal(Utc(day, 14, 30), outOccupied.EndUtc);
    }

    // ================================================================
    // 7. Pre-padding (unpadded) events unchanged
    // ================================================================

    [Fact]
    public async Task UnpaddedEvent_BehavesExactlyAsBefore_OccupiedEqualsVisible()
    {
        var sut = NewSut();
        var day = new DateOnly(2026, 3, 4);
        await SeedNineToFive(sut, Doctor, day);

        // An unpadded (default) appointment 12:00–13:00 — occupied == visible, exactly the S3 behavior.
        var appt = CalendarEvent.Create(Acme, "Plain appt", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(13, 0));
        Assert.Equal(EventPadding.None, appt.Padding);
        appt.SetResource(Doctor, Actor);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(day, 0), Utc(day, 23));

        var busy = Assert.Single(fb.BusyIntervals);
        Assert.Equal(Utc(day, 12, 0), busy.StartUtc);
        Assert.Equal(Utc(day, 13, 0), busy.EndUtc);

        // A booking adjacent to the unpadded appt (back-to-back, 13:00) still succeeds — no phantom padding.
        var outcome = await sut.Booking.Book(Acme, Doctor, "Right after", Utc(day, 13, 0), Utc(day, 14, 0), Actor);
        Assert.True(outcome.Success);
    }

    // ================================================================
    // 8. Padding survives the persistence round-trip
    // ================================================================

    [Fact]
    public async Task EventStore_RoundTrips_PaddingEnvelope()
    {
        var store = new InMemoryCalendarEventStore();
        var day = new DateOnly(2026, 3, 4);

        var ev = CalendarEvent.Create(Acme, "Padded", day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(10, 0), endTime: new TimeOnly(10, 30),
            padding: EventPadding.Of(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30)));
        await store.SaveAsync(ev);

        var loaded = await store.GetAsync(Acme, ev.Id);

        Assert.NotNull(loaded);
        Assert.Equal(TimeSpan.FromMinutes(5), loaded!.Padding.Pre);
        Assert.Equal(TimeSpan.FromMinutes(30), loaded.Padding.Post);
    }
}
