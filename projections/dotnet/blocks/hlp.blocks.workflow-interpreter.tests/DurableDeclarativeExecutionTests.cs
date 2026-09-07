using System.Text.Json;

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
            WorkflowTrigger.For(WorkflowTriggerKind.Event, "decl-1", "Draft")));
        var parked = await store.LoadAsync("decl-1");
        Assert.Equal(WorkflowStatus.Parked, parked!.Status);
        Assert.Equal("PendingApproval", parked.CurrentStep);
        using (var basis = JsonDocument.Parse((await store.EventsAsync("decl-1")).Last().DataJson))
        {
            Assert.Equal("cp-approval-basis", basis.RootElement.GetProperty("kind").GetString());
        }

        // Human confirm → the CP effect builds through the broker and co-commits durably, once.
        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "decl-1", "PendingApproval", "{\"decision\":\"approve\"}")));
        Assert.Equal(WorkflowStatus.Completed, (await store.LoadAsync("decl-1"))!.Status);
        Assert.Single(await store.CommittedEffectPayloadsAsync("decl-1"));
        Assert.Equal(1, provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);

        // Redelivery of the confirm is a durable no-op.
        Assert.True((await dispatcher.DispatchAsync(WorkflowTrigger.For(
            WorkflowTriggerKind.HumanAction, "decl-1", "PendingApproval", "{\"decision\":\"approve\"}")))
            is WorkflowDispatchResult.ReplayedNoOp or WorkflowDispatchResult.Terminal);
        Assert.Single(await store.CommittedEffectPayloadsAsync("decl-1"));
    }

    [Fact]
    public async Task Rejected_workflow_posts_no_effect_and_records_an_override()
    {
        await using var provider = await BuildAsync();
        var store = provider.GetRequiredService<FileJournalWorkflowStore>();
        var dispatcher = provider.GetRequiredService<IWorkflowTriggerDispatcher>();
        await CreateInstanceAsync(store, "decl-2");
        await dispatcher.DispatchAsync(WorkflowTrigger.For(WorkflowTriggerKind.Event, "decl-2", "Draft"));

        Assert.Equal(WorkflowDispatchResult.Advanced, await dispatcher.DispatchAsync(
            WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "decl-2", "PendingApproval", "{\"decision\":\"reject\"}")));

        Assert.Equal(WorkflowStatus.Completed, (await store.LoadAsync("decl-2"))!.Status);
        Assert.Equal("Rejected", (await store.LoadAsync("decl-2"))!.CurrentStep);
        Assert.Empty(await store.CommittedEffectPayloadsAsync("decl-2"));
        var sink = provider.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
        Assert.Equal(1, sink.OverriddenCount);
    }

    // ── Harness ─────────────────────────────────────────────────────────────

    private async Task<ServiceProvider> BuildAsync()
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
        var authored = Authored();
        await definitions.RegisterAsync(WorkflowDefinitionWireMapper.ToModel(authored, Tenant, Key, Version), authored);
        await definitions.PublishAsync(Tenant, Key, Version);
        return provider;
    }

    private static Task CreateInstanceAsync(FileJournalWorkflowStore store, string id)
        => store.CreateInstanceAsync(new WorkflowInstanceRecord
        {
            Id = id,
            TenantId = Tenant,
            DefinitionKey = Key,
            DefinitionVersion = Version,
            CurrentStep = "Draft",
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
