using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine.Persistence;

internal static class FormSubmissionStoreModel
{
    internal static void ValidateAtomicEnvelope(FormSubmissionCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (string.IsNullOrWhiteSpace(commit.IdempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(commit));
        if (commit.Submission.InstanceId != commit.Audit.InstanceId || commit.Submission.InstanceId != commit.Projection.InstanceId)
            throw new ArgumentException("Submission, audit, and projection must name one instance.", nameof(commit));
        if (commit.Submission.Tenant != commit.Audit.Tenant || commit.Submission.Tenant != commit.Projection.Tenant)
            throw new ArgumentException("Submission, audit, and projection must name one tenant.", nameof(commit));
        if (commit.Submission.PartyId != commit.Projection.PartyId
            || !string.Equals(commit.Submission.ActorId, commit.Projection.ActorId, StringComparison.Ordinal))
            throw new ArgumentException("Submission and projection must name one Party and actor.", nameof(commit));
        if (commit.Submission.FormId != commit.Projection.FormId || commit.Submission.DefinitionVersion != commit.Projection.DefinitionVersion)
            throw new ArgumentException("Submission and projection must name one form definition version.", nameof(commit));
        if (commit.Submission.SchemaRef != commit.Projection.SchemaRef
            || !string.Equals(commit.Submission.RecordTypeId, commit.Projection.RecordTypeId, StringComparison.Ordinal))
            throw new ArgumentException("Submission and projection must name one schema reference and record type target.", nameof(commit));
        if (commit.Submission.SubmittedAt != commit.Audit.RecordedAt
            || commit.Submission.SubmittedAt != commit.Projection.SubmittedAt
            || commit.Submission.SubmittedAt != commit.Receipt.SubmittedAt)
            throw new ArgumentException("Submission, audit, projection, and receipt must share one instant.", nameof(commit));
        if (commit.Submission.InstanceId != commit.Receipt.InstanceId)
            throw new ArgumentException("Receipt must name the committed instance.", nameof(commit));
        if (commit.Projection.Attempts != 0 || commit.Projection.LastErrorCode is not null)
            throw new ArgumentException("A new projection outbox row must begin without attempts or an error.", nameof(commit));
        if (commit.Receipt.ProjectionStatus != FormProjectionStatus.Pending || commit.Receipt.ProjectionSkips.Count != 0)
            throw new ArgumentException("A new submission receipt must begin pending without projection skips.", nameof(commit));
        if (commit.Projection.AcceptedCommand is { } command) ValidateAcceptedCommand(commit, command);
    }

    // ADR-0110 decision 1 (plan W6): the accepted command, its evidence and its delivery row are one atomic acceptance, so
    // every member the command shares with the submission must be the submission's, and the acceptance snapshot must be
    // the audit's, before anything is stored.
    private static void ValidateAcceptedCommand(FormSubmissionCommit commit, FormAcceptedRecordCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId) || string.IsNullOrWhiteSpace(command.OperationId)
            || string.IsNullOrWhiteSpace(command.RecordTypeId) || string.IsNullOrWhiteSpace(command.PayloadFingerprint))
            throw new ArgumentException("An accepted command needs its id, operation id, record type and payload fingerprint.", nameof(commit));
        if (!string.Equals(command.CommandId, commit.Projection.OutboxId, StringComparison.Ordinal))
            throw new ArgumentException("An accepted command's id must be its delivery row's outbox id.", nameof(commit));
        if (!string.Equals(command.RecordTypeId, commit.Submission.RecordTypeId, StringComparison.Ordinal)
            || commit.Submission.SchemaRef is null)
            throw new ArgumentException("An accepted command must create the submission's targeted record type under its schema reference.", nameof(commit));
        if (!string.Equals(command.PayloadFingerprint, commit.Submission.RequestFingerprint, StringComparison.Ordinal)
            || command.AcceptedAt != commit.Submission.SubmittedAt)
            throw new ArgumentException("An accepted command must bind the submission's payload fingerprint and instant.", nameof(commit));
        if (command.AcceptanceSnapshot.IsEmpty
            || !command.AcceptanceSnapshot.Span.SequenceEqual(commit.Audit.AuthoritySnapshot.Span))
            throw new ArgumentException("An accepted command must carry the audit's acceptance authority snapshot.", nameof(commit));
        if (command.State != FormAcceptedCommandState.Pending || command.StateActorId is not null || command.StateChangedAt is not null
            || command.RefusalCode is not null || !command.StateSnapshot.IsEmpty || command.BindingFailures != 0)
            throw new ArgumentException("A new accepted command must begin pending with no transition.", nameof(commit));
        // Review M3 and M4: the acceptance identity and the mapped-body digest are bound at acceptance.
        if (!string.Equals(command.AcceptanceAuditId, commit.Audit.AuditId, StringComparison.Ordinal)
            || command.AcceptanceDecidedAt is not { } decidedAt || decidedAt > command.AcceptedAt)
            throw new ArgumentException("An accepted command must name its acceptance audit and a decision instant at or before acceptance.", nameof(commit));
        if (string.IsNullOrWhiteSpace(command.BodyDigestEncoding) || string.IsNullOrWhiteSpace(command.BodyDigest))
            throw new ArgumentException("An accepted command must bind the digest of the Records body it creates.", nameof(commit));
    }

    // Review M1 and R2-M1: a terminal or quarantining lifecycle transition is audited through the existing audit contract.
    // The kernel audit evidence is validated and becomes one more audit envelope of the submission, stored with the
    // transition or not at all.
    internal static FormMutationAuditEnvelope LifecycleAudit(KernelAuditEvidence audit, EntityId instance, TenantId tenant, bool snapshotRequired)
    {
        ArgumentNullException.ThrowIfNull(audit);
        audit.Validate();
        if (audit.OriginatorId is not null)
            throw new ArgumentException("A Forms lifecycle audit names its actor only.", nameof(audit));
        if (snapshotRequired && audit.AuthoritySnapshot.IsEmpty)
            throw new ArgumentException("A cancellation audit carries the cancel decision's authority snapshot.", nameof(audit));
        return new FormMutationAuditEnvelope(audit.AuditId, instance, tenant, audit.ActorId, audit.Payload.ToArray(), audit.RecordedAt)
        {
            AuthoritySnapshot = audit.AuthoritySnapshot.ToArray(),
        };
    }

    // Review M2 and I-1: which pending rows a lease of the given kinds may take. Review R2-M1: a quarantined command is
    // never leased; it stays in the pending pool, visible, until it is resolved.
    internal static bool LeaseAdmits(FormProjectionEnvelope row, FormProjectionLeaseKinds kinds) =>
        row.AcceptedCommand is { } command
            ? command.State != FormAcceptedCommandState.Quarantined && kinds.HasFlag(FormProjectionLeaseKinds.AcceptedCommands)
        : row.RecordTypeId is null ? kinds.HasFlag(FormProjectionLeaseKinds.Untargeted)
        : kinds.HasFlag(FormProjectionLeaseKinds.TargetedWithoutCommand);

    // Review M2 and R2-L4 (reading, accepted 2026-10-12): the one lease order of both built-in stores is the row's queue
    // position, least recently enqueued first. A row enters the queue at its commit and re-enters at the back after every
    // failed attempt (a retry or a binding failure); releasing a lease without an attempt keeps its place. A row that keeps
    // failing therefore yields to every row waiting at its failure, so stalled rows never starve later ones, and a waiting
    // row only moves forward, because every row enqueued after it is placed behind it, so under any inflow no row waits
    // longer than one rotation of the rows ahead of it. Positions come from a store-local sequence (the journal rebuilds it
    // from its frame order on replay), so no clock is read and nothing beyond the frames is persisted.
    internal static FormProjectionEnvelope[] LeaseOrder(IEnumerable<FormProjectionEnvelope> candidates,
        IReadOnlyDictionary<string, long> queuePositions, int maximum) =>
        candidates
            .OrderBy(row => queuePositions.TryGetValue(row.OutboxId, out var position) ? position : long.MaxValue)
            .ThenBy(row => row.OutboxId, StringComparer.Ordinal)
            .Take(maximum)
            .ToArray();

    internal static FormSubmissionCommit Clone(FormSubmissionCommit value) => value with
    {
        Submission = Clone(value.Submission),
        Audit = Clone(value.Audit),
        Projection = Clone(value.Projection),
        Receipt = value.Receipt with { ProjectionSkips = value.Receipt.ProjectionSkips.ToArray() },
    };

    internal static FormSubmissionRecord Clone(FormSubmissionRecord value) =>
        value with { ProtectedAcceptedCandidate = value.ProtectedAcceptedCandidate.ToArray() };

    internal static FormMutationAuditEnvelope Clone(FormMutationAuditEnvelope value) =>
        value with { Payload = value.Payload.ToArray(), AuthoritySnapshot = value.AuthoritySnapshot.ToArray() };

    internal static FormProjectionEnvelope Clone(FormProjectionEnvelope value) =>
        value with
        {
            ProtectedAcceptedValues = value.ProtectedAcceptedValues.ToArray(),
            AcceptedCommand = value.AcceptedCommand is { } command ? Clone(command) : null,
        };

    internal static FormAcceptedRecordCommand Clone(FormAcceptedRecordCommand value) =>
        value with { AcceptanceSnapshot = value.AcceptanceSnapshot.ToArray(), StateSnapshot = value.StateSnapshot.ToArray() };

    // The receipt status a settled accepted command reports on its stored receipt.
    internal static FormProjectionStatus ReceiptStatus(FormAcceptedCommandState state) => state switch
    {
        FormAcceptedCommandState.Refused => FormProjectionStatus.Refused,
        FormAcceptedCommandState.Cancelled => FormProjectionStatus.Cancelled,
        FormAcceptedCommandState.Completed => FormProjectionStatus.Complete,
        _ => FormProjectionStatus.Pending,
    };
}
