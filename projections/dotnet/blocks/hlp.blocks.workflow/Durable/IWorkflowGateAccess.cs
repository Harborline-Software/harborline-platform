using Harborline.Contracts.Authorization;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>
/// The host's answer to one workflow role gate (T-1176, DES-0019 workflows-eng-13 and workflows-auth-10). The engine
/// owns no decider (see <see cref="WorkflowDispatchAuthority"/>): it asks, the host decides with its one authorization
/// gate, and the engine refuses any answer that does not bind the exact intent it asked. The same shape Forms uses for
/// its submit gate (<c>IFormSubmitGateAccess</c>) and Layout for its surface (<c>ILayoutSubmitAccess</c>).
/// </summary>
/// <remarks>
/// DES-0032's four steps are the host's: (1) the act kind, carried as <see cref="WorkflowGateIntent.Element"/>;
/// (2) the actor's effective roles for this record at this moment, from grants in force and in scope;
/// (3) the actor's standings for this record; (4) whether the gate's named role intersects the union. Only after a
/// satisfied answer does the engine run the guard-bound step and then its effects (DES-0019 section 6, L422).
/// </remarks>
public interface IWorkflowGateAccess
{
    /// <summary>Decides one role gate for one intent. The answer's <see cref="WorkflowGateAnswer.Intent"/> must bind it.</summary>
    ValueTask<WorkflowGateAnswer> DecideAsync(WorkflowGateIntent intent, CancellationToken ct = default);
}

/// <summary>Which kind of workflow element a gate guards (DES-0032 step one, the kind of act).</summary>
public enum WorkflowGateElement
{
    /// <summary>A transition a person performs.</summary>
    Transition = 1,

    /// <summary>An effecting action a person's transition fires.</summary>
    Action = 2,
}

/// <summary>
/// One workflow gate question, built by the engine from the pinned definition and the dispatch's verified authority,
/// never from the trigger payload.
/// </summary>
/// <param name="Tenant">The instance's tenant.</param>
/// <param name="Principal">The acting principal: the verified dispatch authority's principal.</param>
/// <param name="InstanceId">The workflow instance.</param>
/// <param name="DefinitionKey">The pinned definition's key.</param>
/// <param name="DefinitionVersion">The pinned definition's version.</param>
/// <param name="Element">Whether the gate guards a transition or an action.</param>
/// <param name="ElementId">The gated element's id.</param>
/// <param name="DecidedAt">The act's decision instant, read once from the host kernel clock.</param>
/// <param name="RequiredRoles">The declared roles, any of which admits (workflows-auth-10).</param>
public sealed record WorkflowGateIntent(
    string Tenant,
    string Principal,
    string InstanceId,
    string DefinitionKey,
    string DefinitionVersion,
    WorkflowGateElement Element,
    string ElementId,
    IReadOnlyList<RoleReference> RequiredRoles,
    DateTimeOffset DecidedAt)
{
    /// <summary>Whether <paramref name="other"/> names exactly this question, roles compared in order.</summary>
    public bool Binds(WorkflowGateIntent? other) =>
        other is not null
        && Tenant == other.Tenant
        && Principal == other.Principal
        && InstanceId == other.InstanceId
        && DefinitionKey == other.DefinitionKey
        && DefinitionVersion == other.DefinitionVersion
        && Element == other.Element
        && ElementId == other.ElementId
        && DecidedAt == other.DecidedAt
        && RequiredRoles.SequenceEqual(other.RequiredRoles);
}

/// <summary>The host's answer: the intent it decided, whether the actor satisfies the gate, and its own evidence.</summary>
/// <param name="Intent">The intent this answer decides; it must bind the asked intent.</param>
/// <param name="Satisfied">Whether the actor's effective roles intersect the gate.</param>
/// <param name="Evidence">The host decider's evidence, unchanged, or <see langword="null"/>.</param>
public sealed record WorkflowGateAnswer(WorkflowGateIntent Intent, bool Satisfied, object? Evidence = null);

/// <summary>Stable codes a refused workflow gate carries.</summary>
public static class WorkflowGateCodes
{
    /// <summary>The host's gate says the actor does not satisfy the declared roles.</summary>
    public const string Denied = "workflow.gate.denied";

    /// <summary>The host returned an answer for a different question than the one asked.</summary>
    public const string AnswerUnbound = "workflow.gate.answer_unbound";
}

/// <summary>
/// Thrown when a workflow gate refuses a step. Nothing is selected, built or recorded. Carries a <see cref="WorkflowGateCodes"/> code.
/// </summary>
public sealed class WorkflowGateRefusedException(string code, string message) : InvalidOperationException(message)
{
    /// <summary>The stable refusal code (a localizing client keys off this).</summary>
    public string Code { get; } = code;
}
