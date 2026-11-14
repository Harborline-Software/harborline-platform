using System.Text.Json.Nodes;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>A lifecycle event projected from one ordered history entry.</summary>
public sealed record WorkflowLifecycleEvent(long Sequence, string Type, string DataJson);

/// <summary>Projects history into the CNCF Serverless Workflow DSL v1.0.0 lifecycle vocabulary.</summary>
/// <remarks>
/// Lifecycle history uses WorkflowStarted, WorkflowSuspended, WorkflowResumed, WorkflowCorrelationStarted,
/// WorkflowCorrelationCompleted, WorkflowCancelled, WorkflowTerminated, WorkflowFaulted, WorkflowCompleted,
/// TaskCreated, TaskStarted, TaskSuspended, TaskResumed, TaskRetried, TaskCancelled, TaskFaulted and
/// TaskCompleted. Its JSON data carries the corresponding v1.0.0 event payload; termination also carries reason.
/// Other history entries are not lifecycle events.
/// </remarks>
public static class WorkflowLifecycleProjection
{
    /// <summary>Reads history in order and returns lifecycle events without delivering them.</summary>
    public static async Task<IReadOnlyList<WorkflowLifecycleEvent>> ReadAsync(
        IWorkflowStore store, string instanceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var history = await store.EventsAsync(instanceId, ct).ConfigureAwait(false);
        var events = new List<WorkflowLifecycleEvent>();
        foreach (var entry in history)
        {
            var type = entry.EventType switch
            {
                "WorkflowStarted" => "io.serverlessworkflow.workflow.started.v1",
                "WorkflowSuspended" => "io.serverlessworkflow.workflow.suspended.v1",
                "WorkflowResumed" => "io.serverlessworkflow.workflow.resumed.v1",
                "WorkflowCorrelationStarted" => "io.serverlessworkflow.workflow.correlation-started.v1",
                "WorkflowCorrelationCompleted" => "io.serverlessworkflow.workflow.correlation-completed.v1",
                "WorkflowCancelled" or "WorkflowTerminated" => "io.serverlessworkflow.workflow.cancelled.v1",
                "WorkflowFaulted" => "io.serverlessworkflow.workflow.faulted.v1",
                "WorkflowCompleted" => "io.serverlessworkflow.workflow.completed.v1",
                "TaskCreated" => "io.serverlessworkflow.task.created.v1",
                "TaskStarted" => "io.serverlessworkflow.task.started.v1",
                "TaskSuspended" => "io.serverlessworkflow.task.suspended.v1",
                "TaskResumed" => "io.serverlessworkflow.task.resumed.v1",
                "TaskRetried" => "io.serverlessworkflow.task.retried.v1",
                "TaskCancelled" => "io.serverlessworkflow.task.cancelled.v1",
                "TaskFaulted" => "io.serverlessworkflow.task.faulted.v1",
                "TaskCompleted" => "io.serverlessworkflow.task.completed.v1",
                _ => null,
            };
            if (type is null) continue;
            var data = JsonNode.Parse(entry.DataJson) as JsonObject
                ?? throw new InvalidDataException("Lifecycle history data must be a JSON object.");
            var task = entry.EventType.StartsWith("Task", StringComparison.Ordinal);
            RequireString(data, task ? "workflow" : "name");
            if (task) RequireString(data, "task");
            var time = entry.EventType switch
            {
                "WorkflowStarted" or "WorkflowCorrelationStarted" or "TaskStarted" => "startedAt",
                "TaskCreated" => "createdAt",
                "WorkflowSuspended" or "TaskSuspended" => "suspendedAt",
                "WorkflowResumed" or "TaskResumed" => "resumedAt",
                "TaskRetried" => "retriedAt",
                "WorkflowCancelled" or "WorkflowTerminated" or "TaskCancelled" => "cancelledAt",
                "WorkflowFaulted" or "TaskFaulted" => "faultedAt",
                _ => "completedAt",
            };
            RequireString(data, time);
            if (!DateTimeOffset.TryParse(data[time]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _))
                throw new InvalidDataException("Lifecycle history has an invalid timestamp.");
            if (entry.EventType == "WorkflowStarted")
            {
                if (data["definition"] is not JsonObject definition)
                    throw new InvalidDataException("Workflow start history must include its definition.");
                RequireString(definition, "name");
                RequireString(definition, "namespace");
                RequireString(definition, "version");
            }
            if (entry.EventType is "WorkflowFaulted" or "TaskFaulted" && data["error"] is not JsonObject)
                throw new InvalidDataException("Fault history must include its error.");
            if (entry.EventType == "WorkflowTerminated")
            {
                if (data["reason"] is not JsonValue reason || !reason.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Termination history must include a reason.");
                data["forced"] = true;
            }
            events.Add(new WorkflowLifecycleEvent(entry.Seq, type, data.ToJsonString()));
        }
        return events;
    }

    private static void RequireString(JsonObject data, string property)
    {
        if (data[property] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Lifecycle history requires '{property}'.");
    }
}
