using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Evaluation;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-590 slice 2: borrower environment admission at every evaluation entry point.</summary>
public sealed class BorrowerEnvironmentTests
{
    private static readonly DateTimeOffset Clock = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);

    private static RuleDefinition Rule(string id, string target, string expr, RuleActionKind action = RuleActionKind.Compute, RuleScope scope = RuleScope.Field)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, scope, target, expr, action);

    private static EvaluationAdmission Narrow(IReadOnlyList<string>? operations = null, IReadOnlyList<string>? variables = null)
        => BorrowerEnvironmentAdmission.Admit(TestAdmission.Declaration(operations, variables)).For(EvaluationPhase.Run);

    private static string? Code(RuleEvaluationResult result) => result.Validations.SingleOrDefault(o => o.RuleId == "rule.engine")?.Validity?.Error?.Code;

    public static IEnumerable<object[]> LiteralMissingCases() => KeyFenceCases("literal");

    private static IEnumerable<object[]> KeyFenceCases(string group)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(FunctionRegisterTests.RepositoryRoot(),
            "conformance/hlp.foundation.rule-runtime/borrower-key-fence.json")))!["cases"]!.AsArray()
            .Where(item => item!["group"]!.GetValue<string>() == group)
            .Select(item => new object[] { item!["id"]!.GetValue<string>(), item.ToJsonString() });

    [Theory(DisplayName = "T-1232 acceptance 1 / rules-ck-28 / rules-eng-26: literal missing keys respect the declaration")]
    [MemberData(nameof(LiteralMissingCases))]
    public void T1232_A1_Literal_missing_keys(string id, string json) => AssertKeyFenceCase(id, json);

    public static IEnumerable<object[]> DynamicMissingCases() => KeyFenceCases("dynamic");

    [Theory(DisplayName = "T-1232 acceptance 2 / rules-eng-26: expression keys admit and their resolved targets respect the declaration")]
    [MemberData(nameof(DynamicMissingCases))]
    public void T1232_A2_Dynamic_missing_keys(string id, string json)
    {
        AssertKeyFenceCase(id, json);
    }

    public static IEnumerable<object[]> GraphFenceCases()
    {
        foreach (var operation in new[] { "missing", "missing_some" })
        foreach (var action in new[] { RuleActionKind.Compute, RuleActionKind.Validate, RuleActionKind.Required })
        foreach (bool computed in new[] { false, true })
        foreach (bool declared in new[] { false, true })
        foreach (bool dataDerived in new[] { false, true })
        foreach (bool incremental in new[] { false, true })
            yield return [operation, action, computed, declared, dataDerived, incremental];
    }

    [Theory]
    [MemberData(nameof(GraphFenceCases))]
    public void T1232_A2_Graph_fence_precedes_target_access(string operation, RuleActionKind action, bool computed, bool declared, bool dataDerived, bool incremental)
    {
        // Oracle: T-1232 acceptance 2: zero forbidden accesses, one declared target access.
        string keys = dataDerived ? """{"var":"field.keys"}""" : operation == "missing"
            ? """{"cat":["row.","x"]}""" : """{"if":[true,["row.x"],[]]}""";
        string missing = operation == "missing" ? "{\"missing\":[" + keys + "]}"
            : "{\"missing_some\":[1," + keys + "]}";
        string expression = "{\"if\":[{\"var\":\"field.trigger\"}," + missing + ",[]]}";
        var definitions = new List<RuleDefinition> { Rule("probe", "items/out", expression, action, RuleScope.Row) };
        if (computed) definitions.Add(Rule("producer", "items/x", "7", scope: RuleScope.Row));
        var graph = new FormRuleGraph(RuleCompiler.Compile(definitions), new FixedClock(Clock),
            Narrow(variables: declared ? ["field", "row"] : ["field"]));
        var instance = RuleInstance.FromJson(JsonNode.Parse("""{"trigger":true,"keys":["row.x"],"items":[{"_id":"r1","x":7}]}""")!.AsObject());
        if (incremental) graph.EvaluateInstance(instance);
        int demands = 0, rawReads = 0, keyReads = 0;
        string owner = action == RuleActionKind.Compute ? "row:items/r1/out" : "probe#r1";
        graph.DemandObserved = (source, key) => { if (source == owner && key == "row:items/r1/x") demands++; };
        graph.RawReadObserved = (source, key) =>
        {
            if (source != owner) return;
            if (key == "row:items/r1/x") rawReads++;
            if (key == "field:keys") keyReads++;
        };
        Check(incremental ? graph.Reevaluate("trigger", RuleInputValue.FromJsonText("true")) : graph.EvaluateInstance(instance), incremental ? "incremental" : "full");

        void Check(RuleEvaluationResult result, string phase)
        {
            var outcome = result.ByRule["probe#r1"];
            var error = action == RuleActionKind.Compute ? outcome.Value?.Error?.Code : outcome.Validity?.Error?.Code;
            if (!declared) Assert.Equal("rule.environment.variable_not_admitted", error);
            else if (action == RuleActionKind.Compute) Assert.True(JsonNode.DeepEquals(JsonNode.Parse("[]"), outcome.Value!.Value), phase);
            else if (action == RuleActionKind.Validate) Assert.Equal("probe", error); // [] is false.
            else Assert.False(result.Visibility["row:items/r1/out"].Required);
            Assert.Equal(declared && computed ? 1 : 0, demands);
            Assert.Equal(declared && !computed ? 1 : 0, rawReads);
            Assert.Equal(dataDerived ? 1 : 0, keyReads);
        }
    }

    [Theory]
    [MemberData(nameof(DynamicMissingCases))]
    public void T1232_A2_Resolved_fence_counts_underlying_reads(string id, string json)
    {
        var test = JsonNode.Parse(json)!;
        var inner = new CountingResolver(test["bag"]!.AsObject());
        var resolver = Narrow(variables: ["field"]).Environment.Fence(inner);
        var context = new EvalContext(resolver, Clock, new EvalBudget(RuleEngineLimits.Default, default));
        if (test["refusal"] is not null)
        {
            Assert.Equal("rule.environment.variable_not_admitted", Assert.Throws<RuleEvalException>(() => HarborlineJsonLogic.Evaluate(test["expression"], context)).Error.Code);
            Assert.Equal(0, inner.Reads);
        }
        else
        {
            Assert.True(JsonNode.DeepEquals(test["value"], HarborlineJsonLogic.Evaluate(test["expression"], context)), id);
            Assert.Equal(1, inner.Reads);
        }
    }

    private sealed class CountingResolver(JsonObject bag) : IValueResolver
    {
        public int Reads { get; private set; }
        public RefValue ResolveVar(string path) { Reads++; return RefValue.Resolved(bag[path]); }
        public RefValue ResolveAgg(string fn, string section, string col) => RefValue.UnavailableAggregate(fn, section, col);
    }

    public static IEnumerable<object[]> FlatBagCases() => KeyFenceCases("flat");

    [Theory(DisplayName = "T-1232 acceptance 3 / rules-ck-28: a field alias cannot expose a dotted flat-bag key")]
    [MemberData(nameof(FlatBagCases))]
    public void T1232_A3_Flat_bag_field_aliases(string id, string json) => AssertKeyFenceCase(id, json);

    public static IEnumerable<object[]> BareRowCases() => KeyFenceCases("row");

    // Oracle: the fixture's literal ["x"] (T-1232 What to fix bullet 1: a bare key means field).
    // The row holds an undeclared x and the declared field x is absent, so a row read answers [].
    [Theory(DisplayName = "T-1232 / rules-ck-28: a bare missing key under a snapshot row reads the declared field, not the row")]
    [MemberData(nameof(BareRowCases))]
    public void T1232_Bare_missing_key_under_row_reads_the_field(string id, string json) => AssertKeyFenceCase(id, json);

    private static void AssertKeyFenceCase(string id, string json)
    {
        var test = JsonNode.Parse(json)!;
        var expression = test["expression"]!.ToJsonString();
        var definition = Rule(id, "out", expression);
        var declaration = TestAdmission.Declaration(operations: ["var", "missing", "missing_some", "cat", "if"], variables: ["field"]);
        var environment = BorrowerEnvironmentAdmission.Admit(declaration);
        var expected = test["refusal"]?.GetValue<string>();
        var compiled = RuleCompiler.Compile([definition]);
        foreach (var phase in new[] { EvaluationPhase.AuthoringValidation, EvaluationPhase.PublishValidation })
        {
            Assert.Equal(test["group"]!.GetValue<string>() == "dynamic" ? null : expected, environment.Refusal([JsonNode.Parse(expression)]));
            Assert.Equal(test["group"]!.GetValue<string>() == "dynamic" ? null : expected, BorrowerEnvironmentAdmission.CheckCompiledGuard(compiled, declaration, phase));
        }
        var envelope = new JsonObject { ["root"] = test["bag"]!.DeepClone() };
        if (test["row"] is { } row) envelope["row"] = row.DeepClone();
        var context = RuleContextSnapshot.FromJsonText(envelope.ToJsonString());
        var result = new GuardEvaluator(new FixedClock(Clock)).EvaluateValue(definition, context, RuleEvalScope.Root, environment.For(EvaluationPhase.Run));
        Assert.Equal(expected, result.Error?.Code);
        var guard = new GuardEvaluator(new FixedClock(Clock)).EvaluateGuard(definition, context, RuleEvalScope.Root, environment.For(EvaluationPhase.Run));
        if (expected is not null) Assert.Equal(expected, guard.Error?.Code);
        else if (test["group"]!.GetValue<string>() == "flat") Assert.Equal(id != "field-alias", guard.Ok);
        else Assert.Equal(test["value"]!.AsArray().Count > 0, guard.Ok);

        if (expected is null)
        {
            Assert.Equal(ValueState.Resolved, result.State);
            Assert.True(JsonNode.DeepEquals(test["value"], result.Value), $"{id}: expected {test["value"]?.ToJsonString() ?? "null"}, actual {result.Value?.ToJsonString() ?? "null"}");
        }
    }

    [Fact(DisplayName = "rules-ck-28: a typed borrower declaration is admitted; one missing a member, borrowing another grammar, asking for an effect, naming an unregistered function or leaving a phase unmarked refuses")]
    public void Declarations_are_admitted_or_refused_by_member()
    {
        var good = TestAdmission.Declaration();
        Assert.Equal(BorrowerEnvironmentAdmission.CanonicalJson(good), BorrowerEnvironmentAdmission.CanonicalJson(BorrowerEnvironmentAdmission.Admit(good).Declaration));

        BorrowerEnvironmentDeclaration[] refused =
        [
            good with { Borrower = " " },
            good with { Grammar = "harborline-jsonlogic/v2" },
            good with { Variables = new Dictionary<string, string>() },
            good with { Operations = ["cat", "http.get"] },
            good with { Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"] },
            good with { MissingValues = "" },
            good with { TimeZone = "" },
            good with { Phases = new Dictionary<EvaluationPhase, bool> { [EvaluationPhase.Run] = true } },
            good with { Replay = "" },
        ];
        foreach (var declaration in refused)
            Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused,
                Assert.Throws<BorrowerEnvironmentException>(() => BorrowerEnvironmentAdmission.Admit(declaration)).Code);

        // Only admission constructs the evidence an entry point requires.
        Assert.Empty(typeof(AdmittedEnvironment).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(typeof(EvaluationAdmission).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    [Fact(DisplayName = "rules-eng-26: admission snapshots mutable declarations")]
    public void Admission_snapshots_declaration_and_evaluation_evidence()
    {
        var original = TestAdmission.Declaration(operations: ["var"], variables: ["field"], phases: [EvaluationPhase.Run]);
        var operations = original.Operations.ToList();
        var variables = new Dictionary<string, string>(original.Variables);
        var effects = original.Effects.ToList();
        var phases = new Dictionary<EvaluationPhase, bool>(original.Phases);
        var environment = BorrowerEnvironmentAdmission.Admit(original with { Operations = operations, Variables = variables, Effects = effects, Phases = phases });
        operations.Add("cat");
        variables["other"] = "other";
        effects.Clear();
        phases[EvaluationPhase.Run] = false;
        phases[EvaluationPhase.Submission] = true;

        Assert.Equal(["var"], environment.Declaration.Operations);
        Assert.Equal(["field"], environment.Declaration.Variables.Keys);
        Assert.Equal(["field-read"], environment.Declaration.Effects);
        Assert.True(environment.Declaration.Phases[EvaluationPhase.Run]);
        Assert.False(environment.Declaration.Phases[EvaluationPhase.Submission]);
        Assert.Equal(BorrowerEnvironmentAdmission.CanonicalJson(original), BorrowerEnvironmentAdmission.CanonicalJson(environment.Declaration));
        Assert.Equal(BorrowerEnvironmentAdmission.CanonicalJson(original), environment.CanonicalJson);
        var admission = environment.For(EvaluationPhase.Run);
        Assert.Equal("rule.environment.phase_not_admitted", Assert.Throws<BorrowerEnvironmentException>(() => environment.For(EvaluationPhase.Submission)).Code);
        var value = Rule("v", "out", """{"cat":["a","b"]}""");
        var context = RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?>());
        Assert.Equal("rule.environment.operation_not_admitted", new GuardEvaluator(new FixedClock(Clock)).EvaluateValue(value, context, RuleEvalScope.Root, admission).Error!.Code);
    }

    [Fact(DisplayName = "rules-eng-26: a phase the declaration marks inapplicable cannot be presented")]
    public void Inapplicable_phase_refuses()
    {
        var environment = BorrowerEnvironmentAdmission.Admit(TestAdmission.Declaration(phases: [EvaluationPhase.Render]));
        Assert.Equal(EvaluationPhase.Render, environment.For(EvaluationPhase.Render).Phase);
        Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
            Assert.Throws<BorrowerEnvironmentException>(() => environment.For(EvaluationPhase.Submission)).Code);
    }

    [Fact(DisplayName = "T-741: compiled guard admission is pure, phase-bound, and retains declared context prefixes")]
    public void Compiled_guard_admission_is_pure_and_checks_declared_prefixes()
    {
        var declaration = TestAdmission.Declaration(variables: ["field", "row", "section"]);
        var permitted = RuleCompiler.Compile([Rule("permitted", "ok", """{"var":"field.status"}""", RuleActionKind.Validate, RuleScope.Schema)]);
        var candidate = RuleCompiler.Compile([Rule("candidate", "ok", """{"var":"candidate.status"}""", RuleActionKind.Validate, RuleScope.Schema)]);
        var workflow = RuleCompiler.Compile([Rule("workflow", "ok", """{"var":"wf.state"}""", RuleActionKind.Validate, RuleScope.Schema)]);
        var timer = RuleCompiler.Compile([Rule("timer", "ok", """{"var":"timer.due"}""", RuleActionKind.Validate, RuleScope.Schema)]);
        var clock = new ThrowingClock();
        var records = new ThrowingRecordReader();

        Assert.Null(BorrowerEnvironmentAdmission.CheckCompiledGuard(permitted, declaration, EvaluationPhase.PublishValidation));
        foreach (var compiled in new[] { candidate, workflow, timer })
            Assert.Equal(BorrowerEnvironmentAdmission.VariableNotAdmitted,
                BorrowerEnvironmentAdmission.CheckCompiledGuard(compiled, declaration, EvaluationPhase.PublishValidation));
        Assert.Equal(0, clock.Reads);
        Assert.Equal(0, records.Reads);

        var parameters = typeof(BorrowerEnvironmentAdmission)
            .GetMethod(nameof(BorrowerEnvironmentAdmission.CheckCompiledGuard))!.GetParameters();
        Assert.Equal([typeof(CompiledGraph), typeof(BorrowerEnvironmentDeclaration), typeof(EvaluationPhase)],
            parameters.Select(parameter => parameter.ParameterType));

        Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted,
            Assert.Throws<BorrowerEnvironmentException>(() => BorrowerEnvironmentAdmission.CheckCompiledGuard(
                permitted, TestAdmission.Declaration(phases: [EvaluationPhase.Render]), EvaluationPhase.PublishValidation)).Code);
    }

    [Fact(DisplayName = "rules-eng-26: guard and value overloads evaluate under an admitted declaration and refuse the unadmitted counterpart")]
    public void Guard_and_value_overloads_require_admission()
    {
        var guard = new GuardEvaluator(new FixedClock(Clock));
        var rule = Rule("g", "ok", """{">":[{"var":"amount"},10]}""", RuleActionKind.Validate);
        var value = Rule("v", "out", """{"cat":["a",{"var":"amount"}]}""");
        var context = RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?> { ["amount"] = 20 });

        Assert.True(guard.EvaluateGuard(rule, context, RuleEvalScope.Root, TestAdmission.Any).Ok);
        Assert.Equal("a20", guard.EvaluateValue(value, context, RuleEvalScope.Root, TestAdmission.Any).Value!.GetValue<string>());

        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, guard.EvaluateGuard(rule, context, RuleEvalScope.Root, null).Error!.Code);
        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, guard.EvaluateValue(value, context, RuleEvalScope.Root, null).Error!.Code);

        // An admitted environment that does not lend `cat` (or `>`) refuses the program that uses it.
        var withoutCat = Narrow(operations: ["var", ">"]);
        Assert.True(guard.EvaluateGuard(rule, context, RuleEvalScope.Root, withoutCat).Ok);
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted, guard.EvaluateValue(value, context, RuleEvalScope.Root, withoutCat).Error!.Code);
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted, guard.EvaluateGuard(rule, context, RuleEvalScope.Root, Narrow(operations: ["var"])).Error!.Code);
    }

    [Fact(DisplayName = "rules-eng-26: full graph evaluation evaluates under an admitted declaration and refuses the unadmitted counterpart")]
    public void Full_graph_requires_admission()
    {
        var compiled = RuleCompiler.Compile([Rule("t", "total", """{"+":[{"var":"a"},1]}""")]);
        var instance = RuleInstance.FromJson(JsonNode.Parse("""{"a":2}""")!.AsObject());

        Assert.Equal(3L, new FormRuleGraph(compiled, new FixedClock(Clock), TestAdmission.Any).EvaluateInstance(instance).Values["field:total"].Value!.GetValue<long>());

        var refused = new FormRuleGraph(compiled, new FixedClock(Clock), null).EvaluateInstance(instance);
        Assert.Equal(BorrowerEnvironmentAdmission.NotAdmitted, Code(refused));
        Assert.False(refused.Values.ContainsKey("field:total"));
        Assert.Equal(BorrowerEnvironmentAdmission.OperationNotAdmitted,
            Code(new FormRuleGraph(compiled, new FixedClock(Clock), Narrow(operations: ["var"])).EvaluateInstance(instance)));
    }

    [Fact(DisplayName = "rules-eng-26: incremental graph evaluation (field change, row add, row remove) evaluates under admission and refuses the unadmitted counterpart")]
    public void Incremental_graph_requires_admission()
    {
        var compiled = RuleCompiler.Compile(
        [
            Rule("t", "total", """{"var":"table.sum(items.amount)"}"""),
            Rule("d", "double", """{"*":[{"var":"a"},2]}"""),
        ]);
        var row = new RuleRow("r1", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(5) });

        var admitted = new FormRuleGraph(compiled, new FixedClock(Clock), TestAdmission.Any);
        admitted.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse("""{"a":1}""")!.AsObject()));
        Assert.Equal(6L, admitted.Reevaluate("a", RuleInputValue.FromJsonText("3")).Values["field:double"].Value!.GetValue<long>());
        Assert.Equal(5L, admitted.AddRow("items", row).Values["field:total"].Value!.GetValue<long>());
        Assert.Equal(0L, admitted.RemoveRow("items", "r1").Values["field:total"].Value!.GetValue<long>());

        foreach (var admission in new[] { null, Narrow(variables: ["field"]) })
        {
            var refused = new FormRuleGraph(compiled, new FixedClock(Clock), admission);
            var code = admission is null ? BorrowerEnvironmentAdmission.NotAdmitted : BorrowerEnvironmentAdmission.VariableNotAdmitted;
            Assert.Equal(code, Code(refused.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse("""{"a":1}""")!.AsObject()))));
            Assert.Equal(code, Code(refused.Reevaluate("a", RuleInputValue.FromJsonText("3"))));
            Assert.Equal(code, Code(refused.Reevaluate("a", JsonValue.Create(3))));
            Assert.Equal(code, Code(refused.AddRow("items", row)));
            Assert.Equal(code, Code(refused.RemoveRow("items", "r1")));
        }
    }

    /// <summary>
    /// Every production call site that reaches an evaluation entry point, by file. A new file that calls one
    /// fails here until it is inventoried with its borrower. The raw seams below the entry points are internal.
    /// </summary>
    private static readonly string[] InventoriedCallSites =
    [
        "projections/dotnet/blocks/hlp.blocks.layout-runtime/LayoutBindingResolution.cs",
        "projections/dotnet/blocks/hlp.blocks.workflow/WorkflowsExpressionEnvironment.cs",
        "projections/dotnet/foundation/hlp.foundation.documents/DocumentsExpressionEnvironment.cs",
        "projections/dotnet/foundation/hlp.foundation.field-runtime/ValueDomainRuntime.cs",
        "projections/dotnet/foundation/hlp.foundation.forms-engine/FormCandidateEvaluator.cs",
        "projections/dotnet/foundation/hlp.foundation.forms-engine/FormEngine.cs",
        "projections/dotnet/foundation/hlp.foundation.rule-authoring/SkinLowering.cs",
    ];

    [Fact(DisplayName = "rules-eng-26: architecture fence — evaluation call sites are inventoried, each presents an admission, and the raw seams stay internal")]
    public void Evaluation_call_sites_are_inventoried()
    {
        var root = FunctionRegisterTests.RepositoryRoot();
        var entry = new Regex(@"new FormRuleGraph\(|\.EvaluateGuard\(|\.EvaluateValue\(", RegexOptions.CultureInvariant);
        var found = Directory.EnumerateFiles(Path.Combine(root, "projections", "dotnet"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "projections", "blazor"), "*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.Contains(".tests/", StringComparison.Ordinal) && !path.Contains("/hlp.foundation.rule-runtime/", StringComparison.Ordinal))
            .Where(path => entry.IsMatch(File.ReadAllText(Path.Combine(root, path))))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(InventoriedCallSites, found);

        foreach (var path in found)
        {
            var source = File.ReadAllText(Path.Combine(root, path));
            foreach (Match call in entry.Matches(source))
            {
                var statement = source[call.Index..source.IndexOf(';', call.Index)];
                Assert.True(statement.Contains(".For(EvaluationPhase.", StringComparison.Ordinal) || statement.Contains("admission", StringComparison.Ordinal),
                    $"{path}: an evaluation call does not present an admission: {statement}");
            }
        }

        var engine = typeof(GuardEvaluator).Assembly;
        foreach (var raw in new[] { "Harborline.Foundation.RuleEngine.Evaluation.HarborlineJsonLogic", "Harborline.Foundation.RuleEngine.RuleEvaluator", "Harborline.Foundation.RuleEngine.Evaluation.EvalContext" })
            Assert.False(engine.GetType(raw, throwOnError: true)!.IsPublic, $"{raw} must stay an internal seam");
    }

    private sealed class ThrowingClock : TimeProvider
    {
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            throw new InvalidOperationException("The admission check must not read the clock.");
        }
    }

    private sealed class ThrowingRecordReader : IValueResolver
    {
        public int Reads { get; private set; }

        public RefValue ResolveVar(string path)
        {
            Reads++;
            throw new InvalidOperationException("The admission check must not read a record.");
        }

        public RefValue ResolveAgg(string fn, string section, string col)
        {
            Reads++;
            throw new InvalidOperationException("The admission check must not read a record.");
        }
    }
}
