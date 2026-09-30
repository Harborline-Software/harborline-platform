using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Stores the protected, tenant-scoped record created by a form submission.</summary>
public sealed record FormSubmissionRecord(
    EntityId InstanceId,
    TenantId Tenant,
    Guid PartyId,
    string ActorId,
    FormDefinitionId FormId,
    SemanticVersion DefinitionVersion,
    string RequestFingerprint,
    ReadOnlyMemory<byte> ProtectedAcceptedCandidate,
    DateTimeOffset SubmittedAt);

/// <summary>Carries the immutable audit payload associated with a committed mutation.</summary>
public sealed record FormMutationAuditEnvelope(
    string AuditId,
    EntityId InstanceId,
    TenantId Tenant,
    string ActorId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset RecordedAt);

/// <summary>Carries the protected values and routing data awaiting projection delivery.</summary>
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
    string? LastErrorCode = null);

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
public sealed record FormSubmissionCommitResult(
    FormSubmissionCommitDisposition Disposition,
    FormSubmitReceipt? Receipt);

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
