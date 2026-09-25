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
        var registers = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Access: access);

        foreach (var validate in new Action[]
        {
            () => LayoutDefinitionAdmission.ValidateForAuthoring(definition, registers),
            () => LayoutDefinitionAdmission.ValidateForPublish(definition, registers),
        })
        {
            var error = Assert.Throws<LayoutDefinitionAdmissionException>(validate);
            Assert.Equal(
                ["/blocks/1/binding", "/blocks/2/binding", "/blocks/3/binding"],
                error.Refusals.Where(refusal => refusal.Code == LayoutDefinitionCodes.BindingUnreadable).Select(refusal => refusal.Pointer));
        }

        // Static content is authored on the block and read from nowhere, so Access is never asked about it.
        Assert.DoesNotContain("static", access.Asked);
        // The same surface admits once the author can read every source it binds.
        LayoutDefinitionAdmission.ValidateForPublish(definition, registers with { Access = new FixtureAccess() });
    }

    [Fact(DisplayName = "layout-auth-24: a binding nested in a repeating block is checked like one at the root")]
    public void ANestedBindingTheAuthorCouldNotReadRefuses()
    {
        var definition = Surface(
            Block("lines", new LayoutQueryBinding("view.lines"), container: new LayoutContainer(LayoutContainerKind.Stack), repeating: true,
                children: [Block("cost", new LayoutRecordFieldBinding("line.cost"))]));
        var registers = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Access: new FixtureAccess(unreadable: ["line.cost"]));

        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition, registers));
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

        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(definition));

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
        var registers = new LayoutHostRegisters(LayoutBlockKindRegistry.Platform, Access: access);

        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() => LayoutDefinitionAdmission.ValidateForPublish(definition, registers));
        var refusal = Assert.Single(error.Refusals);
        Assert.Equal((LayoutDefinitionCodes.DrillThroughForbidden, "/drill_through_targets/1"), (refusal.Code, refusal.Pointer));
        Assert.Equal(["surface.order-detail", "surface.payroll"], access.Opened);

        LayoutDefinitionAdmission.ValidateForPublish(definition, registers with { Access = new FixtureAccess() });
    }

    private sealed class FixtureAccess(IEnumerable<string>? unreadable = null, IEnumerable<string>? unopenable = null) : ILayoutAccess
    {
        private readonly HashSet<string> _unreadable = new(unreadable ?? [], StringComparer.Ordinal);
        private readonly HashSet<string> _unopenable = new(unopenable ?? [], StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public List<string> Opened { get; } = [];

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
