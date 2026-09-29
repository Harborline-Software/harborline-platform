namespace Harborline.Blocks.Workflow.Durable;

/// <summary>
/// Routes a <see cref="WorkflowTrigger"/> (one of the four ADR 0135 D1 triggers) to the
/// <see cref="IWorkflowStepHandler"/> bound for the target instance's definition, and commits the
/// handler's outcome durably through the idempotency-guarded atomic advance.
/// </summary>
public interface IWorkflowTriggerDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="trigger"/>: loads the instance, replays if the step already advanced
    /// (idempotency hit → no-op), else asks the handler for an outcome and commits it atomically.
    /// </summary>
    /// <param name="trigger">The trigger to dispatch.</param>
    /// <param name="authority">
    /// The authorization decision the host made for this act. It must be allowed and target
    /// <c>records:write</c> on the instance (<c>record</c>/instance id) in the instance's tenant, or the
    /// dispatch is refused before a typed handler or the interpreter runs (T-525 item 1).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The dispatch result — what happened (advanced / parked / replayed / unknown-instance).</returns>
    Task<WorkflowDispatchResult> DispatchAsync(
        WorkflowTrigger trigger, WorkflowDispatchAuthority authority, CancellationToken ct = default);
}

/// <summary>
/// The authorization decision a host's gate made for one workflow act, carried into dispatch and verified
/// (never re-decided) by the dispatcher. The engine owns no decider, so the host maps its gate's decision
/// into this shape; <see cref="RequiredOperation"/> on <see cref="RequiredRecordKind"/>/instance id is the
/// act every dispatch verifies, whichever branch (typed handler or interpreter) then runs.
/// </summary>
public sealed record WorkflowDispatchAuthority(
    string Principal,
    string Tenant,
    string Operation,
    string RecordKind,
    string RecordId,
    bool Allowed)
{
    /// <summary>The operation a dispatch authority must carry.</summary>
    public const string RequiredOperation = "records:write";

    /// <summary>The record kind a dispatch authority must target (its id is the instance id).</summary>
    public const string RequiredRecordKind = "record";
}

/// <summary>The outcome of a <see cref="IWorkflowTriggerDispatcher.DispatchAsync"/> call.</summary>
public enum WorkflowDispatchResult
{
    /// <summary>The instance advanced (an effect may have committed).</summary>
    Advanced = 0,

    /// <summary>The instance parked on a delegated step and awaits its performer.</summary>
    Parked = 1,

    /// <summary>The step had already advanced — the recorded result was replayed; nothing new committed.</summary>
    ReplayedNoOp = 2,

    /// <summary>No instance with that id exists.</summary>
    UnknownInstance = 3,

    /// <summary>The instance is in a terminal state (Completed / Failed) — the trigger was ignored.</summary>
    Terminal = 4,
}
