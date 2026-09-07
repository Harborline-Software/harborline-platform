using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Explain;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// ADR 0146 D10 — interactive explainability traces (board F2). Asserts: a trace is emitted on the
/// interactive path and absent unless requested (batch = traceless); the authority filter darkens field
/// references (redact / shredded-subject hash); and — the load-bearing invariant — a field VALUE is NEVER
/// present in any trace param (the trace builder cannot access a resolved value).
/// </summary>
public sealed class RuleTraceTests
{
    // A Visibility rule that HIDES the comp section for high earners: visible iff salary <= 100000.
    private static RuleDefinition HideHighEarnerComp()  => RuleDefinitionFactory.Create(
        Id: "vis.comp",
        Tier: RuleTier.JsonLogic,
        Scope: RuleScope.Section,
        ScopeTarget: "comp",
        Expression: """{ "<=": [ { "var": "salary" }, 100000 ] }""",
        Action: RuleActionKind.Visibility);

    private static (CompiledGraph, RuleEvaluationResult) Evaluate(RuleDefinition rule, JsonObject instance)
    {
        var compiled = RuleCompiler.Compile(new[] { rule });
        var graph = new FormRuleGraph(compiled, RuleEngineLimits.Default, TimeProvider.System);
        var result = graph.EvaluateInstance(RuleInstance.FromJson(instance));
        return (compiled, result);
    }

    [Fact]
    public void InteractivePath_EmitsTrace_BatchPathTraceless()
    {
        var (compiled, result) = Evaluate(HideHighEarnerComp(), new JsonObject { ["salary"] = 150000 });

        // Batch path: the evaluation itself carries no trace — the result is the same object either way.
        // Interactive path: the caller opts in by building the trace (a pure projection over the result).
        var trace = RuleTraceBuilder.BuildForm(compiled, result);
        Assert.NotEmpty(trace);
        Assert.Single(trace);
        Assert.Equal("vis.comp", trace[0].RuleId);
    }

    [Fact]
    public void HighEarner_IsHidden_TraceCarriesCodeAndFieldRef_NeverTheValue()
    {
        var (compiled, result) = Evaluate(HideHighEarnerComp(), new JsonObject { ["salary"] = 150000 });
        var entry = RuleTraceBuilder.BuildForm(compiled, result).Single();

        Assert.Equal(RuleTraceCodes.Hidden, entry.Code);        // "hidden because of the salary threshold"…
        Assert.Equal("salary", entry.Params["reads"]);          // …with the field REFERENCE…
        Assert.Equal("section:comp", entry.Target);

        // …and NEVER the value (board F2). The salary 150000 (and the threshold literal 100000) appear in
        // no trace param — the builder has no access to a resolved value.
        foreach (var (_, v) in entry.Params)
        {
            Assert.DoesNotContain("150000", v);
            Assert.DoesNotContain("100000", v);
        }
    }

    [Fact]
    public void AuthorityFilter_RedactsUnreadableFieldReference()
    {
        var (compiled, result) = Evaluate(HideHighEarnerComp(), new JsonObject { ["salary"] = 150000 });
        var entry = RuleTraceBuilder.BuildForm(compiled, result, new DenyFilter("salary")).Single();

        Assert.Equal(RuleTraceCodes.Hidden, entry.Code);
        Assert.Equal("[redacted]", entry.Params["reads"]); // the field ref itself is withheld from an unauthorized viewer
    }

    [Fact]
    public void AuthorityFilter_ShreddedSubject_DarkensReferenceToHash()
    {
        var (compiled, result) = Evaluate(HideHighEarnerComp(), new JsonObject { ["salary"] = 150000 });
        var entry = RuleTraceBuilder.BuildForm(compiled, result, new ShredFilter("salary")).Single();

        Assert.StartsWith("#", entry.Params["reads"]);            // reference+hash form (board F2)
        Assert.Equal("#" + Fnv1aRef("salary"), entry.Params["reads"]);
        // Cross-tier anchor: the SAME literal is pinned in the TS tier's trace test — byte-identical hashing.
        Assert.Equal("#8561b279", entry.Params["reads"]);
        Assert.DoesNotContain("salary", entry.Params["reads"]);   // the bare name no longer appears
    }

    [Fact]
    public void ValidationFailure_TraceCarriesCauseCode()
    {
        var rule = RuleDefinitionFactory.Create("v.qty", RuleTier.JsonLogic, RuleScope.Field, "qty",
            """{ ">": [ { "var": "qty" }, 0 ] }""", RuleActionKind.Validate);
        var (compiled, result) = Evaluate(rule, new JsonObject { ["qty"] = -3 });
        var entry = RuleTraceBuilder.BuildForm(compiled, result).Single();

        Assert.Equal(RuleTraceCodes.ValidationFailed, entry.Code);
        Assert.Equal("v.qty", entry.Params["cause"]);          // the rule's own localizable code
        Assert.Equal("qty", entry.Params["reads"]);
        Assert.DoesNotContain("-3", string.Join("|", entry.Params.Values)); // never the value
    }

    [Fact]
    public void ComputeValue_TraceCarriesComputedCode_NotTheComputedValue()
    {
        var rule = RuleDefinitionFactory.Create("c.total", RuleTier.JsonLogic, RuleScope.Field, "total",
            """{ "+": [ { "var": "qty" }, { "var": "extra" } ] }""", RuleActionKind.Compute);
        var (compiled, result) = Evaluate(rule, new JsonObject { ["qty"] = 3, ["extra"] = 4 });
        var entry = RuleTraceBuilder.BuildForm(compiled, result).Single();

        Assert.Equal(RuleTraceCodes.ValueComputed, entry.Code);
        Assert.Equal("extra,qty", string.Join(",", entry.Params["reads"].Split(',').OrderBy(x => x))); // both inputs referenced
        Assert.DoesNotContain("7", string.Join("|", entry.Params.Values)); // the computed value (7) is not in the trace
    }

    [Fact]
    public void GuardTrace_PassedAndFailed_CarryCodeAndFieldRef()
    {
        var guard = RuleDefinitionFactory.Create("g.amount", RuleTier.JsonLogic, RuleScope.Schema, "",
            """{ ">": [ { "var": "amount" }, 5000 ] }""", RuleActionKind.Validate);
        var evaluator = new GuardEvaluator();

        var pass = evaluator.EvaluateGuard(guard, new Dictionary<string, JsonNode?> { ["amount"] = 7000 });
        var passTrace = RuleTraceBuilder.BuildGuard(guard, pass);
        Assert.Equal(RuleTraceCodes.GuardPassed, passTrace.Code);
        Assert.Equal("amount", passTrace.Params["reads"]);
        Assert.Equal("guard:g.amount", passTrace.Target);

        var fail = evaluator.EvaluateGuard(guard, new Dictionary<string, JsonNode?> { ["amount"] = 3000 });
        var failTrace = RuleTraceBuilder.BuildGuard(guard, fail);
        Assert.Equal(RuleTraceCodes.GuardFailed, failTrace.Code);
        Assert.Equal("g.amount", failTrace.Params["cause"]);
        Assert.DoesNotContain("3000", string.Join("|", failTrace.Params.Values)); // never the value
    }

    private static string Fnv1aRef(string s)
    {
        const uint offset = 2166136261, prime = 16777619;
        uint h = offset;
        foreach (char c in s) { h ^= (byte)(c & 0xFF); h *= prime; h ^= (byte)((c >> 8) & 0xFF); h *= prime; }
        return h.ToString("x8");
    }

    private sealed class DenyFilter(string field) : ITraceAuthorityFilter
    {
        public TraceFieldDisclosure Disclose(string fieldName)
            => fieldName == field ? TraceFieldDisclosure.Redact : TraceFieldDisclosure.Show;
    }

    private sealed class ShredFilter(string field) : ITraceAuthorityFilter
    {
        public TraceFieldDisclosure Disclose(string fieldName)
            => fieldName == field ? TraceFieldDisclosure.Hash : TraceFieldDisclosure.Show;
    }
}
