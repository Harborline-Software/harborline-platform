using Harborline.Contracts.Fields;
using Harborline.Contracts.Rules;
using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Environments;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

/// <summary>T-590 slice 4: value-domain resolvers are bound behind the pickers and filter through Rules.</summary>
public sealed class ValueDomainRulesBindingTests
{
    [Fact]
    public void T1232_A4_Value_domain_missing_keys_use_the_actual_narrow_declaration()
    {
        var guard = new GuardEvaluator(TimeProvider.System);
        var context = RuleContextSnapshot.FromJsonText("""{"root":{"active":true}}""");
        var admission = ValueDomainExpressionEnvironment.Admitted.For(EvaluationPhase.Render);
        var refused = new RuleDefinition { Id = "missing", Tier = RuleTier.JsonLogic, Scope = RuleScope.Field,
            ScopeTarget = "out", Expression = """{"missing":["row.x"]}""", Action = RuleActionKind.Compute };
        var permitted = refused with { Id = "plain", Expression = """{"var":"active"}""" };
        // Oracle: T-1232 acceptance 4 and the fixture's literal active=true subject field.
        Assert.Equal("rule.environment.variable_not_admitted", BorrowerEnvironmentAdmission.CheckCompiledGuard(RuleCompiler.Compile([refused]), ValueDomainExpressionEnvironment.Declaration, EvaluationPhase.Render));
        Assert.Equal("rule.environment.variable_not_admitted", guard.EvaluateValue(refused, context, RuleEvalScope.Root, admission).Error?.Code);
        Assert.True(guard.EvaluateValue(permitted, context, RuleEvalScope.Root, admission).Value!.GetValue<bool>());
    }

    [Fact(DisplayName = "rules-bound-7: the literal, record and taxonomy resolvers behind the pickers resolve, and a record predicate is evaluated by Rules under the value-domain borrower's admission")]
    public async Task Resolvers_resolve_and_record_predicates_run_through_admitted_rules()
    {
        var fixture = new DomainFixture();
        var scheme = new TaxonomySchemeReference("asset-class", "1");
        fixture.Schemes[scheme] = [DomainFixture.Member("pump"), DomainFixture.Member("valve")];
        fixture.Records["asset"] = [DomainFixture.Member("a1", """{"active":true}"""), DomainFixture.Member("a2", """{"active":false}""")];
        var runtime = fixture.Runtime();

        Assert.Equal(["low", "high"], (await runtime.ResolveAsync(new(LiteralValues: ["low", "high"]), DomainFixture.Scope, "/d")).Values);
        Assert.Equal(["pump", "valve"], (await runtime.ResolveAsync(new(TaxonomyScheme: scheme), DomainFixture.Scope, "/d")).Values);
        Assert.Equal(["a1"], (await runtime.ResolveAsync(new(RecordQuery: new("asset", """{"==":[{"var":"active"},true]}""")), DomainFixture.Scope, "/d")).Values);

        // The predicate borrows Rules through its own declaration, which lends no child-table fold.
        var declaration = ValueDomainExpressionEnvironment.Declaration;
        Assert.Equal("value-domain-predicate", declaration.Borrower);
        Assert.DoesNotContain("agg", declaration.Operations);
        var refused = await Assert.ThrowsAsync<FieldAdmissionException>(async () => await runtime.ResolveAsync(
            new(RecordQuery: new("asset", """{">":[{"var":"table.sum(parts.qty)"},0]}""")), DomainFixture.Scope, "/d"));
        Assert.Equal("field.value_domain_predicate_failed", Assert.Single(refused.Refusals).Code);
        Assert.True(ValueDomainExpressionEnvironment.Admitted.Declaration.Phases[EvaluationPhase.Render]);
    }
}
