using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleThresholdRearmProofTests
{
    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static DueOccurrenceRequest Request(ThresholdCrossing crossing = ThresholdCrossing.Above)
        => new(new("series", "asset", new(null, "Threshold proof", new(ScheduleStartKind.Form, "inspection"),
                Basis: new ThresholdBasis("meter", crossing, 10))),
            Instant("2026-07-01T00:00:00Z"), Instant("2026-07-02T00:00:00Z"), [], []);

    private static IReadOnlyList<SchedulingOccurrence> Derive(DueOccurrenceRequest request, string now = "2026-07-01T11:30:00Z")
        => new ThresholdDueOccurrenceSource().DeriveDue(request, Instant(now));

    // Oracles: threshold-rearm-proof acceptance 1's literal timeline; ADR-0149 decision 5.
    [Fact(DisplayName = "scheduling-eng-3 / scheduling-run-1: threshold latches and exact completion immediately re-arms the latest reading")]
    public void ThresholdLatchAndImmediateRearmHaveLiteralIdentitiesAndInstants()
    {
        var request = Request() with
        {
            Readings = [new("r1", "series", "meter", Instant("2026-07-01T08:00:00Z"), 11),
                new("r2", "series", "meter", Instant("2026-07-01T09:00:00Z"), 12)],
        };
        var first = Assert.Single(Derive(request, "2026-07-01T08:00:00Z"));
        Assert.Equal("series/threshold/r1/initial", first.OccurrenceId);
        Assert.Equal(Instant("2026-07-01T08:00:00Z"), first.Due);
        Assert.Single(Derive(request, "2026-07-01T09:00:00Z"));

        request = request with
        {
            Completions = [new("c1", "series", "series/threshold/r1/initial", Instant("2026-07-01T10:00:00Z"))],
        };
        var atCompletion = Derive(request, "2026-07-01T10:00:00Z");
        Assert.Equal(new[] { "series/threshold/r1/initial", "series/threshold/r2/after/c1" }, atCompletion.Select(o => o.OccurrenceId));
        Assert.Equal(new[] { Instant("2026-07-01T08:00:00Z"), Instant("2026-07-01T10:00:00Z") }, atCompletion.Select(o => o.Due));

        request = request with
        {
            Readings = [.. request.Readings, new("r3", "series", "meter", Instant("2026-07-01T10:30:00Z"), 13)],
            Completions = [.. request.Completions,
                new("unmatched", "series", "series/threshold/other/initial", Instant("2026-07-01T11:00:00Z")),
                new("foreign", "other-series", "series/threshold/r2/after/c1", Instant("2026-07-01T11:15:00Z"))],
        };
        var final = Derive(request);
        Assert.Equal(new[] { "series/threshold/r1/initial", "series/threshold/r2/after/c1" }, final.Select(o => o.OccurrenceId));
        Assert.Equal(new[] { Instant("2026-07-01T08:00:00Z"), Instant("2026-07-01T10:00:00Z") }, final.Select(o => o.Due));
    }

    [Fact(DisplayName = "scheduling-eng-3 / scheduling-run-1: threshold foreign and unmatched completions cannot re-arm an open occurrence")]
    public void ThresholdRequiresExactSeriesAndOccurrenceForCompletion()
    {
        var request = Request() with
        {
            Readings = [new("r1", "series", "meter", Instant("2026-07-01T08:00:00Z"), 11)],
            Completions = [new("foreign", "other-series", "series/threshold/r1/initial", Instant("2026-07-01T09:00:00Z"))],
        };
        Assert.Single(Derive(request));
        request = request with
        {
            Completions = [.. request.Completions,
                new("unmatched", "series", "series/threshold/other/initial", Instant("2026-07-01T10:00:00Z"))],
        };
        var occurrence = Assert.Single(Derive(request));
        Assert.Equal("series/threshold/r1/initial", occurrence.OccurrenceId);
        Assert.Equal(Instant("2026-07-01T08:00:00Z"), occurrence.Due);
    }

    // Oracle: acceptance 2's strict Above/Below 10 table, independent of production comparisons.
    [Theory(DisplayName = "scheduling-eng-3 / scheduling-run-1: threshold Above and Below exclude equality and the opposite polarity")]
    [InlineData(ThresholdCrossing.Above, 10, false)]
    [InlineData(ThresholdCrossing.Above, 9, false)]
    [InlineData(ThresholdCrossing.Above, 11, true)]
    [InlineData(ThresholdCrossing.Below, 10, false)]
    [InlineData(ThresholdCrossing.Below, 11, false)]
    [InlineData(ThresholdCrossing.Below, 9, true)]
    public void ThresholdStrictComparisonHasLiteralBoundaryTable(ThresholdCrossing crossing, int value, bool emits)
    {
        var request = Request(crossing) with
        {
            Readings = [new("r1", "series", "meter", Instant("2026-07-01T08:00:00Z"), value)],
        };
        var occurrences = Derive(request);
        if (!emits)
        {
            Assert.Empty(occurrences);
            return;
        }
        var occurrence = Assert.Single(occurrences);
        Assert.Equal("series/threshold/r1/initial", occurrence.OccurrenceId);
        Assert.Equal(Instant("2026-07-01T08:00:00Z"), occurrence.Due);
    }

    // Oracle: acceptance 2 and the frozen missing-reading contract; each row changes one field only.
    [Theory(DisplayName = "scheduling-eng-3 / scheduling-run-1: threshold requires a matching reading recorded no later than the kernel instant")]
    [InlineData("future")]
    [InlineData("foreign-series")]
    [InlineData("wrong-reading")]
    public void ThresholdIneligibleReadingsRefuseUntilOneInputBecomesEligible(string corrected)
    {
        var future = new ScheduleReading("future", "series", "meter", Instant("2026-07-01T12:00:00Z"), 11);
        var foreign = new ScheduleReading("foreign", "other-series", "meter", Instant("2026-07-01T08:00:00Z"), 11);
        var wrong = new ScheduleReading("wrong", "series", "other-reading", Instant("2026-07-01T08:00:00Z"), 11);
        var request = Request() with { Readings = [future, foreign, wrong] };
        Assert.Equal("scheduling.occurrence.reading_required", Assert.Throws<OccurrenceRefusalException>(() => Derive(request)).Code);

        request = corrected switch
        {
            "future" => request with { Readings = [future with { RecordedAt = Instant("2026-07-01T11:30:00Z") }, foreign, wrong] },
            "foreign-series" => request with { Readings = [future, foreign with { SeriesId = "series" }, wrong] },
            _ => request with { Readings = [future, foreign, wrong with { Reading = "meter" }] },
        };
        var occurrence = Assert.Single(Derive(request));
        if (corrected == "future")
        {
            Assert.Equal("series/threshold/future/initial", occurrence.OccurrenceId);
            Assert.Equal(Instant("2026-07-01T11:30:00Z"), occurrence.Due);
        }
        else if (corrected == "foreign-series")
        {
            Assert.Equal("series/threshold/foreign/initial", occurrence.OccurrenceId);
            Assert.Equal(Instant("2026-07-01T08:00:00Z"), occurrence.Due);
        }
        else
        {
            Assert.Equal("series/threshold/wrong/initial", occurrence.OccurrenceId);
            Assert.Equal(Instant("2026-07-01T08:00:00Z"), occurrence.Due);
        }
    }
}
