using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
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
        => new(RuleCompiler.Compile(rules, limits), limits, new FixedClock(Clock));

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

    // ── static bounds ─────────────────────────────────────────────────────────

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
        var guard = new GuardEvaluator(RuleEngineLimits.Default, new FixedClock(Clock));
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);
        Assert.Throws<RuleEngineTimeoutException>(() => guard.EvaluateGuard(rule, Bag("amount", 100), cts.Token));
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
        var value = new GuardEvaluator(clock: new FixedClock(Clock)).EvaluateValue(rule, new Dictionary<string, JsonNode?>());
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

        var next = graph.Reevaluate("a", JsonValue.Create(20));

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
        var guard = new GuardEvaluator(RuleEngineLimits.Default, new FixedClock(Clock));
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);

        Assert.True(guard.EvaluateGuard(rule, Bag("amount", 100)).Ok);
        var fail = guard.EvaluateGuard(rule, Bag("amount", 10));
        Assert.False(fail.Ok);
        Assert.Equal("g.min", fail.Error!.Code);

        var value = RuleDefinitionFactory.Create("g.fee", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\"money.mul\":[\"10\",\"3\"]}", RuleActionKind.Compute);
        Assert.Equal("30", guard.EvaluateValue(value, new Dictionary<string, JsonNode?>()).Value!.GetValue<string>());
    }

    [Fact]
    public void Guard_evaluator_fails_closed_on_pending()
    {
        var guard = new GuardEvaluator(RuleEngineLimits.Default, new FixedClock(Clock));
        var rule = RuleDefinitionFactory.Create("g.min", RuleTier.JsonLogic, RuleScope.Schema, "",
            "{\">\":[{\"var\":\"amount\"},50]}", RuleActionKind.Validate);
        var bag = new Dictionary<string, JsonNode?> { ["amount"] = new JsonObject { ["@pending"] = true } };
        var v = guard.EvaluateGuard(rule, bag);
        Assert.False(v.Ok);
        Assert.Equal(RuleEngineCodes.PendingAtSave, v.Error!.Code);
    }

    private static Dictionary<string, JsonNode?> Bag(string key, int value)
        => new() { [key] = JsonValue.Create(value) };
}
