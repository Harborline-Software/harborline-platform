using System.Globalization;
using System.Text.Json;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleChainRevisionProofTests
{
    private static DateTimeOffset I(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset At = I("2026-07-01T20:00:00Z");
    private static readonly DateTimeOffset Eight = I("2026-07-01T08:00:00Z");
    private static readonly DateTimeOffset Ten = I("2026-07-01T10:00:00Z");
    private static RelativeChainExpansionResult Expand(RelativeChainExpansionRequest request) => new InMemoryRelativeChainExpansionService().Expand(request);
    private static RelativeChainLedger Apply(RelativeChainExpansionRequest request, RelativeChainExpansionResult result) =>
        new(request.PriorLedger.LedgerVersion + 1, [.. request.PriorLedger.Occurrences, .. result.AppendedOccurrences], [.. request.PriorLedger.Supersessions, .. result.Supersessions]);
    private static void Tuples(IEnumerable<RelativeChainOccurrence> rows, int revision) =>
        Assert.Equal(new[] { ("a", Eight, revision), ("b", Ten, revision) }, rows.Select(o => (o.OccurrenceId.NodeId, o.DueDate, o.OccurrenceId.OccurrenceRevision)));

    [Fact(DisplayName = "scheduling-eng-5 scheduling-run-2 same-instant anchor revision appends and retries")]
    public void SameInstantAnchorRevisionAppendsAndRetries()
    {
        var request = new RelativeChainExpansionRequest(new("visits", 1, "visit", [new("a", null, "PT0S", new("PT1H", "PT1H"), null), new("b", "a", "PT2H", new("PT1H", "PT1H"), null)]), "series", "subject", [new("visit", 1, Eight, AnchorSnapshotState.Present, ["visit/r1"])], new(0, [], []), At, "UTC");
        var first = Expand(request);
        Tuples(first.CurrentOccurrences, 1);
        Tuples(first.AppendedOccurrences, 1);
        Assert.Empty(first.Supersessions);
        Assert.Empty(first.Failures);
        var ledger = Apply(request, first);
        var before = JsonSerializer.Serialize(ledger);
        var revised = request with { PriorLedger = ledger, Anchors = [request.Anchors[0] with { AnchorRevision = 2 }] };
        var next = Expand(revised);
        Assert.Equal(1, next.ExpectedPriorLedgerVersion);
        Tuples(next.AppendedOccurrences, 2);
        Tuples(next.CurrentOccurrences, 2);
        Assert.Empty(next.Failures);
        Assert.Equal(2, next.AppendedOccurrences[0].BasisRevision);
        Assert.Equal("rc1/series/a/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r2", next.AppendedOccurrences[1].BasisRef);
        Assert.Equal(2, next.Supersessions.Count);
        Assert.Collection(next.Supersessions,
            edge => { Assert.Equal(new RelativeChainOccurrenceId("series", "a", Eight, 1), edge.SupersededOccurrenceId); Assert.Equal(new RelativeChainOccurrenceId("series", "a", Eight, 2), edge.SuccessorOccurrenceId); Assert.Equal(SupersessionReasonCode.AnchorRevised, edge.ReasonCode); Assert.Equal(At, edge.RecordedAt); },
            edge => { Assert.Equal(new RelativeChainOccurrenceId("series", "b", Ten, 1), edge.SupersededOccurrenceId); Assert.Equal(new RelativeChainOccurrenceId("series", "b", Ten, 2), edge.SuccessorOccurrenceId); Assert.Equal(SupersessionReasonCode.PredecessorRevised, edge.ReasonCode); Assert.Equal(At, edge.RecordedAt); });
        Tuples(ledger.Occurrences, 1);
        Assert.Equal(before, JsonSerializer.Serialize(ledger));
        var retry = Expand(revised with { PriorLedger = Apply(revised, next) });
        Assert.Equal(2, retry.ExpectedPriorLedgerVersion);
        Tuples(retry.CurrentOccurrences, 2);
        Assert.Empty(retry.AppendedOccurrences);
        Assert.Empty(retry.Supersessions);
        Assert.Empty(retry.Failures);
    }
}
