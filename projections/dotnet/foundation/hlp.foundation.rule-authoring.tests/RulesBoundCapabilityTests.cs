using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Compilation;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>T-590 slice 4: the developer-supplied tiers and skins the editors bind.</summary>
public sealed class RulesBoundCapabilityTests
{
    private static RuleDefinition Rule(Harborline.Contracts.Forms.RuleTier tier) => new()
    {
        Id = "t", Tier = tier, Scope = RuleScope.Field, ScopeTarget = "total", Expression = """{"+":[1,2]}""", Action = RuleActionKind.Compute,
    };

    [Fact(DisplayName = "rules-bound-5: Tier-1 JSON Schema is the kernel's, Tier-2 JSON Logic compiles here, and Tier-3 Power Fx is refused by code behind the same contract")]
    public void Tiers_are_bound_by_contract()
    {
        Assert.Equal(0, RuleCompiler.Compile([Rule(Harborline.Contracts.Forms.RuleTier.JsonSchema)]).RuleCount);
        Assert.Equal(1, RuleCompiler.Compile([Rule(Harborline.Contracts.Forms.RuleTier.JsonLogic)]).RuleCount);
        Assert.Equal(RuleEngineCodes.CompileUnsupportedTier,
            Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile([Rule(Harborline.Contracts.Forms.RuleTier.PowerFx)])).Code);
        Assert.Equal([RuleDefinitionTier.JsonSchema, RuleDefinitionTier.JsonLogic, RuleDefinitionTier.PowerFx], Enum.GetValues<RuleDefinitionTier>());
    }

    [Fact(DisplayName = "rules-bound-6: the author picks a decision table or a formula skin, and both lower to one compiled rule the same engine evaluates")]
    public void Skins_are_bound_and_lower_to_one_engine()
    {
        foreach (var skin in new[] { RuleSkinType.Table, RuleSkinType.Formula })
        {
            var seed = RuleSeeds.BlankDraftFor(skin);
            Assert.Equal(skin == RuleSkinType.Table, seed is DecisionTableDraft);
        }
        var tableSeed = RuleSeeds.BlankTableDraft();
        var table = tableSeed with { Rows = [tableSeed.Rows[0] with { Output = "in-range" }], NoMatch = new NoMatchPosture.Default("none") };
        var formula = RuleSeeds.BlankFormulaDraft() with { Expression = new FormulaExpr.Literal("7", ColumnValueType.Number) };
        foreach (var (id, draft, expected) in new (string, RuleDraft, string)[] { ("table", table, "\"in-range\""), ("formula", formula, "7") })
        {
            var definition = SkinLowering.CompileDraft(draft, id);
            Assert.Equal(Harborline.Contracts.Forms.RuleTier.JsonLogic, definition.Tier);
            var preview = SkinLowering.EvaluatePreview(draft, id, JsonNode.Parse("""{"amount":5}""")!.AsObject(), TimeProvider.System);
            Assert.Equal(expected, preview.Value?.ToJsonString());
        }
        // The table's no-match branch is reachable through the same engine.
        Assert.Equal("\"none\"", SkinLowering.EvaluatePreview(table, "table", JsonNode.Parse("""{"amount":-1}""")!.AsObject(), TimeProvider.System).Value?.ToJsonString());
    }
}
