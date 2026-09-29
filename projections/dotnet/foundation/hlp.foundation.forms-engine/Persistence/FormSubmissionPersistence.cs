using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <inheritdoc />
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

/// <inheritdoc />
public sealed record FormMutationAuditEnvelope(
    string AuditId,
    EntityId InstanceId,
    TenantId Tenant,
    string ActorId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset RecordedAt);

/// <inheritdoc />
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

/// <inheritdoc />
public sealed record FormSubmissionCommit(
    string IdempotencyKey,
    FormSubmissionRecord Submission,
    FormMutationAuditEnvelope Audit,
    FormProjectionEnvelope Projection,
    FormSubmitReceipt Receipt);

/// <inheritdoc />
public enum FormSubmissionCommitDisposition
{
    /// <summary>The submission created a new record.</summary>
    Created,
    /// <summary>The idempotency key replayed the prior result.</summary>
    Replayed,
    /// <summary>The idempotency key conflicts with a different request.</summary>
    Conflict
}

/// <inheritdoc />
public sealed record FormSubmissionCommitResult(
    FormSubmissionCommitDisposition Disposition,
    FormSubmitReceipt? Receipt);

/// <summary>One real submission-store exclusion scope. A commit publishes its complete envelope once.</summary>
public interface IFormSubmissionTransactionScope : IAsyncDisposable
{
    /// <inheritdoc />
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public interface IFormSubmissionTransactionStore
{
    /// <inheritdoc />
    ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default);
    /// <inheritdoc />
    ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default);
}
