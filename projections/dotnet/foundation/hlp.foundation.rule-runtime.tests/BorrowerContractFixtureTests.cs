using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.References;

using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>T-590 slice 5: the released borrower contract fixtures, replayed by the producer.</summary>
public sealed class BorrowerContractFixtureTests
{
    private static readonly JsonObject Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(FunctionRegisterTests.RepositoryRoot(),
        "conformance", "hlp.foundation.rule-runtime", "borrower-contracts.json")))!.AsObject();

    private static readonly GuardEvaluator Guard = new(new FixedClock(new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero)));

    private static RuleDefinition Rule(string id, JsonNode expression, RuleActionKind action = RuleActionKind.Validate)
        => RuleDefinitionFactory.Create(id, RuleTier.JsonLogic, RuleScope.Schema, "", expression.ToJsonString(), action);

    private static RuleContextSnapshot Context(JsonNode? node)
        => RuleContextSnapshot.Capture(node!.AsObject().ToDictionary(p => p.Key, p => p.Value?.DeepClone()));

    internal static BorrowerEnvironmentDeclaration Declaration(JsonObject d) => new(
        d["borrower"]!.GetValue<string>(), d["grammar"]!.GetValue<string>(),
        d["variables"]!.AsObject().ToDictionary(v => v.Key, v => v.Value!.GetValue<string>()),
        [.. d["operations"]!.AsArray().Select(op => op!.GetValue<string>())],
        [.. d["effects"]!.AsArray().Select(effect => effect!.GetValue<string>())],
        d["missingValues"]!.GetValue<string>(), d["timeSource"]!.GetValue<string>(), d["timeZone"]!.GetValue<string>(),
        d["phases"]!.AsObject().ToDictionary(p => Enum.Parse<EvaluationPhase>(p.Key), p => p.Value!.GetValue<bool>()),
        d["replay"]!.GetValue<string>());

    [Fact(DisplayName = "rules-eng-26, rules-ck-28: every borrow-grammar edge's released declaration is admitted, exports canonically with scope and phase exclusions intact, evaluates its admitted probe and refuses the one outside it")]
    public void Every_borrower_edge_has_a_passing_contract_probe()
    {
        var borrowers = Fixture["borrowers"]!.AsArray();
        Assert.Equal(14, borrowers.Count);
        foreach (var borrower in borrowers.Select(node => node!.AsObject()))
        {
            var edge = borrower["edge"]!.GetValue<string>();
            var declaration = Declaration(borrower["declaration"]!.AsObject());
            Assert.Equal(borrower["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(declaration));
            var environment = BorrowerEnvironmentAdmission.Admit(declaration);
            var admission = environment.For(Enum.Parse<EvaluationPhase>(borrower["admittedPhase"]!.GetValue<string>()));

            var admitted = borrower["admitted"]!.AsObject();
            Assert.True(Guard.EvaluateGuard(Rule("admitted", admitted["expression"]!), Context(admitted["context"]), RuleEvalScope.Root, admission).Ok, edge);

            var refused = borrower["refused"]!.AsObject();
            Assert.Equal(refused["code"]!.GetValue<string>(),
                Guard.EvaluateGuard(Rule("refused", refused["expression"]!), Context(new JsonObject()), RuleEvalScope.Root, admission).Error?.Code);

            Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
                () => environment.For(Enum.Parse<EvaluationPhase>(borrower["inapplicablePhase"]!.GetValue<string>()))).Code);
        }
    }

    [Fact(DisplayName = "rules-auth-13: the Rules-to-Records reference probe resolves a field path against the record instance without a copied schema")]
    public void Reference_probe_resolves_without_copied_schema()
    {
        var reference = Fixture["reference"]!.AsObject();
        var compiled = RuleCompiler.Compile([RuleDefinitionFactory.Create("ref", RuleTier.JsonLogic, RuleScope.Field, "out",
            reference["expression"]!.ToJsonString(), RuleActionKind.Compute)]);
        // The rule holds the path and nothing else: no copy of the record's schema travels with it.
        Assert.True(JsonNode.DeepEquals(reference["expression"], Assert.Single(compiled.LoweredAsts)));
        var result = new FormRuleGraph(compiled, new FixedClock(DateTimeOffset.UnixEpoch), TestAdmission.Any)
            .EvaluateInstance(RuleInstance.FromJson(reference["instance"]!.AsObject().DeepClone().AsObject()));
        Assert.Equal(reference["expected"]!.GetValue<long>(), result.Values["field:out"].Value!.GetValue<long>());
    }

    [Fact(DisplayName = "rules-ck-22, rules-ck-23, rules-eng-16: the released predicate, exact-calculation-pin and bounded-agg probes pass")]
    public void Predicate_calculation_and_agg_probes_pass()
    {
        var probes = Fixture["probes"]!.AsObject();

        var p = probes["predicate"]!.AsObject();
        var predicate = new NamedPredicate(p["name"]!.GetValue<string>(), p["version"]!.GetValue<string>(), p["expression"]!.ToJsonString());
        var closure = new PinnedClosure([predicate], []);
        foreach (var consumer in Enum.GetValues<PredicateConsumer>())
            Assert.Equal(p["expectedOk"]!.GetValue<bool>(), Guard.EvaluateGuard(NamedReferences.Bind(consumer, predicate.Pin, closure), Context(p["context"]), RuleEvalScope.Root, TestAdmission.Any).Ok);

        var c = probes["calculationPin"]!.AsObject();
        var calculation = new NamedCalculation(c["name"]!.GetValue<string>(), c["version"]!.GetValue<string>(), c["expression"]!.ToJsonString());
        Assert.Equal(c["expected"]!.GetValue<string>(), NamedReferences.Compute(calculation.Pin, new PinnedClosure([], [calculation]), Guard, Context(c["context"]), TestAdmission.Any).Value!.GetValue<string>());

        var a = probes["boundedAgg"]!.AsObject();
        var compiled = RuleCompiler.Compile([RuleDefinitionFactory.Create("agg", RuleTier.JsonLogic, RuleScope.Field, "out", a["admitted"]!.ToJsonString(), RuleActionKind.Compute)]);
        var value = new FormRuleGraph(compiled, new FixedClock(DateTimeOffset.UnixEpoch), TestAdmission.Any)
            .EvaluateInstance(RuleInstance.FromJson(a["instance"]!.AsObject().DeepClone().AsObject())).Values["field:out"];
        Assert.Equal(a["expected"]!.GetValue<long>(), value.Value!.GetValue<long>());
        foreach (var refused in a["refused"]!.AsArray())
            Assert.Equal(a["refusedCode"]!.GetValue<string>(), Assert.Throws<RuleCompilationException>(() => RuleCompiler.Compile(
                [RuleDefinitionFactory.Create("agg", RuleTier.JsonLogic, RuleScope.Field, "out", refused!.ToJsonString(), RuleActionKind.Compute)])).Code);
    }
}
