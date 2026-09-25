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
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(false, [], Control: new("currency", parameters)), "invoice.amount")), ControlsAndFields);
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

    private static readonly LayoutHostRegisters ControlsAndFields = Controls with
    {
        Fields = new LayoutRecordFieldRegistry(
        [
            new("invoice.reference", FieldScalarValueShape.Text, HasValueDomain: false),
            new("invoice.amount", FieldScalarValueShape.Number, HasValueDomain: false),
            new("invoice.status", FieldScalarValueShape.Text, HasValueDomain: true),
        ]),
    };

    [Fact(DisplayName = "layout-bound-3: publication looks up the field and refuses a control that does not accept its value kind (T-724 ruling 37)")]
    public void PublicationRefusesAControlThatDoesNotAcceptTheFieldValueKind()
    {
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(false, [], Control: new("text")))), ControlsAndFields);
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(false, [], Control: new("currency")), "invoice.amount")), ControlsAndFields);
        AssertPublishRefused(CaptureSurface(new(false, [], Control: new("currency"))), ControlsAndFields,
            LayoutDefinitionCodes.ControlValueKindMismatch, "/blocks/0/capture/control");
        // The field must be found to be checked: an unknown field, or no field register, refuses.
        AssertPublishRefused(CaptureSurface(new(false, [], Control: new("text")), "invoice.unknown"), ControlsAndFields,
            LayoutDefinitionCodes.CaptureFieldUnknown, "/blocks/0/capture/control");
        AssertPublishRefused(CaptureSurface(new(false, [], Control: new("text"))), Controls,
            LayoutDefinitionCodes.CaptureFieldUnknown, "/blocks/0/capture/control");
    }

    [Fact(DisplayName = "layout-bound-10: an authored control cannot displace the value-domain resolver's choice (T-724 ruling 37)")]
    public void AuthoredControlCannotDisplaceTheValueDomainResolver()
    {
        // With no authored control, the field runtime's value-domain resolver picks the editor and Layout passes it through.
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(CaptureSurface(new(true, []), "invoice.status")), ControlsAndFields);
        // Naming a control on that field would displace the resolver's choice, so publication refuses it.
        AssertPublishRefused(CaptureSurface(new(false, [], Control: new("text")), "invoice.status"), ControlsAndFields,
            LayoutDefinitionCodes.ControlDisplacesValueDomain, "/blocks/0/capture/control");
    }

    private static void AssertPublishRefused(LayoutDefinition definition, LayoutHostRegisters registers, string code, string pointer)
    {
        var refused = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(Sealed(definition), registers));
        Assert.Contains(refused.Refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
    }

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

    [Fact(DisplayName = "layout-bound-7: two packs supplying the same page id refuse by name (T-724 ruling 40)")]
    public void TwoPacksSupplyingTheSamePageIdRefuseByName()
    {
        var one = PackPages();
        var layouts = one.Layouts.Values.OrderBy(layout => layout.Id, StringComparer.Ordinal).ToArray();
        var master = one.Masters["pack.master"];

        var twiceLayout = Assert.Throws<LayoutDefinitionAdmissionException>(() => new LayoutPageRegistry([.. layouts, layouts[0]], [master]));
        Assert.Equal("register.pages", twiceLayout.Stage);
        Assert.Equal(new LayoutDefinitionRefusal(LayoutDefinitionCodes.PageSuppliedTwice, "/page_layouts/2"), Assert.Single(twiceLayout.Refusals));
        var twiceMaster = Assert.Throws<LayoutDefinitionAdmissionException>(() => new LayoutPageRegistry(layouts, [master, master]));
        Assert.Equal(new LayoutDefinitionRefusal(LayoutDefinitionCodes.PageSuppliedTwice, "/page_masters/1"), Assert.Single(twiceMaster.Refusals));
        // A master over geometry no pack supplies is named too.
        var orphan = Assert.Throws<LayoutDefinitionAdmissionException>(() => new LayoutPageRegistry([], [master]));
        Assert.Equal(new LayoutDefinitionRefusal(LayoutDefinitionCodes.PageReferenceUnknown, "/page_masters/0/page_layout_id"), Assert.Single(orphan.Refusals));
    }

    [Fact(DisplayName = "layout-auth-20: publication compiles show_when and refuses a malformed guard (T-724 ruling 39)")]
    public void PublicationCompilesShowWhenAndRefusesAMalformedGuard()
    {
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(GuardSurface(Expr("{\"==\":[{\"var\":\"field.status\"},\"open\"]}"), Expr("{\"==\":[{\"var\":\"row.status\"},\"open\"]}"))), LayoutHostRegisters.Platform);

        // A malformed guard would hide its block forever at run time; publication refuses it instead.
        AssertPublishRefused(GuardSurface(Expr("{\"no-such-operator\":[1]}"), null), LayoutHostRegisters.Platform, LayoutDefinitionCodes.GuardInvalid, "/blocks/0/show_when");
        AssertPublishRefused(GuardSurface(Expr("status == open"), null), LayoutHostRegisters.Platform, LayoutDefinitionCodes.GuardInvalid, "/blocks/0/show_when");
        // A row reference compiles only inside the repeating block's rows, exactly as the runtime scopes it.
        AssertPublishRefused(GuardSurface(Expr("{\"==\":[{\"var\":\"row.status\"},\"open\"]}"), null), LayoutHostRegisters.Platform, LayoutDefinitionCodes.GuardInvalid, "/blocks/0/show_when");
        // A draft keeps an unfinished guard while it is authored.
        LayoutDefinitionAdmission.ValidateForAuthoring(GuardSurface(Expr("status == open"), null), LayoutHostRegisters.Platform);
    }

    [Fact(DisplayName = "layout-ck-29: show_when holds exactly one of expression or predicate; neither or both refuses at every stage")]
    public void ShowWhenHoldsExactlyOneOfExpressionOrPredicate()
    {
        var both = new LayoutShowWhen("{\"var\":\"field.flagged\"}", Overdue.Pin);
        var neither = new LayoutShowWhen();
        var blank = new LayoutShowWhen(Expression: " ");
        foreach (var broken in new[] { both, neither, blank })
        {
            // Authoring, publication and the runtime gate all refuse it: a declared guard is required.
            var authoring = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(GuardSurface(broken, null), Predicates));
            Assert.Contains(new LayoutDefinitionRefusal(LayoutDefinitionCodes.GuardFormInvalid, "/blocks/0/show_when"), authoring.Refusals);
            AssertPublishRefused(GuardSurface(broken, null), Predicates, LayoutDefinitionCodes.GuardFormInvalid, "/blocks/0/show_when");
            var runtime = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutPersistedValueAdmission.ValidateForRuntime(GuardSurface(broken, null), Predicates));
            Assert.Contains(new LayoutDefinitionRefusal(LayoutDefinitionCodes.GuardFormInvalid, "/blocks/0/show_when"), runtime.Refusals);
        }
        // An absent guard is no guard: the block always shows, and nothing refuses.
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(GuardSurface(null, null)), LayoutHostRegisters.Platform);
    }

    [Fact(DisplayName = "layout-ck-29: a predicate guard resolves its ExactPin through the pinned closure at publish and refuses one that will not resolve")]
    public void APredicateGuardResolvesItsExactPinThroughThePinnedClosure()
    {
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(GuardSurface(new(Predicate: Overdue.Pin), new(Predicate: Overdue.Pin))), Predicates);

        // No closure resolves nothing, so publication fails closed (T-724 ruling 36's pattern).
        AssertPublishRefused(GuardSurface(new(Predicate: Overdue.Pin), null), LayoutHostRegisters.Platform, LayoutDefinitionCodes.GuardUnresolved, "/blocks/0/show_when/predicate");
        // A pin whose digest is not the closure's, a version the closure lacks, and a floating version all refuse.
        AssertPublishRefused(GuardSurface(new(Predicate: Overdue.Pin with { Digest = new string('0', 64) }), null), Predicates, LayoutDefinitionCodes.GuardUnresolved, "/blocks/0/show_when/predicate");
        AssertPublishRefused(GuardSurface(new(Predicate: Overdue.Pin with { Version = "9.0.0" }), null), Predicates, LayoutDefinitionCodes.GuardUnresolved, "/blocks/0/show_when/predicate");
        AssertPublishRefused(GuardSurface(new(Predicate: Overdue.Pin with { Version = "latest" }), null), Predicates, LayoutDefinitionCodes.GuardUnresolved, "/blocks/0/show_when/predicate");
    }

    [Fact(DisplayName = "layout-ck-29 (T-724 ruling 72): a Layout guard binds its named predicate as PredicateConsumer.LayoutGuard")]
    public void ALayoutGuardIdentifiesItselfAsLayoutGuard()
        => Assert.Equal(Harborline.Foundation.RuleEngine.References.PredicateConsumer.LayoutGuard, LayoutGuardRule.Consumer);

    [Fact(DisplayName = "layout-bound-9: publication admits show_when functions only from the kernel's BuiltInFunctionRegister")]
    public void ShowWhenFunctionsResolveOnlyThroughTheKernelRegister()
    {
        // Registered kernel functions (text, membership, date) publish, at the root and in a row.
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(GuardSurface(
            Expr("{\"in\":[{\"cat\":[{\"var\":\"field.status\"},\"-\",{\"var\":\"field.region\"}]},[\"open-eu\",\"open-us\"]]}"),
            Expr("{\">\":[{\"date.diff\":[{\"date.today\":[]},{\"var\":\"row.due\"}]},30]}"))), LayoutHostRegisters.Platform);

        // A function the register does not have refuses, however it is spelled: no pack library
        // evaluates a guard (T-590's register is the compiler's only operator table).
        foreach (var unregistered in new[] { "lib.is_weekend", "acme::is_weekend", "date.weekday" })
            AssertPublishRefused(GuardSurface(Expr($"{{\"{unregistered}\":[{{\"var\":\"field.due\"}}]}}"), null), LayoutHostRegisters.Platform,
                LayoutDefinitionCodes.GuardInvalid, "/blocks/0/show_when");
    }

    private static readonly Harborline.Foundation.RuleEngine.References.NamedPredicate Overdue = new("invoice.overdue", "1.0.0", "{\"==\":[{\"var\":\"field.status\"},\"overdue\"]}");

    private static readonly LayoutHostRegisters Predicates = LayoutHostRegisters.Platform with
    {
        Predicates = new Harborline.Foundation.RuleEngine.References.PinnedClosure([Overdue], []),
    };

    private static LayoutShowWhen Expr(string expression) => new(Expression: expression);

    private static LayoutDefinition GuardSurface(LayoutShowWhen? surfaceGuard, LayoutShowWhen? rowGuard) => new(
        new("surface.invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, LayoutMedium.Screen, LayoutIntent.Observe,
        [
            new("notice", "layout.text", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Overdue")), [], ShowWhen: surfaceGuard),
            new("lines", "layout.table", new LayoutQueryBinding("views.invoice-lines"),
                [new("amount", "layout.field", new LayoutRecordFieldBinding("line.amount"), [], ShowWhen: rowGuard)],
                Container: new(LayoutContainerKind.Stack), Repeating: true),
        ],
        [], [], [], null, []);

    [Fact(DisplayName = "layout-auth-18: the repeating block both editors author passes admission (T-724 ruling 41)")]
    public void TheRepeatingBlockBothEditorsAuthorPassesAdmission()
    {
        // The React and Blazor editor tests each prove they produce exactly this fixture.
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "_shared", "layout", "authored-repeating-block.json")));
        var drafted = fixture.RootElement.GetProperty("blocks").EnumerateArray().ToArray();

        LayoutBlock Build(JsonElement block) => new(
            block.GetProperty("id").GetString()!,
            block.GetProperty("kind").GetString()!,
            block.GetProperty("binding").GetProperty("kind").GetString() switch
            {
                "query" => new LayoutQueryBinding(block.GetProperty("binding").GetProperty("name").GetString()!),
                "record_field" => new LayoutRecordFieldBinding(block.GetProperty("binding").GetProperty("name").GetString()!),
                var kind => throw new InvalidOperationException($"The fixture uses binding kind '{kind}', which this projection does not map."),
            },
            drafted.Where(child => child.TryGetProperty("parentId", out var parent) && parent.GetString() == block.GetProperty("id").GetString()).Select(Build).ToArray(),
            Container: block.TryGetProperty("container", out var container) ? new(Enum.Parse<LayoutContainerKind>(container.GetString()!, ignoreCase: true)) : null,
            Repeating: block.TryGetProperty("repeating", out var repeating) && repeating.GetBoolean());

        var definition = new LayoutDefinition(
            new("surface.invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
                JsonSerializer.SerializeToElement(new { source = "editor" }), "standard", false, []),
            1, LayoutMedium.Screen, LayoutIntent.Observe,
            drafted.Where(block => !block.TryGetProperty("parentId", out _)).Select(Build).ToArray(),
            [], [], [], null, []);

        LayoutDefinitionAdmission.ValidateForAuthoring(definition, LayoutHostRegisters.Platform);
        LayoutDefinitionAdmission.ValidateForPublish(Sealed(definition), LayoutHostRegisters.Platform);
        Assert.True(Assert.Single(definition.Blocks).Repeating);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }

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

    internal static LayoutDefinition CaptureSurface(LayoutCaptureProperties capture, string fieldPath = "invoice.reference") => new(
        new("surface.invoice", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { source = "test" }), "standard", false, []),
        1, LayoutMedium.Screen, LayoutIntent.Capture,
        [new("reference", "layout.field", new LayoutRecordFieldBinding(fieldPath), [], Capture: capture)],
        [], [], [], null, []);
}
