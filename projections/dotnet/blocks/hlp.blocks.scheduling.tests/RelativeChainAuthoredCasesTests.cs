using System.Text.Json;
using Harborline.Blocks.Scheduling;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class RelativeChainAuthoredCasesTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryRelativeChainExpansionService service = new();

    [Fact(DisplayName = "086-01 external anchor node accepted")]
    public void Case01() { var result = Expand(Definition(External("root", "visit", 0)) with { Anchor = "visit" }, Anchor("visit", 1, D(1))); Assert.Empty(result.Failures); Assert.Equal("visit", Assert.Single(result.CurrentOccurrences).BasisRef); }

    [Fact(DisplayName = "086-02 predecessor node accepted")]
    public void Case02() { var result = Expand(Definition(External("a", "visit", 0), Predecessor("b", "a", 1)) with { Anchor = "visit" }, Anchor("visit", 1, D(1))); Assert.Equal(result.CurrentOccurrences[0].OccurrenceId.ToString(), result.CurrentOccurrences[1].BasisRef); }

    [Fact(DisplayName = "086-03 unknown predecessor refused with ref")]
    public void Case03() { var result = Expand(Definition(Predecessor("b", "missing", 1))); AssertFailure(result, RelativeChainFailureCode.UnsupportedDefinition); Assert.Empty(result.AppendedOccurrences); }

    [Fact(DisplayName = "086-04 cycle refused with cycle refs")]
    public void Case04() { var result = Expand(Definition(Predecessor("a", "b", 1), Predecessor("b", "a", 1))); var failure = AssertFailure(result, RelativeChainFailureCode.UnsupportedDefinition); Assert.NotEmpty(failure.ExplanationRefs); }

    [Fact(DisplayName = "086-05 duplicate node id refused")]
    public void Case05() { var result = Expand(Definition(External("a", "x", 0), External("a", "x", 0))); AssertFailure(result, RelativeChainFailureCode.UnsupportedDefinition); Assert.Empty(result.AppendedOccurrences); }

    [Fact(DisplayName = "086-06 chain node bound is explicit at admission and run")]
    public void Case06() { var nodes = Enumerable.Range(0, 1025).Select(i => External("n" + i, "x", 0)).ToArray(); AssertFailure(Expand(Definition(nodes), Anchor("x", 1, D(1))), RelativeChainFailureCode.UnsupportedDefinition); }

    [Fact(DisplayName = "086-07 positive offset and identical regeneration")]
    public void Case07() { var request = Request(Definition(External("a", "x", 3)), [Anchor("x", 1, D(1))]); var first = service.Expand(request); Assert.Equal(D(4), Assert.Single(first.CurrentOccurrences).DueDate); var second = service.Expand(request with { PriorLedger = Apply(request.PriorLedger, first) }); Assert.Empty(second.AppendedOccurrences); }

    [Fact(DisplayName = "086-08 zero offset")]
    public void Case08() { var occurrence = Assert.Single(Expand(Definition(External("a", "x", 0)), Anchor("x", 1, D(1))).CurrentOccurrences); Assert.Equal(D(1), occurrence.DueDate); Assert.NotEqual("x", occurrence.OccurrenceId.ToString()); }

    [Fact(DisplayName = "086-09 negative offset refused")]
    public void Case09() => AssertFailure(Expand(Definition(External("a", "x", -1)), Anchor("x", 1, D(1))), RelativeChainFailureCode.UnsupportedDefinition);

    [Fact(DisplayName = "086-10 multi-hop offsets compose")]
    public void Case10() { var result = Expand(Definition(External("a", "x", 2), Predecessor("b", "a", 3)), Anchor("x", 1, D(1))); Assert.Equal([D(3), D(6)], result.CurrentOccurrences.Select(item => item.DueDate)); Assert.Equal(result.CurrentOccurrences[0].OccurrenceId.ToString(), result.CurrentOccurrences[1].BasisRef); }

    [Fact(DisplayName = "086-11 inclusive tolerance derived")]
    public void Case11() { var node = External("a", "x", 9) with { Tolerance = new("P2D", "P3D"), Window = new("-P2D", "P3D") }; var occurrence = Assert.Single(Expand(Definition(node), Anchor("x", 1, D(1))).CurrentOccurrences); Assert.Equal(D(8), occurrence.EarliestDate); Assert.Equal(D(13), occurrence.LatestDate); }

    [Fact(DisplayName = "086-12 inclusive query clipping after current resolution")]
    public void Case12() { var result = Expand(Definition(External("a", "x", 0), External("b", "x", 2), External("c", "x", 4)), Anchor("x", 1, D(1))); var due = Derive(Definition(External("a", "x", 0), External("b", "x", 2), External("c", "x", 4)), [], D(1), D(3), D(3)); Assert.Equal([D(1), D(3)], due.Select(item => item.Due)); }

    [Fact(DisplayName = "086-13 cross-process deterministic serialization")]
    public void Case13() { var request = Request(Definition(External("b", "x", 1), External("a", "x", 0)), [Anchor("x", 1, D(1))]); var one = JsonSerializer.Serialize(service.Expand(request)); TimeZoneInfo.ClearCachedData(); var two = JsonSerializer.Serialize(service.Expand(request)); Assert.Equal(one, two); }

    [Fact(DisplayName = "086-14 moving root revises all descendants")]
    public void Case14() { var definition = Definition(External("a", "x", 0), Predecessor("b", "a", 1)); var first = Expand(definition, Anchor("x", 1, D(1))); var second = service.Expand(Request(definition, [Anchor("x", 2, D(2))], Apply(EmptyLedger(), first))); Assert.Equal(2, second.AppendedOccurrences.Count); Assert.Equal([SupersessionReasonCode.AnchorMoved, SupersessionReasonCode.PredecessorRevised], second.Supersessions.Select(edge => edge.ReasonCode)); }

    [Fact(DisplayName = "086-15 moving the one external anchor revises every branch")]
    public void Case15() { var definition = Definition(External("ancestor", "root", 0), External("middle", "root", 1), Predecessor("child", "middle", 1)) with { Anchor = "root" }; var first = Expand(definition, Anchor("root", 1, D(1))); var second = service.Expand(Request(definition, [Anchor("root", 2, D(2))], Apply(EmptyLedger(), first))); Assert.Equal(["ancestor", "child", "middle"], second.AppendedOccurrences.Select(item => item.OccurrenceId.NodeId).Order()); }

    [Fact(DisplayName = "086-16 same-anchor sibling branches both re-anchor")]
    public void Case16() { var definition = Definition(External("a", "x", 0), External("b", "x", 1)); var first = Expand(definition, Anchor("x", 1, D(1))); var second = service.Expand(Request(definition, [Anchor("x", 2, D(3))], Apply(EmptyLedger(), first))); Assert.Equal([D(3), D(4)], second.CurrentOccurrences.Select(item => item.DueDate)); Assert.Equal(2, second.Supersessions.Count); }

    [Fact(DisplayName = "086-17 repeated regeneration idempotent")]
    public void Case17() { var definition = Definition(External("a", "x", 0)); var first = Expand(definition, Anchor("x", 1, D(1))); var ledger = Apply(EmptyLedger(), first); var second = service.Expand(Request(definition, [Anchor("x", 2, D(2))], ledger)); var third = service.Expand(Request(definition, [Anchor("x", 2, D(2))], Apply(ledger, second))); Assert.Empty(third.AppendedOccurrences); Assert.Empty(third.Supersessions); }

    [Fact(DisplayName = "086-18 completed predecessor remains exact historical revision")]
    public void Case18() { var definition = Definition(External("a", "x", 0), Predecessor("b", "a", 1)); var first = Expand(definition, Anchor("x", 1, D(1))); var old = first.CurrentOccurrences[0]; var second = service.Expand(Request(definition, [Anchor("x", 2, D(2))], Apply(EmptyLedger(), first))); var completion = new DueQueueCompletion("completed", "chain/one", old.OccurrenceId.ToString(), old.DueDate); var due = Derive(definition, [completion], D(1), D(5), D(5), Anchor("x", 2, D(2)), Apply(EmptyLedger(), first)); Assert.Equal(2, due.Count); }

    [Fact(DisplayName = "086-19 same-date derivation creates explicit new revision")]
    public void Case19() { var definition = Definition(External("a", "x", 0)); var first = Expand(definition, Anchor("x", 1, D(1))); var second = service.Expand(Request(definition, [Anchor("x", 2, D(1))], Apply(EmptyLedger(), first))); Assert.Equal(2, Assert.Single(second.AppendedOccurrences).OccurrenceId.OccurrenceRevision); Assert.Equal(SupersessionReasonCode.AnchorRevised, Assert.Single(second.Supersessions).ReasonCode); }

    [Fact(DisplayName = "086-20 chain-definition revision policy")]
    public void Case20() { var v1 = Definition(External("a", "x", 0)); var first = Expand(v1, Anchor("x", 1, D(1))); var same = service.Expand(Request(v1 with { DefinitionRevision = 2 }, [Anchor("x", 1, D(1))], Apply(EmptyLedger(), first))); Assert.Empty(same.AppendedOccurrences); var changed = service.Expand(Request(Definition(External("a", "x", 1)) with { DefinitionRevision = 3 }, [Anchor("x", 1, D(1))], Apply(EmptyLedger(), first))); Assert.Equal(SupersessionReasonCode.DefinitionRevised, Assert.Single(changed.Supersessions).ReasonCode); }

    [Fact(DisplayName = "086-21 current due projects through Scheduling port")]
    public void Case21() { var item = Assert.Single(Derive(Definition(External("a", "x", 0)), [], D(1), D(1), D(2))); Assert.StartsWith("rc1/", item.OccurrenceId); }

    [Fact(DisplayName = "086-22 chain occurrences are ordered by due instant at the seam")]
    public void Case22() { var due = Derive(Definition(External("a", "x", 0), External("b", "x", 1)), [], D(1), D(2), D(3)); Assert.Equal(D(1), due[0].Due); Assert.Equal(D(2), due[1].Due); }

    [Fact(DisplayName = "086-23 completion suppresses exact version only")]
    public void Case23() { var definition = Definition(External("a", "x", 0)); var first = Expand(definition, Anchor("x", 1, D(1))); var old = Assert.Single(first.CurrentOccurrences); var second = service.Expand(Request(definition, [Anchor("x", 2, D(1))], Apply(EmptyLedger(), first))); var completion = new DueQueueCompletion("completed", "chain/one", old.OccurrenceId.ToString(), old.DueDate); Assert.Single(Derive(definition, [completion], D(1), D(1), D(1), Anchor("x", 2, D(1)), Apply(EmptyLedger(), first))); }

    [Fact(DisplayName = "086-24 missing moved and deleted anchors have honest outcomes")]
    public void Case24() { var definition = Definition(External("a", "x", 0)); AssertFailure(Expand(definition), RelativeChainFailureCode.MissingAnchor); var first = Expand(definition, Anchor("x", 1, D(1))); var deleted = service.Expand(Request(definition, [new("x", 2, null, AnchorSnapshotState.Deleted, ["anchor/x/r2/deleted"])], Apply(EmptyLedger(), first))); AssertFailure(deleted, RelativeChainFailureCode.DeletedAnchor); Assert.Null(Assert.Single(deleted.Supersessions).SuccessorOccurrenceId); Assert.Throws<OccurrenceRefusalException>(() => Derive(definition, [], D(1), D(2), D(2), new("x", 2, null, AnchorSnapshotState.Deleted, ["deleted"]))); }

    private static IReadOnlyList<SchedulingOccurrence> Derive(RelativeChainDefinition definition, IReadOnlyList<DueQueueCompletion> completions, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, AnchorSnapshot? anchor = null, RelativeChainLedger? ledger = null)
    {
        var schedule = new ScheduleDefinition(null, "chain", new(ScheduleStartKind.Form, "f"), Basis: new EventAnchoredChainBasis(definition.Anchor, definition.Nodes));
        var request = new DueOccurrenceRequest(new("chain/one", "subject:one", schedule, Timezone: "UTC"), from, to, completions, [], Request(definition, [anchor ?? Anchor("x", 1, D(1))], ledger));
        return new RelativeChainDueOccurrenceSource().DeriveDue(request, now).OrderBy(o => o.Due).ToArray();
    }

    private RelativeChainExpansionResult Expand(RelativeChainDefinition definition, params AnchorSnapshot[] anchors) => service.Expand(Request(definition, anchors));
    private static RelativeChainExpansionRequest Request(RelativeChainDefinition definition, IReadOnlyList<AnchorSnapshot> anchors, RelativeChainLedger? ledger = null) => new(definition, "chain/one", "subject:one", anchors, ledger ?? EmptyLedger(), RecordedAt, "UTC");
    private static RelativeChainLedger EmptyLedger() => new(7, [], []);
    private static RelativeChainLedger Apply(RelativeChainLedger ledger, RelativeChainExpansionResult result) => new(ledger.LedgerVersion + 1, ledger.Occurrences.Concat(result.AppendedOccurrences).ToArray(), ledger.Supersessions.Concat(result.Supersessions).ToArray());
    private static RelativeChainDefinition Definition(params ScheduleChainNode[] nodes) => new("definition:one", 1, "x", nodes);
    private static ScheduleChainNode External(string id, string anchor, int offset) => new(id, null, offset < 0 ? "-P" + (-offset) + "D" : "P" + offset + "D", new("PT0S", "PT0S"));
    private static ScheduleChainNode Predecessor(string id, string predecessor, int offset) => new(id, predecessor, "P" + offset + "D", new("PT0S", "PT0S"));
    private static AnchorSnapshot Anchor(string id, int revision, DateTimeOffset date) => new(id, revision, date, AnchorSnapshotState.Present, [$"anchor/{id}/r{revision}"]);
    private static DateTimeOffset D(int day) => new(2026, 8, day, 0, 0, 0, TimeSpan.Zero);
    private static RelativeChainFailure AssertFailure(RelativeChainExpansionResult result, RelativeChainFailureCode code) { var failure = Assert.Single(result.Failures, item => item.Code == code); Assert.NotEmpty(failure.ExplanationRefs); return failure; }
}
