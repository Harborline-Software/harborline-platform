using System.Text.Json;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.Taxonomy.Tests;

public sealed class TaxonomyPinnedSuccessionTests
{
    // DES-0024 eng-1/9, run-4, ck-3/16; interface revision 4.
    // Both versions contain both active targets: only the successor edge differs.
    [Fact(DisplayName = "taxonomy-eng-1 taxonomy-eng-9 taxonomy-run-4: succession stays pinned")]
    public void Pinned_versions_keep_their_successor_and_active_hop_counts()
    {
        var v1 = Definition("1.0.0", [Retired("old", "a"), Active("a"), Active("b")]);
        var v2 = Definition("2.0.0", [Retired("old", "b"), Active("a"), Active("b")]);
        var interpreter = new TaxonomyInterpreter([v1, v2]);

        Check("1.0.0", "a");
        Check("2.0.0", "b");
        Check("1.0.0", "a");
        var missing = Assert.IsType<TaxonomyDefinitionNotFound>(interpreter.ResolveDefinition(Coordinates("3.0.0")));
        Assert.Equal("tenant-a", missing.Coordinates.Tenant);
        Assert.Equal(new TaxonomyDefinitionId("acme", "health", "succession"), missing.Coordinates.DefinitionId);
        Assert.Equal("3.0.0", missing.Coordinates.Version);

        void Check(string version, string expectedCode)
        {
            var pinned = Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(Coordinates(version))).Definition;
            Assert.Equal(version, pinned.Version);
            var successor = Assert.IsType<ResolvedTaxonomySuccession>(interpreter.ResolveSuccession(pinned, "old"));
            Assert.Equal(expectedCode, successor.Node.Code);
            Assert.Equal(1, successor.HopCount);
            var active = Assert.IsType<ResolvedTaxonomySuccession>(interpreter.ResolveSuccession(pinned, expectedCode));
            Assert.Equal(expectedCode, active.Node.Code);
            Assert.Equal(0, active.HopCount);
        }
    }

    // Isolated raw corruption fixtures exercise defensive traversal, not admission.
    // Literal refusal tuples: DES-0024 eng-10/run-4 and interface revision 4.
    [Theory(DisplayName = "taxonomy-eng-10 taxonomy-run-4: refusal names the last resolved hop")]
    [InlineData("missing", "successor_missing", "old")]
    [InlineData("unknown", "successor_unknown", "mid")]
    [InlineData("cycle", "successor_cycle", "mid")]
    public void Nontermination_preserves_request_reason_and_last_hop(string scenario, string reason, string lastHop)
    {
        TaxonomyNode[] nodes = scenario switch
        {
            "missing" => [Retired("old", null)],
            "unknown" => [Retired("old", "mid"), Retired("mid", "missing")],
            "cycle" => [Retired("old", "mid"), Retired("mid", "old")],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var raw = Definition("1.0.0", nodes);
        var interpreter = new TaxonomyInterpreter([raw]);
        var pinned = Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(Coordinates("1.0.0"))).Definition;
        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(interpreter.ResolveSuccession(pinned, "old"));
        Assert.Equal("old", refusal.Code);
        Assert.Equal(reason, refusal.Reason);
        Assert.Equal(lastHop, refusal.LastHopCode);
    }

    // The literal 64/65 edge boundary comes from interface revision 4, not a producer constant.
    [Fact(DisplayName = "taxonomy-eng-8 taxonomy-run-4: succession bounds 64 and 65 edges")]
    public void Sixty_four_edges_succeed_and_a_sixty_fifth_refuses_at_n64()
    {
        var atBoundary = Definition("1.0.0", Chain(64, "n64"));
        var beyondBoundary = Definition("2.0.0", Chain(65, "n65"));
        var interpreter = new TaxonomyInterpreter([atBoundary, beyondBoundary]);
        var pinned = Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(Coordinates("1.0.0"))).Definition;
        var success = Assert.IsType<ResolvedTaxonomySuccession>(interpreter.ResolveSuccession(pinned, "n0"));
        Assert.Equal("n64", success.Node.Code);
        Assert.Equal(64, success.HopCount);
        pinned = Assert.IsType<ResolvedTaxonomyDefinition>(interpreter.ResolveDefinition(Coordinates("2.0.0"))).Definition;
        var refusal = Assert.IsType<TaxonomySuccessionRefusal>(interpreter.ResolveSuccession(pinned, "n0"));
        Assert.Equal("n0", refusal.Code);
        Assert.Equal("successor_depth_exceeded", refusal.Reason);
        Assert.Equal("n64", refusal.LastHopCode);
    }

    private static TaxonomyNode[] Chain(int edges, string terminal) => Enumerable.Range(0, edges)
        .Select(i => Retired($"n{i}", $"n{i + 1}"))
        .Append(Active(terminal)).ToArray();
    private static TaxonomyNode Active(string code) => new(code, code, "active target", TaxonomyNodeStatus.Active, []);
    private static TaxonomyNode Retired(string code, string? successor) => new(code, code, "retired concept",
        TaxonomyNodeStatus.Tombstoned, [], SuccessorCode: successor, DeprecationReason: "retired");
    private static TaxonomyDefinitionCoordinates Coordinates(string version) => new("tenant-a", new("acme", "health", "succession"), version);
    private static TaxonomyDefinition Definition(string version, IReadOnlyList<TaxonomyNode> nodes) => new(
        "tenant-a", new("acme", "health", "succession"), version, TaxonomyGovernanceRegime.Civilian, "author", nodes,
        Envelope: new("acme.health.succession", version, "tenant-a", TaxonomyCascadeLayer.Tenant,
            JsonElement.Parse("{}"), "regulated", false, [], new(1, 0)));
}
