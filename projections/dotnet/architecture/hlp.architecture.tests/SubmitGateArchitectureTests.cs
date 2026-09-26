using System.Reflection;
using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.LayoutRuntime;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms;
using Harborline.Foundation.Forms.Engine;
using Harborline.Foundation.Forms.Exceptions;
using Harborline.Foundation.Forms.Models;

using Xunit;

namespace Harborline.Architecture.Tests;

/// <summary>
/// T-755 (T-724 ruling 81): Forms (forms-ck-4) and Layout (layout-auth-23, layout-ck-31) gate a submit with
/// one <c>hlp.contracts.authorization</c> type, and both resolve it identically.
/// </summary>
public sealed class SubmitGateArchitectureTests
{
    private static readonly AuthorizationCapabilityRegister Capabilities =
        AuthorizationCapabilityRegister.FromDeclarations([new(new("records:write"), 1)]);

    [Fact(DisplayName = "layout-auth-23, forms-ck-4: both editors and both runtimes carry the one hlp.contracts.authorization SubmitGate, and no engine declares its own")]
    public void FormsAndLayoutShareOneGateType()
    {
        Assert.Equal(typeof(SubmitGate), Nullable(typeof(FormDefinition).GetProperty(nameof(FormDefinition.SubmitGate))!));
        Assert.Equal(typeof(SubmitGate), Nullable(typeof(LayoutDefinition).GetProperty(nameof(LayoutDefinition.SubmitGate))!));
        Assert.Contains(typeof(SubmitGate), typeof(IFormSubmitGateAccess).GetMethod(nameof(IFormSubmitGateAccess.SatisfiesAsync))!.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(SubmitGate), Assert.Single(typeof(ILayoutSubmitAccess).GetMethod(nameof(ILayoutSubmitAccess.Satisfies))!.GetParameters()).ParameterType);

        var duplicates = new[] { typeof(FormDefinition).Assembly, typeof(IFormSubmitGateAccess).Assembly, typeof(LayoutDefinition).Assembly, typeof(ILayoutSubmitAccess).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Name.EndsWith("SubmitGate", StringComparison.Ordinal));
        Assert.Empty(duplicates);
    }

    public static TheoryData<string> Gates => ["role", "standing", "capability", "unregistered", "none", "two-arms", "malformed-name"];

    [Theory(DisplayName = "layout-auth-23, forms-ck-4: Forms and Layout admit or refuse the same gate identically, at the same pointer")]
    [MemberData(nameof(Gates))]
    public async Task FormsAndLayoutResolveTheGateIdentically(string name)
    {
        var gate = name switch
        {
            "role" => new SubmitGate(Role: RoleReference.Domain("inspector")),
            "standing" => new SubmitGate(Standing: new("author")),
            "capability" => new SubmitGate(Capability: new("records:write")),
            "unregistered" => new SubmitGate(Capability: new("layout:submit")),
            "none" => new SubmitGate(),
            "two-arms" => new SubmitGate(Role: RoleReference.Domain("inspector"), Capability: new("records:write")),
            _ => new SubmitGate(Capability: new("Records:Write")),
        };

        Assert.Equal(await FormsVerdict(gate), LayoutVerdict(gate));
    }

    // null when admitted, else the refusal's pointer.
    private static async Task<string?> FormsVerdict(SubmitGate gate)
    {
        using var store = new InMemoryFormDefinitionStore(TimeProvider.System, Capabilities);
        try
        {
            await store.RegisterAsync(Form(gate));
            return null;
        }
        catch (FormDefinitionValidationException refusal)
        {
            return refusal.Target;
        }
    }

    private static string? LayoutVerdict(SubmitGate gate)
    {
        try
        {
            LayoutDefinitionAdmission.ValidateForAuthoring(Surface(gate), LayoutHostRegisters.Platform with { Capabilities = Capabilities }, new GrantsAll());
            return null;
        }
        catch (DefinitionRefusalException refusal)
        {
            return Assert.Single(refusal.Refusals).Pointer;
        }
    }

    private static Type Nullable(PropertyInfo property) => System.Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

    private static FormDefinition Form(SubmitGate gate)
    {
        var fields = new Dictionary<string, FieldOverlay> { ["name"] = new(InternationalizedText.FromInvariant("name")) };
        var access = new SectionAccess(ReadRoles: [RoleReference.Domain("*")], WriteRoles: [RoleReference.Domain("tenant:admin")]);
        var now = DateTimeOffset.UnixEpoch;
        return new FormDefinition(
            Id: new FormDefinitionId("submit-gate"),
            Version: new SemanticVersion(1, 0, 0),
            Status: FormDefinitionStatus.Draft,
            Tenant: new TenantId("tenant:acme"),
            Owner: IdentityRef.System,
            SchemaRef: new SchemaId("sha256:submit-gate"),
            Overlay: new HarborlineOverlay(
                Fields: fields,
                Sections: [new FormSection("sec", InternationalizedText.FromInvariant("sec"), ["name"], access)],
                Rules: []),
            Lineage: null,
            CreatedAt: now,
            UpdatedAt: now,
            SubmitGate: gate);
    }

    // A capture surface that pins no form, so it keeps its own gate (layout-ck-31).
    private static LayoutDefinition Surface(SubmitGate gate) => new(
        new LayoutDefinitionEnvelope("surface.customer-edit", "1.0.0", "tenant-a", LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { package = "orders-domain" }), "regulated", LegalHold: false,
            Requires: [new LayoutDefinitionRequirement(LayoutPackIdentity.Capability, "1.0.0")]),
        SchemaVersion: 1,
        Medium: LayoutMedium.Screen,
        DefaultIntent: LayoutIntent.Capture,
        Blocks: [new LayoutBlock("name", "layout.field", new LayoutRecordFieldBinding("customer.name"), [])],
        PageLayouts: [],
        PageMasters: [],
        PageRuns: [],
        SubmitGate: gate,
        DrillThroughTargets: []);

    private sealed class GrantsAll : ILayoutAccess
    {
        public bool CanRead(LayoutBinding binding) => true;

        public bool CanOpen(string surfaceId) => true;
    }
}
