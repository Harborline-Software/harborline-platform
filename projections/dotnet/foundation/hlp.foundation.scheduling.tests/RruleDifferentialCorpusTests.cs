using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

/// <summary>
/// T-1175/occurrence-engine: the differential corpus (ADR-0122 decision 1). Every expected date was recorded from another
/// engine, never from this producer (T-724 ruling 121): <c>Corpus/dateutil-cases.json</c> from python-dateutil 4.0.0 by
/// <c>Corpus/record-dateutil.py</c>, and <c>Corpus/libical-cases.json</c> from libical v4.0.7's
/// <c>src/test/icalrecur_test.txt</c> by <c>Corpus/extract-libical.py</c>, which keeps the cases inside the bounded subset.
/// A timed libical case runs as a timed series read in UTC, so a UTC UNTIL bounds it at the instant, as libical does.
/// </summary>
public sealed class RruleDifferentialCorpusTests
{
    private static readonly IRruleExpansionService Sut = new InMemoryRruleExpansionService();

    public static TheoryData<string> DateutilCases => Ids("dateutil-cases.json", (index, _) => $"dateutil #{index}");

    public static TheoryData<string> LibicalCases => Ids("libical-cases.json", (_, item) => $"libical line {item.GetProperty("line").GetInt32()}");

    [Theory(DisplayName = "T-1175 corpus: the producer matches python-dateutil 4.0.0 on each recorded rule")]
    [MemberData(nameof(DateutilCases))]
    public void MatchesDateutil(string id)
    {
        var item = Case("dateutil-cases.json", (index, _) => $"dateutil #{index}", id);
        var start = Day(item.GetProperty("start"));
        var actual = Sut.ExpandOccurrences(item.GetProperty("rule").GetString()!, start, Day(item.GetProperty("end")),
            lookaheadDays: 0, leadDays: 0, today: start, timezone: "UTC");

        Assert.Equal(Dates(item), actual);
    }

    [Theory(DisplayName = "T-1175 corpus: the producer matches libical v4.0.7's icalrecur_test.txt on each in-subset rule")]
    [MemberData(nameof(LibicalCases))]
    public void MatchesLibical(string id)
    {
        var item = Case("libical-cases.json", (_, value) => $"libical line {value.GetProperty("line").GetInt32()}", id);
        var rule = item.GetProperty("rule").GetString()!;
        var start = Day(item.GetProperty("start"));
        var expected = Dates(item);
        // A rule with COUNT or UNTIL is listed whole, so the walk runs two years past its last date to catch an
        // occurrence wrongly included after it. Any other rule lists its first instances and is compared to the last.
        var bounded = rule.Contains("COUNT=", StringComparison.OrdinalIgnoreCase) || rule.Contains("UNTIL=", StringComparison.OrdinalIgnoreCase);
        var end = bounded ? expected[^1].AddYears(2) : expected[^1];
        var actual = item.TryGetProperty("startTime", out var time)
            ? Sut.ExpandZonedOccurrences(rule, start, TimeOnly.ParseExact(time.GetString()!, "HH:mm:ss", CultureInfo.InvariantCulture), "UTC",
                end: end, lookaheadDays: 0, leadDays: 0, today: start)
            : Sut.ExpandOccurrences(rule, start, end, lookaheadDays: 0, leadDays: 0, today: start, timezone: "UTC");

        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "T-1175 corpus: both recorded corpora are present and non-trivial")]
    public void CorporaArePresent()
    {
        Assert.True(Cases("dateutil-cases.json").Count >= 30);
        Assert.True(Cases("libical-cases.json").Count >= 30);
    }

    private static TheoryData<string> Ids(string file, Func<int, JsonElement, string> id)
    {
        var data = new TheoryData<string>();
        var cases = Cases(file);
        for (var index = 0; index < cases.Count; index++)
            data.Add(id(index, cases[index]));
        return data;
    }

    private static JsonElement Case(string file, Func<int, JsonElement, string> id, string wanted)
    {
        var cases = Cases(file);
        for (var index = 0; index < cases.Count; index++)
            if (id(index, cases[index]) == wanted)
                return cases[index];
        throw new InvalidOperationException($"No corpus case '{wanted}' in {file}.");
    }

    private static IReadOnlyList<JsonElement> Cases(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Corpus", file)));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static DateOnly Day(JsonElement value) =>
        DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static List<DateOnly> Dates(JsonElement item) =>
        item.GetProperty("dates").EnumerateArray().Select(Day).ToList();
}
