using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Interpreter.Tests;

/// <summary>
/// The declarative-execution end-to-end rows re-proven at the narrowed Harborline durable
/// seam (ticket 074 item 4): an AUTHORED definition — registered, admitted, published —
/// executes through the dispatcher's interpreter fallback over the REAL file-journal store,
/// so the park, the SoD-gated human confirm, the committed effect, and the override record
/// are all durable facts, not in-memory ones.
/// </summary>
public sealed class DurableDeclarativeExecutionTests : IDisposable
{
    private static WorkflowDispatchAuthority Authorized(string instanceId) => new(
        "user:operator", Tenant, WorkflowDispatchAuthority.RequiredOperation,
        WorkflowDispatchAuthority.RequiredRecordKind, instanceId, Allowed: true);

    private const string Tenant = "tenant:acme";
    private const string Key = "vendor-invoice-approval.v1";
    private const string Version = "1.0.0";

    private static readonly Guid EngineParty = Guid.Parse("e0000000-0000-0000-0000-0000000000e1");
    private static readonly Guid HumanParty = Guid.Parse("40000000-0000-0000-0000-000000000001");

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "hlp-workflow-declarative-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    [Fact]
    public async Task Authored_workflow_executes_park_then_human_confirm_posts_one_effect()
    {
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "decl-1");

        // Autonomous start → durable CP park with the basis.
        Assert.Equal(WorkflowDispatchResult.Parked, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "decl-1", "Draft"), Authorized("decl-1")));
        var parked = await store.LoadAsync("decl-1");
        Assert.Equal(WorkflowStatus.Parked, parked!.Status);
        Assert.Equal("PendingApproval", parked.CurrentStep);
        using (var basis = JsonDocument.Parse((await store.EventsAsync("decl-1")).Last().DataJson))
        {
            Assert.Equal("cp-approval-basis", basis.RootElement.GetProperty("kind").GetString());
        }

        // Human confirm → the CP effect builds through the broker and co-commits durably, once.
        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "decl-1", "PendingApproval", "{\"decision\":\"approve\"}"), Authorized("decl-1")));
        Assert.Equal(WorkflowStatus.Completed, (await store.LoadAsync("decl-1"))!.Status);
        Assert.Single(await store.CommittedEffectPayloadsAsync("decl-1"));
        Assert.Equal(1, provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);

        // Redelivery of the confirm is a durable no-op.
        Assert.True((await dispatcher.DispatchAsync(WorkflowTrigger.For(
            WorkflowTriggerKind.HumanAction, "decl-1", "PendingApproval", "{\"decision\":\"approve\"}"), Authorized("decl-1")))
            is WorkflowDispatchResult.ReplayedNoOp or WorkflowDispatchResult.Terminal);
        Assert.Single(await store.CommittedEffectPayloadsAsync("decl-1"));
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    public async Task Migration_before_human_action_commit_records_no_approval_decision(string decision)
    {
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var definitions = provider.GetRequiredService<IWorkflowDefinitionStore>();
        var authored = Authored();
        await definitions.RegisterAsync(WorkflowDefinitionWireMapper.ToModel(authored, Tenant, Key, "2.0.0"), authored);
        await definitions.PublishAsync(Tenant, Key, "2.0.0");
        await CreateInstanceAsync(store, "approval-race");
        var regularDispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        Assert.Equal(WorkflowDispatchResult.Parked, await regularDispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "approval-race", "Draft"), Authorized("approval-race")));
        var pausing = new PausingAdvanceStore(store);
        var dispatcher = new WorkflowTriggerDispatcher(pausing, [], interpreter: provider.GetRequiredService<IDeclarativeWorkflowInterpreter>(),
            definitionStore: provider.GetRequiredService<IWorkflowDefinitionExecutionStore>());
        var dispatch = dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "approval-race",
            "PendingApproval", $"{{\"decision\":\"{decision}\"}}"), Authorized("approval-race"));
        await pausing.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sink = provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        try
        {
            Assert.Equal(0, sink.TotalCount);
            await provider.GetRequiredService<WorkflowMigration>()
                .MigrateAsync("approval-race", "2.0.0", new Dictionary<string, string> { ["PendingApproval"] = "PendingApproval" },
                    new WorkflowMigrationAuthority("operator", Tenant, "workflow:migrate", "workflow-instance", "approval-race", true));
        }
        finally { pausing.Continue.SetResult(); }
        Assert.Equal(WorkflowDispatchResult.Discarded, await dispatch);
        Assert.Equal(0, sink.TotalCount);
        Assert.Empty(await store.CommittedEffectPayloadsAsync("approval-race"));
    }

    private sealed class PausingAdvanceStore(FileJournalWorkflowStore inner) : IWorkflowStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WorkflowInstanceRecord?> LoadAsync(string id, CancellationToken ct = default) => inner.LoadAsync(id, ct);
        public Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken ct = default) => inner.CreateInstanceAsync(instance, ct);
        public Task<WorkflowStepIdempotencyRecord?> FindStepResultAsync(WorkflowStepKey key, CancellationToken ct = default) => inner.FindStepResultAsync(key, ct);
        public Task<(bool Current, WorkflowStepIdempotencyRecord? Result)> FindStepResultIfCurrentAsync(WorkflowInstanceRecord expected, WorkflowStepKey key, CancellationToken ct = default) => inner.FindStepResultIfCurrentAsync(expected, key, ct);
        public Task AdvanceAsync(WorkflowStepKey key, WorkflowEffect? effect, string resultJson, string eventType, string eventDataJson, string nextStep, WorkflowStatus nextStatus, CancellationToken ct = default)
            => inner.AdvanceAsync(key, effect, resultJson, eventType, eventDataJson, nextStep, nextStatus, ct);
        public async Task<bool> AdvanceIfCurrentAsync(WorkflowInstanceRecord expected, WorkflowStepKey key, WorkflowEffect? effect, string resultJson, string eventType, string eventDataJson, string nextStep, WorkflowStatus nextStatus, CancellationToken ct = default)
        {
            Entered.SetResult();
            await Continue.Task;
            return await inner.AdvanceIfCurrentAsync(expected, key, effect, resultJson, eventType, eventDataJson, nextStep, nextStatus, ct);
        }
        public Task ParkAsync(string id, string step, string reasonJson, int iteration = 0, CancellationToken ct = default) => inner.ParkAsync(id, step, reasonJson, iteration, ct);
        public Task<bool> ParkIfCurrentAsync(WorkflowInstanceRecord expected, string step, string reasonJson, int iteration, CancellationToken ct = default) => inner.ParkIfCurrentAsync(expected, step, reasonJson, iteration, ct);
    }

    [Fact]
    public async Task Unmatched_trigger_commits_nothing_and_matching_trigger_can_advance()
    {
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "discard-1");
        var before = await store.EventsAsync("discard-1");

        Assert.Equal(WorkflowDispatchResult.Discarded, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Schedule, "discard-1", "Draft"), Authorized("discard-1")));

        Assert.Equal(before.Count, (await store.EventsAsync("discard-1")).Count);
        Assert.Null(await store.FindStepResultAsync(new WorkflowStepKey("discard-1", 0, "Draft")));
        Assert.Equal("Draft", (await store.LoadAsync("discard-1"))!.CurrentStep);
        Assert.Equal(WorkflowDispatchResult.Parked, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "discard-1", "Draft"), Authorized("discard-1")));
        Assert.Equal("PendingApproval", (await store.LoadAsync("discard-1"))!.CurrentStep);
    }

    [Fact]
    public async Task A_matched_trigger_commits_progress_and_rests_where_the_next_edge_does_not_match()
    {
        var chainKey = DeclarativeWorkflowInterpreterTests.ChainKey;
        await using var provider = await BuildAsync(DeclarativeWorkflowInterpreterTests.ChainAuthored(), chainKey);
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "chain-1", chainKey, "A");

        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "chain-1", "A"), Authorized("chain-1")));
        var rested = await store.LoadAsync("chain-1");
        Assert.Equal("B", rested!.CurrentStep);
        Assert.Equal(WorkflowStatus.Running, rested.Status);

        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Schedule, "chain-1", "B"), Authorized("chain-1")));
        var completed = await store.LoadAsync("chain-1");
        Assert.Equal("C", completed!.CurrentStep);
        Assert.Equal(WorkflowStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Declarative_dispatch_under_a_denied_authority_is_refused_before_the_interpreter_runs()
    {
        // T-525 item 1: the interpreter branch verifies the same authority as a typed handler.
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "decl-denied");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "decl-denied", "Draft"),
            Authorized("decl-denied") with { Allowed = false }));

        Assert.Equal("Draft", (await store.LoadAsync("decl-denied"))!.CurrentStep);
    }

    [Fact]
    public async Task Rejected_workflow_posts_no_effect_and_records_an_override()
    {
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "decl-2");
        await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "decl-2", "Draft"), Authorized("decl-2"));

        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "decl-2", "PendingApproval", "{\"decision\":\"reject\"}"), Authorized("decl-2")));

        Assert.Equal(WorkflowStatus.Completed, (await store.LoadAsync("decl-2"))!.Status);
        Assert.Equal("Rejected", (await store.LoadAsync("decl-2"))!.CurrentStep);
        Assert.Empty(await store.CommittedEffectPayloadsAsync("decl-2"));
        var sink = provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
        Assert.Equal(1, sink.OverriddenCount);
    }

    [Fact]
    public async Task Interpreter_decision_error_atomically_fails_the_run_at_the_actual_step_without_retry()
    {
        // ADR-0141 D1: a pinned definition's ambiguous decision fails once, naming B,
        // even though this dispatch started at A. Literal expectations are the oracle.
        var authored = JsonNode.Parse(DeclarativeWorkflowInterpreterTests.ChainAuthored().GetRawText())!;
        authored["transitions"]!.AsArray().Add(JsonNode.Parse(
            """{"id":"t-bc-other","from":"B","on":"issued","to":"C"}"""));
        await using var provider = await BuildAsync(JsonSerializer.SerializeToElement(authored), "ambiguous.v1");
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "bad-1", "ambiguous.v1", "A");
        var trigger = WorkflowTrigger.For(WorkflowTriggerKind.Event, "bad-1", "A");

        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(trigger, Authorized("bad-1")));
        var failed = (await store.LoadAsync("bad-1"))!;
        Assert.Equal(WorkflowStatus.Failed, failed.Status);
        Assert.Equal("B", failed.CurrentStep);
        Assert.Equal(0, failed.Iteration);
        var result = await store.FindStepResultAsync(new WorkflowStepKey("bad-1", 0, "A"));
        Assert.NotNull(result);
        using var payload = JsonDocument.Parse(result.ResultJson);
        Assert.Equal("interpreter-failure", payload.RootElement.GetProperty("kind").GetString());
        Assert.Equal("not-retryable", payload.RootElement.GetProperty("reason").GetString());
        Assert.Equal("B", payload.RootElement.GetProperty("step").GetString());
        var events = await store.EventsAsync("bad-1");
        Assert.Equal("Failed", events.Last().EventType);
        Assert.Equal(result.ResultJson, events.Last().DataJson);
        Assert.Empty(await store.CommittedEffectPayloadsAsync("bad-1"));

        Assert.Equal(WorkflowDispatchResult.Terminal, await dispatcher.DispatchAsync(trigger, Authorized("bad-1")));
        Assert.Equal(events.Count, (await store.EventsAsync("bad-1")).Count);

        store.Dispose();
        using var reopened = new FileJournalWorkflowStore(new FileJournalWorkflowStoreOptions
        {
            JournalPath = Path.Combine(_directory, "workflow.hlwj"),
        }, TimeProvider.System);
        Assert.Equal(WorkflowStatus.Failed, (await reopened.LoadAsync("bad-1"))!.Status);
        Assert.Equal("B", (await reopened.LoadAsync("bad-1"))!.CurrentStep);
        Assert.Equal(result.ResultJson,
            (await reopened.FindStepResultAsync(new WorkflowStepKey("bad-1", 0, "A")))!.ResultJson);
        Assert.Equal(events.Count, (await reopened.EventsAsync("bad-1")).Count);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private async Task<ServiceProvider> BuildAsync(JsonElement? authoredDefinition = null, string key = Key)
    {
        Directory.CreateDirectory(_directory);
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDurableWorkflowEngine();
        services.AddInMemoryWorkflowDefinitionStore();
        services.AddFileJournalWorkflowStore(new FileJournalWorkflowStoreOptions
        {
            JournalPath = Path.Combine(_directory, "workflow.hlwj"),
        });
        services.AddWorkflowEffectFactory(
            "ledger.post-journal-entry", WorkflowEffectReach.Internal,
            (_, request) => new WorkflowEffect((unitOfWork, _) =>
            {
                ((FileJournalWorkflowUnitOfWork)unitOfWork).StageEffectPayload(
                    $"je:{request.StepKey.ToDeterministicGuid("source-reference"):D}");
                return Task.CompletedTask;
            }));
        services.AddSingleton<IWorkflowConfirmationContext>(new StaticConfirmation());
        services.AddDeclarativeWorkflowInterpreter();
        var provider = services.BuildServiceProvider();

        var definitions = provider.GetRequiredService<IWorkflowDefinitionStore>();
        var authored = authoredDefinition ?? Authored();
        await definitions.RegisterAsync(WorkflowDefinitionWireMapper.ToModel(authored, Tenant, key, Version), authored);
        await definitions.PublishAsync(Tenant, key, Version);
        return provider;
    }

    private static Task CreateInstanceAsync(FileJournalWorkflowStore store, string id, string key = Key, string step = "Draft")
        => store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = id,
            TenantId = Tenant,
            DefinitionKey = key,
            DefinitionVersion = Version,
            CurrentStep = step,
            Status = WorkflowStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

    private static JsonElement Authored()
    {
        const string json = """
        {
          "initialState": "Draft",
          "mutability": "Locked",
          "states": [
            { "id": "Draft", "kind": "Normal" },
            { "id": "PendingApproval", "kind": "Normal" },
            { "id": "Posted", "kind": "Terminal" },
            { "id": "Rejected", "kind": "Terminal" }
          ],
          "triggers": [
            { "id": "issued", "kind": "Event", "eventType": "Issued" },
            { "id": "approve", "kind": "HumanAction", "task": "vendor-invoice-approval" },
            { "id": "reject", "kind": "HumanAction", "task": "vendor-invoice-approval" }
          ],
          "transitions": [
            { "id": "t-issue", "from": "Draft", "on": "issued", "to": "PendingApproval" },
            { "id": "t-approve", "from": "PendingApproval", "on": "approve", "to": "Posted", "guard": "decision:approve" },
            { "id": "t-reject", "from": "PendingApproval", "on": "reject", "to": "Rejected", "guard": "decision:reject" }
          ],
          "guards": [
            { "id": "decision:approve" },
            { "id": "decision:reject" }
          ],
          "actions": [
            {
              "id": "a-post-je",
              "on": { "transition": "t-approve" },
              "kind": "CreateRecord",
              "capabilityRef": "ledger.post-journal-entry",
              "classification": "CP"
            }
          ]
        }
        """;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class StaticConfirmation : IWorkflowConfirmationContext
    {
        public WorkflowProposerIdentity EngineProposer { get; } = new(EngineParty, IsHuman: false);

        public ValueTask<WorkflowConfirmerIdentity> ResolveConfirmerAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new WorkflowConfirmerIdentity(HumanParty, IsHuman: true));
    }
}
