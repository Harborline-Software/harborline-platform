using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice CALENDAR-LAYERS additions — the layered free/busy composition:
/// <c>effective_free = (base_availability − exceptions[shared holidays + per-resource
/// vacations/closures]) − occupancy</c>. A shared holiday calendar applied to multiple resources
/// reduces every subscribed resource's free/busy; a per-resource vacation span removes availability
/// across the span; the full composition (holiday + vacation + padded/blocking occupancy) reduces free
/// correctly; the UTC invariant survives DST; the viewer-scoped visibility projection redacts a private
/// appointment's detail for a non-owner while still showing it BUSY; tenant isolation holds.
/// </summary>
public sealed class CalendarLayersTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly TenantId Globex = new("globex");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private static readonly ParticipantRef Nurse = ParticipantRef.Party("party-nurse-jones");
    private static readonly ParticipantRef Room = ParticipantRef.Asset("asset-room-101");

    private sealed record Sut(
        IResourceAvailabilityStore Availability,
        ISharedCalendarStore SharedCalendars,
        ICalendarSubscriptionStore Subscriptions,
        ICalendarEventStore Events,
        IFreeBusyService FreeBusy);

    private static Sut NewSut(IEventDetailVisibilityPolicy? visibilityPolicy = null)
    {
        var rrule = new InMemoryRruleExpansionService();
        var expansion = new CalendarEventExpansionService(rrule);
        var availStore = new InMemoryResourceAvailabilityStore();
        var availExpansion = new AvailabilityExpansionService(rrule);
        var sharedStore = new InMemorySharedCalendarStore();
        var subStore = new InMemoryCalendarSubscriptionStore();
        var resolver = new SharedCalendarResolver(subStore, sharedStore);
        var eventStore = new InMemoryCalendarEventStore();
        var freeBusy = new FreeBusyService(availStore, availExpansion, eventStore, expansion, resolver, visibilityPolicy);
        return new Sut(availStore, sharedStore, subStore, eventStore, freeBusy);
    }

    /// <summary>"9-5 weekdays" recurring window in <paramref name="tz"/> — the bookable supply.</summary>
    private static ResourceAvailability WeekdaysNineToFive(ParticipantRef resource, DateOnly anchorMonday, TenantId tenant, string tz = "UTC")
        => ResourceAvailability.Create(tenant, resource, tz)
            .AddWindow(AvailabilityWindow.Create(anchorMonday, new TimeOnly(9, 0), new TimeOnly(17, 0),
                rrule: "FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR"));

    private static CalendarEvent TimedEvent(
        TenantId tenant, ParticipantRef resource, string title,
        DateOnly day, TimeOnly start, TimeOnly end, Occupancy occupancy, string tz = "UTC")
    {
        var ev = CalendarEvent.Create(tenant, title, day, day, Actor,
            timezone: tz, startTime: start, endTime: end, occupancy: occupancy);
        ev.SetResource(resource, Actor);
        return ev;
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, TimeSpan.Zero);

    // ================================================================
    // 1. Shared holiday calendar applied to MULTIPLE resources
    //    — one holiday reduces EVERY subscribed resource's free/busy
    // ================================================================

    [Fact]
    public async Task SharedHoliday_ReducesFreeBusy_ForEverySubscribedResource()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);          // a Monday
        var wednesday = new DateOnly(2026, 3, 4);       // the holiday

        // Two resources, each available weekdays 9-5.
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Nurse, monday, Acme));

        // A clinic holiday calendar with Wednesday closed; BOTH resources subscribe.
        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(wednesday, wednesday, "Clinic closed"));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Nurse, holidays.Id));

        // Query the whole week for each resource.
        var windowStart = Utc(2026, 3, 2, 0);
        var windowEnd = Utc(2026, 3, 6, 23);

        var drFb = await sut.FreeBusy.FreeBusy(Acme, Doctor, windowStart, windowEnd);
        var nurseFb = await sut.FreeBusy.FreeBusy(Acme, Nurse, windowStart, windowEnd);

        // Mon/Tue/Thu/Fri free (Wed removed by the shared holiday) — for BOTH resources.
        Assert.Equal(4, drFb.FreeSlots.Count);
        Assert.Equal(4, nurseFb.FreeSlots.Count);
        Assert.DoesNotContain(drFb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 4));
        Assert.DoesNotContain(nurseFb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 4));
    }

    [Fact]
    public async Task SharedHoliday_DoesNotAffect_AnUnsubscribedResource()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);
        var wednesday = new DateOnly(2026, 3, 4);

        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Nurse, monday, Acme));

        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(wednesday, wednesday));
        await sut.SharedCalendars.SaveAsync(holidays);
        // ONLY the doctor subscribes.
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        var windowStart = Utc(2026, 3, 2, 0);
        var windowEnd = Utc(2026, 3, 6, 23);

        var drFb = await sut.FreeBusy.FreeBusy(Acme, Doctor, windowStart, windowEnd);
        var nurseFb = await sut.FreeBusy.FreeBusy(Acme, Nurse, windowStart, windowEnd);

        Assert.Equal(4, drFb.FreeSlots.Count);                                  // Wed removed for the doctor
        Assert.Equal(5, nurseFb.FreeSlots.Count);                              // nurse keeps all 5 weekdays
        Assert.Contains(nurseFb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 4));
    }

    // ================================================================
    // 2. Per-resource VACATION SPAN removes availability across the span
    // ================================================================

    [Fact]
    public async Task PerResourceVacationSpan_RemovesAvailability_AcrossTheWholeSpan()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);

        // The doctor is on vacation Tue 03-03 through Thu 03-05 (a 3-day span).
        var avail = WeekdaysNineToFive(Doctor, monday, Acme)
            .AddExceptionSpan(ExceptionSpan.Create(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 5), "Vacation"));
        await sut.Availability.SaveAsync(avail);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(2026, 3, 2, 0), Utc(2026, 3, 6, 23));

        // Only Mon + Fri free (Tue/Wed/Thu removed by the vacation span).
        Assert.Equal(2, fb.FreeSlots.Count);
        Assert.Equal(new DateTime(2026, 3, 2), fb.FreeSlots[0].StartUtc.Date);  // Monday
        Assert.Equal(new DateTime(2026, 3, 6), fb.FreeSlots[1].StartUtc.Date);  // Friday
    }

    [Fact]
    public async Task VacationSpan_RoundTripsThroughTheStore()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);
        var avail = WeekdaysNineToFive(Doctor, monday, Acme)
            .AddExceptionSpan(ExceptionSpan.Create(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4), "Day off"));
        await sut.Availability.SaveAsync(avail);

        // Reload from the store (exercises the snapshot (de)serialization of the new spans).
        var reloaded = await sut.Availability.GetAsync(Acme, Doctor);
        Assert.NotNull(reloaded);
        var span = Assert.Single(reloaded!.ExceptionSpans);
        Assert.Equal(new DateOnly(2026, 3, 4), span.Start);
        Assert.Equal("Day off", span.Reason);
        Assert.True(reloaded.IsExcepted(new DateOnly(2026, 3, 4)));
    }

    // ================================================================
    // 3. The LAYERED composition: a shared holiday + a per-resource
    //    vacation + an occupancy block ALL reduce free, correctly
    // ================================================================

    [Fact]
    public async Task LayeredComposition_SharedHoliday_Plus_Vacation_Plus_Occupancy_AllReduceFree()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);

        // Base supply: weekdays 9-5. Plus a per-resource vacation on FRIDAY 03-06.
        var avail = WeekdaysNineToFive(Doctor, monday, Acme)
            .AddExceptionSpan(ExceptionSpan.Create(new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 6), "Vacation"));
        await sut.Availability.SaveAsync(avail);

        // Shared holiday: WEDNESDAY 03-04 closed; doctor subscribes.
        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4)));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        // Occupancy: a booked appointment on TUESDAY 03-03 12:00-13:00 (Bookable).
        var appt = TimedEvent(Acme, Doctor, "Patient appt", new DateOnly(2026, 3, 3),
            new TimeOnly(12, 0), new TimeOnly(13, 0), Occupancy.Bookable);
        await sut.Events.SaveAsync(appt);

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(2026, 3, 2, 0), Utc(2026, 3, 6, 23));

        // Mon = whole 9-5 free (1 slot). Tue = split by the noon appt (2 slots: 9-12, 13-17).
        // Wed = removed by the shared holiday. Thu = whole 9-5 free (1 slot). Fri = removed by vacation.
        // → 1 (Mon) + 2 (Tue) + 1 (Thu) = 4 free slots.
        Assert.Equal(4, fb.FreeSlots.Count);

        // No free time on Wed (shared holiday) or Fri (vacation).
        Assert.DoesNotContain(fb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 4));
        Assert.DoesNotContain(fb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 6));

        // The Tuesday noon appt is busy.
        Assert.Contains(fb.BusyIntervals, b => b.StartUtc == Utc(2026, 3, 3, 12) && b.EndUtc == Utc(2026, 3, 3, 13));

        // The Tuesday morning + afternoon split is present.
        Assert.Contains(fb.FreeSlots, s => s.StartUtc == Utc(2026, 3, 3, 9) && s.EndUtc == Utc(2026, 3, 3, 12));
        Assert.Contains(fb.FreeSlots, s => s.StartUtc == Utc(2026, 3, 3, 13) && s.EndUtc == Utc(2026, 3, 3, 17));
    }

    [Fact]
    public async Task LayeredComposition_SharedHolidaySpan_OverlappingResourceVacation_IsIdempotentUnion()
    {
        // A shared holiday span 03-03..03-05 AND a per-resource vacation 03-04..03-06 overlap on 03-04/03-05.
        // The union removes Tue..Fri (03-03,04,05,06); only Monday survives.
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);

        var avail = WeekdaysNineToFive(Doctor, monday, Acme)
            .AddExceptionSpan(ExceptionSpan.Create(new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 6), "Vacation"));
        await sut.Availability.SaveAsync(avail);

        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 5), "Closure"));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor, Utc(2026, 3, 2, 0), Utc(2026, 3, 6, 23));

        var only = Assert.Single(fb.FreeSlots);
        Assert.Equal(new DateTime(2026, 3, 2), only.StartUtc.Date);   // only Monday remains
    }

    // ================================================================
    // 4. UTC correctness preserved across DST
    //    — the shared-holiday day filter is applied in the resource's
    //      LOCAL tz, and the surviving windows keep their wall-clock
    //      across the spring-forward boundary (UTC instant shifts).
    // ================================================================

    [Fact]
    public async Task SharedHoliday_AcrossDstBoundary_FiltersTheCorrectLocalDay_AndPreservesUtcInvariant()
    {
        // US DST 2026 springs forward on Sunday 2026-03-08 (02:00 → 03:00 America/Los_Angeles).
        // Week of Mon 03-09..Fri 03-13 is in PDT (UTC-7); the prior week Mon 03-02..Fri 03-06 is PST (UTC-8).
        var sut = NewSut();
        var tz = "America/Los_Angeles";

        // Two consecutive weeks of weekdays 9-5 local.
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, new DateOnly(2026, 3, 2), Acme, tz));

        // Shared holiday on Wed 03-11 (a PDT day, after the DST switch).
        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays")
            .AddException(ExceptionSpan.Create(new DateOnly(2026, 3, 11), new DateOnly(2026, 3, 11)));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        // Query both weeks in UTC (wide enough to cover both PST and PDT instants).
        var fb = await sut.FreeBusy.FreeBusy(Acme, Doctor,
            new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 14, 0, 0, 0, TimeSpan.Zero));

        // 10 weekdays minus the Wed 03-11 holiday = 9 free slots.
        Assert.Equal(9, fb.FreeSlots.Count);

        // The PST week's Monday 03-02 9-5 local = 17:00-01:00 UTC (UTC-8 → 09:00 local is 17:00 UTC).
        var pstMonday = fb.FreeSlots.Single(s => s.StartUtc == new DateTimeOffset(2026, 3, 2, 17, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 1, 0, 0, TimeSpan.Zero), pstMonday.EndUtc);

        // The PDT week's Monday 03-09 9-5 local = 16:00-00:00 UTC (UTC-7 → 09:00 local is 16:00 UTC) —
        // the wall-clock is preserved (still 9 a.m. local), the UTC instant shifted by an hour. This is
        // the DST invariant: the difference is computed in absolute UTC instants, never wall-clock.
        var pdtMonday = fb.FreeSlots.Single(s => s.StartUtc == new DateTimeOffset(2026, 3, 9, 16, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateTimeOffset(2026, 3, 10, 0, 0, 0, TimeSpan.Zero), pdtMonday.EndUtc);

        // The Wed 03-11 (PDT) holiday is filtered — no free slot starts on the 11th (local).
        Assert.DoesNotContain(fb.FreeSlots, s => s.StartUtc == new DateTimeOffset(2026, 3, 11, 16, 0, 0, TimeSpan.Zero));
    }

    // ================================================================
    // 5. Visibility: a private personal appt shows BUSY to a non-owner
    //    but detail only to the owner (grant-scoped, via the policy seam)
    // ================================================================

    [Fact]
    public async Task PrivateAppointment_ShowsBusyToNonOwner_ButDetailOnlyToOwner()
    {
        var sut = NewSut();   // default OwnerOnly policy
        var owner = Guid.NewGuid();
        var otherViewer = Guid.NewGuid();
        var monday = new DateOnly(2026, 3, 2);

        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));

        // A PRIVATE personal appointment (a Blocking occupancy) owned by `owner`, 12-13 on Monday.
        var personal = CalendarEvent.Create(Acme, "Therapy session", monday, monday, owner,
            timezone: "UTC", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(13, 0),
            occupancy: Occupancy.Blocking);
        personal.SetResource(Doctor, owner);
        personal.SetVisibility(EventVisibility.Private, owner, ownerActorId: owner);
        await sut.Events.SaveAsync(personal);

        var windowStart = Utc(2026, 3, 2, 0);
        var windowEnd = Utc(2026, 3, 2, 23);

        // The OWNER sees the detail.
        var ownerView = await sut.FreeBusy.FreeBusyForViewer(Acme, Doctor, owner, windowStart, windowEnd);
        var ownerBusy = Assert.Single(ownerView.BusyEvents);
        Assert.True(ownerBusy.DetailVisible);
        Assert.Equal("Therapy session", ownerBusy.Title);

        // A DIFFERENT principal sees BUSY but the title is redacted.
        var otherView = await sut.FreeBusy.FreeBusyForViewer(Acme, Doctor, otherViewer, windowStart, windowEnd);
        var otherBusy = Assert.Single(otherView.BusyEvents);
        Assert.False(otherBusy.DetailVisible);
        Assert.Equal(BusyEvent.RedactedTitle, otherBusy.Title);
        Assert.NotEqual("Therapy session", otherBusy.Title);
        // The slot is STILL busy (same interval) — visibility never changes availability.
        Assert.Equal(Utc(2026, 3, 2, 12), otherBusy.Interval.StartUtc);
        Assert.Equal(Utc(2026, 3, 2, 13), otherBusy.Interval.EndUtc);

        // FREE is identical for both viewers (a private appt still blocks a booking).
        Assert.Equal(ownerView.FreeSlots.Count, otherView.FreeSlots.Count);
    }

    [Fact]
    public async Task AnonymousViewer_GetsRedactedPrivateDetail_PublicEventsAlwaysVisible()
    {
        var sut = NewSut();
        var owner = Guid.NewGuid();
        var monday = new DateOnly(2026, 3, 2);
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));

        // A public appt 10-11 (visible to all) + a private appt 14-15 (owner-only).
        var pub = TimedEvent(Acme, Doctor, "Public meeting", monday, new TimeOnly(10, 0), new TimeOnly(11, 0), Occupancy.Bookable);
        await sut.Events.SaveAsync(pub);

        var priv = CalendarEvent.Create(Acme, "Secret", monday, monday, owner,
            timezone: "UTC", startTime: new TimeOnly(14, 0), endTime: new TimeOnly(15, 0), occupancy: Occupancy.Blocking);
        priv.SetResource(Doctor, owner);
        priv.SetVisibility(EventVisibility.Private, owner, ownerActorId: owner);
        await sut.Events.SaveAsync(priv);

        // Anonymous viewer (null actor).
        var view = await sut.FreeBusy.FreeBusyForViewer(Acme, Doctor, viewerActorId: null,
            Utc(2026, 3, 2, 0), Utc(2026, 3, 2, 23));

        Assert.Equal(2, view.BusyEvents.Count);
        var publicBusy = view.BusyEvents.Single(b => b.Interval.StartUtc == Utc(2026, 3, 2, 10));
        var privateBusy = view.BusyEvents.Single(b => b.Interval.StartUtc == Utc(2026, 3, 2, 14));
        Assert.True(publicBusy.DetailVisible);
        Assert.Equal("Public meeting", publicBusy.Title);
        Assert.False(privateBusy.DetailVisible);                 // anonymous → private detail redacted
        Assert.Equal(EventVisibility.Private, privateBusy.Visibility);
    }

    [Fact]
    public async Task GrantBackedVisibilityPolicy_WidensPrivateDetail_ToAnAuthorizedNonOwner()
    {
        // Demonstrates the seam the design prescribes: a host registers a grant-backed policy that
        // authorizes a non-owner principal to see a private event's detail (e.g. a delegate / a
        // shared-team-calendar member). The calendar block never references the grant substrate.
        var owner = Guid.NewGuid();
        var delegatePrincipal = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var policy = new StubGrantVisibilityPolicy(authorizedNonOwners: new[] { delegatePrincipal });

        var sut = NewSut(policy);
        var monday = new DateOnly(2026, 3, 2);
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));

        var priv = CalendarEvent.Create(Acme, "Confidential review", monday, monday, owner,
            timezone: "UTC", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(13, 0), occupancy: Occupancy.Blocking);
        priv.SetResource(Doctor, owner);
        priv.SetVisibility(EventVisibility.Private, owner, ownerActorId: owner);
        await sut.Events.SaveAsync(priv);

        var ws = Utc(2026, 3, 2, 0);
        var we = Utc(2026, 3, 2, 23);

        var delegateView = await sut.FreeBusy.FreeBusyForViewer(Acme, Doctor, delegatePrincipal, ws, we);
        Assert.True(Assert.Single(delegateView.BusyEvents).DetailVisible);        // granted → sees detail

        var strangerView = await sut.FreeBusy.FreeBusyForViewer(Acme, Doctor, stranger, ws, we);
        Assert.False(Assert.Single(strangerView.BusyEvents).DetailVisible);       // not granted → redacted
    }

    /// <summary>A stand-in for a host's grant-backed <see cref="IEventDetailVisibilityPolicy"/> (the production seam).</summary>
    private sealed class StubGrantVisibilityPolicy : IEventDetailVisibilityPolicy
    {
        private readonly HashSet<Guid> _authorizedNonOwners;
        public StubGrantVisibilityPolicy(IEnumerable<Guid> authorizedNonOwners)
            => _authorizedNonOwners = new HashSet<Guid>(authorizedNonOwners);

        public bool CanSeeDetail(CalendarEvent ev, Guid? viewerActorId)
        {
            if (ev.Visibility == EventVisibility.Public) return true;
            if (viewerActorId is not { } v) return false;
            return v == ev.OwnerActorId || _authorizedNonOwners.Contains(v);
        }
    }

    // ================================================================
    // 6. Tenant isolation — a shared calendar / subscription / vacation
    //    in tenant Acme never affects tenant Globex
    // ================================================================

    [Fact]
    public async Task SharedHolidayAndVacation_AreTenantIsolated()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);
        var wednesday = new DateOnly(2026, 3, 4);

        // Acme doctor: subscribed to an Acme holiday (Wed closed).
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Acme));
        var acmeHolidays = SharedCalendar.Create(Acme, "Acme Holidays")
            .AddException(ExceptionSpan.Create(wednesday, wednesday));
        await sut.SharedCalendars.SaveAsync(acmeHolidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, acmeHolidays.Id));

        // Globex has a doctor with the SAME ref but its OWN availability, no subscription.
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Doctor, monday, Globex));

        var ws = Utc(2026, 3, 2, 0);
        var we = Utc(2026, 3, 6, 23);

        var acmeFb = await sut.FreeBusy.FreeBusy(Acme, Doctor, ws, we);
        var globexFb = await sut.FreeBusy.FreeBusy(Globex, Doctor, ws, we);

        Assert.Equal(4, acmeFb.FreeSlots.Count);     // Acme: Wed removed by its holiday
        Assert.Equal(5, globexFb.FreeSlots.Count);   // Globex: unaffected — no subscription, no Acme holiday

        // A Globex query cannot even see the Acme shared calendar.
        var globexSubs = await sut.Subscriptions.ListForResourceAsync(Globex, Doctor);
        Assert.Empty(globexSubs);
        var globexShared = await sut.SharedCalendars.ListAsync(Globex);
        Assert.Empty(globexShared);
    }

    // ================================================================
    // 7. Subscription store mechanics (the many-to-many edge)
    // ================================================================

    [Fact]
    public async Task Subscription_IsIdempotent_AndUnsubscribeRemovesIt()
    {
        var sut = NewSut();
        var holidays = SharedCalendar.Create(Acme, "Holidays");
        await sut.SharedCalendars.SaveAsync(holidays);

        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));  // idempotent

        var subs = await sut.Subscriptions.ListForResourceAsync(Acme, Doctor);
        Assert.Single(subs);

        var subscribers = await sut.Subscriptions.ListSubscribersAsync(Acme, holidays.Id);
        Assert.Single(subscribers);
        Assert.Equal(Doctor, subscribers[0]);

        var removed = await sut.Subscriptions.UnsubscribeAsync(Acme, Doctor, holidays.Id);
        Assert.True(removed);
        Assert.Empty(await sut.Subscriptions.ListForResourceAsync(Acme, Doctor));
    }

    [Fact]
    public async Task SharedCalendar_RoundTripsThroughTheStore_WithItsExceptionSpans()
    {
        var sut = NewSut();
        var cal = SharedCalendar.Create(Acme, "Q1 Holidays")
            .AddException(ExceptionSpan.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), "New Year"))
            .AddException(ExceptionSpan.Create(new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 22), "Renovation"));
        await sut.SharedCalendars.SaveAsync(cal);

        var reloaded = await sut.SharedCalendars.GetAsync(Acme, cal.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Q1 Holidays", reloaded!.Name);
        Assert.Equal(2, reloaded.Exceptions.Count);
        Assert.True(reloaded.IsExcepted(new DateOnly(2026, 3, 21)));   // mid-span day
        Assert.False(reloaded.IsExcepted(new DateOnly(2026, 3, 23)));  // day after the span
    }

    // ================================================================
    // 8. Resource-kind generality — a shared holiday applies to an
    //    ASSET (a room) as well as a Party (a doctor)
    // ================================================================

    [Fact]
    public async Task SharedHoliday_AppliesToAnAsset_AsWellAsAParty()
    {
        var sut = NewSut();
        var monday = new DateOnly(2026, 3, 2);
        var wednesday = new DateOnly(2026, 3, 4);

        // A bookable ROOM (Asset), available weekdays 9-5.
        await sut.Availability.SaveAsync(WeekdaysNineToFive(Room, monday, Acme));

        var holidays = SharedCalendar.Create(Acme, "Building Closures")
            .AddException(ExceptionSpan.Create(wednesday, wednesday, "Building closed"));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Room, holidays.Id));

        var fb = await sut.FreeBusy.FreeBusy(Acme, Room, Utc(2026, 3, 2, 0), Utc(2026, 3, 6, 23));

        Assert.Equal(4, fb.FreeSlots.Count);   // the room loses Wednesday too
        Assert.DoesNotContain(fb.FreeSlots, s => s.StartUtc.Date == new DateTime(2026, 3, 4));
    }
}
