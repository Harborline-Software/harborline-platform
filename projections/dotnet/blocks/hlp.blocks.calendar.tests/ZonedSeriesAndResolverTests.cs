using Harborline.Foundation.Scheduling.Models;
using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// T-1175/occurrence-engine in the calendar block: a timed event or availability window expands as a timed series, so a
/// UTC UNTIL bounds it at that instant (RFC 5545 §3.3.10; ADR-0122 decision 3), and local times resolve through the one
/// RFC 5545 §3.3.5 resolver (ADR-0123 decision 4). Expected values are RFC answers and R-0146 call 5's case (d), written
/// as literals (T-724 ruling 121).
/// </summary>
public sealed class ZonedSeriesAndResolverTests
{
    private static readonly TenantId Tenant = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private const string LA = "America/Los_Angeles";
    private const string CaseD = "FREQ=DAILY;UNTIL=20261105T040000Z";

    private static CalendarEventExpansionService Events() => new(new InMemoryRruleExpansionService());

    private static AvailabilityExpansionService Supply() => new(new InMemoryRruleExpansionService());

    private static DateTimeOffset Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T-1175 (d): a timed Los Angeles 20:00 event to UNTIL=20261105T040000Z occurs on 1 to 4 November, not the 5th (RFC 5545 §3.3.10)")]
    public void TimedEventStopsAtTheUntilInstant()
    {
        var ev = CalendarEvent.Create(Tenant, "Evening check", new(2026, 11, 1), new(2026, 11, 1), Actor,
            rrule: CaseD, timezone: LA, startTime: new(20, 0), endTime: new(20, 30));

        var dates = Events().Expand(ev, new(2026, 11, 1), new(2026, 11, 30)).Select(occurrence => occurrence.Start);

        Assert.Equal([new(2026, 11, 1), new(2026, 11, 2), new(2026, 11, 3), new DateOnly(2026, 11, 4)], dates);
    }

    [Fact(DisplayName = "T-1175 (d): an all-day event stays a date-valued series, so a DATE-TIME UNTIL keeps its UTC date (DES-0057 ck-7)")]
    public void AllDayEventKeepsTheDateGranularUntil()
    {
        var ev = CalendarEvent.Create(Tenant, "Closure", new(2026, 11, 1), new(2026, 11, 1), Actor,
            rrule: CaseD, timezone: LA, allDay: true);

        var dates = Events().Expand(ev, new(2026, 11, 1), new(2026, 11, 30)).Select(occurrence => occurrence.Start);

        Assert.Equal([new(2026, 11, 1), new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new DateOnly(2026, 11, 5)], dates);
    }

    [Fact(DisplayName = "T-1175 (d): a timed availability window to UNTIL=20261105T040000Z gives intervals on 1 to 4 November only")]
    public void TimedAvailabilityWindowStopsAtTheUntilInstant()
    {
        var supply = ResourceAvailability.Create(Tenant, Doctor, LA)
            .AddWindow(AvailabilityWindow.Create(new(2026, 11, 1), new(20, 0), new(21, 0), CaseD));

        var starts = Supply().Expand(supply, Utc(11, 1, 0), Utc(11, 30, 0)).Select(interval => interval.StartUtc);

        // 1 November 2026 is the fall-back day, so 20:00 in Los Angeles is already -08:00: 04:00Z the next day, each day.
        Assert.Equal([Utc(11, 2, 4), Utc(11, 3, 4), Utc(11, 4, 4), Utc(11, 5, 4)], starts);
    }

    [Fact]
    public void WindowsTimezoneWorksForRecurringEventsAndAvailability()
    {
        const string zone = "Pacific Standard Time";
        var ev = CalendarEvent.Create(Tenant, "Evening", new(2026, 11, 1), new(2026, 11, 1), Actor,
            rrule: CaseD, timezone: zone, startTime: new(20, 0), endTime: new(20, 30));
        var supply = ResourceAvailability.Create(Tenant, Doctor, zone)
            .AddWindow(AvailabilityWindow.Create(new(2026, 11, 1), new(20, 0), new(21, 0), CaseD));

        Assert.Equal(4, Events().Expand(ev, new(2026, 11, 1), new(2026, 11, 30)).Count);
        Assert.Equal([Utc(11, 2, 4), Utc(11, 3, 4), Utc(11, 4, 4), Utc(11, 5, 4)],
            Events().ExpandInstants(ev, Utc(11, 1, 0), Utc(11, 30, 0)).Select(x => x.StartUtc));
        Assert.Equal([Utc(11, 2, 4), Utc(11, 3, 4), Utc(11, 4, 4), Utc(11, 5, 4)],
            Supply().Expand(supply, Utc(11, 1, 0), Utc(11, 30, 0)).Select(x => x.StartUtc));
    }

    // Oracle: Python zoneinfo (fold=0, the offset before the transition) for each local 23:30 in America/Nuuk.
    [Fact(DisplayName = "T-1175: a daily 23:30 Nuuk event keeps one occurrence per date across the gap, and the gap date resolves at the offset before it")]
    public void GapAcrossMidnightKeepsOneOccurrencePerDate()
    {
        var ev = CalendarEvent.Create(Tenant, "Night check", new(2026, 3, 26), new(2026, 3, 26), Actor,
            rrule: "FREQ=DAILY;COUNT=5", timezone: "America/Nuuk", startTime: new(23, 30), endTime: new(23, 45));

        var starts = Events().ExpandInstants(ev, Utc(3, 26, 0), Utc(4, 2, 0)).Select(occurrence => occurrence.StartUtc);

        Assert.Equal([Utc(3, 27, 1, 30), Utc(3, 28, 1, 30), Utc(3, 29, 1, 30), Utc(3, 30, 0, 30), Utc(3, 31, 0, 30)], starts);
    }

    [Fact(DisplayName = "calendar expansion refuses a null event and an inverted window, and accepts an empty one")]
    public void EventExpansionGuardsItsArguments()
    {
        var ev = CalendarEvent.Create(Tenant, "Intake", new(2026, 2, 10), new(2026, 2, 10), Actor,
            timezone: "UTC", startTime: new(9, 0), endTime: new(9, 30));

        Assert.Throws<ArgumentNullException>(() => Events().Expand(null!, new(2026, 2, 1), new(2026, 2, 28)));
        Assert.Equal("windowEnd", Assert.Throws<ArgumentException>(() => Events().Expand(ev, new(2026, 2, 28), new(2026, 2, 1))).ParamName);
        Assert.Single(Events().Expand(ev, new(2026, 2, 10), new(2026, 2, 10)));

        Assert.Throws<ArgumentNullException>(() => Events().ExpandInstants(null!, Utc(2, 1, 0), Utc(2, 28, 0)));
        Assert.Equal("windowEndUtc", Assert.Throws<ArgumentException>(() => Events().ExpandInstants(ev, Utc(2, 28, 0), Utc(2, 1, 0))).ParamName);
    }

    [Fact(DisplayName = "instant expansion keeps an occurrence that ends exactly at the window start or starts exactly at its end (inclusive bounds)")]
    public void InstantExpansionBoundsAreInclusive()
    {
        var ev = CalendarEvent.Create(Tenant, "Intake", new(2026, 2, 10), new(2026, 2, 10), Actor,
            timezone: "UTC", startTime: new(9, 0), endTime: new(9, 30));

        Assert.Single(Events().ExpandInstants(ev, Utc(2, 10, 9, 30), Utc(2, 10, 12)));
        Assert.Single(Events().ExpandInstants(ev, Utc(2, 10, 6), Utc(2, 10, 9)));
        Assert.Single(Events().ExpandInstants(ev, Utc(2, 10, 9, 15), Utc(2, 10, 9, 15)));
        Assert.Empty(Events().ExpandInstants(ev, Utc(2, 10, 9, 31), Utc(2, 10, 12)));
    }

    [Fact(DisplayName = "availability expansion refuses a missing engine, availability or exception set, and an inverted window")]
    public void AvailabilityExpansionGuardsItsArguments()
    {
        var supply = ResourceAvailability.Create(Tenant, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(new(2026, 3, 2), new(9, 0), new(17, 0)));

        Assert.Throws<ArgumentNullException>(() => new AvailabilityExpansionService(null!));
        Assert.Throws<ArgumentNullException>(() => Supply().Expand(null!, Utc(3, 1, 0), Utc(3, 31, 0)));
        Assert.Throws<ArgumentNullException>(() => Supply().Expand(supply, Utc(3, 1, 0), Utc(3, 31, 0), null!));
        Assert.Equal("windowEndUtc", Assert.Throws<ArgumentException>(() => Supply().Expand(supply, Utc(3, 31, 0), Utc(3, 1, 0))).ParamName);
        // An empty window is allowed (it is not inverted); it clips every interval to nothing.
        Assert.Empty(Supply().Expand(supply, Utc(3, 2, 10), Utc(3, 2, 10)));
    }

    [Fact(DisplayName = "a single availability window applies on its anchor date only, including when the anchor is the first or last local window day")]
    public void SingleWindowAppliesOnItsAnchorOnly()
    {
        var supply = ResourceAvailability.Create(Tenant, Doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(new(2026, 3, 2), new(9, 0), new(17, 0)));

        Assert.Single(Supply().Expand(supply, Utc(3, 2, 0), Utc(3, 2, 23)));
        Assert.Empty(Supply().Expand(supply, Utc(3, 4, 0), Utc(3, 8, 0)));
        Assert.Empty(Supply().Expand(supply, Utc(2, 20, 0), Utc(2, 28, 0)));
    }

    [Fact(DisplayName = "T-1175 (e): the calendar resolver gives the RFC 5545 §3.3.5 instant for an ambiguous and a gap time, by IANA or Windows zone id")]
    public void TimezoneResolverDelegatesToTheSharedResolver()
    {
        var newYork = TimezoneResolver.Resolve("America/New_York");
        Assert.Equal(Utc(11, 1, 5, 30), TimezoneResolver.ToUtcInstant(new(2026, 11, 1), new(1, 30), newYork));
        Assert.Equal(Utc(3, 8, 7, 30), TimezoneResolver.ToUtcInstant(new(2026, 3, 8), new(2, 30), newYork));

        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Assert.Equal(Utc(11, 1, 5, 30), TimezoneResolver.ToUtcInstant(new(2026, 11, 1), new(1, 30), eastern));
    }

    [Fact(DisplayName = "the calendar resolver refuses a blank zone id, a missing zone and a zone with no IANA id")]
    public void TimezoneResolverRefusesWhatItCannotResolve()
    {
        Assert.Throws<ArgumentException>(() => TimezoneResolver.Resolve(" "));
        Assert.Throws<ArgumentNullException>(() => TimezoneResolver.ToUtcInstant(new(2026, 1, 1), new(9, 0), null!));
        var custom = TimeZoneInfo.CreateCustomTimeZone("Harborline/Test", TimeSpan.FromHours(3), "Test", "Test");
        Assert.Throws<ArgumentException>(() => TimezoneResolver.ToUtcInstant(new(2026, 1, 1), new(9, 0), custom));
    }
}
