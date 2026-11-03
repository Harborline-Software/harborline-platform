using System.Numerics;
using System.Reflection;
using System.Text.Json.Nodes;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;
using Harborline.Foundation.Taxonomy;
using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>Proposal002 oracles: literal shared tree/tuples, public producer and both public consumer routes.</summary>
public sealed class CodingSubsumptionTests
{
    private static readonly FixedClock Clock = new(DateTimeOffset.UnixEpoch);
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static JsonObject Fixture => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "corpus", "harborline-ops.json")))!["codingFixture"]!.AsObject();
    private static string Text(JsonObject value, string key) => value[key]!.GetValue<string>();
    private static CodingSubsumptionSnapshot Snapshot(JsonObject? fixture = null)
    {
        fixture ??= Fixture;
        return CodingSubsumptionSnapshot.FromJsonText(Text(fixture, "answersJsonText"), Text(fixture, "expectedBindingsJsonText"));
    }
    private static RuleDefinition Rule(string expression = """{"coding.is":[{"var":"coding"},"asset-class","root"]}""", RuleScope scope = RuleScope.Field, string target = "out")
        => RuleDefinitionFactory.Create("is-root", RuleTier.JsonLogic, scope, target, expression, RuleActionKind.Compute);
    private static RuleContextSnapshot Facts(string coding)
        => RuleContextSnapshot.FromJsonText("{\"root\":{\"coding\":" + coding + "}}");
    private static RuleInstance Instance(string json) => RuleInstance.FromJson(JsonNode.Parse(json)!.AsObject());
    private static ComputedValue Value(string coding, CodingSubsumptionSnapshot? capture = null, RuleEngineLimits? limits = null, string? expression = null)
        => new GuardEvaluator(Clock, limits, capture ?? Snapshot()).EvaluateValue(expression is null ? Rule() : Rule(expression), Facts(coding), RuleEvalScope.Root, TestAdmission.Any);
    private static string Capture(JsonObject fixture)
    {
        var definition = TaxonomyDefinitionJson.Deserialize(Text(fixture, "definitionJsonText"));
        var coordinates = new TaxonomyDefinitionCoordinates(Text(fixture, "tenant"), TaxonomyDefinitionId.Parse(Text(fixture, "definitionId")), Text(fixture, "version"));
        var queries = fixture["queries"]!.AsArray().Select(row => new TaxonomySubsumptionQuery(row![0]!.GetValue<string>(), row[1]!.GetValue<string>(), row[2]!.GetValue<string>())).ToArray();
        return new TaxonomyInterpreter([definition]).CaptureCodingAnswers(Text(fixture, "tenant"), [new(Text(fixture, "system"), coordinates, Text(fixture, "definitionSha256"))], queries, Window);
    }

    [Fact]
    public void Real_producer_matches_independent_shared_wire_and_public_consumers()
    {
        foreach (var fixture in new[] { Fixture, Fixture["alternative"]!.AsObject() })
        {
            var produced = Capture(fixture);
            Assert.Equal(Text(fixture, "answersJsonText"), produced);
            var coding = CodingSubsumptionSnapshot.FromJsonText(produced, Text(fixture, "expectedBindingsJsonText"));
            bool expected = Text(fixture, "version") == "1.0.0";
            Assert.Equal(expected, Value("""{"system":"asset-class","code":"leaf"}""", coding).Value!.GetValue<bool>());
            var graph = new FormRuleGraph(RuleCompiler.Compile([Rule()]), Clock, TestAdmission.Any, null, coding);
            Assert.Equal(expected, graph.EvaluateInstance(Instance("""{"coding":{"system":"asset-class","code":"leaf"}}""")).Values["field:out"].Value!.GetValue<bool>());
        }
    }

    [Fact]
    public void Original_native_constructor_signatures_remain_and_null_opt_in_refuses()
    {
        Assert.NotNull(typeof(GuardEvaluator).GetConstructor([typeof(TimeProvider), typeof(RuleEngineLimits)]));
        Assert.NotNull(typeof(FormRuleGraph).GetConstructor([typeof(CompiledGraph), typeof(TimeProvider), typeof(EvaluationAdmission), typeof(RuleEngineLimits)]));
        Assert.Empty(typeof(CodingSubsumptionSnapshot).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Throws<ArgumentNullException>(() => new GuardEvaluator(Clock, null, null!));
        Assert.Throws<ArgumentNullException>(() => new FormRuleGraph(RuleCompiler.Compile([Rule()]), Clock, TestAdmission.Any, null, null!));
        Assert.DoesNotContain(typeof(GuardEvaluator).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "Harborline.Foundation.Taxonomy");
    }

    [Fact]
    public void Legacy_flat_equality_and_coercions_do_not_opt_in()
    {
        var legacy = new GuardEvaluator(Clock);
        Assert.False(legacy.EvaluateValue(Rule(), Facts("""{"system":"asset-class","code":"leaf"}"""), RuleEvalScope.Root, TestAdmission.Any).Value!.GetValue<bool>());
        Assert.True(legacy.EvaluateValue(Rule(), Facts("""{"system":"asset-class","code":"root"}"""), RuleEvalScope.Root, TestAdmission.Any).Value!.GetValue<bool>());
        var numeric = Rule("""{"coding.is":[{"var":"coding"},"asset-class","123"]}""");
        Assert.True(legacy.EvaluateValue(numeric, Facts("""[{"system":"asset-class","code":123},{"system":"asset-class","code":"not-captured"}]"""), RuleEvalScope.Root, TestAdmission.Any).Value!.GetValue<bool>());
        Assert.False(Value("""{"system":"asset-class","code":123}""").Value!.GetValue<bool>());
    }

    [Theory]
    [InlineData("leaf", "root", true)]
    [InlineData("root", "leaf", false)]
    [InlineData("leaf", "child", true)]
    [InlineData("leaf", "leaf", true)]
    [InlineData("missing", "root", false)]
    public void Descendant_argument_direction_and_complete_definition_unknown_code_are_literal(string descendant, string ancestor, bool expected)
    {
        string expression = "{\"coding.is\":[{\"var\":\"coding\"},\"asset-class\",\"" + ancestor + "\"]}";
        Assert.Equal(expected, Value("{\"system\":\"asset-class\",\"code\":\"" + descendant + "\"}", expression: expression).Value!.GetValue<bool>());
    }

    [Fact]
    public void Every_applicable_array_member_must_have_an_answer_even_after_true()
    {
        var missing = Value("""[{"system":"asset-class","code":"leaf"},{"system":"asset-class","code":"uncaptured"}]""");
        Assert.Equal("rule.coding_answer_missing", missing.Error!.Code);
        Assert.True(Value("""[null,23,{"system":"other","code":"uncaptured"},{"system":"asset-class","code":[]},{"system":"asset-class","code":"leaf"}]""").Value!.GetValue<bool>());
        Assert.False(Value("[]").Value!.GetValue<bool>());
        Assert.Equal("rule.coding_binding_missing", Value("[]", expression: """{"coding.is":[{"var":"coding"},"other","root"]}""").Error!.Code);
    }

    [Fact]
    public void Capture_pins_are_exact_independent_and_owned()
    {
        var f = Fixture;
        var snapshot = Snapshot(f);
        f["answersJsonText"] = "[]";
        Assert.True(Value("""{"system":"asset-class","code":"leaf"}""", snapshot).Value!.GetValue<bool>());
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(Text(Fixture, "answersJsonText"), Text(Fixture["alternative"]!.AsObject(), "expectedBindingsJsonText")));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(Text(Fixture, "answersJsonText"), Text(Fixture, "expectedBindingsJsonText").Replace("tenant-a", "tenant-b", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("tag")]
    [InlineData("digest")]
    [InlineData("version")]
    [InlineData("id")]
    [InlineData("binding-duplicate")]
    [InlineData("answer-duplicate")]
    [InlineData("answer-order")]
    [InlineData("answer-type")]
    [InlineData("answer-alias")]
    [InlineData("empty-answers")]
    [InlineData("extra-tuple")]
    public void Malformed_tuple_documents_refuse_before_construction(string corruption)
    {
        var fixture = Fixture;
        var answers = JsonNode.Parse(Text(fixture, "answersJsonText"))!.AsArray();
        switch (corruption)
        {
            case "tag": answers[0] = "harborline-taxonomy-coding-answers/2"; break;
            case "digest": answers[2]![0]![3] = new string('A', 64); break;
            case "version": answers[2]![0]![2] = "4294967296.0.0"; break;
            case "id": answers[2]![0]![1] = "vendor..asset"; break;
            case "binding-duplicate": answers[2]!.AsArray().Add(answers[2]![0]!.DeepClone()); break;
            case "answer-duplicate": answers[3]!.AsArray().Insert(0, answers[3]![0]!.DeepClone()); break;
            case "answer-order": var last = answers[3]!.AsArray()[6]!.DeepClone(); answers[3]!.AsArray().RemoveAt(6); answers[3]!.AsArray().Insert(0, last); break;
            case "answer-type": answers[3]![0]![3] = "true"; break;
            case "answer-alias": answers[3]![0]![0] = "other"; break;
            case "empty-answers": answers[3] = new JsonArray(); break;
            case "extra-tuple": answers.Add(true); break;
        }
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers.ToJsonString(), Text(fixture, "expectedBindingsJsonText")));
    }

    [Fact]
    public void Each_input_document_obeys_byte_depth_and_value_limits()
    {
        string answers = Text(Fixture, "answersJsonText"), expected = Text(Fixture, "expectedBindingsJsonText");
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(new string(' ', 262145) + answers, expected));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers, new string(' ', 262145) + expected));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(new string('[', 65) + "0" + new string(']', 65), expected));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers, "[" + string.Join(",", Enumerable.Repeat("0", 5000)) + "]"));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText("[\"" + new string('é', 131073) + "\"]", expected));
    }

    [Fact]
    public void Full_incremental_row_and_guard_paths_share_one_capture()
    {
        var coding = Snapshot();
        var compiled = RuleCompiler.Compile([Rule()]);
        var graph = new FormRuleGraph(compiled, Clock, TestAdmission.Any, null, coding);
        Assert.True(graph.EvaluateInstance(Instance("""{"coding":{"system":"asset-class","code":"leaf"}}""")).Values["field:out"].Value!.GetValue<bool>());
        Assert.False(graph.Reevaluate("coding", RuleInputValue.FromJsonText("""{"system":"asset-class","code":"missing"}""")).Values["field:out"].Value!.GetValue<bool>());
        Assert.Equal("rule.coding_answer_missing", graph.Reevaluate("coding", RuleInputValue.FromJsonText("""{"system":"asset-class","code":"uncaptured"}""")).Values["field:out"].Error!.Code);
        var rowRule = Rule("""{"coding.is":[{"var":"row.coding"},"asset-class","root"]}""", RuleScope.Row, "items/out");
        var rows = new FormRuleGraph(RuleCompiler.Compile([rowRule]), Clock, TestAdmission.Any, null, coding);
        var result = rows.EvaluateInstance(Instance("""{"items":[{"_id":"a","coding":{"system":"asset-class","code":"leaf"}}]}"""));
        Assert.Contains(result.Values, pair => pair.Key != "field:out" && pair.Value.Value?.GetValue<bool>() == true);
        var guardRule = RuleDefinitionFactory.Create("is-root", RuleTier.JsonLogic, RuleScope.Field, "out", Rule().Expression, RuleActionKind.Validate);
        Assert.True(new GuardEvaluator(Clock, null, coding).EvaluateGuard(guardRule, Facts("""{"system":"asset-class","code":"leaf"}"""), RuleEvalScope.Root, TestAdmission.Any).Ok);
    }

    [Fact]
    public void Strong_work_transfer_has_literal_bound_and_equality_admission()
    {
        // Proposal002 arithmetic: C=4+8+8, child work=5+9+9;
        // Compute executes twice: 2*(1+23+5000*5000*(20+262144+1)+5).
        var literal = Rule("""{"coding.is":[null,"s","a"]}""");
        // Actual literal bounds: null=4, strings=8 each, child work=5+9+9.
        var proof = CoreWorkDerivation.DeriveGraph(RuleCompiler.Compile([literal]).Rules, RuleEngineLimits.Default, coding: true);
        Assert.Equal(BigInteger.Parse("13108250000058"), proof.MaximumEvaluationWork);
        var equal = RuleEngineLimits.Default with { MaxStaticWork = BigInteger.Parse("13108250000058") };
        var compiled = RuleCompiler.Compile([literal]);
        Assert.Equal("rule.coding_binding_missing", new FormRuleGraph(compiled, Clock, TestAdmission.Any, equal, Snapshot()).EvaluateInstance(Instance("{}")).Values["field:out"].Error!.Code);
        var below = equal with { MaxStaticWork = equal.MaxStaticWork - 1 };
        Assert.Equal("rule.compile.work_exceeded", new FormRuleGraph(compiled, Clock, TestAdmission.Any, below, Snapshot()).EvaluateInstance(Instance("{}")).Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Borrower_refusal_precedes_opt_in_proof_and_value_access()
    {
        var rule = Rule(); var compiled = RuleCompiler.Compile([rule]);
        var limits = RuleEngineLimits.Default with { MaxStaticWork = compiled.WorkProof.MaximumEvaluationWork };
        var snapshot = Facts("null");
        var guard = new GuardEvaluator(Clock, limits, Snapshot());
        Assert.Equal("rule.environment.not_admitted", guard.EvaluateValue(rule, snapshot, RuleEvalScope.Root, null).Error!.Code);
        Assert.Equal("rule.compile.work_exceeded", guard.EvaluateValue(rule, snapshot, RuleEvalScope.Root, TestAdmission.Any).Error!.Code);
        var graph = new FormRuleGraph(compiled, Clock, null, limits, Snapshot());
        Assert.Equal("rule.environment.not_admitted", graph.EvaluateInstance(null!).Validations.Single().Validity!.Error!.Code);
        var restricted = BorrowerEnvironmentAdmission.Admit(TestAdmission.Declaration(operations: ["var"])).For(EvaluationPhase.Run);
        Assert.Equal("rule.environment.operation_not_admitted", guard.EvaluateValue(rule, snapshot, RuleEvalScope.Root, restricted).Error!.Code);
    }

    [Fact]
    public void Ordinal_tuple_order_and_unsigned_exact_version_segments_are_enforced()
    {
        var fixture = Fixture;
        string answers = Text(fixture, "answersJsonText"), expected = Text(fixture, "expectedBindingsJsonText");
        Assert.NotNull(CodingSubsumptionSnapshot.FromJsonText(answers.Replace("1.0.0", "01.0.0", StringComparison.Ordinal), expected.Replace("1.0.0", "01.0.0", StringComparison.Ordinal)));
        foreach (var version in new[] { "latest", "1.0", "-1.0.0", "+1.0.0", "1.0.0 ", "4294967296.0.0" })
            Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers.Replace("1.0.0", version, StringComparison.Ordinal), expected.Replace("1.0.0", version, StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers + "[]", expected));
    }

    [Fact]
    public void Reader_accepts_exact_byte_and_value_envelopes_and_refuses_one_over()
    {
        string answers = Text(Fixture, "answersJsonText"), expected = Text(Fixture, "expectedBindingsJsonText");
        int answerBytes = System.Text.Encoding.UTF8.GetByteCount(answers);
        int expectedBytes = System.Text.Encoding.UTF8.GetByteCount(expected);
        Assert.NotNull(CodingSubsumptionSnapshot.FromJsonText(answers + new string(' ', 262144 - answerBytes), expected));
        Assert.NotNull(CodingSubsumptionSnapshot.FromJsonText(answers, expected + new string(' ', 262144 - expectedBytes)));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers + new string(' ', 262145 - answerBytes), expected));
        var tuples = JsonNode.Parse(answers)!.AsArray();
        var rows = new JsonArray();
        // 5 envelope values + 5 binding values + 998*5 answer values = 5000.
        for (int i = 0; i < 998; i++) rows.Add(new JsonArray("asset-class", "root", i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), false));
        tuples[3] = rows;
        Assert.NotNull(CodingSubsumptionSnapshot.FromJsonText(tuples.ToJsonString(), expected));
        rows.Add(new JsonArray("asset-class", "root", "0998", false));
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(tuples.ToJsonString(), expected));
    }

    [Theory]
    [InlineData("taxonomy.subsumes")]
    [InlineData("taxonomy.succeeds")]
    [InlineData("coding.descendant")]
    public void Captures_add_no_authored_traversal_operators(string operation)
        => Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile([Rule("{\"" + operation + "\":[]}")]));

    [Fact]
    public void Runtime_lookup_accepts_exact_work_and_refuses_one_step_below()
    {
        var coding = Snapshot();
        // One binding candidate + (11+11+1) compared UTF-16 units.
        var binding = new EvalBudget(RuleEngineLimits.Default with { StepBudget = 24 }, CancellationToken.None);
        coding.RequireBinding("asset-class", binding);
        Assert.Equal(24, binding.Steps);
        Assert.Throws<RuleBudgetException>(() => coding.RequireBinding("asset-class", new EvalBudget(RuleEngineLimits.Default with { StepBudget = 23 }, CancellationToken.None)));
        // Four ordered answer candidates to root/child: 34+33+33+44 units.
        var answer = new EvalBudget(RuleEngineLimits.Default with { StepBudget = 144 }, CancellationToken.None);
        Assert.True(coding.Lookup("asset-class", "root", "child", answer));
        Assert.Equal(144, answer.Steps);
        Assert.Throws<RuleBudgetException>(() => coding.Lookup("asset-class", "root", "child", new EvalBudget(RuleEngineLimits.Default with { StepBudget = 143 }, CancellationToken.None)));
    }

    [Theory]
    [InlineData("null", 12, false)]
    [InlineData("null", 11, null)]
    [InlineData("null", 9, null)]
    [InlineData("{\"system\":\"s\",\"code\":\"d\"}", 27, true)]
    [InlineData("{\"system\":\"s\",\"code\":\"d\"}", 26, null)]
    [InlineData("{\"system\":\"t\",\"code\":\"d\"}", 17, false)]
    [InlineData("{\"system\":\"t\",\"code\":\"d\"}", 16, null)]
    public void Public_value_and_guard_pin_literal_opt_in_budget_parity(string candidate, int stepBudget, bool? expected)
    {
        // Literal transfer oracle: AST4 + entry3 + binding4 + candidate1 = 12.
        // A valid coding adds descendant2 + system3; a matching answer adds10: 17 or 27.
        const string expectedBindings = """["harborline-taxonomy-coding-bindings/1","tenant-a",[["s","vendor.domain.asset","1.0.0","0000000000000000000000000000000000000000000000000000000000000000"]]]""";
        const string answers = """["harborline-taxonomy-coding-answers/1","tenant-a",[["s","vendor.domain.asset","1.0.0","0000000000000000000000000000000000000000000000000000000000000000"]],[["s","a","d",true]]]""";
        var coding = CodingSubsumptionSnapshot.FromJsonText(answers, expectedBindings);
        var limits = RuleEngineLimits.Default with { StepBudget = stepBudget };
        var evaluator = new GuardEvaluator(Clock, limits, coding);
        string expression = "{\"coding.is\":[" + candidate + ",\"s\",\"a\"]}";
        var context = RuleContextSnapshot.FromJsonText("""{"root":{}}""");
        var value = evaluator.EvaluateValue(Rule(expression), context, RuleEvalScope.Root, TestAdmission.Any);
        var guardRule = RuleDefinitionFactory.Create("coding-budget-guard", RuleTier.JsonLogic, RuleScope.Field, "out", expression, RuleActionKind.Validate);
        var guard = evaluator.EvaluateGuard(guardRule, context, RuleEvalScope.Root, TestAdmission.Any);
        if (expected is { } boolean)
        {
            Assert.Equal(ValueState.Resolved, value.State);
            Assert.Equal(boolean, value.Value!.GetValue<bool>());
            Assert.Equal(boolean, guard.Ok);
            if (boolean) Assert.Null(guard.Error);
            else Assert.Equal("coding-budget-guard", guard.Error!.Code);
        }
        else
        {
            Assert.Equal("rule.budget_exceeded", value.Error!.Code);
            Assert.False(guard.Ok);
            Assert.Equal("rule.budget_exceeded", guard.Error!.Code);
        }
    }

    [Fact]
    public void Runtime_scans_charge_candidates_strings_and_never_wrap_Int32()
    {
        var limits = RuleEngineLimits.Default with { StepBudget = 10 };
        Assert.Equal("rule.budget_exceeded", Value("""{"system":"asset-class","code":"leaf"}""", limits: limits).Error!.Code);
        var budget = new EvalBudget(RuleEngineLimits.Default with { StepBudget = int.MaxValue }, CancellationToken.None);
        budget.ChargeSize(int.MaxValue);
        Assert.Equal(int.MaxValue, budget.Steps);
        Assert.Throws<RuleBudgetException>(() => budget.Charge());
        Assert.Throws<RuleBudgetException>(() => budget.ChargeSize(int.MaxValue));
        Assert.Throws<RuleBudgetException>(() => new EvalBudget(RuleEngineLimits.Default with { StepBudget = int.MaxValue }, CancellationToken.None).ChargeSize(long.MaxValue));
        var exact = new EvalBudget(RuleEngineLimits.Default with { StepBudget = 4 }, CancellationToken.None);
        exact.ChargeSize(3); exact.Charge();
        Assert.Equal(4, exact.Steps);
        Assert.Throws<RuleBudgetException>(() => exact.Charge());
    }

    [Fact]
    public void Reactive_owned_coding_arrays_preserve_true_then_refuse_an_uncaptured_member()
    {
        var graph = new FormRuleGraph(RuleCompiler.Compile([Rule()]), Clock, TestAdmission.Any, null, Snapshot());
        Assert.False(graph.EvaluateInstance(RuleInstance.FromJsonText("{}")).Values["field:out"].Value!.GetValue<bool>());

        // Reactive value capture owns a field list; cold all-object root arrays remain child tables.
        var matched = graph.Reevaluate("coding", RuleInputValue.FromJsonText("""[{"system":"other","code":"leaf"},{"system":"asset-class","code":"leaf"}]"""));
        Assert.Equal(ValueState.Resolved, matched.Values["field:out"].State);
        Assert.True(matched.Values["field:out"].Value!.GetValue<bool>());

        var missing = graph.Reevaluate("coding", RuleInputValue.FromJsonText("""[{"system":"asset-class","code":"leaf"},{"system":"asset-class","code":"uncaptured"}]"""));
        Assert.Equal(ValueState.Error, missing.Values["field:out"].State);
        Assert.Equal("rule.coding_answer_missing", missing.Values["field:out"].Error!.Code);
    }

    [Fact]
    public void Two_true_coding_answers_remain_true_across_public_consumers()
    {
        const string candidates = """[null,{"system":"asset-class","code":"child"},{"system":"asset-class","code":"leaf"}]""";
        var value = Value(candidates);
        Assert.Equal(ValueState.Resolved, value.State);
        Assert.True(value.Value!.GetValue<bool>());

        var graph = new FormRuleGraph(RuleCompiler.Compile([Rule()]), Clock, TestAdmission.Any, null, Snapshot());
        // The literal null keeps a cold array in the public scalar-field path rather than child tables.
        var cold = graph.EvaluateInstance(Instance("{\"coding\":" + candidates + "}"));
        Assert.Equal(ValueState.Resolved, cold.Values["field:out"].State);
        Assert.True(cold.Values["field:out"].Value!.GetValue<bool>());
        var updated = graph.Reevaluate("coding", RuleInputValue.FromJsonText(candidates));
        Assert.Equal(ValueState.Resolved, updated.Values["field:out"].State);
        Assert.True(updated.Values["field:out"].Value!.GetValue<bool>());
    }

    [Fact]
    public void Opt_in_guard_static_work_admits_equality_and_refuses_one_below()
    {
        const string expression = """{"coding.is":[null,"s","a"]}""";
        var rule = RuleDefinitionFactory.Create("coding-static-guard", RuleTier.JsonLogic, RuleScope.Field, "out", expression, RuleActionKind.Validate);
        var context = RuleContextSnapshot.FromJsonText("""{"root":{}}""");
        // Proposal002: C=4+8+8, child work=5+9+9; Validate executes once.
        // 1+23+5000*5000*(20+262144+1)+5 = 6554125000029.
        var exact = RuleEngineLimits.Default with { MaxStaticWork = BigInteger.Parse("6554125000029") };
        var admitted = new GuardEvaluator(Clock, exact, Snapshot()).EvaluateGuard(rule, context, RuleEvalScope.Root, TestAdmission.Any);
        Assert.False(admitted.Ok);
        Assert.Equal("rule.coding_binding_missing", admitted.Error!.Code);
        var below = RuleEngineLimits.Default with { MaxStaticWork = BigInteger.Parse("6554125000028") };
        var refused = new GuardEvaluator(Clock, below, Snapshot()).EvaluateGuard(rule, context, RuleEvalScope.Root, TestAdmission.Any);
        Assert.False(refused.Ok);
        Assert.Equal("rule.compile.work_exceeded", refused.Error!.Code);
    }

    [Fact]
    public void Legacy_graph_static_work_keeps_flat_transfer_at_equality()
    {
        var compiled = RuleCompiler.Compile([Rule("""{"coding.is":[null,"s","a"]}""")]);
        // The original overload keeps the legacy transfer; Compute executes twice.
        // 2*(1+23+5000*(20+1)+5) = 210058.
        var exact = RuleEngineLimits.Default with { MaxStaticWork = BigInteger.Parse("210058") };
        var admitted = new FormRuleGraph(compiled, Clock, TestAdmission.Any, exact).EvaluateInstance(Instance("{}"));
        Assert.Equal(ValueState.Resolved, admitted.Values["field:out"].State);
        Assert.False(admitted.Values["field:out"].Value!.GetValue<bool>());
        var below = RuleEngineLimits.Default with { MaxStaticWork = BigInteger.Parse("210057") };
        var refused = new FormRuleGraph(compiled, Clock, TestAdmission.Any, below).EvaluateInstance(Instance("{}"));
        Assert.Equal("rule.compile.work_exceeded", refused.Validations.Single().Validity!.Error!.Code);
    }

    [Theory]
    [InlineData(1, "vendor.domain.asset", "vendor.domain.other")]
    [InlineData(2, "1.0.0", "2.0.0")]
    [InlineData(3, "b21015c1098b83f1deb9addf42e10b84438c54a61d4f558d36bd3ceaa712fc87", "0000000000000000000000000000000000000000000000000000000000000000")]
    public void Expected_binding_definition_version_and_digest_are_independently_exact(int tupleIndex, string original, string mismatch)
    {
        var fixture = Fixture;
        string answers = Text(fixture, "answersJsonText");
        string expected = Text(fixture, "expectedBindingsJsonText");
        Assert.NotNull(CodingSubsumptionSnapshot.FromJsonText(answers, expected));

        var changedExpected = JsonNode.Parse(expected)!.AsArray();
        Assert.Equal(original, changedExpected[2]![0]![tupleIndex]!.GetValue<string>());
        Assert.NotEqual(original, mismatch);
        // Only the expected binding pin changes; the complete answer wire stays original.
        changedExpected[2]![0]![tupleIndex] = mismatch;
        Assert.Throws<ArgumentException>(() => CodingSubsumptionSnapshot.FromJsonText(answers, changedExpected.ToJsonString()));
    }
}
