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

    [Fact]
    public void Repeated_mapping_is_byte_identical_and_drops_unlisted_data()
    {
        var fact = Fact();
        var metadata = new WorkflowLifecycleMetadata("store-a", "tenant-a");
        var first = WorkflowLifecycleProjection.Map(fact, metadata);
        var run = new WorkflowInstanceRecord { Id = "run", TenantId = "tenant-a", DefinitionKey = "flow", DefinitionVersion = "1", CurrentStep = "step", Status = WorkflowStatus.Suspended };
        run.Status = WorkflowStatus.Running;
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
}
