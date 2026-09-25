using System.Reflection;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Functions;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.References;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-590 slice 3: named predicates, exact calculation pins, legality citations and the bounded fold.</summary>
public sealed class NamedReferenceTests
{
    private static readonly GuardEvaluator Guard = new(new FixedClock(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));

    private static RuleContextSnapshot Facts(string json)
        => RuleContextSnapshot.Capture(JsonNode.Parse(json)!.AsObject().ToDictionary(p => p.Key, p => p.Value?.DeepClone()));

    [Fact(DisplayName = "rules-ck-22, rules-auth-9: one immutable named predicate is authored once and reused by a view filter, a rule condition, an automation condition, a report population and a Layout guard through its exact pin")]
    public void Named_predicate_is_authored_once_and_reused_by_every_consumer_kind()
    {
        var v1 = new NamedPredicate("overdue", "1.0.0", """{">":[{"var":"days_late"},30]}""");
        var v2 = new NamedPredicate("overdue", "2.0.0", """{">":[{"var":"days_late"},60]}""");
        var closure = new PinnedClosure([v1, v2], []);

        foreach (var consumer in Enum.GetValues<PredicateConsumer>())
        {
            var rule = NamedReferences.Bind(consumer, v1.Pin, closure);
            Assert.Equal(v1.Expression, rule.Expression);
            Assert.True(Guard.EvaluateGuard(rule, Facts("""{"days_late":45}"""), RuleEvalScope.Root, TestAdmission.Any).Ok, consumer.ToString());
        }
        // A new version does not move a consumer that pinned the old one.
        Assert.False(Guard.EvaluateGuard(NamedReferences.Bind(PredicateConsumer.ViewFilter, v2.Pin, closure), Facts("""{"days_late":45}"""), RuleEvalScope.Root, TestAdmission.Any).Ok);
        Assert.NotEqual(v1.Pin.Digest, v2.Pin.Digest);

        // Resolution is from the pinned closure only: absent, tampered or floating pins refuse.
        Assert.Equal(NamedReferences.Unresolved, Assert.Throws<NamedReferenceException>(() => NamedReferences.Bind(PredicateConsumer.RuleCondition, v1.Pin, new PinnedClosure([v2], []))).Code);
        Assert.Equal(NamedReferences.DigestMismatch, Assert.Throws<NamedReferenceException>(() => NamedReferences.Bind(PredicateConsumer.RuleCondition, v1.Pin with { Digest = v2.Pin.Digest }, closure)).Code);
        Assert.Equal(NamedReferences.Floating, Assert.Throws<NamedReferenceException>(() => NamedReferences.Bind(PredicateConsumer.RuleCondition, v1.Pin with { Version = "latest" }, closure)).Code);
        Assert.Equal(NamedReferences.Floating, Assert.Throws<NamedReferenceException>(() => new NamedPredicate("overdue", "^1.0", "true")).Code);
        // The element is immutable: no settable member.
        Assert.DoesNotContain(typeof(NamedPredicate).GetProperties(), p => p.SetMethod is { IsPublic: true });
    }

    [Fact(DisplayName = "rules-ck-23, rules-auth-14: Compute is a pure call through an exact named calculation pin that returns a typed value and writes nothing")]
    public void Compute_is_a_pure_call_through_an_exact_pin()
    {
        var calculation = new NamedCalculation("line-total", "1.0.0", """{"money.mul":[{"var":"price"},{"var":"qty"}]}""");
        var closure = new PinnedClosure([], [calculation]);
        var facts = new Dictionary<string, JsonNode?> { ["price"] = "2.50", ["qty"] = 4 };
        var snapshot = RuleContextSnapshot.Capture(facts);

        var first = NamedReferences.Compute(calculation.Pin, closure, Guard, snapshot, TestAdmission.Any);
        var second = NamedReferences.Compute(calculation.Pin, closure, Guard, snapshot, TestAdmission.Any);
        Assert.Equal("10", first.Value!.GetValue<string>());
        Assert.Equal(first.Value!.ToJsonString(), second.Value!.ToJsonString());
        Assert.Equal(2, facts.Count); // the caller's data is untouched

        Assert.Equal(NamedReferences.Floating, Assert.Throws<NamedReferenceException>(() => NamedReferences.Compute(calculation.Pin with { Version = "*" }, closure, Guard, snapshot, TestAdmission.Any)).Code);
        Assert.Equal(NamedReferences.DigestMismatch, Assert.Throws<NamedReferenceException>(() => NamedReferences.Compute(calculation.Pin with { Digest = new string('0', 64) }, closure, Guard, snapshot, TestAdmission.Any)).Code);
        // There is no inline form: the only way in is a pin.
        Assert.All(typeof(NamedReferences).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Compute"),
            method => Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(string)));
    }

    [Fact(DisplayName = "rules-ck-24, rules-auth-15: a legality rule set is cited by exact pin with provenance, jurisdiction and dates preserved, and never interpreted")]
    public void Legality_rule_set_is_cited_never_interpreted()
    {
        var digest = new string('a', 64);
        var citation = new LegalityRuleSetCitation("pack.payroll-uk", "minimum-wage", "2026.1.0", digest, "GB", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        Assert.Equal("""{"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","effectiveFrom":"2026-04-01","effectiveTo":"2027-03-31","jurisdiction":"GB","name":"minimum-wage","packageId":"pack.payroll-uk","version":"2026.1.0"}""", citation.CanonicalJson);

        // No body, no expression, no evaluate: Rules cannot interpret what it only cites.
        Assert.DoesNotContain(typeof(LegalityRuleSetCitation).GetMembers(BindingFlags.Public | BindingFlags.Instance),
            m => m.Name.Contains("Expression", StringComparison.Ordinal) || m.Name.Contains("Evaluate", StringComparison.Ordinal) || m.Name.Contains("Body", StringComparison.Ordinal));
        Assert.Throws<NamedReferenceException>(() => new LegalityRuleSetCitation("pack", "minimum-wage", "latest", digest, "GB", new DateOnly(2026, 4, 1), null));
        Assert.Throws<NamedReferenceException>(() => new LegalityRuleSetCitation("pack", "minimum-wage", "1.0.0", digest, " ", new DateOnly(2026, 4, 1), null));
        Assert.Throws<NamedReferenceException>(() => new LegalityRuleSetCitation("pack", "minimum-wage", "1.0.0", digest, "GB", new DateOnly(2026, 4, 1), new DateOnly(2026, 3, 1)));
    }

    [Fact(DisplayName = "rules-eng-16, rules-bound-4: agg folds only a named bounded child collection with a registered fold, and every registered fold executes")]
    public void Agg_is_a_bounded_fold_over_a_named_child_collection()
    {
        var instance = RuleInstance.FromJson(JsonNode.Parse("""{"items":[{"id":"a","amount":2,"ok":true},{"id":"b","amount":5,"ok":false}]}""")!.AsObject());
        foreach (var fold in BuiltInFunctionRegister.AggregateFolds)
        {
            var column = fold is "any" or "all" ? "ok" : "amount";
            var compiled = RuleCompiler.Compile([RuleDefinitionFactory.Create("f", RuleTier.JsonLogic, RuleScope.Field, "out", $$"""{"var":"table.{{fold}}(items.{{column}})"}""", RuleActionKind.Compute)]);
            var value = new FormRuleGraph(compiled, new FixedClock(DateTimeOffset.UnixEpoch), TestAdmission.Any).EvaluateInstance(instance).Values["field:out"];
            Assert.Equal(Model.ValueState.Resolved, value.State);
        }
        // An unregistered fold, a dynamic collection or a non-literal fold is not a bounded fold.
        foreach (var expression in new[] { """{"agg":["median","items","amount"]}""", """{"agg":["search","items","amount"]}""", """{"agg":["sum",{"var":"field.section"},"amount"]}""", """{"var":"table.lookup(items.amount)"}""" })
            Assert.Equal(RuleEngineCodes.CompileBadGrammar, Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(
                [RuleDefinitionFactory.Create("f", RuleTier.JsonLogic, RuleScope.Field, "out", expression, RuleActionKind.Compute)])).Code);
    }

    [Fact(DisplayName = "rules-auth-27, rules-auth-28: search, query, interval overlap, taxonomy traversal, catalogue-measure calculation and unbounded folds refuse at publish")]
    public void Search_query_taxonomy_and_measure_calculation_refuse()
    {
        string[] refused =
        [
            """{"search":["assets","pump"]}""", """{"query":["assets",{"==":[{"var":"kind"},"pump"]}]}""",
            """{"interval.overlaps":[{"var":"a"},{"var":"b"}]}""", """{"taxonomy.subsumes":["asset-class","rotating","pump"]}""",
            """{"taxonomy.succeeds":["status","open","closed"]}""", """{"coding.descendant":[{"var":"c"},"sys","parent"]}""",
            """{"measure":["asset.condition-score"]}""", """{"reduce":[{"var":"items"},{"+":[1,1]},0]}""",
            """{"filter":[{"var":"items"},true]}""", """{"map":[{"var":"items"},1]}""",
        ];
        foreach (var expression in refused)
            Assert.Equal(RuleEngineCodes.CompileInvalidExpression, Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(
                [RuleDefinitionFactory.Create("r", RuleTier.JsonLogic, RuleScope.Field, "out", expression, RuleActionKind.Compute)])).Code);

        // coding.is is exact membership: it never matches a descendant, so no subsumption hides in it.
        var coded = RuleDefinitionFactory.Create("c", RuleTier.JsonLogic, RuleScope.Schema, "", """{"coding.is":[{"var":"c"},"asset-class","rotating"]}""", RuleActionKind.Validate);
        Assert.True(Guard.EvaluateGuard(coded, Facts("""{"c":{"system":"asset-class","code":"rotating"}}"""), RuleEvalScope.Root, TestAdmission.Any).Ok);
        Assert.False(Guard.EvaluateGuard(coded, Facts("""{"c":{"system":"asset-class","code":"pump"}}"""), RuleEvalScope.Root, TestAdmission.Any).Ok);

        // The engine assembly cannot reach a catalogue or a search: it references only the contracts.
        Assert.Equal(["Harborline.Contracts", "System.Runtime"], typeof(GuardEvaluator).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!).Where(n => n.StartsWith("Harborline", StringComparison.Ordinal) || n == "System.Runtime").Order(StringComparer.Ordinal));
    }

    [Fact(DisplayName = "rules-eng-16: the asset-scoring decision table (ADR 0096 ruling 5c) folds supplied measure results already in scope and never calls the catalogue")]
    public void Asset_scoring_decision_table_reads_supplied_measure_values()
    {
        var table = new DecisionTableSkin("asset.score", RuleScope.Field, "score", RuleActionKind.Compute, HitPolicy.Priority,
            ["field.measure_condition_score", "field.measure_criticality"],
            [
                new DecisionRow([DecisionCell.Range(JsonValue.Create(0), JsonValue.Create(40)), DecisionCell.Compare(">=", JsonValue.Create(3))], JsonValue.Create("replace"), 10),
                new DecisionRow([DecisionCell.Range(JsonValue.Create(0), JsonValue.Create(70)), DecisionCell.Wildcard], JsonValue.Create("inspect"), 5),
            ],
            NoMatch.WithDefault(JsonValue.Create("monitor")));
        var rule = DecisionTableCompiler.Compile(table);
        var graph = new FormRuleGraph(RuleCompiler.Compile([rule]), new FixedClock(DateTimeOffset.UnixEpoch), TestAdmission.Any);
        string Score(string json) => graph.EvaluateInstance(RuleInstance.FromJson(JsonNode.Parse(json)!.AsObject())).Values["field:score"].Value!.GetValue<string>();

        Assert.Equal("replace", Score("""{"measure_condition_score":35,"measure_criticality":4}"""));
        Assert.Equal("inspect", Score("""{"measure_condition_score":35,"measure_criticality":1}"""));
        Assert.Equal("monitor", Score("""{"measure_condition_score":90,"measure_criticality":5}"""));
    }
}
