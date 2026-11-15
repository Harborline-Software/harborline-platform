using Harborline.Blocks.Workflow.Durable;
using Harborline.Foundation.ExecutionRuntime;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowLifecycleStatusTests
{
    [Theory]
    [InlineData(WorkflowStatus.Running, "running")]
    [InlineData(WorkflowStatus.Parked, "running")]
    [InlineData(WorkflowStatus.Suspended, "running")]
    [InlineData(WorkflowStatus.Completed, "succeeded")]
    [InlineData(WorkflowStatus.Failed, "dead-lettered")]
    [InlineData(WorkflowStatus.Cancelled, "cancelled")]
    [InlineData(WorkflowStatus.Terminated, "cancelled")]
    public void Workflow_status_maps_to_the_ADR_wire_name_and_a_substrate_status(WorkflowStatus status, string expected)
    {
        var actual = WorkflowLifecycleStatus.WireName(status);
        Assert.Equal(expected, actual);
        Assert.Contains(Enum.GetValues<RunStatus>(), value => RunStatusTransitions.WireName(value) == actual);
    }
}
