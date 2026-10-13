using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

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
    DateTimeOffset RecordedAt);

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
    string? RecordTypeId = null);

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
    /// <summary>Marks the projection delivered, recording its skips on the replayable receipt.</summary>
    ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default);
    /// <summary>Releases the lease and records a failed attempt with its stable error code.</summary>
    ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default);
}
