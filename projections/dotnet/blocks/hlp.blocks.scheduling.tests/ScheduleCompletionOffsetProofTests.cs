using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleCompletionOffsetProofTests
{
    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

    private static DueOccurrenceRequest Request(string? firstDue = "2026-07-01T04:00:00Z")
        => new(new("series", "asset", new(null, "Inspection", new(ScheduleStartKind.Form, "inspection"),
                Basis: new CompletionOffsetBasis("PT2H")), Timezone: "UTC",
                FirstDue: firstDue is null ? null : Instant(firstDue)),
            Instant("2026-07-01T00:00:00Z"), Instant("2026-07-01T23:59:59Z"), [], []);

    private static IReadOnlyList<SchedulingOccurrence> Generate(DueOccurrenceRequest request)
        => new CompletionOffsetDueOccurrenceSource().DeriveDue(request, Instant("2026-07-01T16:00:00Z"));

    // Slice acceptance 1: literal UTC PT2H arithmetic and frozen completion identity contract.
    private static void AssertLatest(DueOccurrenceRequest request)
    {
        var occurrence = Assert.Single(Generate(request));
        Assert.Equal(Instant("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal("series/completion/after/c-a", occurrence.OccurrenceId);
    }

    [Theory(DisplayName = "scheduling-eng-3, scheduling-run-1: completion-offset selects the latest eligible actual completion with stable ordinal identity")]
    [InlineData(false, "2026-07-01T04:00:00Z")]
    [InlineData(true, "2026-07-01T04:00:00Z")]
    [InlineData(false, "2026-07-01T06:00:00Z")]
    [InlineData(true, "2026-07-01T06:00:00Z")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public void LatestEligibleActualCompletionIgnoresFirstDueAndEnumerationOrder(bool reverse, string? firstDue)
    {
        DueQueueCompletion[] completions =
        [
            new("c-b", "series", "prior-b", Instant("2026-07-01T10:00:00Z")),
            new("c-old", "series", "prior-old", Instant("2026-07-01T08:00:00Z")),
            new("c-a", "series", "prior-a", Instant("2026-07-01T10:00:00Z")),
        ];
        var foreign = new DueQueueCompletion("foreign", "other-series", "prior-foreign", Instant("2026-07-01T14:00:00Z"));
        var future = new DueQueueCompletion("future", "series", "prior-future", Instant("2026-07-01T18:00:00Z"));
        var request = Request(firstDue);
        foreach (DueQueueCompletion[] supplied in new DueQueueCompletion[][]
            { completions, [.. completions, foreign], [.. completions, future], [.. completions, foreign, future] })
        {
            AssertLatest(request with { Completions = reverse ? supplied.Reverse().ToArray() : supplied });
        }
    }

    [Fact(DisplayName = "scheduling-eng-3, scheduling-run-1: completion-offset refuses missing first due until only FirstDue is corrected")]
    public void NoEligibleCompletionRequiresExplicitFirstDue()
    {
        var request = Request(null) with { Completions =
        [
            new("foreign", "other-series", "prior-foreign", Instant("2026-07-01T14:00:00Z")),
            new("future", "series", "prior-future", Instant("2026-07-01T18:00:00Z")),
        ] };
        Assert.Equal("scheduling.occurrence.first_due_required",
            Assert.Throws<OccurrenceRefusalException>(() => Generate(request)).Code);
        request = request with { Series = request.Series with { FirstDue = Instant("2026-07-01T04:00:00Z") } };
        var occurrence = Assert.Single(Generate(request));
        Assert.Equal(Instant("2026-07-01T04:00:00Z"), occurrence.Due);
        Assert.Equal("series/completion/first", occurrence.OccurrenceId);
    }

    [Fact(DisplayName = "scheduling-eng-3, scheduling-run-1: completion-offset initial marker is disjoint from a completion named first")]
    public void CompletionNamedFirstHasAfterIdentity()
    {
        var request = Request();
        Assert.Equal("series/completion/first", Assert.Single(Generate(request)).OccurrenceId);
        request = request with { Completions = [new("first", "series", "series/completion/first", Instant("2026-07-01T10:00:00Z"))] };
        var occurrence = Assert.Single(Generate(request));
        Assert.Equal(Instant("2026-07-01T12:00:00Z"), occurrence.Due);
        Assert.Equal("series/completion/after/first", occurrence.OccurrenceId);
    }

    [Theory(DisplayName = "scheduling-eng-3, scheduling-run-1: completion-offset horizon includes the exact due point and excludes a later horizon")]
    [InlineData(false, "2026-07-01T04:00:00Z", "series/completion/first")]
    [InlineData(true, "2026-07-01T12:00:00Z", "series/completion/after/c-a")]
    public void HorizonIsInclusive(bool completed, string due, string identity)
    {
        var request = Request() with { From = Instant(due), To = Instant(due) };
        if (completed)
            request = request with { Completions = [new("c-a", "series", "prior", Instant("2026-07-01T10:00:00Z"))] };
        var occurrence = Assert.Single(Generate(request));
        Assert.Equal(Instant(due), occurrence.Due);
        Assert.Equal(identity, occurrence.OccurrenceId);
        Assert.Empty(Generate(request with { From = Instant("2026-07-01T21:00:00Z"), To = Instant("2026-07-01T22:00:00Z") }));
    }
}
