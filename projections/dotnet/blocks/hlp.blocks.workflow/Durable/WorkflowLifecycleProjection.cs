using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>A lifecycle event projected from one ordered history entry.</summary>
public sealed record WorkflowLifecycleEvent(
    [property: JsonPropertyName("specversion")] string SpecVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] JsonElement Data);

/// <summary>Stable source namespaces pinned for this history fact.</summary>
public sealed record WorkflowLifecycleMetadata(string StoreNamespace, string TenantNamespace);

/// <summary>Refuses malformed lifecycle facts without silently dropping them.</summary>
public sealed class WorkflowLifecycleMappingException(string message) : Exception(message);

/// <summary>Projects history into the CNCF Serverless Workflow DSL v1.0.0 lifecycle vocabulary.</summary>
/// <remarks>
/// Lifecycle history uses WorkflowStarted, WorkflowSuspended, WorkflowResumed, WorkflowCorrelationStarted,
/// WorkflowCorrelationCompleted, WorkflowCancelled, WorkflowTerminated, WorkflowFaulted, WorkflowCompleted,
/// TaskCreated, TaskStarted, TaskSuspended, TaskResumed, TaskRetried, TaskCancelled, TaskFaulted and
/// TaskCompleted. Its JSON data carries the corresponding v1.0.0 event payload; termination also carries reason.
/// A run-ending task fact carries workflowStatus identifying the resulting workflow outcome.
/// Other history entries are not lifecycle events.
/// </remarks>
public static class WorkflowLifecycleProjection
{
    /// <summary>Version of the emitted envelope and payload contract.</summary>
    public const int Version = 1;

    /// <summary>Maps a committed fact without reading mutable state or delivering events.</summary>
    public static IReadOnlyList<WorkflowLifecycleEvent> Map(WorkflowEventRecord entry, WorkflowLifecycleMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.StoreNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(metadata.TenantNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.InstanceId);
        if (entry.Seq <= 0) throw new WorkflowLifecycleMappingException("History sequence must be positive.");
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
        if (type is null) return [];
        JsonObject data;
        try { data = JsonNode.Parse(entry.DataJson) as JsonObject ?? throw new WorkflowLifecycleMappingException("Lifecycle history data must be a JSON object."); }
        catch (System.Text.Json.JsonException) { throw new WorkflowLifecycleMappingException("Lifecycle history data is invalid JSON."); }
        var task = entry.EventType.StartsWith("Task", StringComparison.Ordinal);
        RequireString(data, task ? "workflow" : "name");
        if (task)
        {
            RequireString(data, "task");
            if (!Matches(data["task"]!.GetValue<string>(), JsonPointer))
                throw new WorkflowLifecycleMappingException("Task history requires 'task' to be a JSON Pointer.");
        }
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
        if (!Regex.IsMatch(data[time]!.GetValue<string>(), @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}[Tt][0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:[Zz]|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            || !DateTimeOffset.TryParse(data[time]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _))
            throw new WorkflowLifecycleMappingException("Lifecycle history has an invalid timestamp.");
        if (entry.EventType == "WorkflowStarted")
        {
            if (data["definition"] is not JsonObject definition)
                throw new WorkflowLifecycleMappingException("Workflow start history must include its definition.");
            RequireString(definition, "name");
            RequireString(definition, "namespace");
            RequireString(definition, "version");
            if (!Matches(definition["version"]!.GetValue<string>(), SemanticVersion))
                throw new WorkflowLifecycleMappingException("Workflow start history requires a semantic definition version.");
        }
        if (entry.EventType is "WorkflowFaulted" or "TaskFaulted")
        {
            if (data["error"] is not JsonObject error)
                throw new WorkflowLifecycleMappingException("Fault history must include its error.");
            RequireString(error, "type");
            foreach (var field in new[] { "title", "detail", "instance" })
                if (error.TryGetPropertyValue(field, out var value)
                    && (value is not JsonValue optional || !optional.TryGetValue<string>(out _)))
                    throw new WorkflowLifecycleMappingException($"Fault history has an invalid '{field}'.");
            if (error["status"] is not JsonValue status || !status.TryGetValue<int>(out _))
                throw new WorkflowLifecycleMappingException("Fault history requires an integer status.");
        }
        if (entry.EventType == "WorkflowTerminated")
        {
            if (data["reason"] is not JsonValue reason || !reason.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                throw new WorkflowLifecycleMappingException("Termination history must include a reason.");
            data["forced"] = true;
        }
        var allowed = new JsonObject();
        var fields = new List<string> { task ? "workflow" : "name", time };
        if (task) fields.Add("task");
        if (entry.EventType == "WorkflowStarted") fields.Add("definition");
        if (entry.EventType is "WorkflowFaulted" or "TaskFaulted") fields.Add("error");
        if (entry.EventType == "WorkflowTerminated") { fields.Add("reason"); fields.Add("forced"); }
        foreach (var field in fields) allowed[field] = data[field]?.DeepClone();
        if (allowed["definition"] is JsonObject definitionData)
            foreach (var key in definitionData.Select(pair => pair.Key).Except(["name", "namespace", "version"]).ToArray()) definitionData.Remove(key);
        if (allowed["error"] is JsonObject errorData)
            foreach (var key in errorData.Select(pair => pair.Key).Except(["type", "title", "status", "detail", "instance"]).ToArray()) errorData.Remove(key);
        var source = $"urn:harborline:workflow:{Uri.EscapeDataString(metadata.StoreNamespace)}:{Uri.EscapeDataString(metadata.TenantNamespace)}";
        var id = $"{Uri.EscapeDataString(entry.InstanceId)}/{entry.Seq.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{Uri.EscapeDataString(type)}";
        var taskEvent = new WorkflowLifecycleEvent("1.0", id, source, type, JsonSerializer.SerializeToElement(allowed));
        if (entry.EventType is not ("TaskCompleted" or "TaskFaulted") || !data.TryGetPropertyValue("workflowStatus", out var result))
            return [taskEvent];
        var expected = entry.EventType == "TaskCompleted" ? "completed" : "faulted";
        if (result is not JsonValue statusValue || !statusValue.TryGetValue<string>(out var workflowStatus) || workflowStatus != expected)
            throw new WorkflowLifecycleMappingException("Terminal task history has an invalid workflow status.");
        var workflow = new JsonObject { ["name"] = data["workflow"]!.DeepClone(), [time] = data[time]!.DeepClone() };
        if (entry.EventType == "TaskFaulted") workflow["error"] = allowed["error"]!.DeepClone();
        var workflowFact = new WorkflowEventRecord
        {
            InstanceId = entry.InstanceId, Seq = entry.Seq, Step = entry.Step,
            EventType = entry.EventType == "TaskCompleted" ? "WorkflowCompleted" : "WorkflowFaulted",
            DataJson = workflow.ToJsonString(), OccurredAt = entry.OccurredAt,
        };
        var workflowEvent = Map(workflowFact, metadata).Single();
        return [taskEvent, workflowEvent];
    }

    // SemVer 2.0.0, as the CNCF Serverless Workflow v1.0.0 schema pins document.version; [0-9] and \z keep Unicode digits and a trailing newline out.
    private const string SemanticVersion = @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-((?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?\z";

    // RFC 6901 section 3: each reference token is "/" then unescaped characters or "~0" / "~1".
    private const string JsonPointer = @"\A(?:/(?:[^~/]|~[01])*)*\z";

    private static bool Matches(string value, string pattern)
        => Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static void RequireString(JsonObject data, string property)
    {
        if (data[property] is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            throw new WorkflowLifecycleMappingException($"Lifecycle history requires '{property}'.");
    }
}
