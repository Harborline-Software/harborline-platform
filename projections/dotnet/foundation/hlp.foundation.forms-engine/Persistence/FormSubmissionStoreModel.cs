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
    }

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
        value with { Payload = value.Payload.ToArray() };

    internal static FormProjectionEnvelope Clone(FormProjectionEnvelope value) =>
        value with { ProtectedAcceptedValues = value.ProtectedAcceptedValues.ToArray() };
}
