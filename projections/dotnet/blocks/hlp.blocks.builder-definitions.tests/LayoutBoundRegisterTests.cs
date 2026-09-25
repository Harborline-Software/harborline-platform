using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;
using Harborline.Contracts.Forms;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// DES-0052's bound inventory: the developer-supplied registers a Layout surface names and
/// parameterises, and that admission checks every name against.
/// </summary>
public sealed class LayoutBoundRegisterTests
{
    private static readonly JsonElement PrecisionSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new { precision = new { type = "integer", minimum = 0 } },
        additionalProperties = false,
    });

    private static readonly LayoutHostRegisters Controls = new(LayoutBlockKindRegistry.Platform, FieldControls: new LayoutFieldControlRegistry(
    [
        new("text", [FieldScalarValueShape.Text]),
        new("currency", [FieldScalarValueShape.Number], PrecisionSchema),
    ]));

    [Fact(DisplayName = "layout-bound-3: a capture block's field control is a registered control, parameterised")]
    public void CaptureBlockFieldControlIsARegisteredControlParameterised()
    {
        var parameters = JsonSerializer.SerializeToElement(new { precision = 2 });
        var admitted = CaptureSurface(new(false, [], Control: new("currency", parameters)));

        LayoutDefinitionAdmission.ValidateForAuthoring(admitted, Controls);
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(admitted), Controls);
        var roundTrip = LayoutDefinitionJson.Deserialize(LayoutDefinitionJson.SerializeCanonical(admitted));
        var control = Assert.Single(roundTrip.Blocks).Capture!.Control!;
        Assert.Equal("currency", control.Id);
        Assert.Equal(2, control.Parameters!.Value.GetProperty("precision").GetInt32());

        AssertRefused(CaptureSurface(new(false, [], Control: new("signature"))), Controls,
            LayoutDefinitionCodes.FieldControlUnknown, "/blocks/0/capture/control");
        AssertRefused(CaptureSurface(new(false, [], Control: new("currency", JsonSerializer.SerializeToElement(2)))), Controls,
            LayoutDefinitionCodes.ControlParametersInvalid, "/blocks/0/capture/control/parameters");
    }

    [Fact(DisplayName = "layout-bound-3: a control's parameters validate against the schema it declares, and a control with none accepts none (T-724 ruling 38)")]
    public void ControlParametersValidateAgainstTheDeclaredSchema()
    {
        LayoutDefinitionAdmission.ValidateForAuthoring(CaptureSurface(new(false, [], Control: new("currency", JsonSerializer.SerializeToElement(new { precision = 2 })))), Controls);
        AssertRefused(CaptureSurface(new(false, [], Control: new("currency", JsonSerializer.SerializeToElement(new { precision = -1 })))), Controls,
            LayoutDefinitionCodes.ControlParametersInvalid, "/blocks/0/capture/control/parameters");
        AssertRefused(CaptureSurface(new(false, [], Control: new("currency", JsonSerializer.SerializeToElement(new { colour = "red" })))), Controls,
            LayoutDefinitionCodes.ControlParametersInvalid, "/blocks/0/capture/control/parameters");

        // A control that declares no schema takes no parameters: an empty object is no parameters.
        LayoutDefinitionAdmission.ValidateForAuthoring(CaptureSurface(new(false, [], Control: new("text", JsonSerializer.SerializeToElement(new { })))), Controls);
        AssertRefused(CaptureSurface(new(false, [], Control: new("text", JsonSerializer.SerializeToElement(new { multiline = true })))), Controls,
            LayoutDefinitionCodes.ControlParametersInvalid, "/blocks/0/capture/control/parameters");
    }

    [Fact(DisplayName = "layout-bound-3: a named field control with no register to check it against refuses")]
    public void NamedFieldControlWithNoRegisterRefuses()
        => AssertRefused(CaptureSurface(new(false, [], Control: new("text"))), new LayoutHostRegisters(LayoutBlockKindRegistry.Platform),
            LayoutDefinitionCodes.FieldControlUnknown, "/blocks/0/capture/control");

    [Fact(DisplayName = "layout-bound-7: a page run cites the page layout and master a pack supplies")]
    public void PageRunCitesThePageLayoutAndMasterAPackSupplies()
    {
        var pages = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Pages: PackPages());
        var citing = PageSurface(new("run", "pack.a4", "pack.master", ["body"]));

        LayoutDefinitionAdmission.ValidateForAuthoring(citing, pages);
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(citing), pages);
        // Without the pack's register the same citation names nothing.
        AssertRefused(citing, LayoutHostRegisters.Platform, LayoutDefinitionCodes.PageReferenceUnknown, "/page_runs/0/page_layout_id");
        AssertRefused(citing, LayoutHostRegisters.Platform, LayoutDefinitionCodes.PageReferenceUnknown, "/page_runs/0/page_master_id");
        // A cited master must sit over the run's cited geometry.
        AssertRefused(PageSurface(new("run", "pack.letter", "pack.master", ["body"])), pages,
            LayoutDefinitionCodes.PageReferenceUnknown, "/page_runs/0/page_master_id");
    }

    [Fact(DisplayName = "layout-bound-7: a surface-local page definition may not shadow one the pack supplies")]
    public void SurfaceLocalPageDefinitionMayNotShadowAPackOne()
    {
        var pages = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Pages: PackPages());
        var local = PackPages();
        var shadowing = PageSurface(new("run", "pack.a4", "pack.master", ["body"])) with
        {
            PageLayouts = [local.Layouts["pack.a4"]],
            PageMasters = [local.Masters["pack.master"]],
        };

        AssertRefused(shadowing, pages, LayoutDefinitionCodes.PageDefinitionInvalid, "/page_layouts/0");
        AssertRefused(shadowing, pages, LayoutDefinitionCodes.PageDefinitionInvalid, "/page_masters/0");
    }

    [Fact(DisplayName = "layout-bound-8: a capture block's named validation rule resolves in the host register and its tier decides the compiler")]
    public void NamedValidationRuleResolvesAndItsTierDecidesTheCompiler()
    {
        var registers = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, ValidationRules: new LayoutValidationRuleRegistry(
        [
            Rule("rules.amount-positive", RuleTier.JsonLogic, "{\">\":[{\"var\":\"invoice.amount\"},0]}"),
            // A schema-tier rule belongs to the kernel validator; the JsonLogic compiler never sees it.
            Rule("rules.amount-shape", RuleTier.JsonSchema, "{\"minimum\":0}"),
            Rule("rules.malformed", RuleTier.JsonLogic, "{\"no-such-operator\":[1]}"),
            Rule("rules.power-fx", RuleTier.PowerFx, "Amount > 0"),
            Rule("rules.computes", RuleTier.JsonLogic, "{\"+\":[1,2]}", RuleActionKind.Compute),
        ]));

        LayoutDefinitionAdmission.ValidateForAuthoring(CaptureSurface(new(true, ["rules.amount-positive", "rules.amount-shape"])), registers);
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(true, ["rules.amount-positive", "rules.amount-shape"]))), registers);

        AssertRefused(CaptureSurface(new(false, ["rules.unregistered"])), registers,
            LayoutDefinitionCodes.ValidationRuleUnknown, "/blocks/0/capture/validation_rules/0");
        AssertRefused(CaptureSurface(new(false, ["rules.amount-positive", "rules.malformed"])), registers,
            LayoutDefinitionCodes.ValidationRuleInvalid, "/blocks/0/capture/validation_rules/1");
        // The tier this evaluator does not compile refuses rather than being read as JsonLogic.
        AssertRefused(CaptureSurface(new(false, ["rules.power-fx"])), registers,
            LayoutDefinitionCodes.ValidationRuleInvalid, "/blocks/0/capture/validation_rules/0");
        // A rule that computes a value is not a validation rule.
        AssertRefused(CaptureSurface(new(false, ["rules.computes"])), registers,
            LayoutDefinitionCodes.ValidationRuleInvalid, "/blocks/0/capture/validation_rules/0");
    }

    [Fact(DisplayName = "layout-bound-8: named validation rules with no register to resolve them refuse (T-724 ruling 36)")]
    public void NamedValidationRulesWithNoRegisterRefuse()
    {
        var naming = CaptureSurface(new(false, ["rules.amount-positive"]));
        // A draft keeps its names while it is authored; publication must resolve them.
        LayoutDefinitionAdmission.ValidateForAuthoring(naming, LayoutHostRegisters.Platform);
        var publish = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(Sealed(naming), LayoutHostRegisters.Platform));
        Assert.Contains(publish.Refusals, refusal => refusal.Code == LayoutDefinitionCodes.ValidationRuleUnknown);
        // A capture block that names no rule needs no register.
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(true, []))), LayoutHostRegisters.Platform);
    }

    private static RuleDefinition Rule(string id, RuleTier tier, string expression, RuleActionKind action = RuleActionKind.Validate) => new()
    {
        Id = id,
        Tier = tier,
        Scope = RuleScope.Schema,
        ScopeTarget = "",
        Expression = expression,
        Action = action,
    };

    internal static LayoutPageRegistry PackPages() => new(
        [
            new("pack.a4", "a4", LayoutPageOrientation.Portrait, new("12mm", "12mm", "12mm", "12mm"), new("10mm", "10mm")),
            new("pack.letter", "letter", LayoutPageOrientation.Portrait, new("1in", "1in", "1in", "1in"), new("0.5in", "0.5in")),
        ],
        [new("pack.master", "pack.a4", new(null, "first.center", null), new(null, "left.center", null), new(null, "right.center", null))]);

    internal static LayoutDefinition PageSurface(LayoutPageRun run) => new(
        new("surface.statement", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, LayoutMedium.Page, LayoutIntent.Observe,
        [
            new("heading", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Statement")), [], FlowRole: LayoutFlowRole.Static, StaticRegion: "first.center"),
            new("body", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Body")), []),
        ],
        [], [], [run], null, []);

    internal static void AssertRefused(LayoutDefinition definition, LayoutHostRegisters registers, string code, string pointer)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, registers));
        Assert.Contains(refused.Refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
    }

    internal static LayoutDefinition Sealed(LayoutDefinition definition)
        => definition with { Envelope = definition.Envelope with { Requires = [new(LayoutPackIdentity.Capability, "1.0.0")] } };

    internal static LayoutDefinition CaptureSurface(LayoutCaptureProperties capture) => new(
        new("surface.invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, LayoutMedium.Screen, LayoutIntent.Capture,
        [new("reference", "layout.field", new LayoutRecordFieldBinding("invoice.reference"), [], Capture: capture)],
        [], [], [], null, []);
}
