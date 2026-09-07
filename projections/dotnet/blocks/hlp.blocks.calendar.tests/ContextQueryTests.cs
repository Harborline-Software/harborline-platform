using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S3 <b>by-context</b> query (<see cref="ICalendarContextQuery.EventsForContext"/>)
/// — the coverage substrate. The core exposes "what is scheduled against this context" (a floor /
/// project / case) without computing anything about coverage. Tenant-scoped + cross-tenant isolated,
/// symmetric to the S2 by-participant <c>EventsFor</c>.
/// </summary>
public sealed class ContextQueryTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly TenantId Globex = new("globex");
    private static readonly Guid Actor = Guid.NewGuid();

    private static (ICalendarEventStore store, ICalendarContextQuery query) NewSut()
    {
        var store = new InMemoryCalendarEventStore();
        var expansion = new CalendarEventExpansionService(new InMemoryRruleExpansionService());
        var query = new CalendarContextQuery(store, expansion);
        return (store, query);
    }

    private static CalendarEvent EventOn(TenantId tenant, string title, DateOnly day)
        => CalendarEvent.Create(tenant, title, day, day, Actor,
            timezone: "UTC", startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

    private static readonly DateOnly WindowStart = new(2026, 3, 1);
    private static readonly DateOnly WindowEnd = new(2026, 3, 31);

    // ----------------------------------------------------------------
    // EventsForContext returns only events scheduled against that context
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventsForContext_ReturnsOnlyEventsScheduledAgainstThatContext()
    {
        var (store, query) = NewSut();
        var icu = ContextRef.Of("floor", "icu-3");
        var ward = ContextRef.Of("floor", "ward-b");

        var nurseShift = EventOn(Acme, "ICU nurse shift", new DateOnly(2026, 3, 10));
        nurseShift.SetScheduledAgainst(icu, Actor);
        await store.SaveAsync(nurseShift);

        var otherShift = EventOn(Acme, "Ward-B shift", new DateOnly(2026, 3, 11));
        otherShift.SetScheduledAgainst(ward, Actor);
        await store.SaveAsync(otherShift);

        var noContext = EventOn(Acme, "Unanchored", new DateOnly(2026, 3, 12)); // ScheduledAgainst null
        await store.SaveAsync(noContext);

        var scheduled = await query.EventsForContext(Acme, icu, WindowStart, WindowEnd);

        var ev = Assert.Single(scheduled);
        Assert.Equal(nurseShift.Id, ev.Id);
    }

    [Fact]
    public async Task EventsForContext_MatchesByValue_NotReference()
    {
        var (store, query) = NewSut();
        var project = ContextRef.Of("project", "proj-42");

        var task = EventOn(Acme, "Project task", new DateOnly(2026, 3, 10));
        task.SetScheduledAgainst(project, Actor);
        await store.SaveAsync(task);

        // A freshly-constructed-but-equal context ref still matches (value equality).
        var sameProject = ContextRef.Of("project", "proj-42");
        var scheduled = await query.EventsForContext(Acme, sameProject, WindowStart, WindowEnd);

        Assert.Single(scheduled);
    }

    [Fact]
    public async Task EventsForContext_DistinguishesKind_SameValueDifferentKind()
    {
        var (store, query) = NewSut();
        // Same value "42", different kinds — must NOT collide.
        var asFloor = ContextRef.Of("floor", "42");
        var asProject = ContextRef.Of("project", "42");

        var floorEvent = EventOn(Acme, "Floor 42 shift", new DateOnly(2026, 3, 10));
        floorEvent.SetScheduledAgainst(asFloor, Actor);
        await store.SaveAsync(floorEvent);

        var scheduledAgainstProject = await query.EventsForContext(Acme, asProject, WindowStart, WindowEnd);

        Assert.Empty(scheduledAgainstProject);
    }

    // ----------------------------------------------------------------
    // The coverage substrate: multiple events against one context
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventsForContext_ReturnsAllEventsAgainstAContext_TheCoverageSubstrate()
    {
        var (store, query) = NewSut();
        var icu = ContextRef.Of("floor", "icu-3");

        // Three nurses scheduled against the ICU on the same day — the raw data a coverage overlay
        // would count against its minimum (the calendar computes nothing about that minimum).
        for (var i = 0; i < 3; i++)
        {
            var shift = EventOn(Acme, $"Nurse {i} ICU shift", new DateOnly(2026, 3, 10));
            shift.SetScheduledAgainst(icu, Actor);
            shift.AddParticipation(CalendarParticipation.Attendee(ParticipantRef.Party($"party-nurse-{i}")), Actor);
            await store.SaveAsync(shift);
        }

        var scheduled = await query.EventsForContext(Acme, icu, WindowStart, WindowEnd);

        Assert.Equal(3, scheduled.Count);
        // The participants are exposed for the overlay to count — the core does not count them.
        Assert.All(scheduled, ev => Assert.Single(ev.Participations));
    }

    // ----------------------------------------------------------------
    // Tenant isolation
    // ----------------------------------------------------------------

    [Fact]
    public async Task EventsForContext_IsTenantScoped()
    {
        var (store, query) = NewSut();
        var icu = ContextRef.Of("floor", "icu-3");

        var acmeShift = EventOn(Acme, "Acme ICU shift", new DateOnly(2026, 3, 10));
        acmeShift.SetScheduledAgainst(icu, Actor);
        await store.SaveAsync(acmeShift);

        // Globex schedules against the SAME context value — must not leak into Acme's view.
        var globexShift = EventOn(Globex, "Globex ICU shift", new DateOnly(2026, 3, 10));
        globexShift.SetScheduledAgainst(icu, Actor);
        await store.SaveAsync(globexShift);

        var acmeView = await query.EventsForContext(Acme, icu, WindowStart, WindowEnd);
        var ev = Assert.Single(acmeView);
        Assert.Equal(acmeShift.Id, ev.Id);

        var globexView = await query.EventsForContext(Globex, icu, WindowStart, WindowEnd);
        Assert.Equal(globexShift.Id, Assert.Single(globexView).Id);
    }

    [Fact]
    public async Task EventsForContext_ExcludesEventsOutsideTheWindow()
    {
        var (store, query) = NewSut();
        var icu = ContextRef.Of("floor", "icu-3");

        var outOfWindow = EventOn(Acme, "Feb shift", new DateOnly(2026, 2, 10));
        outOfWindow.SetScheduledAgainst(icu, Actor);
        await store.SaveAsync(outOfWindow);

        var scheduled = await query.EventsForContext(Acme, icu, WindowStart, WindowEnd);
        Assert.Empty(scheduled);
    }

    [Fact]
    public async Task EventsForContext_ExcludesCancelledEvents()
    {
        var (store, query) = NewSut();
        var icu = ContextRef.Of("floor", "icu-3");

        var cancelled = EventOn(Acme, "Cancelled ICU shift", new DateOnly(2026, 3, 10));
        cancelled.SetScheduledAgainst(icu, Actor);
        cancelled.Cancel(Actor);
        await store.SaveAsync(cancelled);

        var scheduled = await query.EventsForContext(Acme, icu, WindowStart, WindowEnd);
        Assert.Empty(scheduled);
    }
}
