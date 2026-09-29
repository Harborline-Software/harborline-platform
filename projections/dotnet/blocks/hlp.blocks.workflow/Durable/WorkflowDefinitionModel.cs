namespace Harborline.Blocks.Workflow.Durable;

// ─────────────────────────────────────────────────────────────────────────────
//  WF-KEY — the DECLARATIVE workflow-definition model (ADR 0140; the process keystone).
//
//  The data-driven authoring shape of a hand-written typed IWorkflowStepHandler — the
//  process analog of the dynamic-forms FormDefinition. States / guarded transitions /
//  triggers / actions as DATA. The .NET mirror of @harborline-software/contracts WorkflowDefinition
//  (packages/contracts/src/workflow.ts); a drift between the two is what the structural
//  parity test pins.
//
//  WF-KEY v1 AUTHORS + VALIDATES (WorkflowAdmissionValidator) + STORES + SIMULATES this
//  model; it is NOT executed by a general interpreter. Live execution stays gated behind
//  the deferred A1 interpreter + the broker-PEP (ADR 0135 Amendment 2026-06-24). These
//  POCOs carry NO EF dependency (mirrors DurableWorkflowModel.cs) and NO runtime authority.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Lifecycle status of a <see cref="WorkflowDefinition"/> revision.</summary>
public enum WorkflowDefinitionStatus
{
    /// <summary>Being authored; not executable.</summary>
    Draft = 0,
    /// <summary>Admitted and live; the only status the execution store serves.</summary>
    Published = 1,
    /// <summary>Superseded by a later revision; kept for reference and not served for execution.</summary>
    Deprecated = 2,
    /// <summary>Taken out of service; not served for execution.</summary>
    Withdrawn = 3,
}

/// <summary>Editability posture (ADR 0135 §4.4). Locked = rigid orchestration; Editable = re-pinned on edit.</summary>
public enum WorkflowMutability
{
    /// <summary>Rigid orchestration; running instances are not re-pinned to a new revision.</summary>
    Locked = 0,
    /// <summary>Running instances are re-pinned when the definition is edited.</summary>
    Editable = 1,
}

/// <summary>A state's kind. A <see cref="Terminal"/> state has no outgoing transitions.</summary>
public enum WorkflowStateKind
{
    /// <summary>An intermediate state; admission requires at least one outgoing transition.</summary>
    Normal = 0,
    /// <summary>An end state; admission refuses any outgoing transition.</summary>
    Terminal = 1,
}

/// <summary>
/// The authoring-declared authority an action consumes (ADR 0128). The default
/// <see cref="Unspecified"/> (0) is the fail-closed sentinel: an action that did not
/// declare a classification is REFUSED at admission (never default-allow).
/// </summary>
public enum ActionClassification
{
    /// <summary>No classification declared — refused at admission (fail-closed).</summary>
    Unspecified = 0,

    /// <summary>Consequential: post to GL, send money, delete, touch a CP-locked field. MUST be human-gated.</summary>
    CP = 1,

    /// <summary>Autonomous-permitted.</summary>
    AP = 2,
}

/// <summary>What an action does (ADR 0140 — the security-relevant surface).</summary>
public enum WorkflowActionKind
{
    /// <summary>Sends a notification.</summary>
    Notify = 0,
    /// <summary>Creates a record.</summary>
    CreateRecord = 1,
    /// <summary>Writes a field on an existing record.</summary>
    UpdateField = 2,
    /// <summary>Calls a service capability.</summary>
    InvokeService = 3,
    /// <summary>Starts a child workflow instance.</summary>
    StartSubProcess = 4,
    /// <summary>Publishes a domain event.</summary>
    EmitEvent = 5,
}

/// <summary>A state an instance parks at / advances through (the engine's CurrentStep).</summary>
public sealed class WorkflowStateDef
{
    /// <summary>Stable state id (matches <see cref="WorkflowInstanceRecord.CurrentStep"/>).</summary>
    public required string Id { get; init; }

    /// <summary>Terminal ⇒ no outgoing transitions; Normal ⇒ must have ≥1 outgoing.</summary>
    public WorkflowStateKind Kind { get; init; } = WorkflowStateKind.Normal;

    /// <summary>Presentation lens hint (flow/gantt/kanban); does not affect admission.</summary>
    public string? ViewHint { get; init; }
}

/// <summary>
/// A guarded edge {from, on, to}. The declarative form of a typed handler's switch decision.
/// The optional <see cref="Guard"/> is a rule id evaluated by IGuardEvaluator.
/// </summary>
public sealed class WorkflowTransitionDef
{
    /// <summary>Stable id, unique among its kind within the definition; admission refuses duplicates.</summary>
    public required string Id { get; init; }

    /// <summary>Source state id.</summary>
    public required string From { get; init; }

    /// <summary>The trigger that offers this transition (a <see cref="WorkflowTriggerBindingDef.Id"/>).</summary>
    public required string On { get; init; }

    /// <summary>Destination state id.</summary>
    public required string To { get; init; }

    /// <summary>Optional guard rule id.</summary>
    public string? Guard { get; init; }
}

/// <summary>A trigger the definition declares against (reuses the four <see cref="WorkflowTriggerKind"/>).</summary>
public sealed class WorkflowTriggerBindingDef
{
    /// <summary>Stable id, unique among its kind within the definition; admission refuses duplicates.</summary>
    public required string Id { get; init; }

    /// <summary>Which of the four trigger kinds this is; it decides which of the fields below applies.</summary>
    public required WorkflowTriggerKind Kind { get; init; }

    /// <summary>Event — the subject-form/domain event type that fires this.</summary>
    public string? EventType { get; init; }

    /// <summary>Schedule — the RRULE the daemon expands.</summary>
    public string? Rrule { get; init; }

    /// <summary>HumanAction — the parked human-task key whose typed result fires this.</summary>
    public string? Task { get; init; }

    /// <summary>DependencyComplete — the prerequisite step / sub-process id.</summary>
    public string? Dependency { get; init; }
}

/// <summary>
/// A guarded capability-call fired on entering a state or on a transition (ADR 0140). Carries an
/// authoring-declared CP/AP <see cref="Classification"/> that drives the CP-park fence. Adds no new
/// authority primitive.
/// </summary>
public sealed class WorkflowActionBindingDef
{
    /// <summary>Stable id, unique among its kind within the definition; admission refuses duplicates.</summary>
    public required string Id { get; init; }

    /// <summary>The state id this action fires on entering. Exactly one of OnState / OnTransition is set.</summary>
    public string? OnState { get; init; }

    /// <summary>The transition id this action fires on. Exactly one of OnState / OnTransition is set.</summary>
    public string? OnTransition { get; init; }

    /// <summary>What the action does.</summary>
    public WorkflowActionKind Kind { get; init; }

    /// <summary>The guarded capability invoked (a Tool from the action palette).</summary>
    public required string CapabilityRef { get; init; }

    /// <summary>Authoring-declared authority class. Unspecified ⇒ refused at admission (fail-closed).</summary>
    public ActionClassification Classification { get; init; } = ActionClassification.Unspecified;

    /// <summary>Optional action-condition rule id ("should this run?").</summary>
    public string? Condition { get; init; }
}

/// <summary>A content-addressed reference to a FormDefinition (the composition binding, ADR 0140 D3).</summary>
public sealed class FormDefinitionRef
{
    /// <summary>The referenced form definition's id.</summary>
    public required string FormId { get; init; }
    /// <summary>The exact form definition version the workflow is pinned to.</summary>
    public required string Version { get; init; }
}

/// <summary>
/// The canonical declarative workflow keystone (the process analog of FormDefinition). The editable
/// template the builder authors; the ADR 0135 engine instantiates it into a Process.
/// </summary>
public sealed class WorkflowDefinition
{
    /// <summary>Definition key (matches <see cref="WorkflowInstanceRecord.DefinitionKey"/>).</summary>
    public required string Key { get; init; }

    /// <summary>Canonical "{major}.{minor}.{patch}" — the D7-pinned version.</summary>
    public required string Version { get; init; }

    /// <summary>The revision's lifecycle status; defaults to <see cref="WorkflowDefinitionStatus.Draft"/>.</summary>
    public WorkflowDefinitionStatus Status { get; init; } = WorkflowDefinitionStatus.Draft;

    /// <summary>Owning tenant id.</summary>
    public required string Tenant { get; init; }

    /// <summary>The FormDefinition this process operates on (ADR 0140 D3 composition binding).</summary>
    public FormDefinitionRef? SubjectFormRef { get; init; }

    /// <summary>The editability posture; defaults to <see cref="WorkflowMutability.Locked"/>.</summary>
    public WorkflowMutability Mutability { get; init; } = WorkflowMutability.Locked;

    /// <summary>The state new instances start in (must be a <see cref="States"/> id).</summary>
    public required string InitialState { get; init; }

    /// <summary>Every state; ids must be unique and include <see cref="InitialState"/>.</summary>
    public IReadOnlyList<WorkflowStateDef> States { get; init; } = [];

    /// <summary>Every guarded transition; each must reference declared states, a declared trigger and, if guarded, a listed guard rule.</summary>
    public IReadOnlyList<WorkflowTransitionDef> Transitions { get; init; } = [];

    /// <summary>Every trigger transitions may be offered on.</summary>
    public IReadOnlyList<WorkflowTriggerBindingDef> Triggers { get; init; } = [];

    /// <summary>Every action, each bound to exactly one state entry or one transition.</summary>
    public IReadOnlyList<WorkflowActionBindingDef> Actions { get; init; } = [];

    /// <summary>The guard rule ids referenced by transitions/actions exist in this set (rule ids only here).</summary>
    public IReadOnlyList<string> GuardRuleIds { get; init; } = [];
}
