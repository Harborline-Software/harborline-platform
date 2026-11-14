using System.Text.Json.Nodes;
using Harborline.Blocks.Workflow.Durable;
using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

public sealed class WorkflowLifecycleProjectionTests
{
    [Theory]
    [InlineData("WorkflowStarted", "io.serverlessworkflow.workflow.started.v1", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowSuspended", "io.serverlessworkflow.workflow.suspended.v1", "{\"name\":\"flow-1.example\",\"suspendedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowResumed", "io.serverlessworkflow.workflow.resumed.v1", "{\"name\":\"flow-1.example\",\"resumedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowCorrelationStarted", "io.serverlessworkflow.workflow.correlation-started.v1", "{\"name\":\"flow-1.example\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowCorrelationCompleted", "io.serverlessworkflow.workflow.correlation-completed.v1", "{\"name\":\"flow-1.example\",\"completedAt\":\"2024-07-26T16:59:57-05:00\",\"correlationKeys\":{\"petId\":\"xt84hj202q14s\"}}")]
    [InlineData("WorkflowCancelled", "io.serverlessworkflow.workflow.cancelled.v1", "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowFaulted", "io.serverlessworkflow.workflow.faulted.v1", "{\"name\":\"flow-1.example\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"https://serverlessworkflow.io/spec/1.0.0/errors/communication\",\"title\":\"Service Not Available\",\"status\":503}}")]
    [InlineData("WorkflowCompleted", "io.serverlessworkflow.workflow.completed.v1", "{\"name\":\"flow-1.example\",\"completedAt\":\"2024-07-26T16:59:57-05:00\",\"output\":{\"status\":\"placed\"}}")]
    [InlineData("TaskCreated", "io.serverlessworkflow.task.created.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"createdAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "io.serverlessworkflow.task.started.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskSuspended", "io.serverlessworkflow.task.suspended.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"suspendedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskResumed", "io.serverlessworkflow.task.resumed.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"resumedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskRetried", "io.serverlessworkflow.task.retried.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"retriedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskCancelled", "io.serverlessworkflow.task.cancelled.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskFaulted", "io.serverlessworkflow.task.faulted.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"https://serverlessworkflow.io/spec/1.0.0/errors/communication\",\"title\":\"Service Not Available\",\"status\":503}}")]
    [InlineData("TaskCompleted", "io.serverlessworkflow.task.completed.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"completedAt\":\"2024-07-26T16:59:57-05:00\",\"output\":{\"status\":\"placed\"}}")]
    public async Task Projects_workflow_and_task_history_with_the_CNCF_type_and_payload(string kind, string type, string payload)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hl488le2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var options = new FileJournalWorkflowStoreOptions { JournalPath = Path.Combine(directory, "journal") };
            using var journal = new FileJournalWorkflowStore(options, TimeProvider.System);
            IWorkflowStore store = journal;
            await store.CreateInstanceAsync(new WorkflowInstanceRecord { Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1", CurrentStep = "step", Status = WorkflowStatus.Running });
            await store.AppendHistoryAsync("run", "step", "Migrated", "{}");
            await store.AppendHistoryAsync("run", "step", kind, payload);
            var projected = Assert.Single(await WorkflowLifecycleProjection.ReadAsync(store, "run"));
            Assert.Equal(type, projected.Type);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payload), JsonNode.Parse(projected.DataJson)));
            Assert.Equal(2, projected.Sequence);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Projects_termination_as_forced_cancellation_with_its_reason()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hl488le2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var journal = new FileJournalWorkflowStore(new() { JournalPath = Path.Combine(directory, "journal") }, TimeProvider.System);
            IWorkflowStore store = journal;
            await store.CreateInstanceAsync(new WorkflowInstanceRecord { Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1", CurrentStep = "step", Status = WorkflowStatus.Running });
            await store.AppendHistoryAsync("run", "step", "WorkflowTerminated", "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\",\"reason\":\"operator stop\"}");
            var projected = Assert.Single(await WorkflowLifecycleProjection.ReadAsync(store, "run"));
            Assert.Equal("io.serverlessworkflow.workflow.cancelled.v1", projected.Type);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\",\"reason\":\"operator stop\",\"forced\":true}"), JsonNode.Parse(projected.DataJson)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("WorkflowStarted", "{}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"startedAt\":\"2024-07-26T16:59:57-05:00\",\"definition\":{}}")]
    [InlineData("WorkflowSuspended", "{\"name\":\"flow-1.example\",\"suspendedAt\":\"not-a-time\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowTerminated", "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    public async Task Refuses_lifecycle_history_missing_required_payload(string kind, string payload)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hl488le2-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            using var journal = new FileJournalWorkflowStore(new() { JournalPath = Path.Combine(directory, "journal") }, TimeProvider.System);
            IWorkflowStore store = journal;
            await store.CreateInstanceAsync(new WorkflowInstanceRecord { Id = "run", TenantId = "tenant", DefinitionKey = "flow", DefinitionVersion = "1", CurrentStep = "step", Status = WorkflowStatus.Running });
            await store.AppendHistoryAsync("run", "step", kind, payload);
            await Assert.ThrowsAsync<InvalidDataException>(() => WorkflowLifecycleProjection.ReadAsync(store, "run"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
