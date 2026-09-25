using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-583's authoring refusals over the acting author's Access (DES-0052 layout-auth-24 and
/// layout-auth-36). Access answers every question; admission only asks and records the refusal.
/// </summary>
public sealed class LayoutAuthorizationTests
{
    [Fact(DisplayName = "layout-auth-24: authoring and publication refuse every binding the acting author could not read, and ask nothing of static content")]
    public void EveryBindingTheAuthorCouldNotReadRefusesAtItsPointer()
    {
        var access = new FixtureAccess(unreadable: ["view.payroll", "payroll.total", "employee.salary"]);
        var definition = Surface(
            Block("orders", new LayoutQueryBinding("view.orders")),
            Block("payroll", new LayoutQueryBinding("view.payroll")),
            Block("payroll-total", new LayoutMeasureBinding("payroll.total")),
            Block("salary", new LayoutRecordFieldBinding("employee.salary")),
            Block("help", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Help"))));
        var registers = LayoutHostRegisters.Platform;

        foreach (var validate in new Action[]
        {
            () => LayoutDefinitionAdmission.ValidateForAuthoring(definition, registers, access),
            () => LayoutDefinitionAdmission.ValidateForPublish(definition, registers, access),
        })
        {
            var error = Assert.Throws<DefinitionRefusalException>(validate);
            Assert.Equal(
                ["/blocks/1/binding", "/blocks/2/binding", "/blocks/3/binding"],
                error.Refusals.Where(refusal => refusal.Code == LayoutDefinitionCodes.BindingUnreadable).Select(refusal => refusal.Pointer));
        }

        // Static content is authored on the block and read from nowhere, so Access is never asked about it.
        Assert.DoesNotContain("static", access.Asked);
        // The same surface admits once the author can read every source it binds.
        LayoutDefinitionAdmission.ValidateForPublish(definition, registers, new FixtureAccess());
    }

    [Fact(DisplayName = "layout-auth-24: authoring and publication without the author's Access fail rather than skip the unreadable-binding check (T-724 ruling 75)")]
    public void AuthoringOrPublishingWithoutTheAuthorsAccessFails()
    {
        // A surface that would admit if the check were skipped: only a missing port can refuse it.
        var definition = Surface(Block("payroll", new LayoutQueryBinding("view.payroll")));

        Assert.Throws<ArgumentNullException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, null!));
        Assert.Throws<ArgumentNullException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, LayoutHostRegisters.Platform, null!));
        Assert.Throws<ArgumentNullException>(() => LayoutDefinitionAdmission.ValidateForPublish(definition, null!));
        Assert.Throws<ArgumentNullException>(() => LayoutDefinitionAdmission.ValidateForPublish(definition, LayoutHostRegisters.Platform, null!));
    }

    [Fact(DisplayName = "layout-auth-24: a text binding refuses at each field run the author could not read, asked as that field; literal runs are never asked about")]
    public void ATextRunNamingAFieldTheAuthorCouldNotReadRefuses()
    {
        var access = new FixtureAccess(unreadable: ["employee.salary"]);
        var definition = Surface(Block("pay-line", new LayoutTextBinding(
        [
            new LayoutTextRun(Text: "Name: "),
            new LayoutTextRun(FieldPath: "employee.name"),
            new LayoutTextRun(Text: " earns "),
            new LayoutTextRun(FieldPath: "employee.salary", Fallback: "-"),
        ])));

        var error = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(definition, access));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal((LayoutDefinitionCodes.BindingUnreadable, "/blocks/0/binding/runs/3/field_path"), (refusal.Code, refusal.Pointer));
        // Each field run is asked about exactly as a record-field binding naming it; the text binding
        // itself and its literal runs never reach the port.
        Assert.Equal(["employee.name", "employee.salary"], access.Asked);
        Assert.All(access.Kinds, kind => Assert.Equal(nameof(LayoutRecordFieldBinding), kind));
    }

    [Fact(DisplayName = "layout-auth-24: a binding nested in a repeating block is checked like one at the root")]
    public void ANestedBindingTheAuthorCouldNotReadRefuses()
    {
        var definition = Surface(
            Block("lines", new LayoutQueryBinding("view.lines"), container: new LayoutContainer(LayoutContainerKind.Stack), repeating: true,
                children: [Block("cost", new LayoutRecordFieldBinding("line.cost"))]));
        var error = Assert.Throws<DefinitionRefusalException>(() =>
            LayoutDefinitionAdmission.ValidateForAuthoring(definition, new FixtureAccess(unreadable: ["line.cost"])));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal((LayoutDefinitionCodes.BindingUnreadable, "/blocks/0/children/0/binding"), (refusal.Code, refusal.Pointer));
    }

    [Fact(DisplayName = "layout-auth-36: a filter-propagation edge refuses when its target's binding cannot be narrowed by a selection")]
    public void AFilterEdgeToABlockASelectionCannotNarrowRefuses()
    {
        var definition = Surface(
            Block("orders", new LayoutQueryBinding("view.orders"), filterTargets: ["open-orders", "order-total", "customer-name", "help"]),
            Block("open-orders", new LayoutQueryBinding("view.open-orders")),
            Block("order-total", new LayoutMeasureBinding("orders.total")),
            Block("customer-name", new LayoutRecordFieldBinding("customer.name")),
            Block("help", new LayoutStaticBinding(JsonSerializer.SerializeToElement("Help"))));

        var error = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, LayoutTestAccess.GrantsAll));

        // A query and a measure are sets a selection narrows; a record field and static content are not.
        Assert.Equal(
            [(LayoutDefinitionCodes.FilterTargetUnnarrowable, "/blocks/0/filter_targets/2"), (LayoutDefinitionCodes.FilterTargetUnnarrowable, "/blocks/0/filter_targets/3")],
            error.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
    }

    [Fact(DisplayName = "layout-auth-36: a drill-through target the author could not open refuses; an openable one admits")]
    public void ADrillThroughTargetTheAuthorCouldNotOpenRefuses()
    {
        var definition = Surface(Block("orders", new LayoutQueryBinding("view.orders"))) with
        {
            DrillThroughTargets = ["surface.order-detail", "surface.payroll"],
        };
        var access = new FixtureAccess(unopenable: ["surface.payroll"]);
        var error = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(definition, access));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal((LayoutDefinitionCodes.DrillThroughForbidden, "/drill_through_targets/1"), (refusal.Code, refusal.Pointer));
        Assert.Equal(["surface.order-detail", "surface.payroll"], access.Opened);

        LayoutDefinitionAdmission.ValidateForPublish(definition, new FixtureAccess());
    }

    private sealed class FixtureAccess(IEnumerable<string>? unreadable = null, IEnumerable<string>? unopenable = null) : ILayoutAccess
    {
        private readonly HashSet<string> _unreadable = new(unreadable ?? [], StringComparer.Ordinal);
        private readonly HashSet<string> _unopenable = new(unopenable ?? [], StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public List<string> Opened { get; } = [];

        public List<string> Kinds { get; } = [];

        public bool CanRead(LayoutBinding binding)
        {
            var name = binding switch
            {
                LayoutQueryBinding value => value.ViewDefinitionId,
                LayoutMeasureBinding value => value.MeasurePath,
                LayoutRecordFieldBinding value => value.FieldPath,
                LayoutTemplateBinding value => value.TemplateDefinitionId,
                _ => "static",
            };
            Asked.Add(name);
            Kinds.Add(binding.GetType().Name);
            return !_unreadable.Contains(name);
        }

        public bool CanOpen(string surfaceId)
        {
            Opened.Add(surfaceId);
            return !_unopenable.Contains(surfaceId);
        }
    }

    private static LayoutDefinition Surface(params LayoutBlock[] blocks) => new(
        new LayoutDefinitionEnvelope(
            "surface.orders",
            "1.0.0",
            "tenant-a",
            LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { package = "orders-domain" }),
            "regulated",
            LegalHold: false,
            Requires: [new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")]),
        SchemaVersion: 1,
        Medium: LayoutMedium.Screen,
        DefaultIntent: LayoutIntent.Observe,
        Blocks: blocks,
        PageLayouts: [],
        PageMasters: [],
        PageRuns: [],
        SubmitGate: null,
        DrillThroughTargets: []);

    private static LayoutBlock Block(
        string id,
        LayoutBinding binding,
        LayoutContainer? container = null,
        bool repeating = false,
        IReadOnlyList<LayoutBlock>? children = null,
        IReadOnlyList<string>? filterTargets = null)
        => new(id, "layout.text", binding, children ?? [], Container: container, Repeating: repeating, FilterTargets: filterTargets);
}

/// <summary>An author who may read every source and open every surface, for tests whose subject is not authorization.</summary>
internal static class LayoutTestAccess
{
    public static ILayoutAccess GrantsAll { get; } = new AllowAll();

    private sealed class AllowAll : ILayoutAccess
    {
        public bool CanRead(LayoutBinding binding) => true;

        public bool CanOpen(string surfaceId) => true;
    }
}
