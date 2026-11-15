using System.Text.Json;
using Harborline.Blocks.Workflow.Durable;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowLifecycleMapperTests
{
    private static WorkflowEventRecord Fact(string run = "run", long seq = 1) => new()
    {
        InstanceId = run, Seq = seq, Step = "step", EventType = "WorkflowSuspended",
        DataJson = "{\"name\":\"flow.example\",\"suspendedAt\":\"2024-07-26T16:59:57-05:00\",\"private\":\"secret\"}",
    };

    [Theory]
    [InlineData("TaskCompleted", "completed", "completedAt", "io.serverlessworkflow.task.completed.v1", "io.serverlessworkflow.workflow.completed.v1")]
    [InlineData("TaskFaulted", "faulted", "faultedAt", "io.serverlessworkflow.task.faulted.v1", "io.serverlessworkflow.workflow.faulted.v1")]
    public void Run_ending_task_fact_maps_ordered_distinct_deterministic_events(string kind, string status, string time, string taskType, string workflowType)
    {
        var fact = new WorkflowEventRecord
        {
            InstanceId = "run", Seq = 7, Step = "step", EventType = kind,
            DataJson = "{\"workflow\":\"flow.example\",\"task\":\"/do/1/finish\",\"" + time + "\":\"2024-07-26T16:59:57-05:00\",\"workflowStatus\":\"" + status + "\",\"error\":{\"type\":\"failure\",\"status\":500,\"private\":\"secret\"},\"private\":\"secret\"}",
        };
        var events = WorkflowLifecycleProjection.Map(fact, new("store", "tenant"));
        Assert.Equal(2, events.Count);
        Assert.Equal(taskType, events[0].Type);
        Assert.Equal(workflowType, events[1].Type);
        Assert.Equal(events[0].Source, events[1].Source);
        Assert.NotEqual(events[0].Id, events[1].Id);
        Assert.Equal("flow.example", events[1].Data.GetProperty("name").GetString());
        Assert.Equal("2024-07-26T16:59:57-05:00", events[1].Data.GetProperty(time).GetString());
        Assert.Equal(status == "faulted" ? 3 : 2, events[1].Data.EnumerateObject().Count());
        if (status == "faulted")
        {
            Assert.Equal("failure", events[1].Data.GetProperty("error").GetProperty("type").GetString());
            Assert.Equal(500, events[1].Data.GetProperty("error").GetProperty("status").GetInt32());
            Assert.False(events[1].Data.GetProperty("error").TryGetProperty("private", out _));
        }
        Assert.False(events[0].Data.TryGetProperty("workflowStatus", out _));
        Assert.Equal(JsonSerializer.Serialize(events), JsonSerializer.Serialize(WorkflowLifecycleProjection.Map(fact, new("store", "tenant"))));
    }

    [Theory]
    [InlineData("TaskCompleted", "completedAt")]
    [InlineData("TaskFaulted", "faultedAt")]
    public void Task_fact_without_workflow_status_yields_only_task_event(string kind, string time)
    {
        var fact = new WorkflowEventRecord { InstanceId = "run", Seq = 7, Step = "step", EventType = kind,
            DataJson = "{\"workflow\":\"flow.example\",\"task\":\"task\",\"" + time + "\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"failure\",\"status\":500}}" };
        Assert.Single(WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
    }

    [Theory]
    [InlineData("TaskCompleted", "completedAt", "\"faulted\"")]
    [InlineData("TaskFaulted", "faultedAt", "\"completed\"")]
    [InlineData("TaskCompleted", "completedAt", "\"running\"")]
    [InlineData("TaskCompleted", "completedAt", "null")]
    [InlineData("TaskFaulted", "faultedAt", "42")]
    public void Terminal_task_refuses_other_workflow_status_values(string kind, string time, string status)
    {
        var fact = new WorkflowEventRecord { InstanceId = "run", Seq = 7, Step = "step", EventType = kind,
            DataJson = "{\"workflow\":\"flow.example\",\"task\":\"task\",\"" + time + "\":\"2024-07-26T16:59:57-05:00\",\"workflowStatus\":" + status + ",\"error\":{\"type\":\"failure\",\"status\":500}}" };
        Assert.Throws<WorkflowLifecycleMappingException>(() => WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
    }

    [Fact]
    public void Repeated_mapping_is_byte_identical_and_drops_unlisted_data()
    {
        var fact = Fact();
        var metadata = new WorkflowLifecycleMetadata("store-a", "tenant-a");
        var first = WorkflowLifecycleProjection.Map(fact, metadata);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(WorkflowLifecycleProjection.Map(fact, metadata)));
        var envelope = Assert.Single(first);
        Assert.Equal("1.0", envelope.SpecVersion);
        Assert.Equal("io.serverlessworkflow.workflow.suspended.v1", envelope.Type);
        Assert.DoesNotContain("private", envelope.Data.GetRawText());
        Assert.Contains("suspendedAt", envelope.Data.GetRawText());
        Assert.Equal(1, WorkflowLifecycleProjection.Version);
    }

    [Fact]
    public void Id_and_source_distinguish_runs_sequences_tenants_and_stores()
    {
        var origin = new WorkflowLifecycleMetadata("store-a", "tenant-a");
        var first = Assert.Single(WorkflowLifecycleProjection.Map(Fact(), origin));
        var repeat = Assert.Single(WorkflowLifecycleProjection.Map(Fact(), origin));
        Assert.Equal((first.Source, first.Id), (repeat.Source, repeat.Id));
        Assert.NotEqual(first.Id, Assert.Single(WorkflowLifecycleProjection.Map(Fact("other"), origin)).Id);
        Assert.NotEqual(first.Id, Assert.Single(WorkflowLifecycleProjection.Map(Fact(seq: 2), origin)).Id);
        Assert.NotEqual(first.Source, Assert.Single(WorkflowLifecycleProjection.Map(Fact(), new("store-a", "tenant-b"))).Source);
        Assert.NotEqual(first.Source, Assert.Single(WorkflowLifecycleProjection.Map(Fact(), new("store-b", "tenant-a"))).Source);
    }

    [Fact]
    public void Unknown_history_produces_no_events_and_missing_data_has_typed_refusal()
    {
        var unknown = Fact();
        unknown.EventType = "Migrated";
        Assert.Empty(WorkflowLifecycleProjection.Map(unknown, new("store", "tenant")));
        var missing = Fact();
        missing.DataJson = "{}";
        Assert.Throws<WorkflowLifecycleMappingException>(() => WorkflowLifecycleProjection.Map(missing, new("store", "tenant")));
    }

    [Fact]
    public void Serialized_event_uses_CloudEvents_envelope_and_embedded_data()
    {
        var projected = Assert.Single(WorkflowLifecycleProjection.Map(Fact(), new("store-a", "tenant-a")));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(projected));
        var envelope = json.RootElement;
        Assert.Equal("1.0", envelope.GetProperty("specversion").GetString());
        Assert.Equal("io.serverlessworkflow.workflow.suspended.v1", envelope.GetProperty("type").GetString());
        Assert.Equal("flow.example", envelope.GetProperty("data").GetProperty("name").GetString());
        Assert.False(envelope.TryGetProperty("DataJson", out _));
    }

    [Fact]
    public async Task Committed_fact_maps_identically_after_the_stored_run_changes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hl488le2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var store = new FileJournalWorkflowStore(new() { JournalPath = Path.Combine(directory, "journal") }, TimeProvider.System);
            await store.CreateInstanceAsync(new WorkflowInstanceRecord { Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1", CurrentStep = "first", Status = WorkflowStatus.Running });
            await store.AdvanceAsync(new WorkflowStepKey("run", 0, "first"), null, "{}", "WorkflowSuspended",
                "{\"name\":\"flow.example\",\"suspendedAt\":\"2024-07-26T16:59:57-05:00\"}", "second", WorkflowStatus.Suspended);
            var fact = Assert.Single(await store.EventsAsync("run"));
            var before = JsonSerializer.Serialize(WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
            await store.AdvanceAsync(new WorkflowStepKey("run", 0, "second"), null, "{}", "Advanced", "{}", "third", WorkflowStatus.Running);
            Assert.Equal(WorkflowStatus.Running, (await store.LoadAsync("run"))!.Status);
            Assert.Equal(before, JsonSerializer.Serialize(WorkflowLifecycleProjection.Map(fact, new("store", "tenant"))));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
