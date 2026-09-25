using Harborline.Contracts.Fields;
using Harborline.Foundation.RuleEngine.Environments;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

/// <summary>T-590 slice 4: value-domain resolvers are bound behind the pickers and filter through Rules.</summary>
public sealed class ValueDomainRulesBindingTests
{
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
