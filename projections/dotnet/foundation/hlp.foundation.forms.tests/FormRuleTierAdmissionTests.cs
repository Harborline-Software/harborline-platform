using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;
using Xunit;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// DES-0016 forms-eng-4 and forms-eng-16 (T-485, ADR 0099 acceptance, board finding F10): a rule the
/// platform rule compiler cannot evaluate is refused by the store at validate, instead of being admitted
/// and then silently skipped. The JsonSchema tier is constraint validation only; T-732 compiles it for
/// <see cref="RuleActionKind.Validate"/> (T-724 ruling 36), and every other action must use Rules' JsonLogic.
/// </summary>
public sealed class FormRuleTierAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "forms-eng-16: the store refuses a JsonSchema-tier visibility rule at validate and persists nothing")]
    public async Task JsonSchemaVisibilityRuleIsRefusedByTheStore()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));
        var definition = Form(Rule("hide-employer", RuleTier.JsonSchema, RuleActionKind.Visibility));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(async () => await store.RegisterAsync(definition));

        Assert.Equal(FormDefinitionCodes.RulesTierUnsupported, refusal.Code);
        Assert.Equal("hide-employer", refusal.Target);
        await Assert.ThrowsAsync<FormDefinitionNotFoundException>(
            async () => await store.GetAsync(definition.Tenant, definition.Id, definition.Version));
    }

    [Theory(DisplayName = "forms-eng-4: every non-validation action under the JsonSchema tier is refused, not skipped")]
    [InlineData(RuleActionKind.Visibility)]
    [InlineData(RuleActionKind.Required)]
    [InlineData(RuleActionKind.ReadOnly)]
    [InlineData(RuleActionKind.Compute)]
    [InlineData(RuleActionKind.Presentation)]
    [InlineData(RuleActionKind.Options)]
    public async Task JsonSchemaTierIsRefusedForEveryNonValidationAction(RuleActionKind action)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(Rule("r", RuleTier.JsonSchema, action))));

        Assert.Equal(FormDefinitionCodes.RulesTierUnsupported, refusal.Code);
    }

    [Theory(DisplayName = "forms-eng-16: a tier the compiler cannot evaluate is refused by the store, not only by the publisher")]
    [InlineData(RuleTier.PowerFx)]
    [InlineData((RuleTier)7)]
    public async Task UnevaluableTierIsRefusedByTheStore(RuleTier tier)
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var refusal = await Assert.ThrowsAsync<FormDefinitionValidationException>(
            async () => await store.RegisterAsync(Form(Rule("r", tier, RuleActionKind.Visibility))));

        Assert.Equal(FormDefinitionCodes.RulesTierUnsupported, refusal.Code);
    }

    [Fact(DisplayName = "forms-eng-4: a visibility rule in Rules' JsonLogic grammar is admitted")]
    public async Task JsonLogicVisibilityRuleIsAdmitted()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        var stored = await store.RegisterAsync(Form(Rule("hide-employer", RuleTier.JsonLogic, RuleActionKind.Visibility)));

        Assert.Equal(RuleTier.JsonLogic, Assert.Single(stored.Overlay.Rules).Tier);
    }

    [Fact(DisplayName = "forms-eng-4: a JsonSchema-tier validation rule stays admitted for T-732 to compile")]
    public async Task JsonSchemaValidationRuleIsStillAdmitted()
    {
        using var store = new InMemoryFormDefinitionStore(new FixedClock(Now));

        await store.RegisterAsync(Form(Rule("shape", RuleTier.JsonSchema, RuleActionKind.Validate)));
    }

    private static RuleDefinition Rule(string id, RuleTier tier, RuleActionKind action) =>
        new(id, tier, RuleScope.Field, "employer",
            tier == RuleTier.JsonSchema ? "{\"minLength\":1}" : "{\"==\":[{\"var\":\"employment\"},\"employed\"]}",
            action);

    private static FormDefinition Form(RuleDefinition rule)
    {
        var fields = new[] { "employment", "employer" }
            .ToDictionary(f => f, f => new FieldOverlay(InternationalizedText.FromInvariant(f)));
        var access = new SectionAccess(
            ReadRoles: [Harborline.Contracts.Authorization.RoleReference.Domain("*")],
            WriteRoles: [Harborline.Contracts.Authorization.RoleReference.Domain("tenant:admin")]);
        return new FormDefinition(
            Id: new FormDefinitionId("rule-tier-admission"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:rule-tier-admission"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["employment", "employer"], access)],
                Rules: [rule]),
            Lineage: null,
            CreatedAt: Now,
            UpdatedAt: Now);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
