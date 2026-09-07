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
