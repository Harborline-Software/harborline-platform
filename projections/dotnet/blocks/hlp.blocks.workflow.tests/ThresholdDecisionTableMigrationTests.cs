using System.Text.Json.Nodes;

using Harborline.Blocks.Workflow.Durable;

using Harborline.Contracts.Forms;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0146 D2 migration equivalence (board F1). Proves the hardcoded <see cref="ThresholdDecisionTable"/>
/// migrates faithfully to a decision-table AUTHORING SKIN that compiles to the ratified D1 rule-engine AST
/// and <b>routes identically</b> to the original table across the amount domain — preserving
/// priority/highest-floor-wins, the floor-0 catch-all, and the reified upper bounds.
///
/// <para>SCOPE (Wave 1): this proves the migration is faithful; it does NOT rewire the production
/// <see cref="InvoiceApprovalHandler"/> off the table. In-flight ADR 0135 D7 pins stay against the
/// pre-migration table (no retroactive re-pin). The production consumer cutover — resolving the rule-versioned
/// table through the named registry (ADR 0146 D5, PR #1829) and pinning via its <c>RulePin</c> — lands in the
/// wave that merges the registry and clears the D11 CP-safety re-review. The reference to
/// foundation-rule-engine here is TEST-ONLY (production blocks-workflow takes no rule-engine dependency).</para>
/// </summary>
public sealed class ThresholdDecisionTableMigrationTests
{
    // The canonical v1 invoice-approval table (matches ThresholdDecisionTableTests / the production wiring):
    // strictly-above-$5000 → RequireApproval; $5000.00 exactly and below → AutoApprove.
    private static ThresholdDecisionTableVersion V1() => new()
    {
        Version = "2026-06-23.1",
        EffectiveFrom = new DateTimeOffset(2026, 6, 23, 0, 0, 0, TimeSpan.Zero),
        Rows = new[]
        {
            new ThresholdDecisionRow("under-5k", 0m, ApprovalDecision.AutoApprove),
            new ThresholdDecisionRow("over-5k", 5000.01m, ApprovalDecision.RequireApproval),
        },
    };

    // A second, higher-threshold version — proves the converter is general (not hardcoded to $5k).
    private static ThresholdDecisionTableVersion V2() => new()
    {
        Version = "2026-09-01.1",
        EffectiveFrom = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        Rows = new[]
        {
            new ThresholdDecisionRow("under-10k", 0m, ApprovalDecision.AutoApprove),
            new ThresholdDecisionRow("over-10k", 10000.01m, ApprovalDecision.RequireApproval),
        },
    };

    /// <summary>
    /// Converts a floor-only <see cref="ThresholdDecisionTableVersion"/> to the equivalent decision-table skin:
    /// each row becomes a REIFIED half-open interval <c>[floor_i, floor_{i+1})</c> (the top row open-high),
    /// priority = the floor rank (so highest-floor-wins), floor-0 as the mandatory catch-all. This is the
    /// migration artifact — the same transform the production cutover will promote.
    /// </summary>
    private static DecisionTableSkin ToSkin(ThresholdDecisionTableVersion version, string ruleId)
    {
        var byFloor = version.Rows.OrderBy(r => r.MinAmountInclusive).ToList();
        var rows = new List<DecisionRow>();
        for (int i = 0; i < byFloor.Count; i++)
        {
            JsonNode lo = JsonValue.Create(byFloor[i].MinAmountInclusive);
            JsonNode? hi = i + 1 < byFloor.Count ? JsonValue.Create(byFloor[i + 1].MinAmountInclusive) : null;
            rows.Add(new DecisionRow(
                When: new[] { DecisionCell.Range(lo, hi) },
                Output: JsonValue.Create(byFloor[i].Decision.ToString()),
                Priority: i)); // higher floor → higher priority → highest-floor-wins
        }
        return new DecisionTableSkin(
            RuleId: ruleId,
            Scope: RuleScope.Field,
            ScopeTarget: "decision",
            Action: RuleActionKind.Compute,
            HitPolicy: HitPolicy.Priority,
            Inputs: new[] { "amount" },
            Rows: rows,
            // The original throws on a no-match (below the floor-0 row); invoice amounts are non-negative by
            // construction so this is the out-of-domain sentinel. Board F1: an EXPLICIT default, never a
            // silent null — the migration replaces the original's defensive throw with a defined default.
            NoMatch: NoMatch.WithDefault(JsonValue.Create(ApprovalDecision.AutoApprove.ToString())));
    }

    private static string EvaluateSkin(DecisionTableSkin skin, decimal amount)
    {
        var rule = DecisionTableCompiler.Compile(skin);
        var compiled = RuleCompiler.Compile(new[] { rule });
        var graph = new FormRuleGraph(compiled, RuleEngineLimits.Default, TimeProvider.System);
        var instance = RuleInstance.FromJson(new JsonObject { ["amount"] = JsonValue.Create(amount) });
        var result = graph.EvaluateInstance(instance);
        var outcome = result.ByRule[skin.RuleId];
        Assert.Equal(Harborline.Foundation.RuleEngine.Model.OutputType.Value, outcome.OutputType);
        Assert.Equal(Harborline.Foundation.RuleEngine.Model.ValueState.Resolved, outcome.Value!.State);
        return outcome.Value.Value!.GetValue<string>();
    }

    [Theory(DisplayName = "Migration (board F1): the decision-table skin routes IDENTICALLY to the hardcoded ThresholdDecisionTable across the amount domain")]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(5000.00)]   // $5000 exactly — under the strictly-above gate
    [InlineData(5000.01)]   // the first over-threshold cent
    [InlineData(7500)]
    [InlineData(50000)]
    public void Skin_RoutesIdenticallyTo_V1Table(decimal amount)
    {
        var version = V1();
        var expected = version.Evaluate(amount).Decision.ToString();
        var actual = EvaluateSkin(ToSkin(version, "invoice.approval"), amount);
        Assert.Equal(expected, actual);
    }

    [Theory(DisplayName = "Migration: the converter is general — a $10k-threshold version also routes identically")]
    [InlineData(0)]
    [InlineData(9999.99)]
    [InlineData(10000.00)]  // $10000 exactly — under
    [InlineData(10000.01)]  // first over-threshold cent
    [InlineData(25000)]
    public void Skin_RoutesIdenticallyTo_V2Table(decimal amount)
    {
        var version = V2();
        var expected = version.Evaluate(amount).Decision.ToString();
        var actual = EvaluateSkin(ToSkin(version, "invoice.approval"), amount);
        Assert.Equal(expected, actual);
    }
}
