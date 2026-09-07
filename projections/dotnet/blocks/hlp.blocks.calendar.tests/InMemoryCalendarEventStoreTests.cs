using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for <see cref="InMemoryCalendarEventStore"/> — the date-granular series/occurrence
/// store. Round-trips go through real (de)serialization, so EXDATE + RECURRENCE-ID overrides must
/// survive persistence intact.
/// </summary>
public sealed class InMemoryCalendarEventStoreTests
{
    private static readonly TenantId Tenant = new("acme");
    private static readonly TenantId OtherTenant = new("globex");
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task SaveThenGet_RoundTripsTemporalCore()
    {
        var store = new InMemoryCalendarEventStore();
        var ev = CalendarEvent.Create(
            Tenant, "Quarterly review", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 6), Actor,
            rrule: "FREQ=MONTHLY;INTERVAL=3", timezone: "America/Los_Angeles");

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ev.Id, loaded!.Id);
        Assert.Equal(ev.Title, loaded.Title);
        Assert.Equal(ev.Start, loaded.Start);
        Assert.Equal(ev.End, loaded.End);
        Assert.Equal(ev.Rrule, loaded.Rrule);
        Assert.Equal("America/Los_Angeles", loaded.Timezone);
        Assert.Equal(ev.Status, loaded.Status);
        Assert.Equal(ev.CreatedBy, loaded.CreatedBy);
        Assert.Equal(ev.Version, loaded.Version);
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsExdatesAndOverrides()
    {
        var store = new InMemoryCalendarEventStore();
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");

        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);
        ev.OverrideOccurrence(
            new OccurrenceOverride
            {
                RecurrenceId = new DateOnly(2026, 1, 19),
                NewStart     = new DateOnly(2026, 1, 20),
                NewEnd       = new DateOnly(2026, 1, 20),
                NewTitle     = "Standup (moved)",
            },
            Actor);

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.NotNull(loaded);

        // EXDATE survived.
        Assert.Contains(new DateOnly(2026, 1, 12), loaded!.ExceptionDates);
        Assert.Single(loaded.ExceptionDates);

        // RECURRENCE-ID override survived with all its fields.
        Assert.True(loaded.Overrides.TryGetValue(new DateOnly(2026, 1, 19), out var ov));
        Assert.Equal(new DateOnly(2026, 1, 20), ov!.NewStart);
        Assert.Equal(new DateOnly(2026, 1, 20), ov.NewEnd);
        Assert.Equal("Standup (moved)", ov.NewTitle);
        Assert.False(ov.IsCancelled);

        // And the loaded entity expands identically to the in-memory one.
        var sut = new CalendarEventExpansionService(new Foundation.Scheduling.InMemoryRruleExpansionService());
        var before = sut.Expand(ev, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        var after = sut.Expand(loaded, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));
        Assert.Equal(before.Select(o => (o.Start, o.Title, o.IsOverride)),
                     after.Select(o => (o.Start, o.Title, o.IsOverride)));
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsTimeOfDay()
    {
        // S1: the wall-clock StartTime/EndTime must survive serialization round-trip.
        var store = new InMemoryCalendarEventStore();
        var ev = CalendarEvent.Create(
            Tenant, "Timed standup", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: "America/Los_Angeles",
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new TimeOnly(9, 0), loaded!.StartTime);
        Assert.Equal(new TimeOnly(9, 30), loaded.EndTime);

        // And the loaded entity expands to identical UTC instants.
        var sut = new CalendarEventExpansionService(new Foundation.Scheduling.InMemoryRruleExpansionService());
        var winStart = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var winEnd = new DateTimeOffset(2026, 1, 31, 23, 59, 0, TimeSpan.Zero);
        var before = sut.ExpandInstants(ev, winStart, winEnd);
        var after = sut.ExpandInstants(loaded, winStart, winEnd);
        Assert.Equal(before.Select(o => (o.StartUtc, o.EndUtc, o.Title)),
                     after.Select(o => (o.StartUtc, o.EndUtc, o.Title)));
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsExplicitAllDayCivilDates()
    {
        var store = new InMemoryCalendarEventStore();
        var ev = CalendarEvent.Create(
            Tenant, "DST fallback weekend", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), Actor,
            timezone: "America/New_York", allDay: true);

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.NotNull(loaded);
        Assert.True(loaded!.AllDay);
        // Midnight UTC for these dates is still the prior civil day in New York. Persisting instants
        // rather than DateOnly values would roll the all-day event backwards across the DST boundary.
        Assert.Equal(new DateOnly(2026, 11, 1), loaded.Start);
        Assert.Equal(new DateOnly(2026, 11, 2), loaded.End);
        Assert.Equal(TimeOnly.MinValue, loaded.StartTime);
        Assert.Equal(TimeOnly.MinValue, loaded.EndTime);
    }

    [Fact]
    public void AllDayCreation_RejectsWallClockFields()
    {
        Assert.Throws<ArgumentException>(() => CalendarEvent.Create(
            Tenant, "Invalid all-day event", new DateOnly(2026, 3, 8), new DateOnly(2026, 3, 8), Actor,
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(10, 0), allDay: true));
    }

    [Fact]
    public async Task SaveThenGet_RoundTripsOverrideTimeOfDay()
    {
        // S1: an override's NewStartTime/NewEndTime must survive too.
        var store = new InMemoryCalendarEventStore();
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(
            Tenant, "Standup", anchor, anchor, Actor,
            rrule: "FREQ=WEEKLY;BYDAY=MO", timezone: "America/Los_Angeles",
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

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.True(loaded!.Overrides.TryGetValue(new DateOnly(2026, 1, 19), out var ov));
        Assert.Equal(new TimeOnly(8, 0), ov!.NewStartTime);
        Assert.Equal(new TimeOnly(8, 30), ov.NewEndTime);
    }

    [Fact]
    public async Task Get_IsTenantScoped()
    {
        var store = new InMemoryCalendarEventStore();
        var ev = CalendarEvent.Create(Tenant, "Private", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor);
        await store.SaveAsync(ev);

        // Same id, different tenant → not found (composite (TenantId, Id) keying).
        Assert.Null(await store.GetAsync(OtherTenant, ev.Id));
        Assert.NotNull(await store.GetAsync(Tenant, ev.Id));
    }

    [Fact]
    public async Task List_ReturnsOnlyTenantEvents_OrderedByStart()
    {
        var store = new InMemoryCalendarEventStore();
        var a = CalendarEvent.Create(Tenant, "March", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), Actor);
        var b = CalendarEvent.Create(Tenant, "January", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), Actor);
        var c = CalendarEvent.Create(OtherTenant, "Other", new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1), Actor);
        await store.SaveAsync(a);
        await store.SaveAsync(b);
        await store.SaveAsync(c);

        var list = await store.ListAsync(Tenant);

        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "January", "March" }, list.Select(e => e.Title)); // ordered by Start
        Assert.DoesNotContain(list, e => e.Title == "Other");
    }

    [Fact]
    public async Task Save_IsUpsert_AndPreservesLatestState()
    {
        var store = new InMemoryCalendarEventStore();
        var anchor = new DateOnly(2026, 1, 5);
        var ev = CalendarEvent.Create(Tenant, "Standup", anchor, anchor, Actor, rrule: "FREQ=WEEKLY;BYDAY=MO");
        await store.SaveAsync(ev);

        // Mutate + re-save: the upsert replaces, the EXDATE is now present.
        ev.CancelOccurrence(new DateOnly(2026, 1, 12), Actor);
        await store.SaveAsync(ev);

        var loaded = await store.GetAsync(Tenant, ev.Id);
        Assert.Contains(new DateOnly(2026, 1, 12), loaded!.ExceptionDates);
        Assert.Single(await store.ListAsync(Tenant)); // still one event, not duplicated
    }

    [Fact]
    public async Task Remove_IsIdempotent()
    {
        var store = new InMemoryCalendarEventStore();
        var ev = CalendarEvent.Create(Tenant, "Temp", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 5), Actor);
        await store.SaveAsync(ev);

        Assert.True(await store.RemoveAsync(Tenant, ev.Id));
        Assert.False(await store.RemoveAsync(Tenant, ev.Id)); // second remove is a no-op
        Assert.Null(await store.GetAsync(Tenant, ev.Id));
    }
}
