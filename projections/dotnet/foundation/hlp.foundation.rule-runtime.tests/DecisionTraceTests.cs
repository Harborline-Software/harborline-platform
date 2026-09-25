using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Explain;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Skins;
using Harborline.Foundation.RuleEngine.Standings;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-591: the decision trace and the separately authorized standing evidence read.</summary>
public sealed class DecisionTraceTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private static DecisionTableSkin Tiering(HitPolicy policy) => new(
        "tier.table", RuleScope.Field, "tier", RuleActionKind.Compute, policy, ["amount"],
        [
            new([DecisionCell.Compare(">=", 1000)], "gold", Priority: 1),
            new([DecisionCell.Compare(">=", 100)], "silver", Priority: 5),
        ],
        NoMatch.WithDefault("bronze"));

    private static readonly string[] Leaks = ["1500", "gold", "silver", "bronze"];

    private static RuleDefinition Formula() => RuleDefinitionFactory.Create(
        "limit.check", RuleTier.JsonLogic, RuleScope.Field, "limit", """{"<=":[{"var":"amount"},{"var":"limit"}]}""", RuleActionKind.Validate);

    [Theory(DisplayName = "rules-run-2: a decision-table outcome's trace names the deciding rule, what it read and the table's declared hit policy, and never a value")]
    [InlineData(HitPolicy.Priority, "priority")]
    [InlineData(HitPolicy.FirstMatch, "first-match")]
    public void Decision_table_trace_carries_the_declared_hit_policy(HitPolicy policy, string wire)
    {
        var table = Tiering(policy);
        var compiled = RuleCompiler.Compile([DecisionTableCompiler.Compile(table), Formula()]);
        var result = new FormRuleGraph(compiled, new FixedClock(At), TestAdmission.Any, RuleEngineLimits.Default)
            .EvaluateInstance(RuleInstance.FromJson(new JsonObject { ["amount"] = 1500, ["limit"] = 10 }));

        var trace = RuleTraceBuilder.BuildForm(compiled, result, PassThroughTraceFilter.Instance, [table]);

        var decided = trace.Single(entry => entry.RuleId == "tier.table");
        Assert.Equal(RuleTraceCodes.ValueComputed, decided.Code);
        Assert.Equal("tier.table", decided.Params["rule"]);
        Assert.Equal("amount", decided.Params["reads"]);
        Assert.Equal(wire, decided.Params["hitPolicy"]);
        // A rule no table supplied carries no hit policy; no entry carries a value or an output literal.
        Assert.False(trace.Single(entry => entry.RuleId == "limit.check").Params.ContainsKey("hitPolicy"));
        Assert.All(trace.SelectMany(entry => entry.Params.Values), value =>
        {
            foreach (var leaked in Leaks) Assert.DoesNotContain(leaked, value, StringComparison.Ordinal);
        });
    }

    [Fact(DisplayName = "rules-eng-19: the trace explains which rule decided and why: the failing rule, its reads and its cause code, with no value")]
    public void Trace_explains_the_deciding_rule_and_why()
    {
        var compiled = RuleCompiler.Compile([Formula()]);
        var result = new FormRuleGraph(compiled, new FixedClock(At), TestAdmission.Any, RuleEngineLimits.Default)
            .EvaluateInstance(RuleInstance.FromJson(new JsonObject { ["amount"] = 1500, ["limit"] = 10 }));

        var entry = Assert.Single(RuleTraceBuilder.BuildForm(compiled, result));
        Assert.Equal(("limit.check", RuleTraceCodes.ValidationFailed), (entry.RuleId, entry.Code));
        Assert.Equal("amount,limit", entry.Params["reads"]);
        Assert.Equal("limit.check", entry.Params["cause"]);
        Assert.DoesNotContain(entry.Params.Values, value => value.Contains("1500", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "rules-eng-19: standing evidence with values is its own authorized read: refused before existence is revealed, not-applicable when the rule does not cover the record, and only the declared inputs when available")]
    public async Task Standing_evidence_needs_its_own_authorized_read()
    {
        var rule = StandingTests.Rule("large", "large", """{">":[{"var":"amount"},500]}""", "amount");
        var record = new StandingRecord("a1", "asset", new Dictionary<string, JsonNode?> { ["amount"] = 900, ["owner"] = "secret" });
        var other = record with { RecordType = "invoice" };
        var evaluator = new StandingEvaluator();
        static ValueTask<bool> Deny(StandingRecord row, CancellationToken ct) => ValueTask.FromResult(false);
        static ValueTask<bool> Allow(StandingRecord row, CancellationToken ct) => ValueTask.FromResult(true);

        var refused = await evaluator.ReadEvidenceAsync(Grants.All, record, rule, Deny, TestAdmission.Any, At);
        Assert.Equal((StandingEvidenceAvailability.Refused, null), (refused.Availability, refused.Evidence));
        // Refusal hides whether evidence would exist.
        Assert.Equal(StandingEvidenceAvailability.Refused, (await evaluator.ReadEvidenceAsync(Grants.All, other, rule, Deny, TestAdmission.Any, At)).Availability);

        var absent = await evaluator.ReadEvidenceAsync(Grants.All, other, rule, Allow, TestAdmission.Any, At);
        Assert.Equal((StandingEvidenceAvailability.NotApplicable, null), (absent.Availability, absent.Evidence));

        var read = await evaluator.ReadEvidenceAsync(Grants.All, record, rule, Allow, TestAdmission.Any, At);
        Assert.Equal(StandingEvidenceAvailability.Available, read.Availability);
        var evidence = read.Evidence!;
        Assert.Equal(("large", "1.0.0", true, At), (evidence.RuleId, evidence.RuleVersion, evidence.Carries, evidence.Instant));
        Assert.Equal(["amount"], evidence.Inputs.Keys);
        Assert.Equal(900, evidence.Inputs["amount"]!.GetValue<int>());
    }
}
