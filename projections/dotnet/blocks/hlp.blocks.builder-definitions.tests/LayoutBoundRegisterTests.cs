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
