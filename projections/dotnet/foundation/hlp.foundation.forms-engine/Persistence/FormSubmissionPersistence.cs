using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Stores the protected, tenant-scoped record created by a form submission.</summary>
/// <param name="InstanceId">The form instance.</param>
/// <param name="Tenant">The submission tenant.</param>
/// <param name="PartyId">The submitter's server-derived Party.</param>
/// <param name="ActorId">The submitter.</param>
/// <param name="FormId">The submitted definition id.</param>
/// <param name="DefinitionVersion">The submitted definition version.</param>
/// <param name="RequestFingerprint">The canonical accepted-candidate fingerprint.</param>
/// <param name="ProtectedAcceptedCandidate">The protected accepted values.</param>
/// <param name="SubmittedAt">The engine submit instant.</param>
/// <param name="SchemaRef">The submitted revision's schema reference (D3 binding, kept for recovery); absent on rows
/// written before journal format 2.</param>
/// <param name="RecordTypeId">The submitted revision's record type target, or absent for a form without one.</param>
public sealed record FormSubmissionRecord(
    EntityId InstanceId,
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    FormDefinitionId FormId,
    SemanticVersion DefinitionVersion,
    string RequestFingerprint,
    ReadOnlyMemory<byte> ProtectedAcceptedCandidate,
    DateTimeOffset SubmittedAt,
    SchemaId? SchemaRef = null,
    string? RecordTypeId = null);

/// <summary>Carries the immutable audit payload associated with a committed mutation.</summary>
public sealed record FormMutationAuditEnvelope(
    string AuditId,
    EntityId InstanceId,
    TenantId Tenant,
    string ActorId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset RecordedAt)
{
    /// <summary>
    /// The authority snapshot of the decision this mutation was accepted under (DES-0004 <c>authority_snapshot</c>, ADR
    /// 0068), in the shared <see cref="Harborline.Foundation.Authorization.AuthoritySnapshotEncoding"/> grammar. Set on the
    /// acceptance of a Records command (ADR-0110 decision 1, revision 16) to the submit gate's own evidence, so it commits
    /// atomically with the submission, the command and its delivery row; empty for every other submission. It is staged as
    /// the kernel audit's <see cref="Harborline.Kernel.Core.KernelAuditEvidence.AuthoritySnapshot"/> and is never disclosed
    /// on a receipt. A cancellation's audit envelope (review M1) carries the cancel decision's snapshot the same way; a
    /// refusal's carries none.
    /// </summary>
    public ReadOnlyMemory<byte> AuthoritySnapshot { get; init; }
}

/// <summary>The lifecycle of a durably accepted Records command, held on its delivery row (ADR-0110 decisions 1 and 5).</summary>
public enum FormAcceptedCommandState
{
    /// <summary>Accepted and not yet claimed for execution; a cancel can still win the fence.</summary>
    Pending,
    /// <summary>Durably claimed for execution by a worker before any effect; a cancel is now too late. A claimed command
    /// whose execution was interrupted is executed again, idempotently, by recovery.</summary>
    Claimed,
    /// <summary>Executed: the Records create committed or replayed, or a quarantined command whose effect the host's receipt
    /// lookup found committed (review R3-L1, audited). Terminal.</summary>
    Completed,
    /// <summary>Refused by an execution-time binding or invariant check while its effect provably has not committed: before
    /// any claim, or by the Records execution on the very run that made the claim (no earlier run existed). Terminal and
    /// audited; never retried. A claimed command whose earlier run may have committed is quarantined instead (review
    /// R2-M1).</summary>
    Refused,
    /// <summary>Cancelled before any claim, or abandoned from <see cref="Quarantined"/> once the host established that its
    /// effect did not commit and cannot still commit: either way its effect has not committed and cannot commit. Terminal.</summary>
    Cancelled,
    /// <summary>
    /// Review R2-M1 (DES-0029 section 9, owner ruling 123): a claimed command that execution can neither complete nor refuse,
    /// because an earlier interrupted run may already have committed its Records effect: its stored binding stayed broken
    /// for <see cref="Harborline.Foundation.Forms.Engine.FormEngineOptions.AcceptedCommandBindingRetryLimit"/> binding
    /// failures, or the Records execution refused it after a resumed claim. <b>Not terminal</b>: it is audited, stays in
    /// the pending pool and visible, is never leased or executed again, and a cancel wins only after the host establishes
    /// that its effect did not commit and cannot still commit (<see cref="IFormAcceptedCommandEffects"/>). It leaves
    /// quarantine as completed only through <see cref="FormAcceptedCommandResolution"/> once the same lookup answers that the
    /// effect committed, audited (review R3-L1); <see cref="IFormSubmissionTransactionStore.CompleteProjectionAsync"/>
    /// refuses it.
    /// </summary>
    Quarantined,
}

/// <summary>
/// One durably accepted Records create (ADR-0110 decision 1, plan W6): the exact command a <c>form_only</c> target's
/// submission carries on its delivery row, committed in the same atomic submit as the submission, its audit with the
/// acceptance authority snapshot, and the row. Its tenant, submitter, Party, form id and version, schema reference and case
/// reference are the row's own members, which the store binds to the submission row; this record adds the rest, and only
/// its lifecycle members change after acceptance.
/// </summary>
/// <param name="CommandId">The command's durable id; equal to the delivery row's outbox id.</param>
/// <param name="OperationId">The original operation identity, server-derived at acceptance
/// (<see cref="FormAcceptedCommandReadings.OperationIdFor"/>), never from the client.</param>
/// <param name="RecordTypeId">The Record Type the command creates; equal to the form revision's target.</param>
/// <param name="PayloadFingerprint">The canonical fingerprint of the accepted payload; equal to the submission's.</param>
/// <param name="AcceptedAt">The acceptance instant; equal to the submission instant.</param>
/// <param name="AcceptanceSnapshot">The submit gate's own authority snapshot; equal to the audit's.</param>
public sealed record FormAcceptedRecordCommand(
    string CommandId,
    string OperationId,
    string RecordTypeId,
    string PayloadFingerprint,
    DateTimeOffset AcceptedAt,
    ReadOnlyMemory<byte> AcceptanceSnapshot)
{
    /// <summary>The command's lifecycle state; <see cref="FormAcceptedCommandState.Pending"/> at acceptance.</summary>
    public FormAcceptedCommandState State { get; init; } = FormAcceptedCommandState.Pending;

    /// <summary>The principal of the last transition: the claiming or refusing worker, or the canceller; null while pending.</summary>
    public string? StateActorId { get; init; }

    /// <summary>The instant of the last transition; null while pending.</summary>
    public DateTimeOffset? StateChangedAt { get; init; }

    /// <summary>The terminal refusal code of a refused command, or the code a command was quarantined with (kept if it is
    /// later abandoned or completed); otherwise null.</summary>
    public string? RefusalCode { get; init; }

    /// <summary>
    /// Review R2-L1: how many executions of this claimed command found its stored binding broken. Only binding failures
    /// count toward <see cref="Harborline.Foundation.Forms.Engine.FormEngineOptions.AcceptedCommandBindingRetryLimit"/>;
    /// infrastructure failures, a transient read failure (including a definition that is not found) and a worker equal to
    /// the submitter do not. Zero at acceptance; changed only by the store.
    /// </summary>
    public int BindingFailures { get; init; }

    /// <summary>The cancel decision's own authority snapshot, committed with the cancellation; otherwise empty.</summary>
    public ReadOnlyMemory<byte> StateSnapshot { get; init; }

    /// <summary>
    /// The acceptance audit's id (review M3, L7): the audit committed with the command, which holds the same acceptance
    /// snapshot. Required at acceptance; the Records completion carries it as the command's acceptance identity.
    /// </summary>
    public string? AcceptanceAuditId { get; init; }

    /// <summary>
    /// The instant the acceptance decision was made, exactly the submit-gate evidence's instant (review M3). The acceptance
    /// snapshot's <c>at:</c> fact must equal it and it is at or before <see cref="AcceptedAt"/>. Required at acceptance.
    /// </summary>
    public DateTimeOffset? AcceptanceDecidedAt { get; init; }

    /// <summary>
    /// The encoding of <see cref="BodyDigest"/> as the host reported it (review M4, plan W6), <c>records-write-intent/1</c>
    /// for the current Records operation. Required at acceptance.
    /// </summary>
    public string? BodyDigestEncoding { get; init; }

    /// <summary>
    /// The digest of the Records body the accepted values map to, computed at acceptance by the host's
    /// <see cref="IFormAcceptedRecordBodies"/> with the Records operation's own intent encoder (review M4, plan W6). The
    /// Records completion recomputes it from the body it is given and refuses a mismatch before materializing. Required at
    /// acceptance.
    /// </summary>
    public string? BodyDigest { get; init; }
}

/// <summary>Which pending rows a lease may take (review M2, I-1).</summary>
[Flags]
public enum FormProjectionLeaseKinds
{
    /// <summary>Rows of forms without a record type target: ordinary projections that create no Records.</summary>
    Untargeted = 1,
    /// <summary>Rows that carry a durably accepted Records command.</summary>
    AcceptedCommands = 2,
    /// <summary>Rows of a targeted form that carry no accepted command (a <c>role</c> target, or a row written before
    /// revision 16). Recovery never takes them: delivering one outside its submitter's request would act as the submitter.</summary>
    TargetedWithoutCommand = 4,
    /// <summary>Every pending row.</summary>
    All = Untargeted | AcceptedCommands | TargetedWithoutCommand,
}

/// <summary>Carries the protected values and routing data awaiting projection delivery.</summary>
/// <param name="OutboxId">The outbox row id.</param>
/// <param name="InstanceId">The form instance.</param>
/// <param name="Tenant">The submission tenant.</param>
/// <param name="PartyId">The submitter's Party.</param>
/// <param name="ActorId">The submitter.</param>
/// <param name="FormId">The submitted definition id.</param>
/// <param name="DefinitionVersion">The submitted definition version.</param>
/// <param name="CaseReference">The caller-supplied case reference, bound by the submit-gate authorization when one is attached.</param>
/// <param name="ProtectedAcceptedValues">The protected accepted values.</param>
/// <param name="SubmittedAt">The engine submit instant.</param>
/// <param name="Attempts">Delivery attempts so far.</param>
/// <param name="LastErrorCode">The last stable delivery error.</param>
/// <param name="SchemaRef">The submitted revision's schema reference, equal to the submission row's.</param>
/// <param name="RecordTypeId">The submitted revision's record type target, equal to the submission row's.</param>
public sealed record FormProjectionEnvelope(
    string OutboxId,
    EntityId InstanceId,
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    FormDefinitionId FormId,
    SemanticVersion DefinitionVersion,
    string? CaseReference,
    ReadOnlyMemory<byte> ProtectedAcceptedValues,
    DateTimeOffset SubmittedAt,
    int Attempts = 0,
    string? LastErrorCode = null,
    SchemaId? SchemaRef = null,
    string? RecordTypeId = null)
{
    /// <summary>
    /// The durably accepted Records command this row delivers (ADR-0110, plan W6), or null for an ordinary projection. A
    /// row with a command is never handed to <see cref="Projection.IFormProjectionSink.DeliverAsync"/>: it is executed only
    /// by the configured worker under the store's claim, through <see cref="Projection.IFormProjectionSink.DeliverAcceptedAsync"/>.
    /// </summary>
    public FormAcceptedRecordCommand? AcceptedCommand { get; init; }
}

/// <summary>Groups the submission, audit, projection, and receipt committed as one unit.</summary>
public sealed record FormSubmissionCommit(
    string IdempotencyKey,
    FormSubmissionRecord Submission,
    FormMutationAuditEnvelope Audit,
    FormProjectionEnvelope Projection,
    FormSubmitReceipt Receipt);

/// <summary>Describes whether a submission commit was new, replayed, or conflicting.</summary>
public enum FormSubmissionCommitDisposition
{
    /// <summary>The submission created a new record.</summary>
    Created,
    /// <summary>The idempotency key replayed the prior result.</summary>
    Replayed,
    /// <summary>The idempotency key conflicts with a different request.</summary>
    Conflict
}

/// <summary>Returns the commit disposition and receipt when one exists.</summary>
/// <param name="Disposition">New, replayed or conflicting.</param>
/// <param name="Receipt">The receipt of the new or replayed submission.</param>
/// <param name="Stored">On a replay, a copy of the stored commit the idempotency key is bound to, so the engine can
/// bind a replay's authorization to the write that actually exists (D3).</param>
public sealed record FormSubmissionCommitResult(
    FormSubmissionCommitDisposition Disposition,
    FormSubmitReceipt? Receipt,
    FormSubmissionCommit? Stored = null);

/// <summary>One real submission-store exclusion scope. A commit publishes its complete envelope once.</summary>
public interface IFormSubmissionTransactionScope : IAsyncDisposable
{
    /// <summary>Atomically publishes the submission, audit, projection and receipt, reporting new, replayed or conflicting.</summary>
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <summary>Discards the scope's uncommitted work.</summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>The outcome of one accepted-command transition: the command as it now stands, and whether this call changed it.</summary>
/// <param name="Command">The command after the call, with its current state.</param>
/// <param name="Changed">True when this call made the transition; false when the command was already in a state the
/// transition does not apply to (an idempotent retry, or a command another transition already settled).</param>
public sealed record FormAcceptedCommandTransition(FormAcceptedRecordCommand Command, bool Changed);

/// <summary>
/// The accepted-command lifecycle of a submission store (ADR-0110 decisions 1 and 5, plan W6/W7): the one durable fence
/// on which execution and cancellation contend. Each transition is atomic in the store's own exclusion scope and durable
/// before it returns. A store that implements this port persists <see cref="FormProjectionEnvelope.AcceptedCommand"/> and
/// <see cref="FormMutationAuditEnvelope.AuthoritySnapshot"/> in the same commit as the submission; the engine refuses to
/// accept a command into a store that does not.
/// </summary>
/// <remarks>
/// <para><b>The fence (open item 1, reading implemented):</b> a durable claim before any effect. A worker claims a pending
/// command (<see cref="ClaimAcceptedCommandAsync"/>) before it hands it to the Records create; a cancel transitions only a
/// pending command. Both are compare-and-set on the one row, so exactly one of them wins, and once a command is claimed a
/// cancel is answered <c>records.command.cancel_too_late</c>, because the effect may already have committed.</para>
/// <para><b>Never terminal while the effect may have committed (DES-0029 section 9, owner ruling 123; review R2-M1):</b> a
/// claimed command is refused only on the run that made its claim; one resumed from an earlier claim that cannot be completed
/// or refused safely is quarantined, audited and non-terminal, and leaves quarantine only through
/// <see cref="AbandonQuarantinedCommandAsync"/> (its effect established absent) or <see cref="ResolveQuarantinedCommandAsync"/>
/// (its effect established committed), each audited.</para>
/// <para><b>Open item 10 (reading implemented):</b> a cancel that fails on infrastructure leaves the command pending and
/// does not hold execution back; execution may win the fence meanwhile, and the cancel's retry is then too late.</para>
/// </remarks>
public interface IFormAcceptedCommandStore
{
    /// <summary>
    /// Leases up to <paramref name="maximum"/> undelivered rows of the given <paramref name="kinds"/> exactly as
    /// <see cref="IFormSubmissionTransactionStore.LeasePendingAsync"/> leases every kind. Both built-in stores lease least
    /// recently enqueued first (review M2 and R2-L4): a row enters the queue at its commit and re-enters at the back after
    /// every failed attempt, so a row that keeps failing yields its place to every row waiting at its failure (stalled rows
    /// never starve later ones) and a waiting row only moves forward (under any inflow no row waits forever). A
    /// <see cref="FormAcceptedCommandState.Quarantined"/> command is never leased. A lease is exclusive and does not expire
    /// while its holder runs; the execution path relies on that (a claim made by the run that holds the lease is the only
    /// execution of the command so far), so a W10 store with expiring leases must fence a stale holder the same way.
    /// </summary>
    ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, FormProjectionLeaseKinds kinds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Review R2-L2 (D4, K2): leases the tenant's one undelivered row <paramref name="outboxId"/> that carries no accepted
    /// command, for a redelivery inside its own submitter's request. Null when the row is absent, another tenant's, already
    /// delivered, leased by another delivery, or carries an accepted command (which only the configured worker executes).
    /// </summary>
    ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default);

    /// <summary>Returns the tenant's accepted command, in any state, or null when it is absent or another tenant's.</summary>
    ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a pending command for execution by <paramref name="workerId"/> at <paramref name="at"/>, durably, before any
    /// effect. A command already claimed is returned unchanged (an interrupted execution is executed again); a completed,
    /// refused or cancelled command is returned unchanged and must not be executed. Null when no such command exists.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the tenant's pending command, durably, and commits <paramref name="audit"/> with it as one more audit envelope
    /// of the submission (review M1: the existing audit contract, atomically with the state change). The canceller is the
    /// audit's actor, the instant its recorded instant, and its authority snapshot (required) the cancel decision's, which the
    /// command also keeps. The delivery row leaves the pending pool and the stored receipt reports the cancellation. A command
    /// already cancelled is returned unchanged and no audit is added (an idempotent retry); a claimed, completed or refused
    /// command is returned unchanged and is too late to cancel. Null when the command is absent or another tenant's. An audit
    /// that fails validation, names an originator or reuses an audit id is refused before anything changes.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refuses the command terminally with <paramref name="refusalCode"/>, durably, only while it is still in
    /// <paramref name="expectedState"/> (<see cref="FormAcceptedCommandState.Pending"/> or
    /// <see cref="FormAcceptedCommandState.Claimed"/>; review L2), and commits <paramref name="audit"/> with it as in
    /// <see cref="CancelAcceptedCommandAsync"/> (review M1); the refusing worker is the audit's actor. The row leaves the
    /// pending pool and is never retried. A command in any other state is returned unchanged with no audit. Null when no such
    /// command exists.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState, string refusalCode,
        KernelAuditEvidence audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Review R2-L1: records one execution of a <see cref="FormAcceptedCommandState.Claimed"/> command that found its stored
    /// binding broken, durably and atomically: the command's <see cref="FormAcceptedRecordCommand.BindingFailures"/> grows by
    /// one, the row records a failed attempt with <paramref name="code"/> and its lease is released. A command in any other
    /// state is returned unchanged. Null when no such command exists.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Review R2-M1 (DES-0029 section 9, owner ruling 123): moves a <see cref="FormAcceptedCommandState.Claimed"/> command to
    /// <see cref="FormAcceptedCommandState.Quarantined"/> with <paramref name="code"/>, durably, and commits
    /// <paramref name="audit"/> with it as in <see cref="RefuseAcceptedCommandAsync"/> (the quarantining worker is the
    /// audit's actor). Not terminal: the row stays in the pending pool, visible, its receipt stays pending, and it is never
    /// leased again. A command in any other state is returned unchanged with no audit. Null when no such command exists.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Review R2-M1: cancels the tenant's <see cref="FormAcceptedCommandState.Quarantined"/> command, durably, with
    /// <paramref name="audit"/> as in <see cref="CancelAcceptedCommandAsync"/>. The caller must first have established the
    /// abandonment condition of DES-0029 section 9 (its effect did not commit and cannot still commit; see
    /// <see cref="IFormAcceptedCommandEffects"/>); the store cannot and does not judge it. A command already cancelled is
    /// returned unchanged with no audit; one in any other state is returned unchanged. Null when the command is absent or
    /// another tenant's.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Review R3-L1: completes the tenant's <see cref="FormAcceptedCommandState.Quarantined"/> command, durably, with
    /// <paramref name="audit"/> committed in the same transition as in <see cref="QuarantineAcceptedCommandAsync"/>. The
    /// caller must first have established that its Records effect committed (<see cref="IFormAcceptedCommandEffects"/>
    /// answering <see cref="FormAcceptedCommandEffect.Committed"/>); the store cannot and does not judge it. The row leaves
    /// the pending pool and the stored receipt reports it complete. A command in any other state is returned unchanged with
    /// no audit. Null when the command is absent or another tenant's.
    /// </summary>
    ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default);
}

/// <summary>Persists submissions and manages leases for pending projections.</summary>
public interface IFormSubmissionTransactionStore
{
    /// <summary>Opens an exclusive transaction scope for one submission commit.</summary>
    ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
    /// <summary>Atomically publishes the submission, audit, projection and receipt, reporting new, replayed or conflicting.</summary>
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <summary>Returns the tenant's stored submission for the instance, or null when absent.</summary>
    ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default);
    /// <summary>Leases up to the given number of undelivered projections so no other worker takes them.</summary>
    ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default);
    /// <summary>Returns a leased projection to the pending pool without recording an attempt.</summary>
    ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default);
    /// <summary>Marks the projection delivered, recording its skips on the replayable receipt. A row that carries an accepted
    /// command completes only while <see cref="FormAcceptedCommandState.Claimed"/>, and its command becomes
    /// <see cref="FormAcceptedCommandState.Completed"/>; any other state, including
    /// <see cref="FormAcceptedCommandState.Quarantined"/> (review R3-L1), is refused.</summary>
    ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default);
    /// <summary>Releases the lease and records a failed attempt with its stable error code.</summary>
    ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default);
}
