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
    [InlineData("WorkflowCorrelationCompleted", "io.serverlessworkflow.workflow.correlation-completed.v1", "{\"name\":\"flow-1.example\",\"completedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowCancelled", "io.serverlessworkflow.workflow.cancelled.v1", "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowFaulted", "io.serverlessworkflow.workflow.faulted.v1", "{\"name\":\"flow-1.example\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"https://serverlessworkflow.io/spec/1.0.0/errors/communication\",\"title\":\"Service Not Available\",\"status\":503}}")]
    [InlineData("WorkflowCompleted", "io.serverlessworkflow.workflow.completed.v1", "{\"name\":\"flow-1.example\",\"completedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskCreated", "io.serverlessworkflow.task.created.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"createdAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "io.serverlessworkflow.task.started.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskSuspended", "io.serverlessworkflow.task.suspended.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"suspendedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskResumed", "io.serverlessworkflow.task.resumed.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"resumedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskRetried", "io.serverlessworkflow.task.retried.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"retriedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskCancelled", "io.serverlessworkflow.task.cancelled.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskFaulted", "io.serverlessworkflow.task.faulted.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"https://serverlessworkflow.io/spec/1.0.0/errors/communication\",\"title\":\"Service Not Available\",\"status\":503}}")]
    [InlineData("TaskCompleted", "io.serverlessworkflow.task.completed.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/1/initialize\",\"completedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "io.serverlessworkflow.workflow.started.v1", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0-rc.1+build.5\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "io.serverlessworkflow.workflow.started.v1", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"0.10.2-alpha-1.0\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "io.serverlessworkflow.workflow.started.v1", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"2.0.0+sha.5114f85\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "io.serverlessworkflow.task.started.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/0/a~0b~1c\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "io.serverlessworkflow.task.started.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "io.serverlessworkflow.task.started.v1", "{\"workflow\":\"flow-1.example\",\"task\":\"/do//x\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    public void Projects_workflow_and_task_history_with_the_CNCF_type_and_payload(string kind, string type, string payload)
    {
        var fact = new WorkflowEventRecord { InstanceId = "run", Seq = 2, Step = "step", EventType = kind, DataJson = payload };
        var projected = Assert.Single(WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
        Assert.Equal(type, projected.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(payload), JsonNode.Parse(projected.Data.GetRawText())));
    }

    [Fact]
    public void Projects_termination_as_forced_cancellation_with_its_reason()
    {
        var fact = new WorkflowEventRecord { InstanceId = "run", Seq = 1, Step = "step", EventType = "WorkflowTerminated", DataJson = "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\",\"reason\":\"operator stop\"}" };
        var projected = Assert.Single(WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
        Assert.Equal("io.serverlessworkflow.workflow.cancelled.v1", projected.Type);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\",\"reason\":\"operator stop\",\"forced\":true}"), JsonNode.Parse(projected.Data.GetRawText())));
    }

    [Theory]
    [InlineData("WorkflowStarted", "{}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"startedAt\":\"2024-07-26T16:59:57-05:00\",\"definition\":{}}")]
    [InlineData("WorkflowSuspended", "{\"name\":\"flow-1.example\",\"suspendedAt\":\"not-a-time\"}")]
    [InlineData("WorkflowSuspended", "{\"name\":\"flow-1.example\",\"suspendedAt\":\"07/26/2024\"}")]
    [InlineData("WorkflowFaulted", "{\"name\":\"flow-1.example\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{\"type\":\"urn:error\",\"status\":503,\"title\":{\"internal\":\"value\"}}}")]
    [InlineData("WorkflowFaulted", "{\"name\":\"flow-1.example\",\"faultedAt\":\"2024-07-26T16:59:57-05:00\",\"error\":{}}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowTerminated", "{\"name\":\"flow-1.example\",\"cancelledAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"latest\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"01.0.0\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0-\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0-01\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0+\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"v1.0.0\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("WorkflowStarted", "{\"name\":\"flow-1.example\",\"definition\":{\"name\":\"flow\",\"namespace\":\"example\",\"version\":\"1.0.0\\n\"},\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"task\":\"task\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"task\":\"do/0\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/~2\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/~\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    [InlineData("TaskStarted", "{\"workflow\":\"flow-1.example\",\"task\":\"/do/a~b\",\"startedAt\":\"2024-07-26T16:59:57-05:00\"}")]
    public void Refuses_lifecycle_history_missing_required_payload(string kind, string payload)
    {
        var fact = new WorkflowEventRecord { InstanceId = "run", Seq = 1, Step = "step", EventType = kind, DataJson = payload };
        Assert.Throws<WorkflowLifecycleMappingException>(() => WorkflowLifecycleProjection.Map(fact, new("store", "tenant")));
    }
}
