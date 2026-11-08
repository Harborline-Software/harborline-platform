using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

/// <summary>
/// T-1175/occurrence-engine: R-0146 call 5's cases, each with its RFC 5545 answer as the literal (T-724 ruling 121). The
/// expected values come from the RFC and R-0146's measurements of Ical.Net 5.2.4, python-dateutil and libical, never from
/// the producer. The hand-written walker this replaces gave the bracketed wrong answers.
/// </summary>
public sealed class RruleRfcCaseTests
{
    private static readonly IRruleExpansionService Sut = new InMemoryRruleExpansionService();

    private static IReadOnlyList<DateOnly> Dates(string rule, DateOnly start, DateOnly end) =>
        Sut.ExpandOccurrences(rule, start, end, lookaheadDays: 0, leadDays: 0, today: start, timezone: "UTC");

    [Fact(DisplayName = "R-0146 (a): a month-end anchor skips the months without that day and never clamps (RFC 5545 §3.3.10) [walker: Jan 31, Feb 28, Mar 28, …]")]
    public void MonthEndAnchorSkipsInvalidDates() =>
        Assert.Equal(
            [new(2026, 1, 31), new(2026, 3, 31), new(2026, 5, 31), new(2026, 7, 31), new(2026, 8, 31), new DateOnly(2026, 10, 31)],
            Dates("FREQ=MONTHLY;COUNT=6", new(2026, 1, 31), new(2027, 12, 31)));

    [Fact(DisplayName = "R-0146 (a'): a 29 February anchor recurs on each 29 February and loses none (RFC 5545 §3.3.10) [walker: 2024-02-29 only]")]
    public void LeapDayAnchorKeepsEveryLeapDay() =>
        Assert.Equal(
            [new(2024, 2, 29), new(2028, 2, 29), new DateOnly(2032, 2, 29)],
            Dates("FREQ=YEARLY;COUNT=3", new(2024, 2, 29), new(2040, 12, 31)));

    [Fact(DisplayName = "R-0146 (b): a numeric BYDAY under YEARLY without BYMONTH is an offset within the year, one date a year (Errata 1913) [walker: 2026-01-05, 02-02, 03-02]")]
    public void YearlyOrdinalIsAnOffsetWithinTheYear() =>
        Assert.Equal(
            [new(2026, 1, 5), new(2027, 1, 4), new DateOnly(2028, 1, 3)],
            Dates("FREQ=YEARLY;BYDAY=1MO;COUNT=3", new(2026, 1, 5), new(2030, 12, 31)));

    public static TheoryData<string, string, Type> MalformedRules => new()
    {
        { "an unknown part", "FREQ=DAILY;BOGUS", typeof(NotSupportedException) },
        { "an unknown part with a value", "FREQ=DAILY;BOGUS=1", typeof(NotSupportedException) },
        { "COUNT with UNTIL", "FREQ=DAILY;COUNT=2;UNTIL=20260110", typeof(FormatException) },
        { "a duplicate FREQ", "FREQ=DAILY;FREQ=WEEKLY;COUNT=2", typeof(FormatException) },
        { "a duplicate COUNT", "FREQ=DAILY;COUNT=2;COUNT=3", typeof(FormatException) },
        { "an ordinal BYDAY under WEEKLY", "FREQ=WEEKLY;BYDAY=1MO;COUNT=2", typeof(NotSupportedException) },
        { "an ordinal BYDAY under DAILY", "FREQ=DAILY;BYDAY=-1FR;COUNT=2", typeof(NotSupportedException) },
    };

    [Theory(DisplayName = "R-0146 (c): a malformed rule refuses (RFC 5545 §3.3.10) [walker accepted each]")]
    [MemberData(nameof(MalformedRules))]
    public void MalformedRuleRefuses(string _, string rule, Type refusal) =>
        Assert.Throws(refusal, () => Dates(rule, new(2026, 1, 1), new(2026, 12, 31)));

    [Fact(DisplayName = "R-0146 (c): an unknown FREQ refuses even when the anchor is past the horizon [walker: returned no dates]")]
    public void UnknownFrequencyRefusesBeyondTheHorizon() =>
        Assert.Throws<NotSupportedException>(() =>
            Sut.ExpandOccurrences("FREQ=FOO", new(2030, 1, 1), new(2026, 1, 31), 0, 0, new(2026, 1, 1), "UTC"));

    [Fact(DisplayName = "R-0146 (c): a lower-case rule is valid, because ABNF strings are case-insensitive (RFC 5234 §2.3; RFC 5545 §3.1)")]
    public void LowerCaseRuleIsValid() =>
        Assert.Equal([new(2026, 1, 1), new DateOnly(2026, 1, 2)], Dates("freq=daily;count=2", new(2026, 1, 1), new(2026, 12, 31)));

    [Fact(DisplayName = "R-0146 (d): a zoned series' UTC UNTIL bounds it at that instant: Los Angeles 20:00 daily to 20261105T040000Z is Nov 1 to 4 (RFC 5545 §3.3.10) [walker: Nov 1 to 5]")]
    public void ZonedUntilBoundsAtTheInstant() =>
        Assert.Equal(
            [new(2026, 11, 1), new(2026, 11, 2), new(2026, 11, 3), new DateOnly(2026, 11, 4)],
            Sut.ExpandZonedOccurrences("FREQ=DAILY;UNTIL=20261105T040000Z", new(2026, 11, 1), new(20, 0), "America/Los_Angeles",
                end: new(2026, 11, 30), lookaheadDays: 0, leadDays: 0, today: new(2026, 11, 1)));

    [Fact(DisplayName = "R-0146 (d): a DATE UNTIL on a timed series still bounds it by local date, inclusively (DES-0057 ck-7)")]
    public void ZonedSeriesKeepsADateUntil() =>
        Assert.Equal(
            [new(2026, 11, 1), new(2026, 11, 2), new DateOnly(2026, 11, 3)],
            Sut.ExpandZonedOccurrences("FREQ=DAILY;UNTIL=20261103", new(2026, 11, 1), new(20, 0), "America/Los_Angeles",
                end: new(2026, 11, 30), lookaheadDays: 0, leadDays: 0, today: new(2026, 11, 1)));

    [Fact(DisplayName = "R-0146 (d): a zoned expansion refuses a time zone that is not an IANA id")]
    public void ZonedExpansionRefusesAnUnknownZone() =>
        Assert.Throws<ArgumentException>(() =>
            Sut.ExpandZonedOccurrences("FREQ=DAILY;COUNT=1", new(2026, 1, 1), new(9, 0), "Mars/Olympus_Mons", null, 30, 0, new(2026, 1, 1)));

    public static TheoryData<string, string> NeverMatchingRules => new()
    {
        { "a fifth Monday that must also be the 1st", "FREQ=MONTHLY;BYDAY=5MO;BYMONTHDAY=1" },
        { "a Monday every seven days from a Tuesday", "FREQ=DAILY;INTERVAL=7;BYDAY=MO" },
    };

    // The engine searches no further than the walk's horizon, so a rule that can never match is the empty set RFC 5545
    // gives it, returned promptly rather than searched for without end.
    [Theory(DisplayName = "T-1175: a subset rule whose parts can never all match returns no dates, promptly, instead of enumerating without end")]
    [MemberData(nameof(NeverMatchingRules))]
    public void NeverMatchingRuleIsEmptyAndPrompt(string _, string rule)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.Empty(Dates(rule, new(2026, 1, 6), new(2126, 12, 31)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    // Codex on platform #439 (bcdee2ed): the next match is 11 970 steps later, in 2749. Oracle: python-dateutil 4.0.0.
    [Fact(DisplayName = "T-1175: a valid rule whose next match is centuries away still returns what lies in the requested range")]
    public void SparseRuleReturnsWhatLiesInRange() =>
        Assert.Equal([new DateOnly(2028, 2, 28)],
            Dates("FREQ=DAILY;INTERVAL=22;BYMONTH=2;BYMONTHDAY=28;BYDAY=MO", new(2028, 2, 28), new(2028, 2, 29)));

    // Oracle: python-dateutil 4.0.0 (Corpus/record-dateutil.py's engine), recorded for these rules.
    [Fact(DisplayName = "T-1175: the sparsest rules that do match still expand: a fifth Monday in February, and 28 February on a Monday")]
    public void SparseRulesThatMatchStillExpand()
    {
        Assert.Equal([new(2044, 2, 29), new DateOnly(2072, 2, 29)],
            Dates("FREQ=MONTHLY;BYMONTH=2;BYDAY=5MO;COUNT=2", new(2026, 1, 1), new(2099, 12, 31)));
        Assert.Equal([new(2028, 2, 28), new DateOnly(2033, 2, 28)],
            Dates("FREQ=DAILY;BYMONTH=2;BYMONTHDAY=28;BYDAY=MO;COUNT=2", new(2026, 1, 1), new(2099, 12, 31)));
    }

    [Fact(DisplayName = "T-1175: a series whose next occurrence falls after year 9999 ends there rather than failing")]
    public void SeriesEndsPastYear9999() =>
        Assert.Equal([new DateOnly(2026, 6, 1)], Dates("FREQ=YEARLY;INTERVAL=10000", new(2026, 6, 1), new(9999, 12, 31)));

    // Oracle: RFC 5545 §3.3.10's BYxxx table (BYMONTHDAY expands under YEARLY) and python-dateutil 4.0.0.
    [Fact(DisplayName = "T-1175: YEARLY with BYMONTHDAY and no BYMONTH spans every month, not only the anchor's (RFC 5545 §3.3.10)")]
    public void YearlyMonthDayWithoutMonthSpansEveryMonth() =>
        Assert.Equal([new(2026, 1, 15), new(2026, 2, 15), new DateOnly(2026, 3, 15)],
            Dates("FREQ=YEARLY;BYMONTHDAY=15;COUNT=3", new(2026, 1, 1), new(2030, 12, 31)));

    [Fact(DisplayName = "T-1175: YEARLY with an ordinal BYDAY, BYMONTHDAY and no BYMONTH refuses rather than expand it wrongly")]
    public void YearlyOrdinalWithMonthDayNeedsMonth() =>
        Assert.Throws<NotSupportedException>(() => Dates("FREQ=YEARLY;BYMONTHDAY=1,2,3,4,5,6,7;BYDAY=1MO;COUNT=4", new(2026, 3, 1), new(2030, 12, 31)));

    // Codex on platform #439 (6e7e1385): 23:30 on 28 March 2026 in Nuuk is inside the spring-forward gap. The occurrence
    // keeps its nominal date; only its instant moves (RFC 5545 §3.3.5).
    [Fact(DisplayName = "T-1175: a timed occurrence inside a gap that crosses midnight keeps its own date (Nuuk 23:30, 26 to 30 March 2026)")]
    public void GapAcrossMidnightKeepsTheOccurrenceDate() =>
        Assert.Equal(
            [new(2026, 3, 26), new(2026, 3, 27), new(2026, 3, 28), new(2026, 3, 29), new DateOnly(2026, 3, 30)],
            Sut.ExpandZonedOccurrences("FREQ=DAILY;COUNT=5", new(2026, 3, 26), new(23, 30), "America/Nuuk",
                end: new(2026, 4, 30), lookaheadDays: 0, leadDays: 0, today: new(2026, 3, 26)));

    // Oracle: Python zoneinfo, fold=0 (the offset before the transition): 2026-03-28 23:30 America/Nuuk is 01:30Z on the 29th.
    [Fact(DisplayName = "T-1175: a zoned UTC UNTIL compares against the gap time's resolved instant, not a shifted one")]
    public void ZonedUntilUsesTheResolvedGapInstant() =>
        Assert.Equal(
            [new(2026, 3, 27), new DateOnly(2026, 3, 28)],
            Sut.ExpandZonedOccurrences("FREQ=DAILY;UNTIL=20260329T013000Z", new(2026, 3, 27), new(23, 30), "America/Nuuk",
                end: new(2026, 4, 30), lookaheadDays: 0, leadDays: 0, today: new(2026, 3, 27)));

    [Fact(DisplayName = "R-0146 (e): New York 01:30 on 2026-11-01 is 05:30Z, the first occurrence of the ambiguous time (RFC 5545 §3.3.5) [calendar resolver: 06:30Z]")]
    public void AmbiguousLocalTimeIsItsFirstOccurrence() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            WallClockResolver.ToUtcInstant(new(2026, 11, 1), new(1, 30), "America/New_York"));

    [Fact(DisplayName = "R-0146 (e): New York 02:30 on 2026-03-08 is 07:30Z, read at the offset before the spring-forward gap (RFC 5545 §3.3.5; Errata 4271)")]
    public void GapLocalTimeUsesTheOffsetBeforeTheGap() =>
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 30, 0, TimeSpan.Zero),
            WallClockResolver.ToUtcInstant(new(2026, 3, 8), new(2, 30), "America/New_York"));

    [Fact(DisplayName = "R-0146 (e): an unambiguous local time resolves at its own offset (Los Angeles 20:00 on 2026-11-04 is 04:00Z on the 5th)")]
    public void OrdinaryLocalTimeResolvesAtItsOffset() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 4, 0, 0, TimeSpan.Zero),
            WallClockResolver.ToUtcInstant(new(2026, 11, 4), new(20, 0), "America/Los_Angeles"));
}
