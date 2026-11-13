using System.Text.Json.Nodes;
using Harborline.Blocks.EntityViews;
using Harborline.Foundation.RuleEngine.Environments;
using Xunit;

namespace Harborline.Blocks.EntityViews.Tests;

public sealed class ViewsExpressionEnvironmentTests
{
    [Fact]
    public void Views_ck_14_declaration_matches_the_released_borrower_contract()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
            .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "views-ck-14")!;
        Assert.Equal(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration));
        Assert.Same(ViewsExpressionEnvironment.Declaration, ViewsExpressionEnvironment.Admitted.Declaration);
        Assert.NotEqual(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration with { TimeSource = "other" }));
    }
    [Fact]
    public void Views_ck_14_refuses_another_borrower_and_inapplicable_phases()
    {
        var other = BorrowerEnvironmentAdmission.Admit(ViewsExpressionEnvironment.Declaration with { Borrower = "other" });
        var error = Assert.Throws<BorrowerEnvironmentException>(() => ViewDefinitionCatalogue.Admission(ViewStores.Registers() with { Environment = other }));
        Assert.Equal("rule.environment.not_admitted", error.Code);
        foreach (var phase in new[] { EvaluationPhase.AuthoringValidation, EvaluationPhase.PublishValidation, EvaluationPhase.Submission, EvaluationPhase.SignOff })
            Assert.Equal("rule.environment.phase_not_admitted", Assert.Throws<BorrowerEnvironmentException>(() => ViewsExpressionEnvironment.Admitted.For(phase)).Code);
        Assert.Equal(EvaluationPhase.Render, ViewsExpressionEnvironment.Admitted.For(EvaluationPhase.Render).Phase);
        Assert.Equal(EvaluationPhase.Run, ViewsExpressionEnvironment.Admitted.For(EvaluationPhase.Run).Phase);
    }

    [Fact]
    public async Task Views_ck_14_row_and_function_evaluation_refuse_missing_environment()
    {
        var functions = new ViewExpressionFunctionRegistry();
        var functionError = Assert.Throws<BorrowerEnvironmentException>(() => functions.Evaluate("contains", ["a", "a"], null));
        Assert.Equal("rule.environment.not_admitted", functionError.Code);
        var plan = new ViewQueryPlan("tenant", "record", new Dictionary<string, ViewRecordFieldKind>(), [],
            [new(ViewPredicateSource.Authored, ViewFilter.Equal("name", "a"))], [], null, null, new(0, 10), DateTimeOffset.UnixEpoch);
        var rowError = await Assert.ThrowsAsync<BorrowerEnvironmentException>(async () => await new InMemoryViewRowSource([]).QueryAsync(plan));
        Assert.Equal("rule.environment.not_admitted", rowError.Code);
        var storeError = Assert.Throws<BorrowerEnvironmentException>(() => ViewDefinitionCatalogue.Admission(ViewStores.Registers() with { Environment = null }));
        Assert.Equal("rule.environment.not_admitted", storeError.Code);
    }
}
