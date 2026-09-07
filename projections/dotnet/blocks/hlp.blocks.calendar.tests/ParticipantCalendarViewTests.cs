using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S2 <b>calendar-as-a-view</b> query
/// (<see cref="ICalendarParticipantCalendarQuery"/>): a participant's calendar is derived from the
/// events they participate in. The query must return ONLY events where the participant participates,
/// be tenant-scoped, and never leak one tenant's events into another's view (cross-tenant isolation).
/// </summary>
public sealed class ParticipantCalendarViewTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly TenantId Globex = new("globex");
    private static readonly Guid Actor = Guid.NewGuid();

    private static (ICalendarEventStore store, ICalendarParticipantCalendarQuery query) NewSut()
    {
        var store = new InMemoryCalendarEventStore();
        var expansion = new CalendarEventExpansionService(new InMemoryRruleExpansionService());
        var query = new CalendarParticipantCalendarQuery(store, expansion);
        return (store, query);
    }

    private static CalendarEvent SingleEvent(TenantId tenant, string title, DateOnly day)
        => CalendarEvent.Create(tenant, title, day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

    private static readonly DateOnly WindowStart = new(2026, 3, 1);
    private static readonly DateOnly WindowEnd = new(2026, 3, 31);

    // ----------------------------------------------------------------
    // EventsFor returns only events where the participant participates
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventsFor_ReturnsOnlyEventsWhereParticipantParticipates()
    {
        var (store, query) = NewSut();
        var doctor = ParticipantRef.Party("party-dr-smith");
        var otherDoctor = ParticipantRef.Party("party-dr-jones");

        var mine = SingleEvent(Acme, "Dr Smith appt", new DateOnly(2026, 3, 10));
        mine.SetResource(doctor, Actor);
        await store.SaveAsync(mine);

        var theirs = SingleEvent(Acme, "Dr Jones appt", new DateOnly(2026, 3, 11));
        theirs.SetResource(otherDoctor, Actor);
        await store.SaveAsync(theirs);

        var unrelated = SingleEvent(Acme, "Nobody assigned", new DateOnly(2026, 3, 12)); // no participations
        await store.SaveAsync(unrelated);

        var calendar = await query.EventsFor(Acme, doctor, WindowStart, WindowEnd);

        var ev = Assert.Single(calendar);
        Assert.Equal(mine.Id, ev.Id);
    }

    [Fact]
    public async Task EventsFor_FindsParticipantInAnyRole()
    {
        var (store, query) = NewSut();
        var patient = ParticipantRef.Party("party-patient");
        var room = ParticipantRef.Asset("asset-room-1");

        var appt = SingleEvent(Acme, "Checkup", new DateOnly(2026, 3, 10));
        appt.SetResource(ParticipantRef.Party("party-dr"), Actor);
        appt.AddParticipation(CalendarParticipation.Attendee(patient), Actor);   // attendee role
        appt.AddParticipation(CalendarParticipation.Resource(room), Actor);      // asset-resource role
        await store.SaveAsync(appt);

        // The patient's calendar (attendee) and the room's calendar (resource) both show it.
        Assert.Single(await query.EventsFor(Acme, patient, WindowStart, WindowEnd));
        Assert.Single(await query.EventsFor(Acme, room, WindowStart, WindowEnd));
    }

    [Fact]
    public async Task EventsFor_ExcludesEventsWithNoOccurrenceInWindow()
    {
        var (store, query) = NewSut();
        var doctor = ParticipantRef.Party("party-dr");

        var inWindow = SingleEvent(Acme, "March appt", new DateOnly(2026, 3, 10));
        inWindow.SetResource(doctor, Actor);
        await store.SaveAsync(inWindow);

        var outOfWindow = SingleEvent(Acme, "May appt", new DateOnly(2026, 5, 10));
        outOfWindow.SetResource(doctor, Actor);
        await store.SaveAsync(outOfWindow);

        var calendar = await query.EventsFor(Acme, doctor, WindowStart, WindowEnd);
        var ev = Assert.Single(calendar);
        Assert.Equal(inWindow.Id, ev.Id);
    }

    [Fact]
    public async Task EventsFor_ExcludesCancelledEvent()
    {
        var (store, query) = NewSut();
        var doctor = ParticipantRef.Party("party-dr");

        var cancelled = SingleEvent(Acme, "Cancelled appt", new DateOnly(2026, 3, 10));
        cancelled.SetResource(doctor, Actor);
        cancelled.Cancel(Actor);
        await store.SaveAsync(cancelled);

        Assert.Empty(await query.EventsFor(Acme, doctor, WindowStart, WindowEnd));
    }

    [Fact]
    public async Task EventsFor_RecurringSeries_AppearsWhenItHasAnOccurrenceInWindow()
    {
        var (store, query) = NewSut();
        var doctor = ParticipantRef.Party("party-dr");

        // Weekly Monday standup, anchored Mon 2026-03-02.
        var series = CalendarEvent.Create(
            Acme, "Weekly clinic", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2), Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: "UTC");
        series.SetResource(doctor, Actor);
        await store.SaveAsync(series);

        var ev = Assert.Single(await query.EventsFor(Acme, doctor, WindowStart, WindowEnd));
        Assert.Equal(series.Id, ev.Id);

        // The occurrences view flattens all March Mondays (2,9,16,23,30).
        var occ = await query.OccurrencesFor(Acme, doctor, WindowStart, WindowEnd);
        Assert.Equal(5, occ.Count);
        Assert.All(occ, o => Assert.Equal(series.Id, o.EventId));
    }

    // ----------------------------------------------------------------
    // Tenant scoping + cross-tenant isolation
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventsFor_IsTenantScoped_NeverLeaksAcrossTenants()
    {
        var (store, query) = NewSut();

        // SAME participant id value in two tenants (collision) — each tenant's calendar is isolated.
        var sharedRefValue = "party-dr-smith";
        var acmeDoctor = ParticipantRef.Party(sharedRefValue);
        var globexDoctor = ParticipantRef.Party(sharedRefValue);

        var acmeAppt = SingleEvent(Acme, "Acme appt", new DateOnly(2026, 3, 10));
        acmeAppt.SetResource(acmeDoctor, Actor);
        await store.SaveAsync(acmeAppt);

        var globexAppt = SingleEvent(Globex, "Globex appt", new DateOnly(2026, 3, 10));
        globexAppt.SetResource(globexDoctor, Actor);
        await store.SaveAsync(globexAppt);

        // Acme's view shows only the Acme appt, even though the participant id collides with Globex.
        var acmeCalendar = await query.EventsFor(Acme, acmeDoctor, WindowStart, WindowEnd);
        var a = Assert.Single(acmeCalendar);
        Assert.Equal(acmeAppt.Id, a.Id);
        Assert.Equal(Acme, a.TenantId);

        // Globex's view shows only the Globex appt.
        var globexCalendar = await query.EventsFor(Globex, globexDoctor, WindowStart, WindowEnd);
        var g = Assert.Single(globexCalendar);
        Assert.Equal(globexAppt.Id, g.Id);
        Assert.Equal(Globex, g.TenantId);
    }

    [Fact]
    public async Task EventsFor_EmptyWhenParticipantHasNoEvents()
    {
        var (store, query) = NewSut();
        var lonely = SingleEvent(Acme, "Someone else's appt", new DateOnly(2026, 3, 10));
        lonely.SetResource(ParticipantRef.Party("party-someone"), Actor);
        await store.SaveAsync(lonely);

        Assert.Empty(await query.EventsFor(Acme, ParticipantRef.Party("party-nobody"), WindowStart, WindowEnd));
    }

    [Fact]
    public async Task EventsFor_OrdersByStartThenId()
    {
        var (store, query) = NewSut();
        var doctor = ParticipantRef.Party("party-dr");

        var later = SingleEvent(Acme, "Later", new DateOnly(2026, 3, 20));
        later.SetResource(doctor, Actor);
        var earlier = SingleEvent(Acme, "Earlier", new DateOnly(2026, 3, 5));
        earlier.SetResource(doctor, Actor);
        await store.SaveAsync(later);
        await store.SaveAsync(earlier);

        var calendar = await query.EventsFor(Acme, doctor, WindowStart, WindowEnd);
        Assert.Equal(new[] { earlier.Id, later.Id }, calendar.Select(e => e.Id));
    }

    [Fact]
    public async Task EventsFor_RejectsInvertedWindow()
    {
        var (_, query) = NewSut();
        await Assert.ThrowsAsync<ArgumentException>(
            () => query.EventsFor(Acme, ParticipantRef.Party("p"), WindowEnd, WindowStart));
    }
}
