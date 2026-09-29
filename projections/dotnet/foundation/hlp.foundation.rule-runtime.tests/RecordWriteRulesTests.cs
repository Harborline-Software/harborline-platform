using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Records;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-978: the Rules stage a Records write runs before commit (DES-0018 rules-eng-13/14, L272, L274).</summary>
public sealed class RecordWriteRulesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);

    private static RuleDefinition Rule(string id, string expression, RuleActionKind action = RuleActionKind.Validate)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, RuleScope.Schema, "", expression, action);

    private static readonly RuleDefinition PositiveRent = Rule("rent-positive", """{">":[{"var":"rent"},0]}""");

    private static JsonObject Record(int rent) => new() { ["rent"] = rent, ["unit"] = "4B" };

    [Fact(DisplayName = "T-978: a record write whose bound rule refuses it is refused with the rule named")]
    public void Violating_write_is_refused_with_the_rule_named()
    {
        var rules = RecordWriteRules.Bind("lease", [PositiveRent]);

        var refusal = rules.Evaluate(Record(0), At);

        Assert.Equal(new RecordRuleRefusal("rent-positive", "rent-positive"), refusal);
    }

    [Fact(DisplayName = "T-978: a record write that satisfies every bound rule passes the stage")]
    public void Satisfying_write_passes()
    {
        var rules = RecordWriteRules.Bind("lease", [PositiveRent, Rule("unit-named", """{"!!":[{"var":"unit"}]}""")]);

        Assert.Null(rules.Evaluate(Record(1200), At));
    }

    [Fact(DisplayName = "T-978: each bound rule is evaluated, so the second rule's refusal is not skipped")]
    public void Every_rule_is_evaluated()
    {
        var rules = RecordWriteRules.Bind("lease", [PositiveRent, Rule("unit-named", """{"!!":[{"var":"unit"}]}""")]);

        Assert.Equal("unit-named", rules.Evaluate(new JsonObject { ["rent"] = 5 }, At)?.RuleId);
    }

    [Fact(DisplayName = "L274: a bound rule that cannot be interpreted during the write refuses it rather than being skipped")]
    public void Uninterpretable_rule_refuses_the_write()
    {
        var rules = RecordWriteRules.Bind("lease", [PositiveRent], RuleEngineLimits.Default with { StepBudget = 1 });

        var refusal = rules.Evaluate(Record(1200), At);

        Assert.NotNull(refusal);
        Assert.Equal("rent-positive", refusal.RuleId);
        Assert.NotEqual("rent-positive", refusal.Code);
    }

    [Fact(DisplayName = "T-978 ruling: a record rule reuses the standing shape, so binding refuses anything but a schema-scoped validation predicate over top-level fields")]
    public void Binding_refuses_a_rule_outside_the_standing_shape()
    {
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind("lease", [Rule("c", """{"var":"rent"}""", RuleActionKind.Compute)]));
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind("lease",
            [RuleDefinitionFactory.Create("f", RuleTier.JsonLogic, RuleScope.Field, "rent", """{">":[{"var":"rent"},0]}""", RuleActionKind.Validate)]));
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind("lease", [Rule("t", """{">":[{"agg":["sum","rows","amount"]},0]}""")]));
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind("lease", [Rule("m", """{">":[{"agg":["sum","rows","amount"]},{"var":"rent"}]}""")]));
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind("lease", [PositiveRent, PositiveRent]));
        Assert.Throws<ArgumentException>(() => RecordWriteRules.Bind(" ", [PositiveRent]));
        Assert.Throws<ArgumentNullException>(() => RecordWriteRules.Bind("lease", null!));
        Assert.Throws<ArgumentNullException>(() => RecordWriteRules.Bind("lease", [null!]));
        Assert.Throws<ArgumentNullException>(() => RecordWriteRules.Bind("lease", [PositiveRent]).Evaluate(null!, At));
    }

    [Fact(DisplayName = "T-978: rules evaluate in rule-id order whatever order the generation lists them, so the refusal named is deterministic")]
    public void Rules_evaluate_in_rule_id_order()
    {
        var rules = RecordWriteRules.Bind("lease", [Rule("unit-named", """{"!!":[{"var":"unit"}]}"""), PositiveRent]);

        Assert.Equal(["rent-positive", "unit-named"], rules.Rules.Select(rule => rule.Id));
        Assert.Equal("rent-positive", rules.Evaluate(new JsonObject { ["rent"] = 0 }, At)?.RuleId);
        Assert.Equal("lease", rules.RecordType);
    }

    [Fact(DisplayName = "rules-eng-26: the stage evaluates under the Records borrower declaration released as records-auth-12, in its Submission phase")]
    public void Stage_evaluates_under_the_released_records_declaration()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(FunctionRegisterTests.RepositoryRoot(),
            "conformance", "hlp.foundation.rule-runtime", "borrower-contracts.json")))!["borrowers"]!.AsArray()
            .Single(node => node!["declaration"]!["borrower"]!.GetValue<string>() == "records-auth-12")!;

        Assert.Equal(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(RecordWriteRules.Declaration));
        Assert.Equal(EvaluationPhase.Submission, RecordWriteRules.Phase);
        Assert.Equal(fixture["admittedPhase"]!.GetValue<string>(), RecordWriteRules.Phase.ToString());
    }
}
