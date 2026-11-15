namespace Harborline.Blocks.Workflow.Durable;

/// <summary>Maps engine state to execution-runtime wire names (ADR-0132 decision 5).</summary>
public static class WorkflowLifecycleStatus
{
    /// <summary>Returns the substrate status wire name for an engine status.</summary>
    public static string WireName(WorkflowStatus status) => status switch
    {
        WorkflowStatus.Running or WorkflowStatus.Parked or WorkflowStatus.Suspended => "running",
        WorkflowStatus.Completed => "succeeded",
        WorkflowStatus.Failed => "dead-lettered",
        WorkflowStatus.Cancelled or WorkflowStatus.Terminated => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
