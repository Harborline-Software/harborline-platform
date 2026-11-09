using Harborline.Contracts.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harborline.Foundation.Forms.Engine.Tests;

/// <summary>
/// The role vocabulary the engine tests publish gated forms against (T-485/submit-gate-name-resolution, forms-auth-11,
/// L356). Publication now resolves a submit gate's role arm, so a test that publishes a role-gated form through the real
/// definition store supplies a vocabulary that declares its gate's role rather than dropping the gate. Each role here is
/// one an existing test publishes a gate on.
/// </summary>
internal static class FormGateRegisters
{
    public static readonly RoleVocabulary Roles = RoleVocabulary.FromApi(
    [
        Role("4f0c2b8e-0000-4000-8000-000000000011", "inspector", "Inspector"),
        Role("4f0c2b8e-0000-4000-8000-000000000012", "author", "Author"),
        Role("4f0c2b8e-0000-4000-8000-000000000013", "admin", "Admin"),
    ]);

    /// <summary>The store with this vocabulary, taking the same optional arguments as its constructor.</summary>
    public static InMemoryFormDefinitionStore Store(
        TimeProvider? time = null,
        IFormRecordTypePolicyReader? recordTypePolicies = null) =>
        new(time ?? TimeProvider.System, recordTypePolicies: recordTypePolicies, roles: Roles);

    /// <summary>
    /// Registers the vocabulary so a container-built definition store resolves role gates through it. The store's
    /// register-taking constructor also needs a <see cref="TimeProvider"/>; without one the container falls back to the
    /// parameterless constructor, which has no registers and so refuses every role arm at publication. The system clock is
    /// added only when the test registered none.
    /// </summary>
    public static IServiceCollection AddFormGateRegisters(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        return services.AddSingleton(Roles);
    }

    private static RoleDefinition Role(string id, string name, string display) =>
        new(Guid.Parse(id), RoleReference.Domain(name), display, new(RoleOwnerKind.Package, "forms-engine-tests"), IsSealed: false);
}
