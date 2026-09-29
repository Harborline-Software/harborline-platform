using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// T-818: the static work proof is an admission ceiling, refused fail-closed at compile and again at
/// graph construction under the graph's own limits. The TypeScript twin is static-work-ceiling.test.ts.
/// </summary>
public sealed class StaticWorkCeilingTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));

    private static RuleDefinition Compute(string id, string target, string expr, RuleScope scope = RuleScope.Field)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, scope, target, expr, RuleActionKind.Compute);

    private static readonly RuleDefinition[] Literal = [Compute("literal", "x", "1")];

    private static RuleDefinition[] Rows(int count) => [.. Enumerable.Range(0, count)
        .Select(i => Compute($"row{i}", $"items/c{i}", "{\"missing\":[{\"var\":\"rowKeys\"}]}", RuleScope.Row))];

    private static RuleDefinition[] Fields(int count) => [.. Enumerable.Range(0, count)
        .Select(i => Compute($"f{i}", $"f{i}", "{\"missing\":[{\"var\":\"keys\"}]}"))];

    private static RuleEngineLimits At(BigInteger ceiling) => RuleEngineLimits.Default with { MaxStaticWork = ceiling };

    private static string Work(CompiledGraph compiled) => compiled.WorkProof.MaximumEvaluationWork.ToString(CultureInfo.InvariantCulture);

    private static RuleInstance Instance(string json) => RuleInstance.FromJson(JsonNode.Parse(json)!.AsObject());

    private static RuleEvaluationResult[] EveryEntryPoint(FormRuleGraph graph) =>
    [
        graph.EvaluateInstance(Instance("{\"y\":1}")),
        graph.Reevaluate("y", JsonValue.Create(2)),
        graph.Reevaluate("y", RuleInputValue.FromJsonText("2")),
        graph.AddRow("items", new RuleRow("r1", new Dictionary<string, JsonNode?>())),
        graph.RemoveRow("items", "r1"),
    ];

    private static void AssertFailClosed(RuleEvaluationResult result, string code)
    {
        Assert.True(result.IsSaveBlocked);
        Assert.Equal(["rule.engine"], result.ByRule.Keys);
        var refusal = Assert.Single(result.Validations);
        Assert.False(refusal.Validity!.Ok);
        Assert.Equal(code, refusal.Validity.Error!.Code);
        Assert.Empty(refusal.Validity.Error.Params);
    }

    [Fact]
    public void Compiler_admits_a_program_exactly_at_the_ceiling_and_refuses_it_one_unit_below()
    {
        var proof = RuleCompiler.Compile(Literal).WorkProof.MaximumEvaluationWork;
        Assert.Equal(new BigInteger(130), proof);
        Assert.Equal(proof, RuleCompiler.Compile(Literal, At(proof)).WorkProof.MaximumEvaluationWork);

        var refusal = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(Literal, At(proof - 1)));

        Assert.Equal(RuleEngineCodes.CompileWorkExceeded, refusal.Code);
        Assert.Equal("rule.compile.work_exceeded", refusal.Code);
        Assert.Equal(new Dictionary<string, string> { ["proof"] = "130", ["ceiling"] = "129" }, refusal.Params);
        Assert.Null(refusal.RuleId);
        Assert.Equal("graph work proof 130 exceeds the static work ceiling 129 (more than 1x); "
            + "lower maxTableRowsPerAggregate or maxGraphNodes, or author fewer dynamic Row or missing reads and fewer aggregate references",
            refusal.Message);
    }

    [Fact]
    public void Compiler_reports_the_factor_over_the_ceiling_and_no_factor_for_a_zero_ceiling()
    {
        Assert.Contains("ceiling 1 (more than 130x);", Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(Literal, At(1))).Message);
        var zero = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(Literal, At(0)));
        Assert.Equal(new Dictionary<string, string> { ["proof"] = "130", ["ceiling"] = "0" }, zero.Params);
        Assert.Contains("ceiling 0; lower", zero.Message);
    }

    [Fact]
    public void Compiler_leaves_the_other_compile_refusals_without_params()
        => Assert.Empty(Assert.Throws<RuleCompilationException>(
            () => RuleCompiler.Compile([Compute("bad", "x", "{\"unknown_operator\":[]}")])).Params);

    [Fact]
    public void Default_ceiling_is_ten_to_the_twenty_sixth_and_leaves_the_step_budget_unchanged()
    {
        Assert.Equal(BigInteger.Pow(10, 26), RuleEngineLimits.Default.MaxStaticWork);
        Assert.Equal(250_000, RuleEngineLimits.Default.StepBudget);
    }

    [Fact]
    public void Default_ceiling_admits_the_calibration_diagnostics_and_refuses_the_smallest_over_default_row_program()
    {
        Assert.Equal("1631738386921228193152", Work(RuleCompiler.Compile(Fields(64))));
        Assert.Equal("3983736295385685821324000", Work(RuleCompiler.Compile(Rows(1))));
        Assert.Equal("99593407384642145533100000", Work(RuleCompiler.Compile(Rows(25))));

        var refusal = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(Rows(26)));

        Assert.Equal(RuleEngineCodes.CompileWorkExceeded, refusal.Code);
        Assert.Equal(new Dictionary<string, string>
        {
            ["proof"] = "103577143680027831354424000",
            ["ceiling"] = "100000000000000000000000000",
        }, refusal.Params);
    }

    [Fact]
    public void Graph_fails_closed_at_every_entry_point_when_its_limits_put_the_proof_over_the_ceiling()
    {
        var graph = new FormRuleGraph(RuleCompiler.Compile(Literal), Clock, TestAdmission.Any, At(129));

        Assert.Equal(new BigInteger(130), graph.WorkProof.MaximumEvaluationWork);
        foreach (var result in EveryEntryPoint(graph)) AssertFailClosed(result, RuleEngineCodes.CompileWorkExceeded);
    }

    [Fact]
    public void Graph_evaluates_normally_at_exactly_its_own_proof()
    {
        var graph = new FormRuleGraph(RuleCompiler.Compile(Literal), Clock, TestAdmission.Any, At(130));
        var results = EveryEntryPoint(graph);

        // The untyped JsonNode reevaluation always refuses with its own snapshot code.
        AssertFailClosed(results[1], RuleEngineCodes.ContextSnapshotRequired);
        foreach (var result in results.Where((_, index) => index != 1))
        {
            Assert.False(result.IsSaveBlocked);
            Assert.Equal(1, result.Values["field:x"].Value!.GetValue<int>());
        }
    }

    [Fact]
    public void Graph_keeps_the_admission_refusal_first_when_both_apply()
    {
        var graph = new FormRuleGraph(RuleCompiler.Compile(Literal), Clock, admission: null, At(0));

        foreach (var result in EveryEntryPoint(graph)) AssertFailClosed(result, BorrowerEnvironmentAdmission.NotAdmitted);
    }

    [Fact]
    public void Graph_recomputes_the_proof_under_its_own_structural_limits_instead_of_trusting_the_compile_time_proof()
    {
        var row = RuleCompiler.Compile(Rows(1));
        Assert.True(row.WorkProof.MaximumEvaluationWork < RuleEngineLimits.Default.MaxStaticWork);

        var graph = new FormRuleGraph(row, Clock, TestAdmission.Any, RuleEngineLimits.Default with { MaxTableRowsPerAggregate = 200_000 });

        Assert.Equal("984112130658328582132400000", graph.WorkProof.MaximumEvaluationWork.ToString(CultureInfo.InvariantCulture));
        AssertFailClosed(graph.EvaluateInstance(Instance("{}")), RuleEngineCodes.CompileWorkExceeded);
    }
}
