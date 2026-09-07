using System.Collections.Generic;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// ADR 0135 A0 — engine-level unit coverage of REAL ITERATION + the loop-back MAX-ITERATIONS CAP, driving the
/// REAL <see cref="WorkflowTriggerDispatcher"/> over a minimal in-memory store + a tiny test handler whose one
/// step always loop-back-parks back onto itself (a deterministic "runaway"). Isolates the engine arithmetic
/// (counter bump on loop-back, distinct key per iteration, escalation at the cap) from the financial handlers.
/// </summary>
public sealed class WorkflowIterationCapTests
{
    private const string DefKey = "test-loop";
    private const string LoopStep = "loop";

    /// <summary>A handler whose <c>loop</c> step always loop-back-parks onto itself — a deterministic runaway.</summary>
    private sealed class AlwaysLoopBackHandler : IWorkflowStepHandler
    {
        public string DefinitionKey => DefKey;
        public ValueTask<WorkflowStepOutcome> DecideAsync(
            WorkflowInstanceRecord instance, WorkflowTrigger trigger, CancellationToken ct = default)
            => ValueTask.FromResult(WorkflowStepOutcome.ParkLoopBack(LoopStep, "{\"loop\":true}"));
    }

    /// <summary>A minimal in-memory store: instances (with the durable iteration) + idempotency rows.</summary>
    private sealed class InMemoryStore : IWorkflowStore
    {
        private readonly Dictionary<string, WorkflowInstanceRecord> _instances = new();
        private readonly Dictionary<string, WorkflowStepIdempotencyRecord> _idem = new();
        public List<string> IdempotencyKeys => new(_idem.Keys);

        public Task<WorkflowInstanceRecord?> LoadAsync(string instanceId, CancellationToken ct = default)
            => Task.FromResult(_instances.TryGetValue(instanceId, out var i) ? Clone(i) : null);

        public Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken ct = default)
        {
            _instances[instance.Id] = Clone(instance);
            return Task.CompletedTask;
        }

        public Task<WorkflowStepIdempotencyRecord?> FindStepResultAsync(WorkflowStepKey key, CancellationToken ct = default)
            => Task.FromResult(_idem.TryGetValue(key.Value, out var r) ? r : null);

        public Task AdvanceAsync(
            WorkflowStepKey key, WorkflowEffect? effect, string resultJson, string eventType,
            string eventDataJson, string nextStep, WorkflowStatus nextStatus, CancellationToken ct = default)
        {
            _idem[key.Value] = new WorkflowStepIdempotencyRecord
            {
                Key = key.Value, InstanceId = key.InstanceId, Step = key.Step,
                Iteration = key.Iteration, ResultJson = resultJson, CompletedAt = DateTimeOffset.UtcNow,
            };
            var inst = _instances[key.InstanceId];
            inst.CurrentStep = nextStep;
            inst.Status = nextStatus;
            return Task.CompletedTask;
        }

        public Task ParkAsync(string instanceId, string step, string reasonJson, int iteration = 0, CancellationToken ct = default)
        {
            var inst = _instances[instanceId];
            inst.CurrentStep = step;
            inst.Status = WorkflowStatus.Parked;
            inst.Iteration = iteration;   // persist the (possibly bumped) durable counter
            return Task.CompletedTask;
        }

        private static WorkflowInstanceRecord Clone(WorkflowInstanceRecord r) => new()
        {
            Id = r.Id, TenantId = r.TenantId, DefinitionKey = r.DefinitionKey,
            DefinitionVersion = r.DefinitionVersion, CurrentStep = r.CurrentStep,
            Iteration = r.Iteration, Status = r.Status, StateJson = r.StateJson,
            CreatedAt = r.CreatedAt, UpdatedAt = r.UpdatedAt,
        };
    }

    private static WorkflowInstanceRecord NewInstance(string id) => new()
    {
        Id = id, TenantId = "t", DefinitionKey = DefKey, DefinitionVersion = "v1",
        CurrentStep = LoopStep, Status = WorkflowStatus.Running,
    };

    [Fact(DisplayName = "A0: a loop-back park BUMPS the durable iteration by exactly one per pass")]
    public async Task LoopBackPark_BumpsIterationByOne()
    {
        var store = new InMemoryStore();
        await store.CreateInstanceAsync(NewInstance("loop-1"));
        var dispatcher = new WorkflowTriggerDispatcher(store, new[] { new AlwaysLoopBackHandler() },
            new WorkflowEngineOptions { MaxIterations = 100 });

        Assert.Equal(0, (await store.LoadAsync("loop-1"))!.Iteration);

        await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "loop-1", LoopStep));
        Assert.Equal(1, (await store.LoadAsync("loop-1"))!.Iteration);

        await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "loop-1", LoopStep));
        Assert.Equal(2, (await store.LoadAsync("loop-1"))!.Iteration);
    }

    [Fact(DisplayName = "A0 (bug-1353): a runaway loop-back ESCALATES to terminal Failed at the cap — it does NOT loop forever")]
    public async Task RunawayLoop_EscalatesAtCap()
    {
        var store = new InMemoryStore();
        await store.CreateInstanceAsync(NewInstance("loop-cap"));
        var dispatcher = new WorkflowTriggerDispatcher(store, new[] { new AlwaysLoopBackHandler() },
            new WorkflowEngineOptions { MaxIterations = 5 });

        // Drive the loop step repeatedly; it MUST reach the terminal Failed state within a bounded count.
        WorkflowDispatchResult result = WorkflowDispatchResult.Parked;
        for (var i = 0; i < 50 && result != WorkflowDispatchResult.Advanced && result != WorkflowDispatchResult.Terminal; i++)
        {
            result = await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "loop-cap", LoopStep));
        }

        var inst = await store.LoadAsync("loop-cap");
        Assert.Equal(WorkflowStatus.Failed, inst!.Status);
        Assert.Equal(WorkflowTriggerDispatcher.EscalatedStep, inst.CurrentStep);
        // The escalation reached terminal at iteration < cap (iter 4 → nextIteration 5 >= cap 5 ⇒ escalate).
        Assert.True(inst.Iteration < 5);
    }

    [Fact(DisplayName = "A0: MaxIterations must be > 0 — a non-positive cap is rejected at construction")]
    public void NonPositiveCap_Throws()
    {
        var store = new InMemoryStore();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowTriggerDispatcher(store, new[] { new AlwaysLoopBackHandler() },
                new WorkflowEngineOptions { MaxIterations = 0 }));
    }

    [Fact(DisplayName = "A0: with no options the default cap (50) applies — the dispatcher still bounds a runaway")]
    public async Task DefaultOptions_StillBoundsRunaway()
    {
        var store = new InMemoryStore();
        await store.CreateInstanceAsync(NewInstance("loop-default"));
        var dispatcher = new WorkflowTriggerDispatcher(store, new[] { new AlwaysLoopBackHandler() });

        WorkflowDispatchResult result = WorkflowDispatchResult.Parked;
        var steps = 0;
        for (; steps < 1000 && result != WorkflowDispatchResult.Advanced && result != WorkflowDispatchResult.Terminal; steps++)
        {
            result = await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "loop-default", LoopStep));
        }

        Assert.Equal(WorkflowStatus.Failed, (await store.LoadAsync("loop-default"))!.Status);
        Assert.True(steps <= 50, "the default cap (50) must bound the runaway");
    }
}
