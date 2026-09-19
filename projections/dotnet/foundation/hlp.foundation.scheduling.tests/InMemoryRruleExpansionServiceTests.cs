using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

/// <summary>
/// Coverage for <see cref="InMemoryRruleExpansionService"/> implementing
/// the fleet bounded RRULE subset per council-verdict-net-arch-2026-06-12T2213Z §C-2.
///
/// Tests migrated from Harborline.Blocks.WorkOrders.Tests plus new coverage
/// for BYMONTHDAY, BYDAY (simple and ordinal), COUNT, UNTIL, YEARLY,
/// and the "monthly on the 1st" correctness case named in §C-2.
/// </summary>
public sealed class InMemoryRruleExpansionServiceTests
{
    private static readonly IRruleExpansionService Sut = new InMemoryRruleExpansionService();
    // Anchor for deterministic tests: 2026-01-01 (Thursday).
    private static readonly DateOnly Today = new(2026, 1, 1);

    // ----------------------------------------------------------------
    // Migrated from blocks-work-orders (must stay green — regression guard)
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_DailyFreq_ReturnsCorrectCount()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY",
            start: Today,
            end: null,
            lookaheadDays: 10,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        // start + lookahead 10 days, inclusive of both ends: 11 occurrences.
        Assert.Equal(11, occurrences.Count);
        Assert.Equal(Today, occurrences[0]);
        Assert.Equal(Today.AddDays(10), occurrences[^1]);
    }

    [Fact]
    public void Expand_MonthlyInterval3_ReturnsQuarterly()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;INTERVAL=3",
            start: Today,
            end: new DateOnly(2026, 12, 31),
            lookaheadDays: 365,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        // Jan 1, Apr 1, Jul 1, Oct 1 = 4 occurrences within 2026.
        Assert.Equal(4, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), occurrences[0]);
        Assert.Equal(new DateOnly(2026, 10, 1), occurrences[^1]);
    }

    [Fact]
    public void Expand_WeeklyInterval2_ReturnsBiweekly()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=WEEKLY;INTERVAL=2",
            start: Today,
            end: Today.AddDays(28),
            lookaheadDays: 28,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        // Jan 1, Jan 15, Jan 29 — 3 occurrences (Jan 29 = today+28).
        Assert.Equal(3, occurrences.Count);
    }

    [Fact]
    public void Expand_EndsOn_DoesNotExceedBound()
    {
        var hardEnd = new DateOnly(2026, 1, 5);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY",
            start: Today,
            end: hardEnd,
            lookaheadDays: 365,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.All(occurrences, d => Assert.True(d <= hardEnd));
        Assert.Equal(hardEnd, occurrences[^1]);
    }

    [Fact]
    public void Expand_LeadDaysHonored_SkipsEarlyOccurrences()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY",
            start: Today,
            end: Today.AddDays(10),
            lookaheadDays: 10,
            leadDays: 5,
            today: Today,
            timezone: "UTC");

        // leadDays=5 + today=Jan 1 → earliest = Jan 6.
        Assert.All(occurrences, d => Assert.True(d >= Today.AddDays(5)));
    }

    [Fact]
    public void Expand_MissingFreq_Throws()
    {
        Assert.Throws<FormatException>(() => Sut.ExpandOccurrences(
            rrule: "INTERVAL=2",
            start: Today, end: null,
            lookaheadDays: 10, leadDays: 0,
            today: Today, timezone: "UTC"));
    }

    [Fact]
    public void Expand_EmptyRrule_Throws()
    {
        Assert.Throws<ArgumentException>(() => Sut.ExpandOccurrences(
            rrule: "  ",
            start: Today, end: null,
            lookaheadDays: 10, leadDays: 0,
            today: Today, timezone: "UTC"));
    }

    // ----------------------------------------------------------------
    // New: YEARLY
    // (previously threw NotSupportedException — now supported)
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_YearlyFreq_ReturnsAnnualOccurrences()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=YEARLY",
            start: Today,
            end: new DateOnly(2030, 12, 31),
            lookaheadDays: 365 * 5,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        // 2026, 2027, 2028, 2029, 2030 = 5 occurrences on Jan 1.
        Assert.Equal(5, occurrences.Count);
        Assert.All(occurrences, d => Assert.Equal(1, d.Day));
        Assert.All(occurrences, d => Assert.Equal(1, d.Month));
    }

    // ----------------------------------------------------------------
    // New: BYMONTHDAY — the named correctness gap from §C-2
    // "rent due on the 1st" is the single most common recurring-invoice
    // rule; the prior stub silently ignored BYMONTHDAY entirely.
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Monthly_ByMonthDay1_RentDueOnFirst()
    {
        // CANONICAL C-2 test: FREQ=MONTHLY;BYMONTHDAY=1 ("monthly on the 1st").
        // Anchor: 2026-01-15 (mid-month); first occurrence should be Feb 1.
        var anchor = new DateOnly(2026, 1, 15);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;BYMONTHDAY=1",
            start: anchor,
            end: new DateOnly(2026, 6, 30),
            lookaheadDays: 180,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        // Feb 1, Mar 1, Apr 1, May 1, Jun 1 = 5 occurrences.
        Assert.Equal(5, occurrences.Count);
        Assert.All(occurrences, d => Assert.Equal(1, d.Day));
        Assert.Equal(new DateOnly(2026, 2, 1), occurrences[0]);
        Assert.Equal(new DateOnly(2026, 6, 1), occurrences[^1]);
    }

    [Fact]
    public void Expand_Monthly_ByMonthDay1_AnchorOnFirst_IncludesAnchor()
    {
        // When anchor IS the 1st, it should be included as first occurrence.
        var anchor = new DateOnly(2026, 1, 1);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;BYMONTHDAY=1",
            start: anchor,
            end: new DateOnly(2026, 3, 31),
            lookaheadDays: 90,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), occurrences[0]);
        Assert.Equal(new DateOnly(2026, 3, 1), occurrences[^1]);
    }

    [Fact]
    public void Expand_Monthly_ByMonthDay15_ReturnsCorrectDays()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;BYMONTHDAY=15",
            start: Today,
            end: new DateOnly(2026, 6, 30),
            lookaheadDays: 180,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.All(occurrences, d => Assert.Equal(15, d.Day));
        Assert.True(occurrences.Count >= 5); // Jan 15 ... Jun 15
    }

    // ----------------------------------------------------------------
    // New: BYDAY simple (weekly)
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Weekly_ByDay_MoWeFr_ReturnsCorrectDays()
    {
        // Mon/Wed/Fri for 2 weeks from 2026-01-05 (Monday).
        var anchor = new DateOnly(2026, 1, 5);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=WEEKLY;BYDAY=MO,WE,FR",
            start: anchor,
            end: new DateOnly(2026, 1, 18),
            lookaheadDays: 14,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        // Jan 5(Mo), 7(We), 9(Fr), 12(Mo), 14(We), 16(Fr) = 6
        Assert.Equal(6, occurrences.Count);
        Assert.All(occurrences, d => Assert.Contains(d.DayOfWeek,
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }));
    }

    [Fact]
    public void Expand_WeeklyInterval2_ByDay_HonorsIntervalFromStart()
    {
        // The anchor is a Wednesday so the test also proves that INTERVAL is
        // measured across RFC weeks, not seven-day buckets starting at DTSTART.
        var anchor = new DateOnly(2026, 1, 7);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO",
            start: anchor,
            end: new DateOnly(2026, 2, 9),
            lookaheadDays: 35,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(
            [
                new DateOnly(2026, 1, 19),
                new DateOnly(2026, 2, 2),
            ],
            occurrences);
    }

    // ----------------------------------------------------------------
    // New: BYDAY ordinal (monthly) — "1MO" = first Monday
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Monthly_ByDay_FirstMonday_CorrectDates()
    {
        // FREQ=MONTHLY;BYDAY=1MO — first Monday of each month.
        // Jan 2026 first Monday = Jan 5.
        var anchor = new DateOnly(2026, 1, 1);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;BYDAY=1MO",
            start: anchor,
            end: new DateOnly(2026, 4, 30),
            lookaheadDays: 120,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(4, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 5), occurrences[0]);
        Assert.Equal(new DateOnly(2026, 2, 2), occurrences[1]);
        Assert.Equal(new DateOnly(2026, 3, 2), occurrences[2]);
        Assert.Equal(new DateOnly(2026, 4, 6), occurrences[3]);
        Assert.All(occurrences, d => Assert.Equal(DayOfWeek.Monday, d.DayOfWeek));
    }

    [Fact]
    public void Expand_Monthly_ByDay_LastFriday_CorrectDates()
    {
        // FREQ=MONTHLY;BYDAY=-1FR — last Friday of each month.
        // Jan 2026 last Friday = Jan 30.
        var anchor = new DateOnly(2026, 1, 1);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;BYDAY=-1FR",
            start: anchor,
            end: new DateOnly(2026, 3, 31),
            lookaheadDays: 90,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 30), occurrences[0]);
        Assert.Equal(new DateOnly(2026, 2, 27), occurrences[1]);
        Assert.Equal(new DateOnly(2026, 3, 27), occurrences[2]);
        Assert.All(occurrences, d => Assert.Equal(DayOfWeek.Friday, d.DayOfWeek));
    }

    [Fact]
    public void Expand_MonthlyInterval3_ByMonthDay_HonorsIntervalFromStart()
    {
        var anchor = new DateOnly(2026, 1, 1);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=15",
            start: anchor,
            end: new DateOnly(2026, 12, 31),
            lookaheadDays: 365,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(
            [
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 4, 15),
                new DateOnly(2026, 7, 15),
                new DateOnly(2026, 10, 15),
            ],
            occurrences);
    }

    [Fact]
    public void Expand_MonthlyInterval3_ByDay_HonorsIntervalFromStart()
    {
        var anchor = new DateOnly(2026, 1, 1);
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=MONTHLY;INTERVAL=3;BYDAY=1MO",
            start: anchor,
            end: new DateOnly(2026, 12, 31),
            lookaheadDays: 365,
            leadDays: 0,
            today: anchor,
            timezone: "UTC");

        Assert.Equal(
            [
                new DateOnly(2026, 1, 5),
                new DateOnly(2026, 4, 6),
                new DateOnly(2026, 7, 6),
                new DateOnly(2026, 10, 5),
            ],
            occurrences);
    }

    [Fact]
    public void Expand_UnsupportedComponent_IsRefused()
    {
        var exception = Assert.Throws<NotSupportedException>(() => Sut.ExpandOccurrences(
            rrule: "FREQ=WEEKLY;BYWEEKNO=2",
            start: Today,
            end: Today.AddDays(30),
            lookaheadDays: 30,
            leadDays: 0,
            today: Today,
            timezone: "UTC"));

        Assert.Contains("BYWEEKNO", exception.Message, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------
    // New: COUNT
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Count_LimitsOccurrences()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY;COUNT=5",
            start: Today,
            end: null,
            lookaheadDays: 365,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.Equal(5, occurrences.Count);
        Assert.Equal(Today, occurrences[0]);
        Assert.Equal(Today.AddDays(4), occurrences[^1]);
    }

    // ----------------------------------------------------------------
    // New: UNTIL
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Until_StopsAtBoundary()
    {
        // UNTIL=20260110 → only Jan 1..10.
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY;UNTIL=20260110",
            start: Today,
            end: null,
            lookaheadDays: 365,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.Equal(10, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 10), occurrences[^1]);
    }

    [Fact]
    public void Expand_Until_WinsOverCount_WhenBothPresent()
    {
        // Per RFC 5545: UNTIL wins when both present.
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY;UNTIL=20260105;COUNT=100",
            start: Today,
            end: null,
            lookaheadDays: 365,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.Equal(5, occurrences.Count);
        Assert.Equal(new DateOnly(2026, 1, 5), occurrences[^1]);
    }

    // ----------------------------------------------------------------
    // New: BYMONTH
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Yearly_ByMonth3_MarchOnly()
    {
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=YEARLY;BYMONTH=3",
            start: new DateOnly(2026, 3, 15),
            end: new DateOnly(2030, 12, 31),
            lookaheadDays: 365 * 5,
            leadDays: 0,
            today: new DateOnly(2026, 3, 15),
            timezone: "UTC");

        Assert.True(occurrences.Count >= 4);
        Assert.All(occurrences, d => Assert.Equal(3, d.Month));
    }

    // ----------------------------------------------------------------
    // T-647 / DES-0057 eng-9: an out-of-bound or malformed value of an
    // admitted part is refused naming the part, never dropped.
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=31", "BYMONTHDAY")]
    [InlineData("FREQ=MONTHLY;BYMONTHDAY=0", "BYMONTHDAY")]
    [InlineData("FREQ=DAILY;INTERVAL=0", "INTERVAL")]
    [InlineData("FREQ=DAILY;COUNT=0", "COUNT")]
    [InlineData("FREQ=DAILY;UNTIL=2026-01-01", "UNTIL")]
    [InlineData("FREQ=WEEKLY;BYDAY=XX", "BYDAY")]
    [InlineData("FREQ=MONTHLY;BYDAY=6MO", "BYDAY")]
    [InlineData("FREQ=YEARLY;BYMONTH=13", "BYMONTH")]
    public void Expand_OutOfBoundValue_IsRefusedNamingThePart(string rrule, string part)
    {
        var exception = Assert.Throws<FormatException>(() => Expand(rrule, Today, Today.AddDays(365)));
        Assert.Contains(part, exception.Message, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------
    // T-647 / DES-0057 eng-7: every admitted selector applies under every
    // FREQ it is admitted with (RFC 5545 §3.3.10 limit/expand table).
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Daily_ByDay_LimitsToNamedWeekdays()
    {
        // Anchor Thu 1 Jan 2026; Mondays 5, 12 and Wednesdays 7, 14.
        Assert.Equal(
            [new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 7), new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 14)],
            Expand("FREQ=DAILY;BYDAY=MO,WE", Today, new DateOnly(2026, 1, 14)));
    }

    [Fact]
    public void Expand_Daily_ByMonthDay_LimitsToFirstOfMonth()
    {
        Assert.Equal(
            [new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1)],
            Expand("FREQ=DAILY;BYMONTHDAY=1", new DateOnly(2026, 1, 15), new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void Expand_Monthly_ByDayAndByMonthDay_Intersect()
    {
        // A Monday on days 1..7 is the first Monday: the same dates as BYDAY=1MO.
        Assert.Equal(
            [new DateOnly(2026, 1, 5), new DateOnly(2026, 2, 2), new DateOnly(2026, 3, 2), new DateOnly(2026, 4, 6)],
            Expand("FREQ=MONTHLY;BYDAY=MO;BYMONTHDAY=1,2,3,4,5,6,7", Today, new DateOnly(2026, 4, 30)));
    }

    [Fact]
    public void Expand_Weekly_ByMonthDay_IsRefused()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => Expand("FREQ=WEEKLY;BYMONTHDAY=15", Today, Today.AddDays(60)));
        Assert.Contains("BYMONTHDAY", exception.Message, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------
    // T-647 / DES-0057 eng-8: YEARLY selectors expand the year rather than
    // filtering the anniversary of the anchor.
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_Yearly_ByMonthAndByMonthDay_AnchoredInJanuary_YieldsEveryMarch15()
    {
        Assert.Equal(
            [
                new DateOnly(2026, 3, 15), new DateOnly(2027, 3, 15), new DateOnly(2028, 3, 15),
                new DateOnly(2029, 3, 15), new DateOnly(2030, 3, 15),
            ],
            Expand("FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=15", Today, new DateOnly(2030, 12, 31)));
    }

    [Fact]
    public void Expand_Yearly_ByDayAndByMonth_YieldsEveryMondayInJanuary()
    {
        Assert.Equal(
            [
                new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 19), new DateOnly(2026, 1, 26),
                new DateOnly(2027, 1, 4), new DateOnly(2027, 1, 11), new DateOnly(2027, 1, 18), new DateOnly(2027, 1, 25),
            ],
            Expand("FREQ=YEARLY;BYDAY=MO;BYMONTH=1", Today, new DateOnly(2027, 12, 31)));
    }

    [Fact]
    public void Expand_YearlyInterval2_ByMonthAndByMonthDay_HonorsIntervalFromStart()
    {
        Assert.Equal(
            [new DateOnly(2026, 3, 15), new DateOnly(2028, 3, 15), new DateOnly(2030, 3, 15)],
            Expand("FREQ=YEARLY;INTERVAL=2;BYMONTH=3;BYMONTHDAY=15", Today, new DateOnly(2030, 12, 31)));
    }

    private static IReadOnlyList<DateOnly> Expand(string rrule, DateOnly start, DateOnly end) =>
        Sut.ExpandOccurrences(rrule, start, end, lookaheadDays: 3650, leadDays: 0, today: start, timezone: "UTC");

    // ----------------------------------------------------------------
    // Occurrence cap guard
    // ----------------------------------------------------------------

    [Fact]
    public void Expand_OccurrenceCap_NeverExceeds1000()
    {
        // Daily with a 10-year horizon would produce ~3650 but cap at 1000.
        var occurrences = Sut.ExpandOccurrences(
            rrule: "FREQ=DAILY",
            start: Today,
            end: Today.AddDays(5000),
            lookaheadDays: 5000,
            leadDays: 0,
            today: Today,
            timezone: "UTC");

        Assert.Equal(1000, occurrences.Count);
    }
}
