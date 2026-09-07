using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Persistence;

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

public sealed record FormMutationAuditEnvelope(
    string AuditId,
    EntityId InstanceId,
    TenantId Tenant,
    string ActorId,
    ReadOnlyMemory<byte> Payload,
    DateTimeOffset RecordedAt);

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

public sealed record FormSubmissionCommit(
    string IdempotencyKey,
    FormSubmissionRecord Submission,
    FormMutationAuditEnvelope Audit,
    FormProjectionEnvelope Projection,
    FormSubmitReceipt Receipt);

public enum FormSubmissionCommitDisposition { Created, Replayed, Conflict }

public sealed record FormSubmissionCommitResult(
    FormSubmissionCommitDisposition Disposition,
    FormSubmitReceipt? Receipt);

public interface IFormSubmissionTransactionStore
{
    ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default);
    ValueTask<FormSubmissionRecord?> GetAsync(TenantId tenant, EntityId instanceId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(int maximum, CancellationToken cancellationToken = default);
    ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default);
    ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default);
    ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default);
}
