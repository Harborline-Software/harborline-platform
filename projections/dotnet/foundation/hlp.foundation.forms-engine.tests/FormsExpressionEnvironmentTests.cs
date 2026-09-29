using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Environments;
using Harborline.Foundation.RuleEngine.Graph;

using State = Harborline.Foundation.Forms.Models;

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

    // ck-7 S3: Forms declares caller/clock/record_type but binds only the candidate, and `caller.x` used to
    // lower to `field.caller.x`, so a rule meant to read the authenticated principal read a client-supplied
    // "caller.x" property instead and the submission committed. The compiler now refuses the unsupplied root.
    [Theory(DisplayName = "rules-ck-28, forms-ck-13: a rule addressing an unsupplied scope root cannot read a client-supplied value of that name")]
    [InlineData("caller.role", "owner")]
    [InlineData("caller.id", "attacker")]
    [InlineData("caller", "attacker")]
    [InlineData("clock.now", "2000-01-01")]
    [InlineData("record_type.id", "spoofed")]
    public async Task Unsupplied_scope_root_does_not_read_the_candidate(string path, string spoofed)
    {
        var rule = new State.RuleDefinition("guard.root", State.RuleTier.JsonLogic, State.RuleScope.Schema, "",
            $$$"""{"==":[{"var":"{{{path}}}"},"{{{spoofed}}}"]}""", State.RuleActionKind.Validate);
        var harness = await FormEngineOrchestrationTests.Harness.CreateAsync(
            roles: ["admin"], schemaJson: """{"type":"object"}""", definitionFactory: (schema, tenant) => WithRule(schema, tenant, rule));
        // Field reads are flat, so the spoof is a top-level property literally named after the path.
        using var candidate = JsonDocument.Parse($$$"""{"name":"x","{{{path}}}":"{{{spoofed}}}"}""");

        // Before the fix the client's value satisfied the rule and the submission committed.
        var validate = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.ValidateAsync(harness.Definition.Id, candidate));
        Assert.Equal("rule.compile.bad_grammar", Assert.Single(validate.Errors).Code);
        var submit = await Assert.ThrowsAsync<FormEngineValidationException>(async () =>
            await harness.Engine.SubmitAsync(new(harness.Definition.Id, candidate, Guid.NewGuid().ToString("N"))));
        Assert.Equal("rule.compile.bad_grammar", Assert.Single(submit.Errors).Code);
        Assert.Equal(0, (await harness.Store.CountsAsync()).Submissions);

        Assert.Throws<Harborline.Foundation.Forms.Exceptions.FormDefinitionValidationException>(() =>
            RuleCompileAdmission.ValidateOrThrow(harness.Definition));
    }

    private static State.FormDefinition WithRule(string schema, TenantId tenant, State.RuleDefinition rule)
    {
        var definition = FormEngineOrchestrationTests.Harness.CreateDefinition(schema, tenant);
        return definition with { Overlay = definition.Overlay with { Rules = [rule] } };
    }
}
