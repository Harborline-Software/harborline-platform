using System.Text.Json;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Authorization;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-583 slice 2: the role and standing arms of <c>submit_gate</c> (DES-0052 layout-auth-23, T-724 ruling 77)
/// and Layout's declared permissions (T-724 ruling 76).
/// </summary>
public sealed class LayoutSubmitGateTests
{
    private static readonly RoleReference Editor = RoleReference.Domain("customer-editor");

    private static readonly RoleVocabulary Roles = RoleVocabulary.FromApi(
        [new(Guid.Parse("7d4c1a9e-0000-4000-8000-000000000001"), Editor, "Customer editor", new(RoleOwnerKind.Package, "orders-domain"), IsSealed: false)]);

    private static readonly IReadOnlySet<RecordStandingReference> Standings = new HashSet<RecordStandingReference> { new("assigned-reviewer") };

    [Fact(DisplayName = "layout-auth-23: a submit gate's role arm resolves through Access's role vocabulary, and an undefined role refuses by name")]
    public void TheRoleArmResolvesThroughTheRoleVocabulary()
    {
        var registers = LayoutHostRegisters.Platform with { Roles = Roles };
        LayoutDefinitionAdmission.ValidateForAuthoring(Capture(new(Role: Editor)), registers, LayoutTestAccess.GrantsAll);
        LayoutDefinitionAdmission.ValidateForPublish(Capture(new(Role: Editor)), registers, LayoutTestAccess.GrantsAll);

        foreach (var role in new[] { RoleReference.Domain("customer-approver"), new RoleReference("tenant.invented", "customer-editor") })
            AssertRefused(Capture(new(Role: role)), registers, LayoutDefinitionCodes.SubmitGateRoleUnknown, "/submit_gate/role");
        // Publication fails closed without the vocabulary; nothing is string-matched.
        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() =>
            LayoutDefinitionAdmission.ValidateForPublish(Capture(new(Role: Editor)), LayoutHostRegisters.Platform, LayoutTestAccess.GrantsAll));
        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.SubmitGateRoleUnknown, "/submit_gate/role")], error.Refusals);
    }

    [Fact(DisplayName = "layout-auth-23: a submit gate's standing arm resolves through the declared standings, and an undeclared standing refuses by name")]
    public void TheStandingArmResolvesThroughTheDeclaredStandings()
    {
        var registers = LayoutHostRegisters.Platform with { Standings = Standings };
        LayoutDefinitionAdmission.ValidateForAuthoring(Capture(new(Standing: new("assigned-reviewer"))), registers, LayoutTestAccess.GrantsAll);
        LayoutDefinitionAdmission.ValidateForPublish(Capture(new(Standing: new("assigned-reviewer"))), registers, LayoutTestAccess.GrantsAll);

        AssertRefused(Capture(new(Standing: new("account-owner"))), registers, LayoutDefinitionCodes.SubmitGateStandingUnknown, "/submit_gate/standing/name");
        var error = Assert.Throws<LayoutDefinitionAdmissionException>(() =>
            LayoutDefinitionAdmission.ValidateForPublish(Capture(new(Standing: new("assigned-reviewer"))), LayoutHostRegisters.Platform, LayoutTestAccess.GrantsAll));
        Assert.Equal([new LayoutDefinitionRefusal(LayoutDefinitionCodes.SubmitGateStandingUnknown, "/submit_gate/standing/name")], error.Refusals);
    }

    [Fact(DisplayName = "T-724 ruling 76: Layout declares layout:author, layout:publish and layout:open, and no layout:submit")]
    public void LayoutDeclaresAuthorPublishAndOpenAndNoSubmit()
    {
        Assert.Equal(["layout:author", "layout:publish", "layout:open"], LayoutPermissions.Declarations.Select(declaration => declaration.Capability.Name));
        var register = AuthorizationCapabilityRegister.FromDeclarations([.. LayoutPermissions.Declarations, new(new("records:write"), 1)]);
        foreach (var name in new[] { LayoutPermissions.Author, LayoutPermissions.Publish, LayoutPermissions.Open })
            Assert.NotNull(register.Resolve(new(name)));
        Assert.Null(register.Resolve(new("layout:submit")));

        // So a surface cannot gate its submit on a capability Layout would declare for itself.
        AssertRefused(Capture(new(Capability: new("layout:submit"))), LayoutHostRegisters.Platform with { Capabilities = register },
            LayoutDefinitionCodes.SubmitGateCapabilityUnknown, "/submit_gate/capability/name");
    }

    private static void AssertRefused(LayoutDefinition definition, LayoutHostRegisters registers, string code, string pointer)
    {
        foreach (var validate in new Action[]
        {
            () => LayoutDefinitionAdmission.ValidateForAuthoring(definition, registers, LayoutTestAccess.GrantsAll),
            () => LayoutDefinitionAdmission.ValidateForPublish(definition, registers, LayoutTestAccess.GrantsAll),
        })
        {
            var error = Assert.Throws<LayoutDefinitionAdmissionException>(validate);
            Assert.Equal([new LayoutDefinitionRefusal(code, pointer)], error.Refusals);
        }
    }

    private static LayoutDefinition Capture(LayoutSubmitGate gate) => new(
        new LayoutDefinitionEnvelope(
            "surface.customer-edit",
            "1.0.0",
            "tenant-a",
            LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { package = "orders-domain" }),
            "regulated",
            LegalHold: false,
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
}
