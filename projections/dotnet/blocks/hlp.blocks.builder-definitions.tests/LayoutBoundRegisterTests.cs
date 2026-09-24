using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// DES-0052's bound inventory: the developer-supplied registers a Layout surface names and
/// parameterises, and that admission checks every name against.
/// </summary>
public sealed class LayoutBoundRegisterTests
{
    private static readonly LayoutHostRegisters Controls = new(LayoutBlockKindRegistry.Platform, FieldControls: new LayoutFieldControlRegistry(["text", "currency"]));

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
            LayoutDefinitionCodes.CapturePropertiesInvalid, "/blocks/0/capture/control/parameters");
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
