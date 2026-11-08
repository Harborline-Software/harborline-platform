using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

/// <summary>
/// T-1175/occurrence-engine: the subset validator's edges and the refusal fields the adapter keeps. Expected values are
/// literals from RFC 5545 §3.3.10, DES-0057 §10 ruling 3 and the exception's documented fields (T-724 ruling 121).
/// </summary>
public sealed class RruleSubsetTests
{
    private static readonly IRruleExpansionService Sut = new InMemoryRruleExpansionService();
    private static readonly DateOnly Today = new(2026, 1, 1);

    public static TheoryData<string> AdmittedParts => ["FREQ", "INTERVAL", "COUNT", "UNTIL", "BYDAY", "BYMONTHDAY", "BYMONTH"];

    [Theory(DisplayName = "T-1175: an admitted part written with no value refuses as malformed, naming the part")]
    [MemberData(nameof(AdmittedParts))]
    public void AdmittedPartWithoutValueIsMalformed(string part)
    {
        var rule = part == "FREQ" ? "FREQ;COUNT=2" : $"FREQ=DAILY;{part}";

        var refusal = Assert.Throws<FormatException>(() => Sut.ExpandOccurrences(rule, Today, null, 30, 0, Today, "UTC"));

        Assert.Equal($"RRULE component '{part}' has no value.", refusal.Message);
    }

    [Fact(DisplayName = "T-1175: a repeated part refuses as malformed, naming the part (RFC 5545 §3.3.10: each part only once)")]
    public void RepeatedPartNamesThePart() =>
        Assert.Equal("RRULE component 'INTERVAL' is given more than once.",
            Assert.Throws<FormatException>(() => Sut.ExpandOccurrences("FREQ=DAILY;INTERVAL=2;INTERVAL=3", Today, null, 30, 0, Today, "UTC")).Message);

    [Fact(DisplayName = "T-1175: spaces around parts and empty segments are ignored, as before")]
    public void SpacesAndEmptySegmentsAreIgnored() =>
        Assert.Equal([Today, Today.AddDays(1)],
            Sut.ExpandOccurrences(" FREQ=DAILY ; ; COUNT=2 ", Today, null, 30, 0, Today, "UTC"));

    [Fact(DisplayName = "T-1175: an unsupported frequency and an ordinal under WEEKLY name what is refused")]
    public void UnsupportedValuesAreNamed()
    {
        Assert.Contains("'SECONDLY'", Assert.Throws<NotSupportedException>(() =>
            Sut.ExpandOccurrences("FREQ=SECONDLY;COUNT=2", Today, null, 30, 0, Today, "UTC")).Message, StringComparison.Ordinal);
        Assert.Contains("FREQ=WEEKLY", Assert.Throws<NotSupportedException>(() =>
            Sut.ExpandOccurrences("FREQ=WEEKLY;BYDAY=2TU", Today, null, 30, 0, Today, "UTC")).Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "T-1175: a bound out of range names the range, and a COUNT or INTERVAL names a positive integer")]
    public void BoundRefusalsNameTheirRange()
    {
        Assert.Equal("RRULE component 'BYMONTHDAY' value '29' is not an integer in 1..28.",
            Assert.Throws<FormatException>(() => Sut.ExpandOccurrences("FREQ=MONTHLY;BYMONTHDAY=29", Today, null, 30, 0, Today, "UTC")).Message);
        Assert.Equal("RRULE component 'COUNT' value '0' is not a positive integer.",
            Assert.Throws<FormatException>(() => Sut.ExpandOccurrences("FREQ=DAILY;COUNT=0", Today, null, 30, 0, Today, "UTC")).Message);
    }

    [Fact(DisplayName = "T-1175: the zoned expansion refuses a blank rule, and a blank or unknown zone at the timezone parameter")]
    public void ZonedExpansionGuardsItsArguments()
    {
        Assert.Equal("rrule", Assert.Throws<ArgumentException>(() =>
            Sut.ExpandZonedOccurrences(" ", Today, new(9, 0), "UTC", null, 30, 0, Today)).ParamName);
        Assert.Equal("timezone", Assert.Throws<ArgumentException>(() =>
            Sut.ExpandZonedOccurrences("FREQ=DAILY;COUNT=1", Today, new(9, 0), " ", null, 30, 0, Today)).ParamName);
        var unknown = Assert.Throws<ArgumentException>(() =>
            Sut.ExpandZonedOccurrences("FREQ=DAILY;COUNT=1", Today, new(9, 0), "Mars/Olympus_Mons", null, 30, 0, Today));
        Assert.Equal(("timezone", "'Mars/Olympus_Mons' is not an IANA time zone id. (Parameter 'timezone')"), (unknown.ParamName, unknown.Message));
    }

    [Fact(DisplayName = "T-1175: a capped walk under UNTIL reports the UNTIL date as the range end and UNTIL as its bound (DES-0057 §10 ruling 3)")]
    public void CapRefusalUnderUntilReportsTheUntilDate()
    {
        var refusal = Assert.Throws<RruleExpansionCapExceededException>(() =>
            Sut.ExpandOccurrences("FREQ=DAILY;UNTIL=20300101", Today, new(2031, 1, 1), 0, 0, Today, "UTC"));

        Assert.Equal((new DateOnly(2030, 1, 1), RecurrenceBound.Until, 1_000, 1_001),
            (refusal.RequestedRangeEnd, refusal.Bound, refusal.CandidateLimit, refusal.CandidatesExamined));
    }

    [Fact(DisplayName = "T-1175: a capped walk under COUNT reports COUNT as its bound and the horizon as the range end")]
    public void CapRefusalUnderCountReportsCount()
    {
        var refusal = Assert.Throws<RruleExpansionCapExceededException>(() =>
            Sut.ExpandOccurrences("FREQ=DAILY;COUNT=5000", Today, new(2031, 1, 1), 0, 0, Today, "UTC"));

        Assert.Equal((new DateOnly(2031, 1, 1), RecurrenceBound.Count), (refusal.RequestedRangeEnd, refusal.Bound));
    }

    [Fact(DisplayName = "T-1175: a capped walk with neither COUNT nor UNTIL reports no bound")]
    public void CapRefusalWithoutBoundReportsNone() =>
        Assert.Equal(RecurrenceBound.None, Assert.Throws<RruleExpansionCapExceededException>(() =>
            Sut.ExpandOccurrences("FREQ=DAILY", Today, new(2031, 1, 1), 0, 0, Today, "UTC")).Bound);
}
