using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Conformance;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// T-1025: the reactive paths the shared corpus cannot reach. These are the .NET twins of <c>reactive.test.ts</c>'s
/// "does not retain a rejected over-limit reactive row" and "a fail-closed graph reports no pending work".
/// </summary>
public sealed class RuleErrorParameterTests
{
    private static readonly DateTimeOffset Clock = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private static RuleDefinition Compute(string id, string target, string expr) =>
        RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, RuleScope.Field, target, expr, RuleActionKind.Compute);

    [Fact]
    public void An_over_limit_reactive_row_refuses_the_aggregate_naming_its_section_and_leaves_other_cells_alone()
    {
        var limits = RuleEngineLimits.Default with { MaxTableRowsPerAggregate = 1 };
        var graph = new FormRuleGraph(
            RuleCompiler.Compile([
                Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}"),
                Compute("c.other", "other", "{\"var\":\"table.sum(items2.amount)\"}"),
            ], limits),
            new FixedClock(Clock), TestAdmission.Any, limits);
        graph.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse(
            "{\"items\":[{\"_id\":\"r1\",\"amount\":1}],\"items2\":[{\"_id\":\"s1\",\"amount\":5}]}")!.AsObject()));

        var refused = graph.AddRow("items", new RuleRow("r2", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(2) }));

        Assert.Equal("""{"error":{"code":"rule.table_too_large","params":{"section":"items"}},"state":"Error"}""",
            CanonicalJson.SerializeComputedValue(refused.Values["agg:items/sum/amount"]));
        Assert.Equal("""{"state":"Resolved","value":1}""", CanonicalJson.SerializeComputedValue(refused.Values["field:total"]));
        // A section whose name merely starts with the refused one keeps its aggregate.
        Assert.Equal("""{"state":"Resolved","value":5}""", CanonicalJson.SerializeComputedValue(refused.Values["agg:items2/sum/amount"]));
    }

    [Fact]
    public void A_fail_closed_graph_reports_no_pending_work()
    {
        var limits = RuleEngineLimits.Default with { MaxGraphNodes = 1 };
        var graph = new FormRuleGraph(
            RuleCompiler.Compile([Compute("a.x", "x", "1"), Compute("a.y", "y", "2")], limits),
            new FixedClock(Clock), TestAdmission.Any, limits);

        var refused = graph.EvaluateInstance(RuleInstance.FromJson(new JsonObject()));

        Assert.Equal(RuleEngineCodes.GraphTooLarge, Assert.Single(refused.Validations).Validity!.Error!.Code);
        Assert.False(refused.HasPending);
    }
}
