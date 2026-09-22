using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// .NET integrity-tier unit coverage (SPINE-1 §6.2): graph build + cycle rejection,
/// every resource bound (fail-closed with the exact stable code), the guard evaluator,
/// visibility merge, reactive re-eval (raw-field dependents + referential stability),
/// incremental child-table edits, and the fail-closed save gate.
/// </summary>
public sealed class RuleEngineUnitTests
{
    private static readonly DateTimeOffset Clock = new(2026, 6, 30, 0, 0, 0, TimeSpan.Zero);

    private static RuleDefinition Compute(string id, string target, string expr, RuleScope scope = RuleScope.Field) => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, scope, target, expr, RuleActionKind.Compute);

    private static RuleInstance Instance(string json)
        => RuleInstance.FromJson(JsonNode.Parse(json)!.AsObject());

    private static FormRuleGraph Graph(IReadOnlyList<RuleDefinition> rules, RuleEngineLimits? limits = null)
        => new(RuleCompiler.Compile(rules, limits), new FixedClock(Clock), limits);

    [Fact]
    public void Graph_and_guard_reject_a_missing_business_clock()
    {
        var compiled = RuleCompiler.Compile([]);

        Assert.Throws<ArgumentNullException>(() => new FormRuleGraph(compiled, clock: null!));
        Assert.Throws<ArgumentNullException>(() => new GuardEvaluator(clock: null!));
    }

    // ── cycle + tier rejection ────────────────────────────────────────────────

    [Fact]
    public void Compile_rejects_cycle_with_path_diagnostic()
    {
        var rules = new[]
        {
            Compute("c.x", "x", "{\"+\":[{\"var\":\"y\"},1]}"),
            Compute("c.y", "y", "{\"+\":[{\"var\":\"x\"},1]}"),
        };
        var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(rules));
        Assert.Equal(RuleEngineCodes.CompileCycle, ex.Code);
        Assert.NotNull(ex.CyclePath);
        Assert.NotEmpty(ex.CyclePath!);
    }

    [Fact]
    public void Compile_rejects_powerfx_tier()
    {
        var rule = RuleDefinitionFactory.Create("c.p", RuleTier.PowerFx, RuleScope.Field, "p", "{\"var\":\"a\"}", RuleActionKind.Compute);
        var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));
        Assert.Equal(RuleEngineCodes.CompileUnsupportedTier, ex.Code);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void CompilerDoesNotInterpretAnUnknownTierAsJsonLogic(int tier)
    {
        var rule = RuleDefinitionFactory.Create("unknown-tier", (RuleTier)tier,
            RuleScope.Field, "total", "1", RuleActionKind.Compute);

        var error = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));

        Assert.Equal(RuleEngineCodes.CompileUnsupportedTier, error.Code);
        Assert.Equal("unknown-tier", error.RuleId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void CompilerDoesNotInterpretAnUnknownActionAsVisibility(int action)
    {
        var rule = RuleDefinitionFactory.Create("unknown-action", RuleTier.JsonLogic,
            RuleScope.Field, "total", "true", (RuleActionKind)action);

        var error = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));

        Assert.Equal("rule.compile.unknown_action", error.Code);
        Assert.Equal("unknown-action", error.RuleId);
    }

    [Theory]
    [InlineData("{\"unknown_operator\":[]}")]
    [InlineData("{\"regex\":[\"a\",\".*\"]}")]
    [InlineData("{\"if\":[false,{\"unknown_operator\":[]},true]}")]
    public void CompilerRefusesUnsupportedOperatorsWithoutNeedingAnyRecords(string expression)
    {
        var rule = Compute("closed-grammar", "total", expression);

        var error = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));

        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, error.Code);
        Assert.Equal("closed-grammar", error.RuleId);
    }

    [Theory]
    [InlineData("[{\"unknown_operator\":[]}]")]
    [InlineData("{\"metadata\":{\"unknown_operator\":[]},\"label\":\"literal\"}")]
    public void OperatorAdmissionDoesNotInterpretLiteralDataAsExecutableSyntax(string expression)
    {
        Assert.Equal(1, RuleCompiler.Compile(new[] { Compute("literal-data", "result", expression) }).RuleCount);
    }

    [Theory]
    [InlineData("{\"date.today\":[\"unexpected\"]}")]
    [InlineData("{\"coding.is\":[{\"var\":\"code\"},\"system\"]}")]
    public void CompilerRefusesExecutableOperatorsWithInvalidArityBeforeEvaluation(string expression)
    {
        var error = Assert.Throws<RuleCompilationException>(() =>
            RuleCompiler.Compile(new[] { Compute("invalid-arity", "result", expression) }));

        Assert.Equal(RuleEngineCodes.CompileInvalidExpression, error.Code);
        Assert.Equal("invalid-arity", error.RuleId);
    }

    // ── static bounds ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DefaultCompilerReferenceBoundaryIsInclusive(int count, bool admitted)
    {
        var refs = string.Join(",", Enumerable.Range(0, count).Select(i => $"{{\"var\":\"r{i}\"}}"));
        var rules = new[] { Compute("reference-boundary", "total", $"{{\"+\":[{refs}]}}") };

        if (admitted)
            Assert.Equal(1, RuleCompiler.Compile(rules).RuleCount);
        else
            Assert.Equal(RuleEngineCodes.CompileTooManyRefs,
                Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(rules)).Code);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void DefaultCompilerDependencyDepthBoundaryIsInclusive(int depth, bool admitted)
    {
        var rules = Enumerable.Range(0, depth)
            .Select(i => Compute($"dependency-{i}", $"f{i}", $"{{\"var\":\"f{i + 1}\"}}"))
            .ToArray();

        if (admitted)
            Assert.Equal(depth, RuleCompiler.Compile(rules).RuleCount);
        else
            Assert.Equal(RuleEngineCodes.CompileDepthExceeded,
                Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(rules)).Code);
    }

    [Fact]
    public void Compile_rejects_excess_dependency_depth()
    {
        var rules = new List<RuleDefinition> { Compute("c.f0", "f0", "{\"+\":[{\"var\":\"seed\"},1]}") };
        for (int i = 1; i <= 70; i++)
            rules.Add(Compute($"c.f{i}", $"f{i}", $"{{\"+\":[{{\"var\":\"f{i - 1}\"}},1]}}"));
        var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(rules));
        Assert.Equal(RuleEngineCodes.CompileDepthExceeded, ex.Code);
    }

    [Fact]
    public void Compile_rejects_excess_references()
    {
        var refs = string.Join(",", Enumerable.Range(0, 70).Select(i => $"{{\"var\":\"r{i}\"}}"));
        var rule = Compute("c.many", "many", $"{{\"+\":[{refs}]}}");
        var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));
        Assert.Equal(RuleEngineCodes.CompileTooManyRefs, ex.Code);
    }

    [Fact]
    public void Compile_rejects_oversized_ast()
    {
        var limits = RuleEngineLimits.Default with { MaxAstNodes = 3 };
        var rule = Compute("c.big", "big", "{\"+\":[{\"var\":\"a\"},{\"var\":\"b\"},{\"var\":\"c\"}]}");
        var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }, limits));
        Assert.Equal(RuleEngineCodes.CompileAstTooLarge, ex.Code);
    }

    // ── runtime bounds (fail-closed) ──────────────────────────────────────────

    [Fact]
    public void Graph_too_large_fails_closed()
    {
        var limits = RuleEngineLimits.Default with { MaxGraphNodes = 1 };
        var rules = new[]
        {
            Compute("c.a", "a2", "{\"+\":[{\"var\":\"x\"},1]}"),
            Compute("c.b", "b2", "{\"+\":[{\"var\":\"y\"},1]}"),
        };
        var result = Graph(rules, limits).EvaluateInstance(Instance("{\"x\":1,\"y\":2}"));
        Assert.True(result.IsSaveBlocked);
        Assert.Contains(result.Validations, v => v.Validity!.Error!.Code == RuleEngineCodes.GraphTooLarge);
    }

    [Fact]
    public void Step_budget_fails_closed()
    {
        var limits = RuleEngineLimits.Default with { StepBudget = 0 };
        var result = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") }, limits)
            .EvaluateInstance(Instance("{\"a\":1}"));
        Assert.True(result.IsSaveBlocked);
        Assert.Contains(result.Validations, v => v.Validity!.Error!.Code == RuleEngineCodes.BudgetExceeded);
    }

    [Fact]
    public void Wall_clock_is_infrastructure_fault_not_a_divergent_outcome()
    {
        // D1 ratification 2026-07-01, fix 1: the wall-clock / cancellation is a NON-authoritative
        // liveness guard. It must NOT become an evaluation outcome (a `rule.timeout` result would make
        // a slow tier disagree with a fast tier — breaking replay-determinism + the byte-identical
        // corpus). It PROPAGATES as an infrastructure exception instead.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        var ex = Assert.Throws<RuleEngineTimeoutException>(() => graph.EvaluateInstance(Instance("{\"a\":1}"), cts.Token));
        Assert.Equal(RuleEngineCodes.Timeout, ex.Code);
    }

    [Fact]
    public void Wall_clock_on_a_guard_is_infrastructure_fault_not_a_verdict()
    {
        // The workflow guard tier is symmetric: a cancelled/timed-out guard is an infra fault, not a
        // "transition denied" verdict — it propagates rather than returning Validity.Invalid(timeout).
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var guard = new GuardEvaluator(new FixedClock(Clock), RuleEngineLimits.Default);
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);
        Assert.Throws<RuleEngineTimeoutException>(() => guard.EvaluateGuard(rule, RuleContextSnapshot.Capture(Bag("amount", 100)), RuleEvalScope.Root, cts.Token));
    }

    [Fact]
    public void Graph_EvaluationPinsOneInjectedInstantAcrossAllOutcomes()
    {
        var clock = new AdvancingClock(
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 0, 0, 1, TimeSpan.Zero));
        var graph = new FormRuleGraph(RuleCompiler.Compile(
            [
                Compute("today.one", "one", "{\"date.today\":[]}"),
                Compute("today.two", "two", "{\"date.today\":[]}"),
            ]), clock, RuleEngineLimits.Default);

        var result = graph.EvaluateInstance(Instance("{}"));

        Assert.Equal(1, clock.Reads);
        Assert.Equal("2026-06-30", result.Values["field:one"].Value!.GetValue<string>());
        Assert.Equal("2026-06-30", result.Values["field:two"].Value!.GetValue<string>());
    }

    [Fact]
    public void Reevaluate_treats_date_today_as_an_input_without_recomputing_unaffected_outcomes()
    {
        var beforeMidnight = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var afterMidnight = new DateTimeOffset(2026, 7, 1, 0, 0, 1, TimeSpan.Zero);
        var rules = new[]
        {
            Compute("c.today", "today", "{\"date.today\":[]}"),
            Compute("c.label", "label", "{\"cat\":[{\"var\":\"today\"},\"/\",{\"var\":\"other\"}]}"),
            Compute("c.stable", "stable-output", "{\"var\":\"stable\"}"),
            RuleDefinitionFactory.Create("v.today", RuleTier.JsonLogic, RuleScope.Field, "today-valid", "{\"==\":[{\"date.today\":[]},\"2026-07-01\"]}", RuleActionKind.Validate),
        };
        var clock = new AdvancingClock(beforeMidnight, afterMidnight);
        var graph = new FormRuleGraph(RuleCompiler.Compile(rules), clock);
        var first = graph.EvaluateInstance(Instance("{\"other\":\"before\",\"stable\":\"unchanged\"}"));
        var stableOutcome = first.ByRule["c.stable"];

        var incremental = graph.Reevaluate("other", RuleInputValue.FromJsonText("\"after\""));
        var full = new FormRuleGraph(RuleCompiler.Compile(rules), new FixedClock(afterMidnight))
            .EvaluateInstance(Instance("{\"other\":\"after\",\"stable\":\"unchanged\"}"));

        Assert.Equal(2, clock.Reads);
        Assert.Equal(full.Values["field:today"].Value!.GetValue<string>(), incremental.Values["field:today"].Value!.GetValue<string>());
        Assert.Equal(full.Values["field:label"].Value!.GetValue<string>(), incremental.Values["field:label"].Value!.GetValue<string>());
        Assert.Equal(full.ByRule["v.today"].Validity!.Ok, incremental.ByRule["v.today"].Validity!.Ok);
        Assert.Same(stableOutcome, incremental.ByRule["c.stable"]);
    }

    [Fact]
    public void Graph_keeps_an_owned_instance_when_the_caller_mutates_the_original_after_evaluation()
    {
        var instance = Instance("{\"a\":1}");
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        graph.EvaluateInstance(instance);
        instance.Fields["a"] = JsonValue.Create(99);

        var result = graph.AddRow("unrelated", new RuleRow("r1", new Dictionary<string, JsonNode?>()));

        Assert.Equal(2L, result.Values["field:b"].Value!.GetValue<long>());
    }

    [Fact]
    public void EvaluateInstance_refuses_a_converter_backed_value_added_after_capture_without_running_its_converter()
    {
        var canaryPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "t589", "converter-canary-" + Guid.NewGuid().ToString("N"));
        var instance = Instance("{\"a\":1}");
        instance.Fields["unsafe"] = JsonValue.Create(new WritingPayload(canaryPath));

        try
        {
            var result = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") }).EvaluateInstance(instance);

            Assert.True(result.IsSaveBlocked);
            Assert.False(File.Exists(canaryPath));
        }
        finally
        {
            if (File.Exists(canaryPath)) File.Delete(canaryPath);
        }
    }

    [Fact]
    public void Reactive_entries_refuse_converter_backed_nodes_before_the_converter_runs()
    {
        var canaryPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "t589", "converter-reactive-" + Guid.NewGuid().ToString("N"));
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        graph.EvaluateInstance(Instance("{\"a\":1}"));
        var unsafeValue = JsonValue.Create(new WritingPayload(canaryPath));
        var row = new RuleRow("r1", new Dictionary<string, JsonNode?>());
        row.Fields["unsafe"] = unsafeValue;

        try
        {
            Assert.Throws<InvalidOperationException>(() => new RuleRow("r0", new Dictionary<string, JsonNode?> { ["unsafe"] = unsafeValue }));
            Assert.Equal(RuleEngineCodes.ContextSnapshotRequired, graph.Reevaluate("a", unsafeValue).Validations.Single().Validity!.Error!.Code);
            Assert.Equal(RuleEngineCodes.ContextSnapshotRequired, graph.AddRow("items", row).Validations.Single().Validity!.Error!.Code);
            Assert.False(File.Exists(canaryPath));
            Assert.Equal(3L, graph.Reevaluate("a", RuleInputValue.FromJsonText("2")).Values["field:b"].Value!.GetValue<long>());
        }
        finally
        {
            if (File.Exists(canaryPath)) File.Delete(canaryPath);
        }
    }

    [Fact]
    public void Add_row_refuses_a_custom_value_inserted_under_a_previously_captured_object()
    {
        var canaryPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "t589", "converter-nested-" + Guid.NewGuid().ToString("N"));
        var source = RuleInstance.FromJsonText("{\"template\":[{\"data\":{\"ok\":true}}]}");
        var row = source.Tables["template"][0];
        row.Fields["data"]!.AsObject()["poison"] = JsonValue.Create(new WritingPayload(canaryPath));
        var graph = Graph(new[] { Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}") });
        graph.EvaluateInstance(Instance("{\"items\":[{\"amount\":1}]}"));

        try
        {
            Assert.Equal(RuleEngineCodes.ContextSnapshotRequired, graph.AddRow("items", row).Validations.Single().Validity!.Error!.Code);
            Assert.False(File.Exists(canaryPath));
            Assert.Equal(3L, graph.AddRow("items", new RuleRow("safe", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(2) })).Values["field:total"].Value!.GetValue<long>());
        }
        finally
        {
            if (File.Exists(canaryPath)) File.Delete(canaryPath);
        }
    }

    [Fact]
    public void Host_instance_capture_refuses_a_custom_value_before_serialization()
    {
        var canaryPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "t589", "converter-host-" + Guid.NewGuid().ToString("N"));
        var hostile = new JsonObject { ["payload"] = JsonValue.Create(new WritingPayload(canaryPath)) };
        try
        {
            Assert.Equal(RuleEngineCodes.ContextSnapshotRequired,
                Assert.Throws<InvalidOperationException>(() => RuleInstance.FromJson(hostile)).Message);
            Assert.False(File.Exists(canaryPath));
        }
        finally
        {
            if (File.Exists(canaryPath)) File.Delete(canaryPath);
        }
    }

    [Fact]
    public void RuleInstance_refuses_host_data_over_the_shared_utf8_input_envelope()
    {
        var source = new JsonObject { ["payload"] = new string('a', RuleContextSnapshot.MaxUtf8Bytes) };

        Assert.Throws<ArgumentException>(() => RuleInstance.FromJson(source));
    }

    [Fact]
    public void Initial_instance_at_the_utf8_envelope_is_evaluated_without_a_second_metadata_charge()
    {
        const string Prefix = "{\"payload\":\"";
        const string Suffix = "\"}";
        int payloadBytes = RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(Prefix + Suffix);
        var instance = RuleInstance.FromJsonText(Prefix + new string('é', payloadBytes / 2) + Suffix);

        var result = Graph(new[] { Compute("c.copy", "copy", "{\"var\":\"payload\"}") }).EvaluateInstance(instance);

        Assert.Equal(ValueState.Resolved, result.Values["field:copy"].State);
    }

    [Fact]
    public void Initial_instance_uses_json_stringify_bytes_for_supplementary_unicode_controls_escaped_names_and_numbers()
    {
        string Source(string payload) => "{\"\\u006Eame\\u0022\":\"😀😀\u2028\\b\\f\",\"numeric\":1e+00,\"negative\":-0,\"payload\":\"" + payload + "\"}";
        string JsonStringifyDocument(string payload) => "{\"name\\\"\":\"😀😀\u2028\\b\\f\",\"numeric\":1,\"negative\":0,\"payload\":\"" + payload + "\"}";
        var payload = new string('a', RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(Source("")));

        Assert.Equal(RuleContextSnapshot.MaxUtf8Bytes, Encoding.UTF8.GetByteCount(Source(payload)));
        Assert.True(Encoding.UTF8.GetByteCount(JsonStringifyDocument(payload)) <= RuleContextSnapshot.MaxUtf8Bytes);
        Assert.Throws<ArgumentException>(() => RuleInstance.FromJsonText(Source(payload + "a")));

        var result = Graph(new[] { Compute("c.copy", "copy", "{\"var\":\"payload\"}") })
            .EvaluateInstance(RuleInstance.FromJsonText(Source(payload)));

        Assert.Equal(ValueState.Resolved, result.Values["field:copy"].State);
    }

    [Fact]
    public void Reactive_composition_uses_json_stringify_bytes_for_supplementary_unicode_controls_escaped_names_and_numbers()
    {
        string Source(string payload) => "{\"\\u006Eame\\u0022\":\"😀😀\u2028\\b\\f\",\"numeric\":1e+00,\"negative\":-0,\"payload\":\"" + payload + "\"}";
        string JsonStringifyDocument(string payload) => "{\"name\\\"\":\"😀😀\u2028\\b\\f\",\"numeric\":1,\"negative\":0,\"payload\":\"" + payload + "\"}";
        var payload = new string('a', RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(JsonStringifyDocument("")));
        var graph = Graph(new[] { Compute("c.copy", "copy", "{\"var\":\"payload\"}") });
        graph.EvaluateInstance(RuleInstance.FromJsonText(Source("")));

        var result = graph.Reevaluate("payload", RuleInputValue.FromJsonText("\"" + payload + "\""));

        Assert.Equal(ValueState.Resolved, result.Values["field:copy"].State);
    }

    [Fact]
    public void Host_instance_at_the_logical_json_stringify_limit_is_evaluated()
    {
        const string Prefix = "{\"payload\":\"";
        const string Suffix = "\"}";
        var payload = new string('a', RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(Prefix + Suffix));
        var host = new JsonObject { ["payload"] = payload };

        var result = Graph(new[] { Compute("c.copy", "copy", "{\"var\":\"payload\"}") }).EvaluateInstance(RuleInstance.FromJson(host));

        Assert.Equal(ValueState.Resolved, result.Values["field:copy"].State);
    }

    [Fact]
    public void Initial_mutable_fields_enforce_the_shared_node_bound_before_copying()
    {
        var instance = RuleInstance.FromJsonText("{\"seed\":null}");
        JsonNode? trusted = instance.Fields["seed"];
        instance.Fields.Clear();
        for (int i = 0; i < RuleContextSnapshot.MaxNodes - 1; i++) instance.Fields["f" + i] = trusted;

        var admitted = Graph(Array.Empty<RuleDefinition>()).EvaluateInstance(instance);
        Assert.False(admitted.IsSaveBlocked);

        instance.Fields["over"] = trusted;
        var refused = Graph(Array.Empty<RuleDefinition>()).EvaluateInstance(instance);
        Assert.Equal(RuleEngineCodes.InputTooLarge, refused.Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Initial_mutable_fields_refuse_cumulative_utf8_growth_before_copying()
    {
        const string Prefix = "{\"seed\":\"";
        const string Suffix = "\"}";
        int payloadBytes = (RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(Prefix + Suffix)) / 2;
        var instance = RuleInstance.FromJsonText(Prefix + new string('é', payloadBytes / 2) + Suffix);
        instance.Fields["growth"] = instance.Fields["seed"];

        var refused = Graph(Array.Empty<RuleDefinition>()).EvaluateInstance(instance);

        Assert.Equal(RuleEngineCodes.InputTooLarge, refused.Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Inferred_table_ids_do_not_consume_nodes_after_initial_capture_or_reactive_edits()
    {
        var rows = string.Join(',', Enumerable.Range(0, 2000).Select(i => "{\"value\":1}"));
        var graph = Graph(new[] { Compute("c.copy", "copy", "{\"var\":\"a\"}") });
        var first = graph.EvaluateInstance(RuleInstance.FromJsonText("{\"a\":1,\"items\":[" + rows + "]}"));

        Assert.False(first.IsSaveBlocked);
        Assert.Equal(2L, graph.Reevaluate("a", RuleInputValue.FromJsonText("2")).Values["field:copy"].Value!.GetValue<long>());
        Assert.Equal(2L, graph.AddRow("other", new RuleRow("r1", new Dictionary<string, JsonNode?> { ["v"] = JsonValue.Create(1) })).Values["field:copy"].Value!.GetValue<long>());
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void Initial_mutable_composition_of_trusted_values_honours_the_depth_bound(int nestedObjects, bool admitted)
    {
        var source = RuleInstance.FromJsonText("{\"leaf\":null}");
        JsonNode? current = source.Fields["leaf"];
        source.Fields.Remove("leaf");
        for (int i = 0; i < nestedObjects; i++) current = new JsonObject { ["next"] = current };
        source.Fields["value"] = current;

        var result = Graph(Array.Empty<RuleDefinition>()).EvaluateInstance(source);

        if (admitted) Assert.False(result.IsSaveBlocked);
        else Assert.Equal(RuleEngineCodes.InputTooLarge, result.Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Reactive_value_capture_admits_json_null_but_instance_and_guard_require_objects()
    {
        var graph = Graph(new[] { Compute("c.value", "value", "{\"var\":\"a\"}") });
        graph.EvaluateInstance(Instance("{\"a\":1}"));

        var result = graph.Reevaluate("a", RuleInputValue.FromJsonText("null"));

        Assert.Equal(ValueState.Resolved, result.Values["field:value"].State);
        Assert.Null(result.Values["field:value"].Value);
        Assert.Throws<ArgumentException>(() => RuleInstance.FromJsonText("null"));
        Assert.Throws<ArgumentException>(() => RuleContextSnapshot.FromJsonText("null"));
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, false)]
    public void RuleInstance_json_text_depth_counts_containers_and_allows_a_scalar_at_the_deepest_level(int nestedArrays, bool admitted)
    {
        string value = new string('[', nestedArrays) + "null" + new string(']', nestedArrays);
        string json = "{\"x\":" + value + "}"; // root object + nested arrays = 64/65 containers.
        if (admitted) _ = RuleInstance.FromJsonText(json);
        else Assert.Throws<ArgumentException>(() => RuleInstance.FromJsonText(json));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("\"text\"")]
    [InlineData("true")]
    public void Initial_composed_depth_counts_containers_not_the_scalar_leaf(string leafJson)
    {
        RuleInstance Build(int arrays)
        {
            var source = RuleInstance.FromJsonText("{\"leaf\":" + leafJson + "}");
            JsonNode? value = source.Fields["leaf"];
            source.Fields.Clear();
            for (int i = 0; i < arrays; i++) value = new JsonArray(value);
            source.Fields["value"] = value;
            return source;
        }

        Assert.False(Graph([]).EvaluateInstance(Build(63)).IsSaveBlocked); // root + 63 arrays = 64 containers
        Assert.Equal(RuleEngineCodes.InputTooLarge,
            Graph([]).EvaluateInstance(Build(64)).Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Initial_composed_envelope_counts_escaped_member_quotes_and_utf8_keys_exactly()
    {
        var fieldNames = Enumerable.Range(0, 16).Select(i => "f" + i).Append("f\"é").ToArray();
        var rowNames = Enumerable.Range(0, 16).Select(i => "r" + i).Append("r\"é").ToArray();
        const string Section = "s\"é";
        const string RowId = "row\"é";

        string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        string Document(string payload) => "{" + string.Join(',', fieldNames.Select(name => Quote(name) + ":\"\""))
            + ",\"payload\":\"" + payload + "\"," + Quote(Section) + ":[{\"_id\":" + Quote(RowId) + ","
            + string.Join(',', rowNames.Select(name => Quote(name) + ":\"\"")) + "}]}";
        RuleInstance Compose(string payload)
        {
            var empty = RuleInstance.FromJsonText("{\"v\":\"\"}").Fields["v"];
            var payloadValue = RuleInstance.FromJsonText("{\"v\":\"" + payload + "\"}").Fields["v"];
            var instance = new RuleInstance();
            foreach (var name in fieldNames) instance.Fields[name] = empty;
            instance.Fields["payload"] = payloadValue;
            instance.Tables[Section] = [new RuleRow(RowId, rowNames.ToDictionary(name => name, _ => empty, StringComparer.Ordinal))];
            return instance;
        }

        var baseBytes = Encoding.UTF8.GetByteCount(Document(""));
        var atLimit = new string('a', RuleContextSnapshot.MaxUtf8Bytes - baseBytes);

        Assert.Equal(RuleContextSnapshot.MaxUtf8Bytes, Encoding.UTF8.GetByteCount(Document(atLimit)));
        Assert.False(Graph([]).EvaluateInstance(Compose(atLimit)).IsSaveBlocked);
        Assert.Equal(RuleEngineCodes.InputTooLarge,
            Graph([]).EvaluateInstance(Compose(atLimit + "a")).Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Reactive_value_capture_enforces_ascii_non_ascii_byte_and_node_boundaries()
    {
        string asciiAt = "\"" + new string('a', RuleContextSnapshot.MaxUtf8Bytes - 2) + "\"";
        string nonAsciiAt = "\"" + new string('é', (RuleContextSnapshot.MaxUtf8Bytes - 2) / 2) + "\"";
        _ = RuleInputValue.FromJsonText(asciiAt);
        _ = RuleInputValue.FromJsonText(nonAsciiAt);
        Assert.Throws<ArgumentException>(() => RuleInputValue.FromJsonText("\"" + new string('a', RuleContextSnapshot.MaxUtf8Bytes - 1) + "\""));
        Assert.Throws<ArgumentException>(() => RuleInputValue.FromJsonText("\"" + new string('é', ((RuleContextSnapshot.MaxUtf8Bytes - 2) / 2) + 1) + "\""));

        _ = RuleInputValue.FromJsonText("[" + string.Join(',', Enumerable.Repeat("null", RuleContextSnapshot.MaxNodes - 1)) + "]");
        Assert.Throws<ArgumentException>(() => RuleInputValue.FromJsonText("[" + string.Join(',', Enumerable.Repeat("null", RuleContextSnapshot.MaxNodes)) + "]"));
    }

    [Fact]
    public void Rejected_cumulative_new_section_row_does_not_mutate_instance_or_cached_outcomes()
    {
        string initial = "{" + string.Join(',', Enumerable.Range(0, RuleContextSnapshot.MaxNodes - 2)
            .Select(i => i == 0 ? "\"a\":1" : "\"f" + i + "\":null")) + "}";
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        var first = graph.EvaluateInstance(Instance(initial));

        var refused = graph.AddRow("new-section", new RuleRow("r1", new Dictionary<string, JsonNode?> { ["v"] = null }));

        Assert.True(refused.IsSaveBlocked);
        Assert.Equal(RuleEngineCodes.InputTooLarge, refused.Validations.Single().Validity!.Error!.Code);
        var after = graph.Reevaluate("a", RuleInputValue.FromJsonText("2"));
        Assert.Equal(3L, after.Values["field:b"].Value!.GetValue<long>());
        var stable = after.ByRule["c.b"];
        Assert.Same(stable, graph.Reevaluate("unused", RuleInputValue.FromJsonText("null")).ByRule["c.b"]);
    }

    [Fact]
    public void Rejected_cumulative_byte_growth_leaves_the_prior_graph_state_usable()
    {
        const string Prefix = "{\"a\":1,\"payload\":\"";
        const string Suffix = "\"}";
        int payloadBytes = RuleContextSnapshot.MaxUtf8Bytes - Encoding.UTF8.GetByteCount(Prefix + Suffix);
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        graph.EvaluateInstance(RuleInstance.FromJsonText(Prefix + new string('é', payloadBytes / 2) + Suffix));

        var refused = graph.Reevaluate("unused", RuleInputValue.FromJsonText("null"));

        Assert.Equal(RuleEngineCodes.InputTooLarge, refused.Validations.Single().Validity!.Error!.Code);
        Assert.Equal(3L, graph.Reevaluate("a", RuleInputValue.FromJsonText("2")).Values["field:b"].Value!.GetValue<long>());
    }

    [Fact]
    public void Returned_graph_value_cannot_poison_cached_json_with_a_converter()
    {
        var canaryPath = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "t589", "result-canary-" + Guid.NewGuid().ToString("N"));
        var graph = Graph(new[] { Compute("c.object", "object", "{\"if\":[true,{\"left\":1,\"right\":2},null]}") });
        var first = graph.EvaluateInstance(Instance("{\"a\":1}"));
        var exposed = first.Values["field:object"].Value!.AsObject();
        exposed["poison"] = JsonValue.Create(new WritingPayload(canaryPath));
        try
        {
            var next = graph.Reevaluate("unrelated", RuleInputValue.FromJsonText("null"));
            Assert.False(next.Values["field:object"].Value!.AsObject().ContainsKey("poison"));
            Assert.False(File.Exists(canaryPath));
        }
        finally
        {
            if (File.Exists(canaryPath)) File.Delete(canaryPath);
        }
    }

    [Fact]
    public void Guard_var_result_is_already_detached_from_its_captured_context()
    {
        var guard = new GuardEvaluator(new FixedClock(Clock), RuleEngineLimits.Default);
        var rule = RuleDefinitionFactory.Create("g.object", RuleTier.JsonLogic, RuleScope.Schema, "", "{\"var\":\"payload\"}", RuleActionKind.Compute);
        var context = RuleContextSnapshot.FromJsonText("{\"root\":{\"payload\":{\"left\":1,\"right\":2}}}");
        var first = guard.EvaluateValue(rule, context, RuleEvalScope.Root);
        first.Value!.AsObject()["poison"] = true;

        Assert.False(guard.EvaluateValue(rule, context, RuleEvalScope.Root).Value!.AsObject().ContainsKey("poison"));
    }

    [Fact]
    public void Reactive_member_names_over_the_input_envelope_are_refused_before_state_mutation()
    {
        var graph = Graph(new[] { Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}") });
        graph.EvaluateInstance(Instance("{\"a\":1}"));
        string oversized = new string('x', RuleContextSnapshot.MaxUtf8Bytes + 1);

        Assert.Equal(RuleEngineCodes.InputTooLarge, graph.Reevaluate(oversized, RuleInputValue.FromJsonText("null")).Validations.Single().Validity!.Error!.Code);
        Assert.Equal(2L, graph.Reevaluate("a", RuleInputValue.FromJsonText("1")).Values["field:b"].Value!.GetValue<long>());
    }

    [Fact]
    public void Initial_mutated_instance_over_the_total_node_envelope_is_refused()
    {
        var instance = Instance("{\"seed\":null}");
        var branded = instance.Fields["seed"];
        for (var i = 0; i < RuleContextSnapshot.MaxNodes; i++) instance.Fields["f" + i] = branded;

        var result = Graph([]).EvaluateInstance(instance);

        Assert.True(result.IsSaveBlocked);
        Assert.Equal(RuleEngineCodes.InputTooLarge, result.Validations.Single().Validity!.Error!.Code);
    }

    [Fact]
    public void Graph_captures_an_added_row_before_a_later_structural_evaluation()
    {
        var graph = Graph(new[] { Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}") });
        graph.EvaluateInstance(Instance("{\"items\":[{\"amount\":0}]}"));
        var row = new RuleRow("r1", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(2) });
        graph.AddRow("items", row);
        row.Fields["amount"] = JsonValue.Create(99);

        var result = graph.AddRow("items", new RuleRow("r2", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(0) }));

        Assert.Equal(2L, result.Values["agg:items/sum/amount"].Value!.GetValue<long>());
    }

    [Fact]
    public void Graph_does_not_retain_a_rejected_over_limit_reactive_row()
    {
        var limits = RuleEngineLimits.Default with { MaxTableRowsPerAggregate = 1 };
        var graph = Graph(new[] { Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}") }, limits);
        graph.EvaluateInstance(Instance("{\"items\":[{\"amount\":1}]}"));

        var rejected = graph.AddRow("items", new RuleRow("r2", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(2) }));
        Assert.Equal(ValueState.Error, rejected.Values["agg:items/sum/amount"].State);

        var after = graph.Reevaluate("unrelated", RuleInputValue.FromJsonText("null"));
        Assert.Equal(1L, after.Values["field:total"].Value!.GetValue<long>());
        Assert.Equal(1L, after.Values["agg:items/sum/amount"].Value!.GetValue<long>());
    }

    [Fact]
    public void Over_limit_unused_and_row_only_tables_are_accepted_not_silently_dropped()
    {
        var limits = RuleEngineLimits.Default with { MaxTableRowsPerAggregate = 1 };
        var graph = Graph(new[]
        {
            Compute("c.copy", "copy", "{\"var\":\"a\"}"),
            Compute("c.row", "rows/doubled", "{\"+\":[{\"var\":\"row.value\"},1]}", RuleScope.Row),
        }, limits);
        graph.EvaluateInstance(Instance("{\"a\":1,\"unused\":[{\"value\":1}],\"rows\":[{\"value\":1}]}"));

        var result = graph.AddRow("unused", new RuleRow("r2", new Dictionary<string, JsonNode?> { ["value"] = JsonValue.Create(2) }));
        var rowResult = graph.AddRow("rows", new RuleRow("r3", new Dictionary<string, JsonNode?> { ["value"] = JsonValue.Create(2) }));

        Assert.False(result.IsSaveBlocked);
        Assert.Equal(3L, rowResult.Values["row:rows/r3/doubled"].Value!.GetValue<long>());
    }

    [Fact]
    public void Table_too_large_fails_closed()
    {
        var limits = RuleEngineLimits.Default with { MaxTableRowsPerAggregate = 1 };
        var rule = Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}");
        var result = Graph(new[] { rule }, limits)
            .EvaluateInstance(Instance("{\"items\":[{\"amount\":1},{\"amount\":2}]}"));
        Assert.Equal(ValueState.Error, result.Values["agg:items/sum/amount"].State);
        Assert.Equal(RuleEngineCodes.TableTooLarge, result.Values["agg:items/sum/amount"].Error!.Code);
    }

    [Fact]
    public void Unregistrable_aggregate_is_a_publish_time_refusal_not_runtime_data()
    {
        // Ticket 162 review (150-family direction): arity-4 and expression-valued/non-string-arg
        // agg nodes used to skip reference extraction, register no fold cell, and then refuse
        // per-keystroke at runtime with no authoring-time signal — permanently un-saveable data.
        // The compiler refuses them instead, identically to the TS tier.
        foreach (var expression in new[]
        {
            "{\"agg\":[\"sum\",\"items\",\"amount\",\"extra\"]}", // arity 4
            "{\"agg\":[\"sum\",{\"var\":\"field.section\"},\"amount\"]}", // expression-valued arg
        })
        {
            var rule = Compute("c.dyn", "dyn", expression);
            var ex = Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(new[] { rule }));
            Assert.Equal(RuleEngineCodes.CompileBadGrammar, ex.Code);
        }
    }

    [Fact]
    public void Compiled_aggregate_the_context_cannot_provide_refuses_with_the_one_shared_shape()
    {
        // The guard tier's flat context bag carries no tables, so a well-formed agg reference is
        // unavailable data there: rule.bad_reference with params {agg: "section/fn/col"} — the
        // exact shape (and param order) the shared corpus pins byte-identically across tiers (ticket 162).
        var rule = Compute("g.total", "", "{\"var\":\"table.sum(items.amount)\"}", RuleScope.Schema);
        var value = new GuardEvaluator(clock: new FixedClock(Clock)).EvaluateValue(rule, RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?>()), RuleEvalScope.Root);
        Assert.Equal(ValueState.Error, value.State);
        Assert.Equal(RuleEngineCodes.BadReference, value.Error!.Code);
        Assert.Equal("items/sum/amount", value.Error!.Params["agg"]);
    }

    // ── numeric / collation determinism (D1 ratification fix 2) ───────────────

    [Fact]
    public void Money_avg_aggregate_fails_closed_no_inexact_decimal_division()
    {
        // Money math is exact-or-fail-closed: there is NO rounding mode. avg over a decimal-string
        // (money) column needs inexact decimal division (undefined in v1) → fail closed with a stable
        // code rather than silently falling back to IEEE-754 double. (Corpus proves the exact money
        // add/mul/sum byte-identity; this pins the deliberate avg refusal, inspecting the agg cell.)
        var rule = Compute("c.avg", "avg", "{\"var\":\"table.avg(items.price)\"}");
        var result = Graph(new[] { rule })
            .EvaluateInstance(Instance("{\"items\":[{\"price\":\"0.1\"},{\"price\":\"0.2\"}]}"));
        Assert.Equal(ValueState.Error, result.Values["agg:items/avg/price"].State);
        Assert.Equal(RuleEngineCodes.MoneyAggUnsupported, result.Values["agg:items/avg/price"].Error!.Code);
    }

    // ── reactive ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reevaluate_dirties_dependents_of_a_raw_field_and_preserves_others()
    {
        var rules = new[]
        {
            Compute("c.b", "b", "{\"+\":[{\"var\":\"a\"},1]}"),
            Compute("c.d", "d", "{\"+\":[{\"var\":\"x\"},1]}"),
        };
        var graph = Graph(rules);
        var first = graph.EvaluateInstance(Instance("{\"a\":10,\"x\":100}"));
        var dBefore = first.ByRule["c.d"];

        var next = graph.Reevaluate("a", RuleInputValue.FromJsonText("20"));

        Assert.Equal(21L, next.Values["field:b"].Value!.GetValue<long>());
        Assert.Same(dBefore, next.ByRule["c.d"]); // not re-evaluated — referentially unchanged
    }

    [Fact]
    public void AddRow_then_RemoveRow_relink_the_aggregate()
    {
        var rule = Compute("c.total", "total", "{\"var\":\"table.sum(items.amount)\"}");
        var graph = Graph(new[] { rule });
        graph.EvaluateInstance(Instance("{\"items\":[{\"_id\":\"r1\",\"amount\":10},{\"_id\":\"r2\",\"amount\":20}]}"));

        var added = graph.AddRow("items", new RuleRow("r3", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(5) }));
        Assert.Equal(35L, added.Values["agg:items/sum/amount"].Value!.GetValue<long>());

        var removed = graph.RemoveRow("items", "r1");
        Assert.Equal(25L, removed.Values["agg:items/sum/amount"].Value!.GetValue<long>());
    }

    // ── save gate (pending / error / validity) ───────────────────────────────

    [Fact]
    public void Pending_value_blocks_save_on_the_integrity_tier()
    {
        var rule = Compute("c.ship", "ship", "{\"cat\":[{\"var\":\"city\"},\"!\"]}");
        var result = Graph(new[] { rule }).EvaluateInstance(Instance("{\"city\":{\"@pending\":true}}"));
        Assert.True(result.HasPending);
        Assert.True(result.IsSaveBlocked);
    }

    [Fact]
    public void Failing_validation_blocks_save()
    {
        var rule = RuleDefinitionFactory.Create("v.pos", RuleTier.JsonLogic, RuleScope.Field, "qty",
            "{\">\":[{\"var\":\"qty\"},0]}", RuleActionKind.Validate);
        var result = Graph(new[] { rule }).EvaluateInstance(Instance("{\"qty\":-1}"));
        Assert.True(result.IsSaveBlocked);
        Assert.Single(result.Validations);
        Assert.Equal("v.pos", result.Validations[0].Validity!.Error!.Code);
    }

    [Fact]
    public void Broken_required_rule_produces_one_refusing_validity_outcome()
    {
        var rule = RuleDefinitionFactory.Create(
            "req.broken",
            RuleTier.JsonLogic,
            RuleScope.Field,
            "name",
            "{\"/\":[1,0]}",
            RuleActionKind.Required);

        var result = Graph(new[] { rule }).EvaluateInstance(new RuleInstance());

        var refusal = Assert.Single(result.Validations);
        Assert.Equal("req.broken", refusal.RuleId);
        Assert.Equal(RuleEngineCodes.DivByZero, refusal.Validity!.Error!.Code);
        Assert.Equal("req.broken", refusal.Validity.Error.Params["rule"]);
        Assert.True(result.IsSaveBlocked);
        Assert.False(result.Visibility.ContainsKey(CellAddress.Field("name").Key));
    }

    // ── visibility merge ──────────────────────────────────────────────────────

    [Fact]
    public void Visibility_outcomes_merge_per_cell()
    {
        var rules = new[]
        {
            RuleDefinitionFactory.Create("vis.panel", RuleTier.JsonLogic, RuleScope.Section, "panel",
                "{\"==\":[{\"var\":\"show\"},true]}", RuleActionKind.Visibility),
            RuleDefinitionFactory.Create("ro.panel", RuleTier.JsonLogic, RuleScope.Section, "panel",
                "{\"==\":[{\"var\":\"lock\"},true]}", RuleActionKind.ReadOnly),
        };
        var result = Graph(rules).EvaluateInstance(Instance("{\"show\":false,\"lock\":true}"));
        var merged = result.Visibility["section:panel"];
        Assert.False(merged.Visible);
        Assert.True(merged.ReadOnly);
    }

    // ── guard evaluator ───────────────────────────────────────────────────────

    [Fact]
    public void Guard_evaluator_passes_fails_and_computes()
    {
        var guard = new GuardEvaluator(new FixedClock(Clock), RuleEngineLimits.Default);
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);

        Assert.True(guard.EvaluateGuard(rule, RuleContextSnapshot.Capture(Bag("amount", 100)), RuleEvalScope.Root).Ok);
        var fail = guard.EvaluateGuard(rule, RuleContextSnapshot.Capture(Bag("amount", 10)), RuleEvalScope.Root);
        Assert.False(fail.Ok);
        Assert.Equal("g.min", fail.Error!.Code);

        var value = RuleDefinitionFactory.Create("g.fee", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\"money.mul\":[\"10\",\"3\"]}", RuleActionKind.Compute);
        Assert.Equal("30", guard.EvaluateValue(value, RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?>()), RuleEvalScope.Root).Value!.GetValue<string>());
    }

    [Fact]
    public void Guard_evaluator_fails_closed_on_pending()
    {
        var guard = new GuardEvaluator(new FixedClock(Clock), RuleEngineLimits.Default);
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);
        var bag = new Dictionary<string, JsonNode?> { ["amount"] = new JsonObject { ["@pending"] = true } };
        var v = guard.EvaluateGuard(rule, RuleContextSnapshot.Capture(bag), RuleEvalScope.Root);
        Assert.False(v.Ok);
        Assert.Equal(RuleEngineCodes.PendingAtSave, v.Error!.Code);
    }

    private static Dictionary<string, JsonNode?> Bag(string key, int value)
        => new() { [key] = JsonValue.Create(value) };

    private sealed class AdvancingClock(params DateTimeOffset[] instants) : TimeProvider
    {
        private int _next;
        public int Reads { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return instants[Math.Min(_next++, instants.Length - 1)];
        }
    }

    [JsonConverter(typeof(WritingPayloadConverter))]
    private sealed record WritingPayload(string Path);

    private sealed class WritingPayloadConverter : JsonConverter<WritingPayload>
    {
        public override WritingPayload? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }

        public override void Write(Utf8JsonWriter writer, WritingPayload value, JsonSerializerOptions options)
        {
            File.WriteAllText(value.Path, "converter-ran");
            writer.WriteStringValue("unsafe");
        }
    }
}
