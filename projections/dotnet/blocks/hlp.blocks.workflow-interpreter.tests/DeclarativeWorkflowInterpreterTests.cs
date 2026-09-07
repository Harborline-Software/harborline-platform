using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Interpreter.Tests;

/// <summary>
/// Behaviour tests for <see cref="DeclarativeWorkflowInterpreter"/> (ADR 0135 A1) — driven against the REAL
/// execution store (fail-closed re-admit), the REAL broker-PEP (delegate-registered effect factories, real
/// registry-derived CP/AP classification + SoD), and the REAL admission validator. They pin the load-bearing
/// new execution semantics: an AP effect is built autonomously through the broker; a CP effect PARKS then is
/// built ONLY on a human confirm that passes separation-of-duties; a non-human / engine-self confirmer is
/// refused; reject records an override with no effect; send-back loops back; the CP effect request is
/// re-derived from the pinned definition, not the confirm payload.
/// </summary>
public sealed class DeclarativeWorkflowInterpreterTests
{
    private const string Tenant = "tenant:acme";
    private const string CpKey = "vendor-invoice-approval.v1";
    private const string ApKey = "notify-on-issue.v1";
    private const string Version = "1.0.0";

    private static readonly Guid EngineParty = Guid.Parse("e0000000-0000-0000-0000-0000000000e1");
    private static readonly Guid HumanParty = Guid.Parse("40000000-0000-0000-0000-000000000001");

    // ── AUTONOMOUS: a CP workflow parks on the human-task carrying the CP basis ──

    [Fact]
    public async Task Autonomous_start_parks_on_the_human_task_with_the_cp_basis()
    {
        var sp = await NewProviderWithCpDefinitionAsync(HumanConfirmer());
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var instance = CpInstance("appr-1", step: "Draft");

        var outcome = await interpreter.DecideAsync(
            instance, WorkflowTrigger.For(WorkflowTriggerKind.Event, "appr-1", "Draft"));

        Assert.Equal(WorkflowStepOutcomeKind.Park, outcome.Kind);
        Assert.Equal("PendingApproval", outcome.NextStep);
        Assert.Equal(WorkflowStatus.Parked, outcome.NextStatus);
        Assert.Null(outcome.Effect);

        using var basis = JsonDocument.Parse(outcome.EventDataJson);
        Assert.Equal("cp-approval-basis", basis.RootElement.GetProperty("kind").GetString());
        Assert.Equal("ledger.post-journal-entry", basis.RootElement.GetProperty("capabilityRef").GetString());
        var verbs = basis.RootElement.GetProperty("typedOutcomes").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("approve", verbs);
        Assert.Contains("reject", verbs);
    }

    // ── CP CONFIRM: a human approve builds the CP effect THROUGH the broker ──

    [Fact]
    public async Task Human_approve_confirms_and_builds_the_cp_effect_through_the_broker()
    {
        var sp = await NewProviderWithCpDefinitionAsync(HumanConfirmer());
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var parked = CpInstance("appr-2", step: "PendingApproval");

        var outcome = await interpreter.DecideAsync(
            parked, Human("appr-2", "PendingApproval", "approve"));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal("Posted", outcome.NextStep);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.NotNull(outcome.Effect); // the effect was built — but ONLY via the broker's SoD-gated confirm path.

        // The D-INV-7 metric recorded exactly one confirmation.
        var sink = sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(1, sink.ConfirmedCount);
    }

    // ── SoD: a non-human confirmer is refused (nothing built, nothing recorded) ──

    [Fact]
    public async Task Non_human_confirmer_is_refused_by_separation_of_duties()
    {
        var sp = await NewProviderWithCpDefinitionAsync(
            confirmer: new WorkflowConfirmerIdentity(HumanParty, IsHuman: false));
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var parked = CpInstance("appr-3", step: "PendingApproval");

        await Assert.ThrowsAsync<WorkflowSodViolationException>(async () =>
            await interpreter.DecideAsync(parked, Human("appr-3", "PendingApproval", "approve")));

        var sink = sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
    }

    // ── SoD: the engine (non-human proposer) cannot be its own confirmer ──

    [Fact]
    public async Task Engine_self_confirm_is_refused_by_separation_of_duties()
    {
        // A human confirmer whose party id equals the engine (non-human) proposer's — the agent-proposed-CP
        // "distinct confirmer" rule fires even though the confirmer is human.
        var sp = await NewProviderWithCpDefinitionAsync(
            confirmer: new WorkflowConfirmerIdentity(EngineParty, IsHuman: true));
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var parked = CpInstance("appr-4", step: "PendingApproval");

        await Assert.ThrowsAsync<WorkflowSodViolationException>(async () =>
            await interpreter.DecideAsync(parked, Human("appr-4", "PendingApproval", "approve")));
    }

    // ── REJECT: no effect, override recorded ──

    [Fact]
    public async Task Human_reject_completes_with_no_effect_and_records_an_override()
    {
        var sp = await NewProviderWithCpDefinitionAsync(HumanConfirmer());
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var parked = CpInstance("appr-5", step: "PendingApproval");

        var outcome = await interpreter.DecideAsync(parked, Human("appr-5", "PendingApproval", "reject"));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal("Rejected", outcome.NextStep);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.Null(outcome.Effect);

        var sink = sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
        Assert.Equal(1, sink.OverriddenCount);
    }

    // ── SEND-BACK: a bounded loop-back ──

    [Fact]
    public async Task Send_back_loops_back_to_the_earlier_state()
    {
        var sp = await NewProviderWithCpDefinitionAsync(HumanConfirmer());
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var parked = CpInstance("appr-6", step: "PendingApproval");

        var outcome = await interpreter.DecideAsync(parked, Human("appr-6", "PendingApproval", "send-back"));

        Assert.Equal(WorkflowStepOutcomeKind.Park, outcome.Kind);
        Assert.Equal("Draft", outcome.NextStep);
        Assert.True(outcome.IsLoopBack);
    }

    // ── AP: an autonomous effecting action is built with no human ──

    [Fact]
    public async Task Ap_effect_is_built_autonomously_through_the_broker()
    {
        var sp = await NewProviderWithApDefinitionAsync();
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();
        var instance = new WorkflowInstanceRecord
        {
            Id = "ap-1", TenantId = Tenant, DefinitionKey = ApKey, DefinitionVersion = Version,
            CurrentStep = "Start", Status = WorkflowStatus.Running,
        };

        var outcome = await interpreter.DecideAsync(
            instance, WorkflowTrigger.For(WorkflowTriggerKind.Event, "ap-1", "Start"));

        Assert.Equal(WorkflowStepOutcomeKind.Advance, outcome.Kind);
        Assert.Equal("Notified", outcome.NextStep);
        Assert.Equal(WorkflowStatus.Completed, outcome.NextStatus);
        Assert.NotNull(outcome.Effect); // built autonomously — no human, no SoD.
    }

    // ── FAIL-CLOSED: a now-inadmissible pinned definition throws at load (never executes) ──

    [Fact]
    public async Task A_now_inadmissible_pinned_definition_throws_at_load_and_never_executes()
    {
        var throwingStore = new ThrowingExecutionStore();
        var sp = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddDurableWorkflowEngine()
            .AddSingleton<IWorkflowDefinitionExecutionStore>(throwingStore)
            .AddSingleton<IWorkflowConfirmationContext>(new FakeConfirmation(HumanConfirmer()))
            .AddDeclarativeWorkflowInterpreter()
            .BuildServiceProvider();
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();

        await Assert.ThrowsAsync<WorkflowAdmissionException>(async () =>
            await interpreter.DecideAsync(
                CpInstance("bad-1", "PendingApproval"),
                Human("bad-1", "PendingApproval", "approve")));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Harness
    // ─────────────────────────────────────────────────────────────────────────

    private static WorkflowConfirmerIdentity HumanConfirmer() => new(HumanParty, IsHuman: true);

    private static WorkflowTrigger Human(string instanceId, string step, string verb)
        => WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, instanceId, step, $"{{\"decision\":\"{verb}\"}}");

    private static WorkflowInstanceRecord CpInstance(string id, string step) => new()
    {
        Id = id, TenantId = Tenant, DefinitionKey = CpKey, DefinitionVersion = Version,
        CurrentStep = step, Status = step == "Draft" ? WorkflowStatus.Running : WorkflowStatus.Parked,
        StateJson = "{\"amount\":9000,\"debitAccount\":\"5000\",\"creditAccount\":\"2000\",\"memo\":\"vendor bill\"}",
    };

    private static async Task<ServiceProvider> NewProviderWithCpDefinitionAsync(WorkflowConfirmerIdentity confirmer)
    {
        var sp = BuildProvider(confirmer);
        await PersistAndPublishAsync(sp, CpAuthored(), CpKey);
        return sp;
    }

    private static async Task<ServiceProvider> NewProviderWithApDefinitionAsync()
    {
        var sp = BuildProvider(HumanConfirmer());
        await PersistAndPublishAsync(sp, ApAuthored(), ApKey);
        return sp;
    }

    private static ServiceProvider BuildProvider(WorkflowConfirmerIdentity confirmer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddDurableWorkflowEngine();

        // Delegate-registered effect factories (SC2 F-1 Owed #2 mechanism): the host hands in a build closure;
        // the internal factory type is never named. A no-op effect suffices — the assertions are on the OUTCOME
        // (the effect was produced ONLY through the broker's gated path), not on staging.
        services.AddWorkflowEffectFactory(
            "ledger.post-journal-entry", WorkflowEffectReach.Internal,
            (_, _) => new WorkflowEffect((_, _) => Task.CompletedTask));
        // notify.email is AP in the registry; reach Internal keeps it AP here (the OutboundExternal force is
        // covered by WorkflowEffectBrokerTests) — this exercises the interpreter's AP autonomous path.
        services.AddWorkflowEffectFactory(
            "notify.email", WorkflowEffectReach.Internal,
            (_, _) => new WorkflowEffect((_, _) => Task.CompletedTask));

        services.AddInMemoryWorkflowDefinitionStore();
        services.AddSingleton<IWorkflowConfirmationContext>(new FakeConfirmation(confirmer));
        services.AddDeclarativeWorkflowInterpreter();
        return services.BuildServiceProvider();
    }

    private static async Task PersistAndPublishAsync(IServiceProvider sp, JsonElement authored, string key)
    {
        var store = sp.GetRequiredService<IWorkflowDefinitionStore>();
        var model = WorkflowDefinitionWireMapper.ToModel(authored, Tenant, key, Version);
        await store.RegisterAsync(model, authored);
        await store.PublishAsync(Tenant, key, Version);
    }

    /// <summary>The vendor-invoice-approval shape: Draft →(issued)→ PendingApproval →(human approve, CP post)→ Posted.</summary>
    private static JsonElement CpAuthored() => JsonSerializer.SerializeToElement(new
    {
        key = CpKey,
        version = Version,
        status = "Published",
        tenant = Tenant,
        initialState = "Draft",
        states = new object[]
        {
            new { id = "Draft", kind = "Normal" },
            new { id = "PendingApproval", kind = "Normal" },
            new { id = "Posted", kind = "Terminal" },
            new { id = "Rejected", kind = "Terminal" },
        },
        triggers = new object[]
        {
            new { id = "issued", kind = "Event", eventType = "Issued" },
            new { id = "approve", kind = "HumanAction", task = "vendor-invoice-approval" },
            new { id = "reject", kind = "HumanAction", task = "vendor-invoice-approval" },
            new { id = "sendback", kind = "HumanAction", task = "vendor-invoice-approval" },
        },
        transitions = new object[]
        {
            new { id = "t-issue", from = "Draft", @on = "issued", to = "PendingApproval" },
            new { id = "t-approve", from = "PendingApproval", @on = "approve", to = "Posted", guard = "decision:approve" },
            new { id = "t-reject", from = "PendingApproval", @on = "reject", to = "Rejected", guard = "decision:reject" },
            new { id = "t-sendback", from = "PendingApproval", @on = "sendback", to = "Draft", guard = "decision:send-back" },
        },
        guards = new object[]
        {
            new { id = "decision:approve" },
            new { id = "decision:reject" },
            new { id = "decision:send-back" },
        },
        actions = new object[]
        {
            new
            {
                id = "a-post-je",
                @on = new { transition = "t-approve" },
                kind = "CreateRecord",
                capabilityRef = "ledger.post-journal-entry",
                classification = "CP",
            },
        },
    });

    /// <summary>The notify-on-issue shape: Start →(go, AP notify)→ Notified. No human — an autonomous AP effect.</summary>
    private static JsonElement ApAuthored() => JsonSerializer.SerializeToElement(new
    {
        key = ApKey,
        version = Version,
        status = "Published",
        tenant = Tenant,
        initialState = "Start",
        states = new object[]
        {
            new { id = "Start", kind = "Normal" },
            new { id = "Notified", kind = "Terminal" },
        },
        triggers = new object[] { new { id = "go", kind = "Event", eventType = "Issued" } },
        transitions = new object[] { new { id = "t-go", from = "Start", @on = "go", to = "Notified" } },
        actions = new object[]
        {
            new
            {
                id = "a-notify",
                @on = new { transition = "t-go" },
                kind = "Notify",
                capabilityRef = "notify.email",
                classification = "AP",
            },
        },
    });

    private sealed class FakeConfirmation(WorkflowConfirmerIdentity confirmer) : IWorkflowConfirmationContext
    {
        public WorkflowProposerIdentity EngineProposer { get; } = new(EngineParty, IsHuman: false);

        public ValueTask<WorkflowConfirmerIdentity> ResolveConfirmerAsync(CancellationToken ct = default)
            => ValueTask.FromResult(confirmer);
    }

    /// <summary>An execution store that always fails the load-time re-admit (models a now-inadmissible definition).</summary>
    private sealed class ThrowingExecutionStore : IWorkflowDefinitionExecutionStore
    {
        public ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(
            string tenant, string key, CancellationToken ct = default)
            => throw Inadmissible();

        public ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(
            string tenant, string key, string version, CancellationToken ct = default)
            => throw Inadmissible();

        private static WorkflowAdmissionException Inadmissible()
            => new(new WorkflowAdmissionResult
            {
                Violations = new[]
                {
                    new WorkflowAdmissionViolation(
                        WorkflowAdmissionCodes.ClassificationMismatch, "capability reclassified to CP", "a-post-je"),
                },
            });
    }
}
