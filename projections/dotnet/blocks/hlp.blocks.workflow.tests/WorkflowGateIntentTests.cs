using Harborline.Blocks.Workflow.Durable;
using Harborline.Contracts.Authorization;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowGateIntentTests
{
    // Oracle: the exact-question binding property from T-1176 and DES-0032, using independent literals.
    private static WorkflowGateIntent Intent() => new(
        "tenant", "principal", "instance", "definition", "1.0.0", WorkflowGateElement.Transition,
        "approve", [new RoleReference("sys.roles", "Approver")],
        new DateTimeOffset(2026, 11, 10, 12, 34, 56, TimeSpan.Zero));

    [Fact]
    [Trait("Holds", "workflows-auth-10")]
    public void An_identical_question_including_the_decision_instant_binds()
    {
        Assert.True(Intent().Binds(Intent()));
        Assert.False(Intent().Binds(null));
    }

    [Theory]
    [Trait("Holds", "workflows-auth-10")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void A_changed_question_field_including_one_tick_of_decision_time_does_not_bind(int field)
    {
        var asked = Intent();
        var answer = field switch
        {
            0 => Intent() with { Tenant = "other" },
            1 => Intent() with { Principal = "other" },
            2 => Intent() with { InstanceId = "other" },
            3 => Intent() with { DefinitionKey = "other" },
            4 => Intent() with { DefinitionVersion = "2.0.0" },
            5 => Intent() with { Element = WorkflowGateElement.Action },
            6 => Intent() with { ElementId = "other" },
            7 => Intent() with { RequiredRoles = [] },
            _ => Intent() with { DecidedAt = new DateTimeOffset(2026, 11, 10, 12, 34, 56, TimeSpan.Zero).AddTicks(1) },
        };
        Assert.False(asked.Binds(answer));
    }
}
