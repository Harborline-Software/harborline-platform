using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleOccurrenceWindowProofTests
{
    // Oracles: T-491/occurrence-window-proof acceptance 1–2 at Control e65293f4c;
    // DES-0022 eng-6/eng-7/run-1, ADR-0123 decision 1 and ADR-0149 decisions 1–2.
    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static SchedulingOccurrence Derive(string observation, IReadOnlyList<DueQueueCompletion> completions,
        bool hasWindow = true)
    {
        var definition = new ScheduleDefinition(null, "Inspection", new(ScheduleStartKind.Form, "inspection"),
            DueDate: "2026-07-01T12:00:00Z",
            Window: hasWindow ? new("2026-07-01T09:00:00Z", "2026-07-01T15:00:00Z") : null,
            Tolerance: new("PT1H", "PT1H"));
        var request = new DueOccurrenceRequest(new("series", "asset", definition, Timezone: "UTC"),
            Instant("2026-07-01T00:00:00Z"), Instant("2026-07-02T23:59:59Z"), completions, []);
        var result = Assert.Single(new LoneDueOccurrenceSource().DeriveDue(request, Instant(observation)));
        Assert.Equal("series", result.SeriesId);
        Assert.Equal("series/lone", result.OccurrenceId);
        Assert.Equal(Instant("2026-07-01T12:00:00Z"), result.Due);
        if (hasWindow)
        {
            Assert.Equal(Instant("2026-07-01T09:00:00Z"), result.Earliest);
            Assert.Equal(Instant("2026-07-01T15:00:00Z"), result.Latest);
        }
        else
        {
            Assert.Null(result.Earliest);
            Assert.Null(result.Latest);
        }
        return result;
    }

    [Theory(DisplayName = "scheduling-eng-6, scheduling-eng-7, scheduling-run-1: completion window and tolerance boundary literals")]
    [InlineData("2026-07-01T08:59:59Z", OccurrenceClassification.Void)]
    [InlineData("2026-07-01T09:00:00Z", OccurrenceClassification.Early)]
    [InlineData("2026-07-01T10:00:00Z", OccurrenceClassification.Early)]
    [InlineData("2026-07-01T11:00:00Z", OccurrenceClassification.OnTime)]
    [InlineData("2026-07-01T12:00:00Z", OccurrenceClassification.OnTime)]
    [InlineData("2026-07-01T13:00:00Z", OccurrenceClassification.OnTime)]
    [InlineData("2026-07-01T14:00:00Z", OccurrenceClassification.Late)]
    [InlineData("2026-07-01T15:00:00Z", OccurrenceClassification.Late)]
    [InlineData("2026-07-01T15:00:01Z", OccurrenceClassification.Void)]
    public void CompletedBoundaryLiterals(string completedAt, OccurrenceClassification expected)
    {
        var result = Derive("2026-07-01T16:00:00Z",
            [new("completion", "series", "series/lone", Instant(completedAt))]);
        Assert.Equal(expected, result.Classification);
        Assert.False(result.IsDue);
        Assert.Equal(Instant(completedAt), result.CompletedAt);
    }

    [Theory(DisplayName = "scheduling-eng-6, scheduling-eng-7, scheduling-run-1: uncompleted due and latest boundary literals")]
    [InlineData("2026-07-01T11:00:00Z", OccurrenceClassification.Pending, false)]
    [InlineData("2026-07-01T12:00:00Z", OccurrenceClassification.OnTime, true)]
    [InlineData("2026-07-01T14:00:00Z", OccurrenceClassification.Late, true)]
    [InlineData("2026-07-01T15:00:00Z", OccurrenceClassification.Late, true)]
    [InlineData("2026-07-01T15:00:01Z", OccurrenceClassification.Void, false)]
    public void UncompletedBoundaryLiterals(string observation, OccurrenceClassification expected, bool isDue)
    {
        var result = Derive(observation, []);
        Assert.Equal(expected, result.Classification);
        Assert.Equal(isDue, result.IsDue);
        Assert.Null(result.CompletedAt);
    }

    [Fact(DisplayName = "scheduling-eng-6, scheduling-eng-7, scheduling-run-1: absent validity deadline remains late and due")]
    public void RemovingOnlyWindowLeavesLateOccurrenceDue()
    {
        var result = Derive("2026-07-02T20:00:00Z", [], hasWindow: false);
        Assert.Equal(OccurrenceClassification.Late, result.Classification);
        Assert.True(result.IsDue);
        Assert.Null(result.CompletedAt);
    }

    [Theory(DisplayName = "scheduling-eng-6, scheduling-eng-7, scheduling-run-1: exact completion eligibility boundary literals")]
    [InlineData("other-series", "series/lone", "2026-07-01T12:00:00Z", false)]
    [InlineData("series", "series/other", "2026-07-01T12:00:00Z", false)]
    [InlineData("series", "series/lone", "2026-07-01T12:00:01Z", false)]
    [InlineData("series", "series/lone", "2026-07-01T12:00:00Z", true)]
    public void CompletionEligibilityLiterals(string series, string occurrence, string completedAt, bool eligible)
    {
        var result = Derive("2026-07-01T12:00:00Z",
            [new("completion", series, occurrence, Instant(completedAt))]);
        Assert.Equal(OccurrenceClassification.OnTime, result.Classification);
        Assert.Equal(!eligible, result.IsDue);
        if (eligible)
            Assert.Equal(Instant("2026-07-01T12:00:00Z"), result.CompletedAt);
        else
            Assert.Null(result.CompletedAt);
    }
}
