using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine;
using Harborline.Foundation.RuleEngine.Context;
using Harborline.Foundation.RuleEngine.Environments;

using Xunit;

namespace Harborline.Foundation.Documents.Tests;

/// <summary>T-593 item 4 (owner ruling Q10): Documents declares its environment through Rules' shared type.</summary>
public sealed class DocumentsExpressionEnvironmentTests
{
    private static readonly GuardEvaluator Evaluator = new(TimeProvider.System);

    [Fact(DisplayName = "documents-ck-25: the typed declaration matches Rules' released borrower fixture and survives canonical export")]
    public void DeclarationMatchesTheReleasedBorrowerContract()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "borrower-contracts.json")))!["borrowers"]!
            .AsArray().Single(entry => entry!["declaration"]?["borrower"]?.GetValue<string>() == "documents-ck-25")!;

        Assert.Equal(fixture["canonical"]!.GetValue<string>(), BorrowerEnvironmentAdmission.CanonicalJson(DocumentsExpressionEnvironment.Declaration));
        Assert.Same(DocumentsExpressionEnvironment.Declaration, DocumentsExpressionEnvironment.Admitted.Declaration);
    }

    [Fact(DisplayName = "documents-ck-25: a document guard evaluates only under the admitted environment; an undeclared variable, an inapplicable phase or an unauthorized declaration refuses")]
    public void GuardEvaluatesOnlyUnderTheAdmittedEnvironment()
    {
        var open = RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?> { ["status"] = "open" });
        var closed = RuleContextSnapshot.Capture(new Dictionary<string, JsonNode?> { ["status"] = "closed" });
        const string guard = """{"==":[{"var":"field.status"},"open"]}""";

        Assert.True(DocumentsExpressionEnvironment.EvaluateGuard(Evaluator, "notes", guard, open, EvaluationPhase.Render).Ok);
        Assert.False(DocumentsExpressionEnvironment.EvaluateGuard(Evaluator, "notes", guard, closed, EvaluationPhase.Render).Ok);

        var undeclared = DocumentsExpressionEnvironment.EvaluateGuard(
            Evaluator, "notes", """{"==":[{"var":"wf.state"},"open"]}""", open, EvaluationPhase.Render);
        Assert.Equal(BorrowerEnvironmentAdmission.VariableNotAdmitted, undeclared.Error?.Code);

        Assert.Equal(BorrowerEnvironmentAdmission.PhaseNotAdmitted, Assert.Throws<BorrowerEnvironmentException>(
            () => DocumentsExpressionEnvironment.EvaluateGuard(Evaluator, "notes", guard, open, EvaluationPhase.Submission)).Code);
        Assert.Equal(BorrowerEnvironmentAdmission.DeclarationRefused, Assert.Throws<BorrowerEnvironmentException>(
            () => BorrowerEnvironmentAdmission.Admit(DocumentsExpressionEnvironment.Declaration with
            {
                Effects = [BorrowerEnvironmentAdmission.FieldRead, "network"],
            })).Code);
    }
}
