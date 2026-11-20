using Harborline.Contracts.Authorization;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class SchedulingPermissionsTests
{
    [Fact(DisplayName = "execution-runtime-cc-4: ADR-0181 declares author, plan and commit at version one")]
    public void DeclaresTheThreeCapabilities()
    {
        Assert.Equal(["scheduling:author", "scheduling:plan", "scheduling:commit"],
            SchedulingPermissions.Declarations.Select(d => d.Capability.Name));
        var register = AuthorizationCapabilityRegister.FromDeclarations(SchedulingPermissions.Declarations);
        Assert.All(SchedulingPermissions.Declarations, declaration => Assert.Equal(1, declaration.Version));
        foreach (var name in new[] { "scheduling:author", "scheduling:plan", "scheduling:commit" })
            Assert.NotNull(register.Resolve(new(name)));
        Assert.Equal("scheduling:author", SchedulingPermissions.Author);
        Assert.Equal("scheduling:plan", SchedulingPermissions.Plan);
        Assert.Equal("scheduling:commit", SchedulingPermissions.Commit);
        Assert.Equal("schedule", SchedulingPermissions.ScheduleRecordKind);
    }
}
