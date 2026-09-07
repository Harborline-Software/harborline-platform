using Harborline.Blocks.Workflow.Durable;

using Xunit;

namespace Harborline.Blocks.Workflow.Tests;

/// <summary>
/// WF-KEY admission validator — the adversarial coverage for the security keystone
/// (ADR 0140 / ADR 0135 A1 R-1). The two load-bearing invariants are fail-closed:
///   (1) an UNCLASSIFIED action is refused;
///   (2) a CP action reachable from an AUTONOMOUS trigger with no interposed human-task is refused.
/// The canonical invoice-approval shape (autonomous Issued event → parked approval → CP post on the
/// HUMAN approve transition) MUST admit; the adversarial variants (the CP post fired autonomously) MUST
/// be refused. This is the test the deep-review keys on — an admissible graph stays admissible, a
/// fence-violating graph cannot publish.
/// </summary>
public sealed class WorkflowAdmissionValidatorTests
{
    private static readonly IWorkflowAdmissionValidator Validator = new WorkflowAdmissionValidator();

    // ── builders for the canonical invoice-approval graph ────────────────────
    //
    //   Draft --(Issued: Event, autonomous)--> PendingApproval
    //   PendingApproval --(approve: HumanAction)--> Posted   [CP "post JE" fires here]
    //   PendingApproval --(reject: HumanAction)--> Rejected

    private static WorkflowTriggerBindingDef Trig(string id, WorkflowTriggerKind kind) => new() { Id = id, Kind = kind };

    private static WorkflowDefinition InvoiceApproval(
        WorkflowActionBindingDef postAction)
        => new()
        {
            Key = "invoice-approval",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "Draft",
            Mutability = WorkflowMutability.Locked,
            States =
            [
                new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
                new() { Id = "PendingApproval", Kind = WorkflowStateKind.Normal },
                new() { Id = "Posted", Kind = WorkflowStateKind.Terminal },
                new() { Id = "Rejected", Kind = WorkflowStateKind.Terminal },
            ],
            Triggers =
            [
                Trig("issued", WorkflowTriggerKind.Event),
                Trig("approve", WorkflowTriggerKind.HumanAction),
                Trig("reject", WorkflowTriggerKind.HumanAction),
            ],
            Transitions =
            [
                new() { Id = "t-issue", From = "Draft", On = "issued", To = "PendingApproval" },
                new() { Id = "t-approve", From = "PendingApproval", On = "approve", To = "Posted" },
                new() { Id = "t-reject", From = "PendingApproval", On = "reject", To = "Rejected" },
            ],
            Actions = [postAction],
        };

    private static WorkflowActionBindingDef PostJe(
        ActionClassification classification,
        string? onState = null,
        string? onTransition = null)
        => new()
        {
            Id = "a-post-je",
            OnState = onState,
            OnTransition = onTransition,
            Kind = WorkflowActionKind.CreateRecord,
            CapabilityRef = "ledger.post-journal-entry",
            Classification = classification,
        };

    // ── ADMIT: the canonical safe shape ──────────────────────────────────────

    [Fact]
    public void Admits_cp_post_on_the_human_approve_transition()
    {
        // The post-JE CP action fires on the HumanAction approve transition — the human IS the gate.
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onTransition: "t-approve"));
        var result = Validator.Validate(def);
        Assert.True(result.IsValid, string.Join("; ", result.Violations.Select(x => x.Code)));
    }

    [Fact]
    public void Admits_cp_post_on_a_human_only_reachable_terminal_state()
    {
        // Posted is reachable ONLY via the HumanAction approve transition ⇒ not autonomously tainted ⇒ a
        // CP on-enter action there is human-gated ⇒ admissible.
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onState: "Posted"));
        var result = Validator.Validate(def);
        Assert.True(result.IsValid, string.Join("; ", result.Violations.Select(x => x.Code)));
    }

    [Fact]
    public void Admits_an_ap_action_on_an_autonomous_transition()
    {
        // A NON-CP (AP) notify on the autonomous Issued transition is fine — only CP is fenced.
        var def = InvoiceApproval(new WorkflowActionBindingDef
        {
            Id = "a-notify",
            OnTransition = "t-issue",
            Kind = WorkflowActionKind.Notify,
            CapabilityRef = "notify.email",
            Classification = ActionClassification.AP,
        });
        Assert.True(Validator.Validate(def).IsValid);
    }

    // ── REFUSE: the adversarial fence violations ─────────────────────────────

    [Fact]
    public void Refuses_cp_post_fired_on_the_autonomous_issued_transition()
    {
        // The CP post-JE fires on the autonomous Issued event transition — no human gate ⇒ REFUSE.
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onTransition: "t-issue"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    [Fact]
    public void Refuses_cp_post_on_an_autonomously_reachable_state()
    {
        // PendingApproval is reachable via the autonomous Issued event ⇒ a CP on-enter action there fires
        // autonomously ⇒ REFUSE.
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onState: "PendingApproval"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    [Fact]
    public void Refuses_cp_post_on_the_initial_state()
    {
        // The initial state is autonomously tainted (a schedule/event could fire the first step) ⇒ a CP
        // on-enter action on Draft ⇒ REFUSE.
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onState: "Draft"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    [Fact]
    public void Refuses_an_unclassified_action_fail_closed()
    {
        // No CP/AP classification ⇒ refuse, never default-allow.
        var def = InvoiceApproval(PostJe(ActionClassification.Unspecified, onTransition: "t-approve"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.ActionUnclassified);
    }

    [Fact]
    public void Refuses_an_out_of_range_classification_fail_closed()
    {
        // System.Text.Json numeric-enum deserialization can yield an out-of-range ActionClassification
        // (e.g. "classification": 99 ⇒ (ActionClassification)99). It is NEITHER CP NOR AP ⇒ must refuse —
        // parity with the TS gate's `!== 'CP' && !== 'AP'` (this case used to fail-OPEN on .NET).
        var def = InvoiceApproval(PostJe((ActionClassification)99, onTransition: "t-approve"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.ActionUnclassified);
    }

    // ── ADR 0143 (red-team Chain 1+5 / F2) — classification is REGISTRY-DERIVED, not author-trusted ──

    private static WorkflowActionBindingDef Action(
        string capabilityRef, ActionClassification classification, string onTransition)
        => new()
        {
            Id = "a-post-je",
            OnTransition = onTransition,
            Kind = WorkflowActionKind.CreateRecord,
            CapabilityRef = capabilityRef,
            Classification = classification,
        };

    [Fact]
    public void Refuses_a_cp_capability_declared_ap_classification_mismatch()
    {
        // The exact shipped PostJe(classification,…) hole: `ledger.post-journal-entry` is CP in the
        // authority registry; declaring it AP must NOT let it slip past the human-gate as AP. The
        // author label is a non-authoritative assertion that must match the derived class.
        var def = InvoiceApproval(PostJe(ActionClassification.AP, onTransition: "t-approve"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.ClassificationMismatch);
    }

    [Fact]
    public void Refuses_an_unknown_capability_declared_ap_fail_closed_to_cp()
    {
        // Unknown ⇒ CP (fail-closed); declaring AP ⇒ mismatch ⇒ refuse. An unregistered capability
        // can never be laundered to AP through an authored label.
        var def = InvoiceApproval(Action("totally.unregistered", ActionClassification.AP, "t-approve"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.ClassificationMismatch);
    }

    [Fact]
    public void Admits_an_unknown_capability_declared_cp_on_the_human_approve_transition()
    {
        // Unknown ⇒ CP; declaring CP ⇒ match; fires on the human approve transition ⇒ human-gated ⇒ admits.
        var def = InvoiceApproval(Action("totally.unregistered", ActionClassification.CP, "t-approve"));
        var result = Validator.Validate(def);
        Assert.True(result.IsValid, string.Join("; ", result.Violations.Select(x => x.Code)));
    }

    [Fact]
    public void Refuses_an_unknown_capability_declared_cp_on_the_autonomous_issued_transition()
    {
        // Unknown ⇒ CP; declaring CP ⇒ match; but it fires on the autonomous Issued edge ⇒ CP fence refuses.
        var def = InvoiceApproval(Action("totally.unregistered", ActionClassification.CP, "t-issue"));
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    // ── FINDING 1 — the two taint-model holes the tightened fence must now refuse ─

    [Fact]
    public void Refuses_cp_post_on_an_autonomous_schedule_edge_downstream_of_a_human_approval()
    {
        // Draft --approve(HumanAction)--> Approved --tick(Schedule)--> Done, CP post-JE on the Schedule edge.
        // A human gated ENTRY into Approved, but the CP fires on the AUTONOMOUS tick — the human did NOT gate
        // the FIRING. The old "human anywhere upstream" taint model ADMITTED this; the tightened rule refuses.
        var def = new WorkflowDefinition
        {
            Key = "downstream-autonomous",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "Draft",
            States =
            [
                new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
                new() { Id = "Approved", Kind = WorkflowStateKind.Normal },
                new() { Id = "Done", Kind = WorkflowStateKind.Terminal },
            ],
            Triggers =
            [
                Trig("approve", WorkflowTriggerKind.HumanAction),
                Trig("tick", WorkflowTriggerKind.Schedule),
            ],
            Transitions =
            [
                new() { Id = "t-approve", From = "Draft", On = "approve", To = "Approved" },
                new() { Id = "t-tick", From = "Approved", On = "tick", To = "Done" },
            ],
            Actions = [PostJe(ActionClassification.CP, onTransition: "t-tick")],
        };
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    [Fact]
    public void Refuses_cp_on_enter_re_fired_by_an_autonomous_back_edge()
    {
        // Draft --approve(HumanAction)--> A --tick(Schedule)--> B --ev(Event)--> A, CP on-enter on A.
        // One human approval enters A (the CP fires, authorized); then the autonomous A->B->A loop re-enters
        // A and re-fires the on-enter CP every lap with NO further human (the double-post hole). The tightened
        // rule refuses: A has an autonomous incoming edge, so not EVERY entry into A is human-gated.
        var def = new WorkflowDefinition
        {
            Key = "back-edge-refire",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "Draft",
            States =
            [
                new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
                new() { Id = "A", Kind = WorkflowStateKind.Normal },
                new() { Id = "B", Kind = WorkflowStateKind.Normal },
            ],
            Triggers =
            [
                Trig("approve", WorkflowTriggerKind.HumanAction),
                Trig("tick", WorkflowTriggerKind.Schedule),
                Trig("ev", WorkflowTriggerKind.Event),
            ],
            Transitions =
            [
                new() { Id = "t-approve", From = "Draft", On = "approve", To = "A" },
                new() { Id = "t-tick", From = "A", On = "tick", To = "B" },
                new() { Id = "t-ev", From = "B", On = "ev", To = "A" },
            ],
            Actions = [PostJe(ActionClassification.CP, onState: "A")],
        };
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    [Fact]
    public void Flags_a_terminal_state_with_an_outgoing_transition()
    {
        // Posted is Terminal but given an outgoing edge — violates the Terminal contract (no outgoing).
        var def = new WorkflowDefinition
        {
            Key = "terminal-outgoing",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "Draft",
            States =
            [
                new() { Id = "Draft", Kind = WorkflowStateKind.Normal },
                new() { Id = "Posted", Kind = WorkflowStateKind.Terminal },
            ],
            Triggers =
            [
                Trig("approve", WorkflowTriggerKind.HumanAction),
                Trig("loop", WorkflowTriggerKind.HumanAction),
            ],
            Transitions =
            [
                new() { Id = "t-approve", From = "Draft", On = "approve", To = "Posted" },
                new() { Id = "t-loop", From = "Posted", On = "loop", To = "Draft" }, // Terminal with outgoing
            ],
            Actions = [],
        };
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.TerminalHasOutgoing);
    }

    [Fact]
    public void EnsureAdmissible_throws_on_a_fence_violation()
    {
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onTransition: "t-issue"));
        var ex = Assert.Throws<WorkflowAdmissionException>(() => Validator.EnsureAdmissible(def));
        Assert.Contains(ex.Result.Violations, x => x.Code == WorkflowAdmissionCodes.CpReachableWithoutHumanTask);
    }

    // ── reachability + referential integrity ─────────────────────────────────

    [Fact]
    public void Refuses_an_unreachable_state()
    {
        var def = InvoiceApproval(PostJe(ActionClassification.CP, onTransition: "t-approve"));
        var orphaned = new WorkflowDefinition
        {
            Key = def.Key,
            Version = def.Version,
            Tenant = def.Tenant,
            InitialState = def.InitialState,
            States = [.. def.States, new WorkflowStateDef { Id = "Orphan", Kind = WorkflowStateKind.Terminal }],
            Triggers = def.Triggers,
            Transitions = def.Transitions,
            Actions = def.Actions,
        };
        var result = Validator.Validate(orphaned);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.UnreachableState);
    }

    [Fact]
    public void Refuses_a_dead_end_non_terminal_state()
    {
        var def = new WorkflowDefinition
        {
            Key = "dead-end",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "A",
            States =
            [
                new() { Id = "A", Kind = WorkflowStateKind.Normal },
                new() { Id = "B", Kind = WorkflowStateKind.Normal }, // reachable, non-terminal, no outgoing
            ],
            Triggers = [Trig("go", WorkflowTriggerKind.HumanAction)],
            Transitions = [new() { Id = "t", From = "A", On = "go", To = "B" }],
            Actions = [],
        };
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.DeadEndState);
    }

    [Fact]
    public void Refuses_a_dangling_transition_reference()
    {
        var def = new WorkflowDefinition
        {
            Key = "dangling",
            Version = "1.0.0",
            Tenant = "tenant:acme",
            InitialState = "A",
            States = [new() { Id = "A", Kind = WorkflowStateKind.Terminal }],
            Triggers = [Trig("go", WorkflowTriggerKind.HumanAction)],
            Transitions = [new() { Id = "t", From = "A", On = "go", To = "Nowhere" }],
            Actions = [],
        };
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.DanglingReference);
    }

    [Fact]
    public void Refuses_an_action_binding_naming_neither_state_nor_transition()
    {
        var def = InvoiceApproval(new WorkflowActionBindingDef
        {
            Id = "a-bad",
            Kind = WorkflowActionKind.Notify,
            CapabilityRef = "notify.email",
            Classification = ActionClassification.AP,
            // neither OnState nor OnTransition
        });
        var result = Validator.Validate(def);
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, x => x.Code == WorkflowAdmissionCodes.ActionBindingInvalid);
    }
}
