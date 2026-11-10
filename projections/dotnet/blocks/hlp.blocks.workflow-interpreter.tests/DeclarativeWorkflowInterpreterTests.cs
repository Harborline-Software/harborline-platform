using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using Harborline.Blocks.Workflow.Durable;
using Harborline.Contracts.Authorization;

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

    // ── workflows-ck-5 (R-0155 D1): an autonomous edge fires only on the trigger its `on` binding names. SCXML:
    //    "If no transition matches in any state, the event is discarded." The run rests where it is, with no
    //    transition, effect or park. ──

    [Theory]
    [InlineData(WorkflowTriggerKind.Schedule)]
    [InlineData(WorkflowTriggerKind.DependencyComplete)]
    public async Task Workflows_ck_5_an_autonomous_trigger_the_edge_does_not_name_leaves_the_run_where_it_is(WorkflowTriggerKind delivered)
    {
        var ap = Interpreter(await NewProviderWithApDefinitionAsync());
        var rested = await ap.DecideAsync(ApInstance("match-ap"), WorkflowTrigger.For(delivered, "match-ap", "Start"));
        Assert.Equal(WorkflowStepOutcomeKind.Discard, rested.Kind);
        Assert.Equal("Start", rested.NextStep);
        Assert.Equal(WorkflowStatus.Running, rested.NextStatus);
        Assert.Null(rested.Effect);

        var cp = Interpreter(await NewProviderWithCpDefinitionAsync(HumanConfirmer()));
        var unparked = await cp.DecideAsync(CpInstance("match-cp", "Draft"), WorkflowTrigger.For(delivered, "match-cp", "Draft"));
        Assert.Equal(WorkflowStepOutcomeKind.Discard, unparked.Kind);
        Assert.Equal("Draft", unparked.NextStep);
        Assert.Equal(WorkflowStatus.Running, unparked.NextStatus);
        Assert.Null(unparked.Effect);
    }

    [Fact]
    public async Task Workflows_ck_5_the_trigger_the_edge_names_takes_it()
    {
        var ap = Interpreter(await NewProviderWithApDefinitionAsync());
        var taken = await ap.DecideAsync(ApInstance("match-go"), WorkflowTrigger.For(WorkflowTriggerKind.Event, "match-go", "Start"));
        Assert.Equal("Notified", taken.NextStep);
        Assert.Equal(WorkflowStatus.Completed, taken.NextStatus);
        Assert.NotNull(taken.Effect);
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

    // ── FAIL CLOSED ON AN UNENFORCED GATE (DES-0019 workflows-eng-13, section 6 L422; OWASP deny by default,
    //    CWE-862). The interpreter has no authorization gate yet, so a declared role or standing refuses with a
    //    stable code before anything is selected, built or recorded. Removing the refusal makes each of these
    //    run the gated element and fail. ──

    [Fact]
    public async Task A_human_transition_that_declares_a_role_is_refused_and_builds_nothing()
    {
        var sp = BuildProvider(HumanConfirmer());
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve",
            "requiredRoles", new JsonArray(new JsonObject { ["vocabulary"] = "sys.roles", ["name"] = "Approver" })), CpKey);
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();

        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await interpreter.DecideAsync(CpInstance("gate-1", "PendingApproval"), Human("gate-1", "PendingApproval", "approve")));

        Assert.Equal("workflow.interpreter.gate_unenforced", refusal.Code);
        Assert.Contains("'t-approve'", refusal.Message, StringComparison.Ordinal);
        var sink = sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
        Assert.Equal(0, sink.OverriddenCount);
    }

    [Fact]
    public async Task A_gate_on_an_element_the_step_does_not_reach_does_not_block_the_step()
    {
        // The refusal is per element: the reject path carries no gate and still records its override.
        var sp = BuildProvider(HumanConfirmer());
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve",
            "requiredRoles", new JsonArray(new JsonObject { ["vocabulary"] = "sys.roles", ["name"] = "Approver" })), CpKey);
        var interpreter = sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();

        var outcome = await interpreter.DecideAsync(CpInstance("gate-4", "PendingApproval"), Human("gate-4", "PendingApproval", "reject"));

        Assert.Equal("Rejected", outcome.NextStep);
        Assert.Equal(1, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().OverriddenCount);
    }

    // ── T-1176 Q1: a gate on an autonomous element is refused at admission; the runtime refusal is the backstop ──

    [Fact]
    public async Task An_autonomous_transition_that_declares_a_standing_is_refused_at_admission_and_at_runtime()
    {
        var authored = Gate(ApAuthored(), "transitions", "t-go", "requiredStandings", new JsonArray("assigned-approver"));

        var admission = await Assert.ThrowsAsync<WorkflowAdmissionException>(async () =>
            await PersistAndPublishAsync(BuildProvider(HumanConfirmer()), authored, ApKey));
        Assert.Contains(admission.Result.Violations, v => v.Code == WorkflowAdmissionCodes.GateOnAutonomousStep && v.Locator == "t-go");

        // A store that hands over a definition without re-admitting it still cannot run the gated step.
        var sp = BuildProvider(HumanConfirmer(), rawDefinition: (ApKey, authored), gate: new RecordingGate(satisfied: true));
        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await sp.GetRequiredService<IDeclarativeWorkflowInterpreter>().DecideAsync(
                ApInstance("gate-2"), WorkflowTrigger.For(WorkflowTriggerKind.Event, "gate-2", "Start"), Authority("gate-2")));
        Assert.Equal(DeclarativeWorkflowInterpreterCodes.GateUnenforced, refusal.Code);
        Assert.Empty(sp.GetRequiredService<RecordingGate>().Asked);
    }

    [Fact]
    public async Task An_autonomous_effecting_action_that_declares_a_role_is_refused_at_admission_and_before_its_effect()
    {
        var authored = Gate(ApAuthored(), "actions", "a-notify",
            "requiredRoles", new JsonArray(new JsonObject { ["vocabulary"] = "sys.roles", ["name"] = "Author" }));

        var admission = await Assert.ThrowsAsync<WorkflowAdmissionException>(async () =>
            await PersistAndPublishAsync(BuildProvider(HumanConfirmer()), authored, ApKey));
        Assert.Contains(admission.Result.Violations, v => v.Code == WorkflowAdmissionCodes.GateOnAutonomousStep && v.Locator == "a-notify");

        var sp = BuildProvider(HumanConfirmer(), rawDefinition: (ApKey, authored), gate: new RecordingGate(satisfied: true));
        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await sp.GetRequiredService<IDeclarativeWorkflowInterpreter>().DecideAsync(
                ApInstance("gate-3"), WorkflowTrigger.For(WorkflowTriggerKind.Event, "gate-3", "Start"), Authority("gate-3")));
        Assert.Contains("'a-notify'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_gated_step_reachable_by_a_person_and_automatically_is_refused_at_admission_and_on_the_automatic_path()
    {
        // 'Posted' is entered by the person's approve (t-approve) AND by an automatic edge (t-auto, Event). Its on-enter
        // action carries a role: since an automatic path reaches it, the gate is refused, whatever the human path does.
        var root = JsonNode.Parse(CpAuthored().GetRawText())!.AsObject();
        root["transitions"]!.AsArray().Add(new JsonObject { ["id"] = "t-auto", ["from"] = "PendingApproval", ["on"] = "issued", ["to"] = "Posted" });
        var action = root["actions"]!.AsArray()[0]!.AsObject();
        action["on"] = new JsonObject { ["state"] = "Posted" };
        action["requiredRoles"] = ApproverRole();
        var authored = JsonSerializer.SerializeToElement(root);

        var admission = await Assert.ThrowsAsync<WorkflowAdmissionException>(async () =>
            await PersistAndPublishAsync(BuildProvider(HumanConfirmer()), authored, CpKey));
        Assert.Contains(admission.Result.Violations, v => v.Code == WorkflowAdmissionCodes.GateOnAutonomousStep && v.Locator == "a-post-je");

        var gate = new RecordingGate(satisfied: true);
        var sp = BuildProvider(HumanConfirmer(), rawDefinition: (CpKey, authored), gate: gate);
        var refusal = await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("mixed-1", "Draft"),
                WorkflowTrigger.For(WorkflowTriggerKind.Event, "mixed-1", "Draft"), Authority("mixed-1")));
        Assert.Contains("'a-post-je'", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(gate.Asked);
        Assert.Equal(0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    // ── T-1176/platform-role-gate: the host gate port decides a role gate where a person acts ──

    [Fact]
    public async Task A_role_gated_human_transition_the_actor_does_not_hold_is_refused_before_any_effect()
    {
        var gate = new RecordingGate(satisfied: false);
        var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve", "requiredRoles", ApproverRole()), CpKey);

        var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("role-1", "PendingApproval"), Human("role-1", "PendingApproval", "approve"), Authority("role-1")));

        Assert.Equal(WorkflowGateCodes.Denied, refusal.Code);
        var asked = Assert.Single(gate.Asked);
        Assert.Equal(WorkflowGateElement.Transition, asked.Element);
        Assert.Equal("t-approve", asked.ElementId);
        Assert.Equal(DispatchPrincipal, asked.Principal);
        var sink = sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>();
        Assert.Equal(0, sink.ConfirmedCount);
        Assert.Equal(0, sink.OverriddenCount);
    }

    [Fact]
    public async Task A_role_gated_human_transition_the_actor_holds_runs_and_builds_its_effect()
    {
        var gate = new RecordingGate(satisfied: true);
        var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve", "requiredRoles", ApproverRole()), CpKey);

        var outcome = await Interpreter(sp).DecideAsync(
            CpInstance("role-2", "PendingApproval"), Human("role-2", "PendingApproval", "approve"), Authority("role-2"));

        Assert.Equal("Posted", outcome.NextStep);
        Assert.NotNull(outcome.Effect);
        Assert.Equal(1, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
        Assert.Equal([new RoleReference("sys.roles", "Approver")], Assert.Single(gate.Asked).RequiredRoles);
    }

    [Fact]
    public async Task A_role_named_only_in_the_trigger_payload_changes_nothing()
    {
        var gate = new RecordingGate(satisfied: false);
        var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve", "requiredRoles", ApproverRole()), CpKey);
        var forged = WorkflowTrigger.For(WorkflowTriggerKind.HumanAction, "role-3", "PendingApproval",
            "{\"decision\":\"approve\",\"principal\":\"admin\",\"requiredRoles\":[],\"roles\":[\"sys.roles/Approver\"]}");

        await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("role-3", "PendingApproval"), forged, Authority("role-3")));

        var asked = Assert.Single(gate.Asked);
        Assert.Equal(DispatchPrincipal, asked.Principal);
        Assert.Equal([new RoleReference("sys.roles", "Approver")], asked.RequiredRoles);
    }

    [Fact]
    public async Task An_answer_for_a_different_intent_is_refused()
    {
        var sp = BuildProvider(HumanConfirmer(), gate: new RecordingGate(satisfied: true, rebind: i => i with { Principal = "someone-else" }));
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve", "requiredRoles", ApproverRole()), CpKey);

        var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("role-4", "PendingApproval"), Human("role-4", "PendingApproval", "approve"), Authority("role-4")));

        Assert.Equal(WorkflowGateCodes.AnswerUnbound, refusal.Code);
        Assert.Equal(0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    [Fact]
    public async Task A_role_gated_effecting_action_the_actor_does_not_hold_is_refused_before_the_broker_builds_it()
    {
        var gate = new RecordingGate(satisfied: false);
        var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "actions", "a-post-je", "requiredRoles", ApproverRole()), CpKey);

        var refusal = await Assert.ThrowsAsync<WorkflowGateRefusedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("role-5", "PendingApproval"), Human("role-5", "PendingApproval", "approve"), Authority("role-5")));

        Assert.Equal(WorkflowGateCodes.Denied, refusal.Code);
        Assert.Equal(WorkflowGateElement.Action, Assert.Single(gate.Asked).Element);
        Assert.Equal(0, sp.GetRequiredService<CountingWorkflowApprovalDecisionSink>().ConfirmedCount);
    }

    [Fact]
    public async Task Without_a_verified_authority_or_for_a_standing_gate_the_interim_refusal_stands()
    {
        var gate = new RecordingGate(satisfied: true);
        var sp = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(sp, Gate(CpAuthored(), "transitions", "t-approve", "requiredRoles", ApproverRole()), CpKey);

        // The authority-free overload names no actor, so there is no one to decide for.
        await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await Interpreter(sp).DecideAsync(CpInstance("role-6", "PendingApproval"), Human("role-6", "PendingApproval", "approve")));

        var standing = BuildProvider(HumanConfirmer(), gate: gate);
        await PersistAndPublishAsync(standing, Gate(CpAuthored(), "transitions", "t-approve", "requiredStandings", new JsonArray("assigned-approver")), CpKey);
        await Assert.ThrowsAsync<WorkflowGateUnenforcedException>(async () =>
            await Interpreter(standing).DecideAsync(CpInstance("role-7", "PendingApproval"), Human("role-7", "PendingApproval", "approve"), Authority("role-7")));

        Assert.Empty(gate.Asked);
    }

    private const string DispatchPrincipal = "dispatch-principal";

    private static IDeclarativeWorkflowInterpreter Interpreter(IServiceProvider sp)
        => sp.GetRequiredService<IDeclarativeWorkflowInterpreter>();

    private static JsonArray ApproverRole() => new(new JsonObject { ["vocabulary"] = "sys.roles", ["name"] = "Approver" });

    private static WorkflowDispatchAuthority Authority(string instanceId) => new(
        DispatchPrincipal, Tenant, WorkflowDispatchAuthority.RequiredOperation, WorkflowDispatchAuthority.RequiredRecordKind, instanceId, Allowed: true);

    private sealed class RecordingGate(bool satisfied, Func<WorkflowGateIntent, WorkflowGateIntent>? rebind = null) : IWorkflowGateAccess
    {
        public List<WorkflowGateIntent> Asked { get; } = [];

        public ValueTask<WorkflowGateAnswer> DecideAsync(WorkflowGateIntent intent, CancellationToken ct = default)
        {
            Asked.Add(intent);
            return ValueTask.FromResult(new WorkflowGateAnswer(rebind?.Invoke(intent) ?? intent, satisfied));
        }
    }

    private sealed class RawExecutionStore(string key, JsonElement authored) : IWorkflowDefinitionExecutionStore
    {
        private WorkflowDefinitionRecord Record => new(Tenant, key, Version, WorkflowDefinitionStatus.Published, authored);

        public ValueTask<WorkflowDefinitionRecord?> GetAdmittedCurrentPublishedAsync(string tenant, string k, CancellationToken ct = default)
            => ValueTask.FromResult<WorkflowDefinitionRecord?>(Record);

        public ValueTask<WorkflowDefinitionRecord> GetAdmittedAsync(string tenant, string k, string version, CancellationToken ct = default)
            => ValueTask.FromResult(Record);
    }

    private static JsonElement Gate(JsonElement authored, string collection, string id, string member, JsonArray gate)
    {
        var root = JsonNode.Parse(authored.GetRawText())!.AsObject();
        var element = root[collection]!.AsArray().Single(e => (string?)e!["id"] == id)!.AsObject();
        element[member] = gate;
        return JsonSerializer.SerializeToElement(root);
    }

    private static WorkflowInstanceRecord ApInstance(string id) => new()
    {
        Id = id, TenantId = Tenant, DefinitionKey = ApKey, DefinitionVersion = Version,
        CurrentStep = "Start", Status = WorkflowStatus.Running, StateJson = "{}",
    };

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

    private static ServiceProvider BuildProvider(
        WorkflowConfirmerIdentity confirmer, (string Key, JsonElement Authored)? rawDefinition = null, IWorkflowGateAccess? gate = null)
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
        if (rawDefinition is { } raw)
            services.AddSingleton<IWorkflowDefinitionExecutionStore>(new RawExecutionStore(raw.Key, raw.Authored));
        if (gate is not null)
        {
            services.AddSingleton(gate);
            if (gate is RecordingGate recording) services.AddSingleton(recording);
        }
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
