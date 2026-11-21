using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Interpreter.Tests;

public sealed partial class DeclarativeWorkflowInterpreterTests
{
    // DES-0019 workflows-eng-4/ck-4, ADR-0141 D1. These literals come from the
    // autonomous-traversal-proof record at control 221d9269 (T-724 ruling 121).
    [Fact(DisplayName = "workflows-eng-4 workflows-ck-4: effect-free A -> B -> C completes without changing the input")]
    public async Task Workflows_eng_4_workflows_ck_4_autonomous_traversal_completes_without_mutating_input()
    {
        using var provider = BuildProvider(HumanConfirmer(), (ChainKey, AutonomousTraversalGraph()));
        var instance = AutonomousTraversalInstance();

        var outcome = await Interpreter(provider).DecideAsync(
            instance, WorkflowTrigger.For(WorkflowTriggerKind.Event, instance.Id, "A"));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal("C", outcome.NextStep);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.Null(outcome.Effect);
        Assert.False(outcome.IsLoopBack);
        AssertAutonomousTraversalInputUnchanged(instance);
    }

    [Fact(DisplayName = "workflows-eng-4: a reached dead end throws the exact typed error at B without changing the input")]
    public async Task Workflows_eng_4_autonomous_traversal_dead_end_names_B_without_mutating_input()
    {
        var graph = JsonNode.Parse(AutonomousTraversalGraph().GetRawText())!;
        graph["transitions"]!.AsArray().RemoveAt(1);
        await AssertAutonomousTraversalRefusedAtB(JsonSerializer.SerializeToElement(graph));
    }

    [Fact(DisplayName = "workflows-eng-4: a reached ambiguous branch throws the exact typed error at B without changing the input")]
    public async Task Workflows_eng_4_autonomous_traversal_ambiguity_names_B_without_mutating_input()
    {
        var graph = JsonNode.Parse(AutonomousTraversalGraph().GetRawText())!;
        graph["transitions"]!.AsArray().Add(JsonNode.Parse(
            """{"id":"other-bc","from":"B","on":"issued","to":"C"}"""));
        await AssertAutonomousTraversalRefusedAtB(JsonSerializer.SerializeToElement(graph));
    }

    private static JsonElement AutonomousTraversalGraph()
    {
        // Owned copy of the existing mixed-trigger chain: both edges now bind Event.
        var graph = JsonNode.Parse(ChainAuthored().GetRawText())!;
        graph["transitions"]![1]!["on"] = "issued";
        return JsonSerializer.SerializeToElement(graph);
    }

    private static WorkflowInstanceRecord AutonomousTraversalInstance() => new()
    {
        Id = "autonomous-traversal-proof", TenantId = Tenant, DefinitionKey = ChainKey,
        DefinitionVersion = "1.0.0", CurrentStep = "A", Status = WorkflowStatus.Running,
        Iteration = 7, StateJson = """{"proof":731}""",
    };

    private static async Task AssertAutonomousTraversalRefusedAtB(JsonElement graph)
    {
        // Fixture-only raw store reaches the interpreter backstop. Production still re-admits.
        using var provider = BuildProvider(HumanConfirmer(), (ChainKey, graph));
        var instance = AutonomousTraversalInstance();
        try
        {
            var error = await Assert.ThrowsAsync<WorkflowInterpreterDecisionException>(async () =>
                await Interpreter(provider).DecideAsync(
                    instance, WorkflowTrigger.For(WorkflowTriggerKind.Event, instance.Id, "A")));
            Assert.Equal("B", error.Step);
        }
        finally
        {
            AssertAutonomousTraversalInputUnchanged(instance);
        }
    }

    private static void AssertAutonomousTraversalInputUnchanged(WorkflowInstanceRecord instance)
    {
        Assert.Equal("A", instance.CurrentStep);
        Assert.Equal(WorkflowStatus.Running, instance.Status);
        Assert.Equal(7, instance.Iteration);
        Assert.Equal("1.0.0", instance.DefinitionVersion);
        Assert.Equal("""{"proof":731}""", instance.StateJson);
    }
}
