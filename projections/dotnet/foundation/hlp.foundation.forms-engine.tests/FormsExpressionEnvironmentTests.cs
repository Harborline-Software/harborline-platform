using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Graph;

using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>T-590 slice 2: the concrete Forms callers present Forms' own admitted declaration.</summary>
public sealed class FormsExpressionEnvironmentTests
{
    [Fact(DisplayName = "rules-eng-26, rules-ck-28: Forms' declaration (forms-ck-13) is admitted for render and submission and its unadmitted counterparts refuse")]
    public void Forms_declaration_is_admitted_and_its_counterparts_refuse()
    {
        var declaration = FormsExpressionEnvironment.Declaration;
        Assert.Equal("forms-ck-13", declaration.Borrower);
        Assert.Equal([BorrowerEnvironmentAdmission.FieldRead], declaration.Effects);
        Assert.Equal(["caller", "candidate", "clock", "field", "record_type", "row"], declaration.Variables.Keys.Order(StringComparer.Ordinal));

        var rule = new Harborline.Contracts.Forms.RuleDefinition
        {
            Id = "t", Tier = Harborline.Contracts.Forms.RuleTier.JsonLogic, Scope = Harborline.Contracts.Forms.RuleScope.Field,
            ScopeTarget = "total", Expression = """{"+":[{"var":"a"},1]}""", Action = Harborline.Contracts.Forms.RuleActionKind.Compute,
        };
        var compiled = RuleCompiler.Compile([rule]);
        var instance = RuleInstance.FromJson(JsonNode.Parse("""{"a":1}""")!.AsObject());
        foreach (var phase in new[] { EvaluationPhase.Render, EvaluationPhase.Submission })
            Assert.Equal(2L, new FormRuleGraph(compiled, TimeProvider.System, FormsExpressionEnvironment.Admitted.For(phase))
                .EvaluateInstance(instance).Values["field:total"].Value!.GetValue<long>());

        // A phase Forms marks inapplicable, a network effect and an unregistered function all refuse.
        Assert.Throws<BorrowerEnvironmentException>(() => FormsExpressionEnvironment.Admitted.For(EvaluationPhase.Run));
        Assert.Throws<BorrowerEnvironmentException>(() => BorrowerEnvironmentAdmission.Admit(declaration with { Effects = [.. declaration.Effects, "network"] }));
        Assert.Throws<BorrowerEnvironmentException>(() => BorrowerEnvironmentAdmission.Admit(declaration with { Operations = [.. declaration.Operations, "http.get"] }));
    }
}
