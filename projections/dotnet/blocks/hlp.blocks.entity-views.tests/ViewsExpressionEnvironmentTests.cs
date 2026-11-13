using System.Text.Json.Nodes;
using System.Reflection;
using Harborline.Blocks.BuilderDefinitions;
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
        Assert.Equal(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Admitted.Declaration));
        Assert.NotEqual(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration with { TimeSource = "other" }));
    }
    [Fact]
    public void Views_ck_14_store_refuses_evidence_mutated_after_validator_creation()
    {
        var variables = new Dictionary<string, string>(ViewsExpressionEnvironment.Declaration.Variables);
        var environment = BorrowerEnvironmentAdmission.Admit(ViewsExpressionEnvironment.Declaration with { Variables = variables });
        var validate = ViewDefinitionCatalogue.Admission(ViewStores.Registers() with { Environment = environment });
        variables["clock"] = "other";
        Assert.Equal("evaluated-at", environment.Declaration.Variables["clock"]);
        Assert.Equal(BorrowerEnvironmentAdmission.CanonicalJson(ViewsExpressionEnvironment.Declaration), environment.CanonicalJson);
        typeof(AdmittedEnvironment).GetField("<CanonicalJson>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(environment, "invalid");
        var document = new DefinitionDocument(ViewDefinitionCatalogue.KeyOf("tenant", "view"), "1.0.0", "1.0.0", "{}");
        var error = Assert.Throws<BorrowerEnvironmentException>(() => validate(document, DefinitionAdmissionPhase.Author));
        Assert.Equal("rule.environment.not_admitted", error.Code);
    }
    [Fact]
    public void Views_ck_14_refuses_operations_added_after_admission()
    {
        var operations = new List<string>();
        var environment = BorrowerEnvironmentAdmission.Admit(ViewsExpressionEnvironment.Declaration with { Operations = operations });
        operations.AddRange(ViewsExpressionEnvironment.Declaration.Operations);
        var error = Assert.Throws<BorrowerEnvironmentException>(() => ViewDefinitionCatalogue.Admission(ViewStores.Registers() with { Environment = environment }));
        Assert.Equal("rule.environment.not_admitted", error.Code);
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

    internal static AdmittedEnvironment OtherBorrower() => BorrowerEnvironmentAdmission.Admit(
        ViewsExpressionEnvironment.Declaration with { Borrower = "other" });

    internal static AdmittedEnvironment OtherDeclaration() => BorrowerEnvironmentAdmission.Admit(
        ViewsExpressionEnvironment.Declaration with { TimeSource = "other" });

    internal static EvaluationAdmission InapplicablePhase()
    {
        return (EvaluationAdmission)Activator.CreateInstance(typeof(EvaluationAdmission), BindingFlags.NonPublic | BindingFlags.Instance,
            null, [ViewsExpressionEnvironment.Admitted, EvaluationPhase.Submission], null)!;
    }

    [Fact]
    public async Task Views_ck_14_row_and_function_refuse_mismatched_evidence_and_phase()
    {
        var functions = new ViewExpressionFunctionRegistry();
        foreach (var (evidence, code) in new[]
        {
            (OtherBorrower().For(EvaluationPhase.Run), "rule.environment.not_admitted"),
            (OtherDeclaration().For(EvaluationPhase.Run), "rule.environment.not_admitted"),
            (InapplicablePhase(), "rule.environment.phase_not_admitted"),
        })
        {
            Assert.Equal(code, Assert.Throws<BorrowerEnvironmentException>(() => functions.Evaluate("contains", ["a", "a"], evidence)).Code);
            var plan = new ViewQueryPlan("tenant", "record", new Dictionary<string, ViewRecordFieldKind>(), [],
                [], [], null, null, new(0, 10), DateTimeOffset.UnixEpoch, evidence);
            var error = await Assert.ThrowsAsync<BorrowerEnvironmentException>(async () => await new InMemoryViewRowSource([]).QueryAsync(plan));
            Assert.Equal(code, error.Code);
        }
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
