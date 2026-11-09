using Harborline.Contracts.Authorization;

namespace Harborline.Foundation.Forms.Tests;

/// <summary>
/// The role vocabulary and standing set the Forms tests publish gated forms against (T-485/submit-gate-name-resolution,
/// forms-auth-11, L356). Publication now resolves a submit gate's role and standing arms, so a test that publishes a
/// gated form supplies the registers that declare its gate's names rather than dropping the gate. Each name here is a
/// name an existing test gates on; nothing is declared that no test uses.
/// </summary>
internal static class FormGateRegisters
{
    public static readonly RoleReference Inspector = RoleReference.Domain("inspector");
    public static readonly RoleReference TenantAdmin = RoleReference.Domain("tenant:admin");
    public static readonly RecordStandingReference Author = new("author");

    public static readonly RoleVocabulary Roles = RoleVocabulary.FromApi(
    [
        new(Guid.Parse("4f0c2b8e-0000-4000-8000-000000000001"), Inspector, "Inspector", new(RoleOwnerKind.Package, "forms-tests"), IsSealed: false),
        new(Guid.Parse("4f0c2b8e-0000-4000-8000-000000000002"), TenantAdmin, "Tenant admin", new(RoleOwnerKind.Package, "forms-tests"), IsSealed: false),
    ]);

    public static readonly IReadOnlySet<RecordStandingReference> Standings = new HashSet<RecordStandingReference> { Author };

    /// <summary>The store with these registers, taking the same optional arguments as its constructor.</summary>
    public static InMemoryFormDefinitionStore Store(
        TimeProvider? time = null,
        AuthorizationCapabilityRegister? capabilities = null,
        IFormRecordTypePolicyReader? recordTypePolicies = null,
        IFormRecordCreationDeclarations? recordCreations = null,
        IFormRecordCreationDeclarations? recordCreationProducer = null) =>
        new(time ?? TimeProvider.System, capabilities, recordTypePolicies, recordCreations, recordCreationProducer, Roles, Standings);
}
