using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Contracts.Fields;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-583 slice 3: a surface narrows what Records and other packages declared and never widens it
/// (DES-0052 layout-auth-29, layout-auth-30). Every refusal is raised with its stage stated explicitly.
/// </summary>
public sealed class LayoutNarrowingRefusalTests
{
    private static readonly LayoutHostRegisters RequiredReference = LayoutHostRegisters.Platform with
    {
        Fields = new LayoutRecordFieldRegistry([new LayoutRecordFieldDescriptor("invoice.reference", FieldScalarValueShape.Text, HasValueDomain: false, Required: true)]),
    };

    [Fact(DisplayName = "layout-auth-29: an explicit Required = false on a field Records requires is refused at authoring and publication (T-724 ruling 78)")]
    public void AnExplicitFalseOnARecordsRequiredFieldIsRefused()
    {
        var surface = LayoutBoundRegisterTests.Sealed(LayoutBoundRegisterTests.CaptureSurface(new LayoutCaptureProperties(false, [])));

        var authoring = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForAuthoring(surface, RequiredReference, LayoutTestAccess.GrantsAll));
        Assert.Equal(DefinitionAdmissionPhase.Author, authoring.Stage);
        Assert.Equal([new DefinitionRefusal(LayoutDefinitionCodes.RequirementDropped, "/blocks/0/capture/required")], authoring.Refusals);

        var publishing = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(surface, RequiredReference, LayoutTestAccess.GrantsAll));
        Assert.Equal(DefinitionAdmissionPhase.Publish, publishing.Stage);
        Assert.Equal([new DefinitionRefusal(LayoutDefinitionCodes.RequirementDropped, "/blocks/0/capture/required")], publishing.Refusals);

        // Publication cannot tell whether Records requires a field it cannot look up, so an explicit
        // false there fails closed rather than being admitted unchecked (T-724 rulings 36 and 75).
        var unknown = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(surface, LayoutHostRegisters.Platform, LayoutTestAccess.GrantsAll));
        Assert.Equal([new DefinitionRefusal(LayoutDefinitionCodes.CaptureFieldUnknown, "/blocks/0/capture/required")], unknown.Refusals);

        // The same false on a field Records leaves optional overrides nothing and admits.
        var optional = LayoutHostRegisters.Platform with
        {
            Fields = new LayoutRecordFieldRegistry([new LayoutRecordFieldDescriptor("invoice.reference", FieldScalarValueShape.Text, HasValueDomain: false)]),
        };
        LayoutDefinitionAdmission.ValidateForPublish(surface, optional, LayoutTestAccess.GrantsAll);
    }

    [Fact(DisplayName = "layout-auth-29: an omitted Required override keeps the Records requirement rather than being read as false (T-724 ruling 78)")]
    public void AnOmittedRequiredKeepsTheRecordsRequirement()
    {
        var authored = LayoutBoundRegisterTests.Sealed(LayoutBoundRegisterTests.CaptureSurface(new LayoutCaptureProperties(null, [])));
        var json = Encoding.UTF8.GetString(LayoutDefinitionJson.SerializeCanonical(authored));
        var capture = JsonNode.Parse(json)!["blocks"]![0]!["capture"]!.AsObject();

        // Omission travels as omission: nothing is written, and nothing is read back as false.
        Assert.False(capture.ContainsKey("required"));
        var persisted = LayoutDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));
        Assert.Null(persisted.Blocks[0].Capture!.Required);

        // An omitted override is no override: it admits on a Records-required field at every stage.
        LayoutDefinitionAdmission.ValidateForAuthoring(persisted, RequiredReference, LayoutTestAccess.GrantsAll);
        LayoutDefinitionAdmission.ValidateForPublish(persisted, RequiredReference, LayoutTestAccess.GrantsAll);
        LayoutPersistedValueAdmission.ValidateForRuntime(persisted, RequiredReference);
        // Adding a requirement is the one override a surface may make (layout-auth-21).
        LayoutDefinitionAdmission.ValidateForPublish(persisted with { Blocks = [persisted.Blocks[0] with { Capture = new LayoutCaptureProperties(true, []) }] },
            RequiredReference, LayoutTestAccess.GrantsAll);
    }

    [Fact(DisplayName = "layout-auth-29: value-domain widening is impossible by construction, because capture properties carry no value-domain member (T-724 ruling 78)")]
    public void CapturePropertiesCarryNoValueDomainMember()
    {
        // The surface contract can add a requirement, name rules, override the prompt and pick a
        // registered control, whose parameters its own declared schema admits (T-724 ruling 38). A
        // member that could state permitted values would need its own widening check first.
        Assert.Equal(["Control", "PromptOverride", "Required", "ValidationRules"], typeof(LayoutCaptureProperties).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(["Id", "Parameters"], typeof(LayoutFieldControl).GetProperties().Select(property => property.Name).Order());
    }

    private static readonly CrossPackageEndpoint Surface = new("pkg.billing", "surface.invoice", "1.0.0", new string('a', 64));
    private static readonly CrossPackageEndpoint Orders = new("pkg.orders", "view.open-orders", "2.1.0", new string('b', 64));
    private static readonly CrossPackageEndpoint Totals = new("pkg.orders", "measure.order-total", "1.0.0", new string('c', 64));
    private static readonly CrossPackageEndpoint Hidden = new("pkg.orders", "view.internal", "1.0.0", new string('d', 64));
    private static readonly PackageExposure OrdersExposure = new("pkg.orders", [Orders, Totals]);

    private static LayoutDefinition Consumer(params string[] requires) => LayoutBoundRegisterTests.CaptureSurface(new LayoutCaptureProperties(null, [])) with
    {
        Envelope = LayoutBoundRegisterTests.CaptureSurface(new LayoutCaptureProperties(null, [])).Envelope with
        {
            Requires = [new(LayoutPackIdentity.Capability, "1.0.0"), .. requires.Select(package => new LayoutDefinitionRequirement(package))],
        },
    };

    private static CrossPackageReference Reference(string pointer, CrossPackageEndpoint target) => new(pointer, Surface, target);

    [Fact(DisplayName = "layout-auth-30 (authoring-only): a cross-package edge the surface requires and the producer exposes at that version is permitted")]
    public void ADeclaredAndExposedEdgeIsPermitted()
    {
        var report = LayoutCrossPackageAuthoring.Check(Consumer("pkg.orders"), [Reference("/blocks/0/binding", Orders), Reference("/blocks/1/binding", Totals)], [OrdersExposure]);
        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Empty(report.Refusals);
    }

    [Fact(DisplayName = "layout-auth-30 (authoring-only): a cross-package edge with no declared dependency on either side refuses by name at its pointer, naming the target only where the producer exposes it")]
    public void AnEdgeUndeclaredOnEitherSideRefuses()
    {
        var stale = Orders with { Version = "2.0.0" };
        var report = LayoutCrossPackageAuthoring.Check(Consumer(),
            [Reference("/blocks/0/binding", Orders), Reference("/blocks/1/binding", Hidden), Reference("/blocks/2/binding", Surface with { DefinitionId = "view.sibling" })],
            [OrdersExposure]);
        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Equal(
            [new DefinitionRefusal(LayoutDefinitionCodes.ReferenceDependencyUndeclared, "/blocks/0/binding", "pkg.orders/view.open-orders@2.1.0"),
             new DefinitionRefusal(LayoutDefinitionCodes.ReferenceDependencyUndeclared, "/blocks/1/binding"),
             new DefinitionRefusal(LayoutDefinitionCodes.ReferenceNotExposed, "/blocks/1/binding")],
            report.Refusals);

        // Declared on the consumer's side, but the producer exposes another version or none at all.
        var declared = LayoutCrossPackageAuthoring.Check(Consumer("pkg.orders"), [Reference("/blocks/0/binding", stale), Reference("/blocks/1/binding", Hidden)], [OrdersExposure]);
        Assert.Equal(
            [new DefinitionRefusal(LayoutDefinitionCodes.ReferenceExposureIncompatible, "/blocks/0/binding", "pkg.orders/view.open-orders@2.1.0"),
             new DefinitionRefusal(LayoutDefinitionCodes.ReferenceNotExposed, "/blocks/1/binding")],
            declared.Refusals);
    }
}
