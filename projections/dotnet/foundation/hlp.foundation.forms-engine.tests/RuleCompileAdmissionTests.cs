using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Exceptions;
using State = Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Engine.Tests;

public sealed class RuleCompileAdmissionTests
{
    [Fact]
    public void Valid_Rules_And_Guards_Pass()
    {
        var definition = Definition(
            rules:
            [
                Rule("account-match", State.RuleScope.Field, "accountConfirm",
                    """{"==":[{"var":"accountConfirm"},{"var":"account"}]}""",
                    State.RuleActionKind.Validate),
            ],
            pages: Pages("""{"==":[{"var":"account"},"us"]}"""));

        RuleCompileAdmission.ValidateOrThrow(definition);
    }

    [Fact]
    public void No_Rules_No_Pages_Passes() => RuleCompileAdmission.ValidateOrThrow(Definition());

    [Fact]
    public void Tier1_JsonSchema_Rules_Are_Skipped()
    {
        var definition = Definition(rules:
        [
            Rule("tier1", State.RuleScope.Field, "account", "not json - tier 1 is not compiled",
                State.RuleActionKind.Validate, State.RuleTier.JsonSchema),
        ]);

        RuleCompileAdmission.ValidateOrThrow(definition);
    }

    [Fact]
    public void Malformed_Json_Rule_Expression_Rejects_With_Stable_Code()
    {
        var definition = Definition(rules:
        [
            Rule("broken", State.RuleScope.Field, "account", "{not valid json", State.RuleActionKind.Validate),
        ]);

        var exception = Assert.Throws<FormDefinitionValidationException>(() => RuleCompileAdmission.ValidateOrThrow(definition));

        Assert.Equal(FormDefinitionCodes.RulesUncompilable, exception.Code);
        Assert.Contains("broken", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Compute_Cycle_Rejects_With_Stable_Code()
    {
        var definition = Definition(rules:
        [
            Rule("compute-a", State.RuleScope.Field, "account", """{"var":"accountConfirm"}""", State.RuleActionKind.Compute),
            Rule("compute-b", State.RuleScope.Field, "accountConfirm", """{"var":"account"}""", State.RuleActionKind.Compute),
        ]);

        var exception = Assert.Throws<FormDefinitionValidationException>(() => RuleCompileAdmission.ValidateOrThrow(definition));

        Assert.Equal(FormDefinitionCodes.RulesUncompilable, exception.Code);
    }

    [Fact]
    public void PowerFx_Tier_Rejects_At_Admission()
    {
        var definition = Definition(rules:
        [
            Rule("fx", State.RuleScope.Field, "account", "Upper(account)", State.RuleActionKind.Validate, State.RuleTier.PowerFx),
        ]);

        var exception = Assert.Throws<FormDefinitionValidationException>(() => RuleCompileAdmission.ValidateOrThrow(definition));

        Assert.Equal(FormDefinitionCodes.RulesUncompilable, exception.Code);
    }

    [Fact]
    public void Uncompilable_Guard_Rejects_With_Guard_Code()
    {
        var definition = Definition(pages: Pages("""{"var":"table.sum("}"""));

        var exception = Assert.Throws<FormDefinitionValidationException>(() => RuleCompileAdmission.ValidateOrThrow(definition));

        Assert.Equal(FormDefinitionCodes.RulesGuardUncompilable, exception.Code);
        Assert.Equal("p2", exception.Target);
        Assert.Contains("p2", exception.Message, StringComparison.Ordinal);
    }

    private static State.RuleDefinition Rule(
        string id,
        State.RuleScope scope,
        string target,
        string expression,
        State.RuleActionKind action,
        State.RuleTier tier = State.RuleTier.JsonLogic) =>
        new(id, tier, scope, target, expression, action);

    private static IReadOnlyList<State.FormPage> Pages(string guard) =>
    [
        new("p1", State.InternationalizedText.FromInvariant("p1"), ["sec-a"]),
        new("p2", State.InternationalizedText.FromInvariant("p2"), ["sec-b"], guard),
    ];

    private static State.FormDefinition Definition(
        IReadOnlyList<State.RuleDefinition>? rules = null,
        IReadOnlyList<State.FormPage>? pages = null)
    {
        var now = DateTimeOffset.Parse("2026-07-01T12:00:00Z");
        var fields = new Dictionary<string, State.FieldOverlay>(StringComparer.Ordinal)
        {
            ["account"] = new(State.InternationalizedText.FromInvariant("account")),
            ["accountConfirm"] = new(State.InternationalizedText.FromInvariant("accountConfirm")),
        };
        var access = new State.SectionAccess(
            [Harborline.Contracts.Authorization.RoleReference.Domain("writer")],
            [Harborline.Contracts.Authorization.RoleReference.Domain("writer")]);
        State.FormSection[] sections =
        [
            new("sec-a", State.InternationalizedText.FromInvariant("sec-a"), ["account"], access),
            new("sec-b", State.InternationalizedText.FromInvariant("sec-b"), ["accountConfirm"], access),
        ];

        return new(
            new("admission-test"),
            new(1, 0, 0),
            State.FormDefinitionStatus.Draft,
            new TenantId("tenant:acme"),
            State.IdentityRef.System,
            new("schema:admission-test"),
            new(fields, sections, rules ?? [], Pages: pages),
            null,
            now,
            now);
    }
}
