using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Represents the form submission record contract used by this package.</summary>
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

/// <summary>Represents the form mutation audit envelope contract used by this package.</summary>
public sealed record FormMutationAuditEnvelope(
    string AuditId,
    EntityId InstanceId,
    TenantId Tenant,
    string ActorId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset RecordedAt);

/// <summary>Represents the form projection envelope contract used by this package.</summary>
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

/// <summary>Represents the form submission commit contract used by this package.</summary>
public sealed record FormSubmissionCommit(
    string IdempotencyKey,
    FormSubmissionRecord Submission,
    FormMutationAuditEnvelope Audit,
    FormProjectionEnvelope Projection,
    FormSubmitReceipt Receipt);

/// <summary>Defines the supported form submission commit disposition values.</summary>
public enum FormSubmissionCommitDisposition
{
    /// <summary>The created option.</summary>
    Created,
    /// <summary>The replayed option.</summary>
    Replayed,
    /// <summary>The conflict option.</summary>
    Conflict
}

/// <summary>Represents the form submission commit result contract used by this package.</summary>
public sealed record FormSubmissionCommitResult(
    FormSubmissionCommitDisposition Disposition,
    FormSubmitReceipt? Receipt);

/// <summary>One real submission-store exclusion scope. A commit publishes its complete envelope once.</summary>
public interface IFormSubmissionTransactionScope : IAsyncDisposable
{
    /// <summary>Executes the commit async contract.</summary>
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <summary>Executes the rollback async contract.</summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents the iform submission transaction store contract used by this package.</summary>
public interface IFormSubmissionTransactionStore
{
    /// <summary>Executes the begin transaction async contract.</summary>
    ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
    /// <summary>Executes the commit async contract.</summary>
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    /// <summary>Executes the get async contract.</summary>
    ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default);
    /// <summary>Executes the lease pending async contract.</summary>
    ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default);
    /// <summary>Executes the release projection lease async contract.</summary>
    ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default);
    /// <summary>Executes the complete projection async contract.</summary>
    ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default);
    /// <summary>Executes the retry projection async contract.</summary>
    ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default);
}
