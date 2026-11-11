using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;
using Harborline.Kernel.Core;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class RelativeChainOccurrenceTests
{
    private static DateTimeOffset I(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static readonly DateTimeOffset Now = I("2026-07-01T20:00:00Z");
    private static ScheduleChainNode Node(string id, string? after, string offset)
        => new(id, after, offset, new("PT1H", "PT2H"), new("-PT3H", "PT4H"));
    private static RelativeChainExpansionRequest Request(params ScheduleChainNode[] nodes)
        => new(new("visits", 1, "visit", nodes), "series", "subject",
            [new("visit", 1, I("2026-07-01T08:00:00Z"), AnchorSnapshotState.Present, ["visit/r1"])], new(0, [], []), Now, "UTC");
    private static RelativeChainExpansionResult Expand(RelativeChainExpansionRequest request)
        => new InMemoryRelativeChainExpansionService().Expand(request);
    private static RelativeChainLedger Apply(RelativeChainExpansionRequest request, RelativeChainExpansionResult result)
        => new(request.PriorLedger.LedgerVersion + 1, [.. request.PriorLedger.Occurrences, .. result.AppendedOccurrences],
            [.. request.PriorLedger.Supersessions, .. result.Supersessions]);

    [Fact(DisplayName = "scheduling-eng-5, scheduling-run-2, L449: moving the one anchor re-anchors every branch and supersedes exact revisions")]
    public void MovingAnchorReanchorsWholeChainAndKeepsLedgerEvidence()
    {
        var request = Request(Node("a", null, "PT0S"), Node("b", "a", "PT2H"), Node("c", null, "P1D"));
        var first = Expand(request);
        Assert.Empty(first.Failures);
        Assert.Equal([I("2026-07-01T08:00:00Z"), I("2026-07-01T10:00:00Z"), I("2026-07-02T08:00:00Z")], first.CurrentOccurrences.Select(o => o.DueDate));
        Assert.Equal(I("2026-07-01T05:00:00Z"), first.CurrentOccurrences[0].EarliestDate);
        Assert.Equal(I("2026-07-01T12:00:00Z"), first.CurrentOccurrences[0].LatestDate);
        var ledger = Apply(request, first);
        var moved = request with { PriorLedger = ledger, Anchors = [new("visit", 2, I("2026-07-02T08:00:00Z"), AnchorSnapshotState.Present, ["visit/r2"])] };
        var next = Expand(moved);
        Assert.Equal([I("2026-07-02T08:00:00Z"), I("2026-07-02T10:00:00Z"), I("2026-07-03T08:00:00Z")], next.CurrentOccurrences.Select(o => o.DueDate));
        Assert.Equal(3, next.AppendedOccurrences.Count);
        Assert.Equal([SupersessionReasonCode.AnchorMoved, SupersessionReasonCode.PredecessorRevised, SupersessionReasonCode.AnchorMoved], next.Supersessions.Select(s => s.ReasonCode));
        Assert.All(next.Supersessions, s => { Assert.NotEmpty(s.ExplanationRefs); Assert.Equal(Now, s.RecordedAt); Assert.NotNull(s.SuccessorOccurrenceId); });
        Assert.All(first.CurrentOccurrences, o => Assert.Contains(next.Supersessions, s => s.SupersededOccurrenceId == o.OccurrenceId));
        Assert.Equal(1, next.ExpectedPriorLedgerVersion);
        var retry = Expand(moved with { PriorLedger = Apply(moved, next) });
        Assert.Empty(retry.AppendedOccurrences);
        Assert.Empty(retry.Supersessions);
        Assert.Equal(next.CurrentOccurrences, retry.CurrentOccurrences);
    }

    [Fact]
    public void SameInputsSameKeysAndRegenerationAppendsNothing()
    {
        var request = Request(Node("a", null, "PT0S"), Node("b", "a", "P1W"));
        var first = Expand(request);
        Assert.Equal("rc1/series/a/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", first.CurrentOccurrences[0].OccurrenceId.ToString());
        Assert.Equal(I("2026-07-08T08:00:00Z"), first.CurrentOccurrences[1].DueDate);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first.CurrentOccurrences), System.Text.Json.JsonSerializer.Serialize(Expand(request).CurrentOccurrences));
        var retry = Expand(request with { PriorLedger = Apply(request, first) });
        Assert.Empty(retry.AppendedOccurrences);
        Assert.Empty(retry.Supersessions);
        Assert.Equal(first.CurrentOccurrences, retry.CurrentOccurrences);
    }

    [Fact]
    public void SameInstantNewAnchorRevisionSupersedesWithoutRewritingHistory()
    {
        var request = Request(Node("a", null, "PT0S"));
        var first = Expand(request);
        var next = Expand(request with { PriorLedger = Apply(request, first), Anchors = [request.Anchors[0] with { AnchorRevision = 2 }] });
        Assert.Equal(SupersessionReasonCode.AnchorRevised, Assert.Single(next.Supersessions).ReasonCode);
        Assert.Equal(2, Assert.Single(next.AppendedOccurrences).OccurrenceId.OccurrenceRevision);
        Assert.Equal(1, Assert.Single(first.CurrentOccurrences).OccurrenceId.OccurrenceRevision);
    }

    [Fact]
    public void EffectiveDefinitionChangeSupersedesButMetadataRevisionAloneDoesNot()
    {
        var request = Request(Node("a", null, "PT0S"));
        var first = Expand(request);
        request = request with { PriorLedger = Apply(request, first) };
        Assert.Empty(Expand(request with { Definition = request.Definition with { DefinitionRevision = 2 } }).AppendedOccurrences);
        var next = Expand(request with { Definition = request.Definition with { DefinitionRevision = 3, Nodes = [Node("a", null, "PT1H")] } });
        Assert.Equal(SupersessionReasonCode.DefinitionRevised, Assert.Single(next.Supersessions).ReasonCode);
        Assert.Equal(I("2026-07-01T09:00:00Z"), Assert.Single(next.CurrentOccurrences).DueDate);
    }

    [Fact]
    public void RemovedNodeHasTerminalSupersession()
    {
        var request = Request(Node("a", null, "PT0S"), Node("b", "a", "PT1H"));
        var first = Expand(request);
        var next = Expand(request with { PriorLedger = Apply(request, first), Definition = request.Definition with { Nodes = [request.Definition.Nodes[0]] } });
        Assert.Equal("a", Assert.Single(next.CurrentOccurrences).OccurrenceId.NodeId);
        Assert.Null(Assert.Single(next.Supersessions).SuccessorOccurrenceId);
        Assert.Equal("b", next.Supersessions[0].NodeId);
    }

    [Fact]
    public void MissingAndDeletedAnchorsAreExplicitFailuresAndDeletionSupersedesDescendants()
    {
        var request = Request(Node("a", null, "PT0S"), Node("b", "a", "PT1H"));
        Assert.Contains(Expand(request with { Anchors = [] }).Failures, f => f.Code == RelativeChainFailureCode.MissingAnchor);
        var first = Expand(request);
        var deleted = Expand(request with { PriorLedger = Apply(request, first), Anchors = [new("visit", 2, null, AnchorSnapshotState.Deleted, ["visit/deleted"])] });
        Assert.Equal(2, deleted.Supersessions.Count);
        Assert.All(deleted.Supersessions, s => Assert.Null(s.SuccessorOccurrenceId));
        Assert.Empty(deleted.CurrentOccurrences);
        Assert.Contains(deleted.Failures, f => f.Code == RelativeChainFailureCode.DeletedAnchor);
    }

    [Theory]
    [InlineData("-PT1H", null)]
    [InlineData("-P10675199DT2H48M5.4775808S", null)]
    [InlineData("P36501D", null)]
    [InlineData("PT1H", "missing")]
    [InlineData("PT1H", "a")]
    public void InvalidOffsetsUnknownPredecessorsAndCyclesRefuse(string offset, string? after)
    {
        var result = Expand(Request(Node("a", after, offset)));
        Assert.NotEmpty(result.Failures);
        Assert.Empty(result.AppendedOccurrences);
        Assert.All(result.Failures, f => Assert.NotEmpty(f.ExplanationRefs));
    }

    [Fact]
    public void DuplicateNodesRefuse()
    {
        var result = Expand(Request(Node("a", null, "PT0S"), Node("a", null, "PT1H")));
        Assert.NotEmpty(result.Failures);
        Assert.Empty(result.AppendedOccurrences);
    }

    [Fact]
    public void CorruptDuplicateAndForkedLedgersRefuse()
    {
        var request = Request(Node("a", null, "PT0S"));
        var occurrence = Assert.Single(Expand(request).CurrentOccurrences);
        Assert.Equal(RelativeChainFailureCode.CorruptLedger, Assert.Single(Expand(request with { PriorLedger = new(1, [occurrence, occurrence], []) }).Failures).Code);
        var fork = occurrence with { OccurrenceId = occurrence.OccurrenceId with { OccurrenceRevision = 2 } };
        Assert.Equal(RelativeChainFailureCode.AmbiguousCurrentOccurrence, Assert.Single(Expand(request with { PriorLedger = new(1, [occurrence, fork], []) }).Failures).Code);
    }

    [Fact]
    public void OldCompletionDoesNotCompleteTheMovedOccurrenceAndNodeWithoutWindowNeverVoids()
    {
        var chain = Request(Node("a", null, "PT0S") with { Window = null });
        var first = Expand(chain);
        chain = chain with { PriorLedger = Apply(chain, first), Anchors = [chain.Anchors[0] with { AnchorRevision = 2, AnchorDate = I("2026-07-02T08:00:00Z") }] };
        var definition = new ScheduleDefinition(null, "Visits", new(ScheduleStartKind.Form, "visit"),
            Basis: new EventAnchoredChainBasis(chain.Definition.Anchor, chain.Definition.Nodes));
        var request = new DueOccurrenceRequest(new("series", "subject", definition, Timezone: "UTC"), I("2026-07-01T00:00:00Z"), I("2026-07-03T23:00:00Z"),
            [new("old-completion", "series", first.CurrentOccurrences[0].OccurrenceId.ToString(), I("2026-07-01T09:00:00Z"))], [], chain);
        var occurrence = Assert.Single(new RelativeChainDueOccurrenceSource().DeriveDue(request, I("2026-07-03T20:00:00Z")));
        Assert.Equal(I("2026-07-02T08:00:00Z"), occurrence.Due);
        Assert.True(occurrence.IsDue);
        Assert.Null(occurrence.CompletedAt);
        Assert.Null(occurrence.Latest);
        Assert.Equal(OccurrenceClassification.Late, occurrence.Classification);
    }
}
