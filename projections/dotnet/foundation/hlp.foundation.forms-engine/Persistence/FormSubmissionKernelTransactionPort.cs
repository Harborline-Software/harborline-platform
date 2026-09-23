using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Buffers the Forms commit until the shared kernel boundary owns the commit point.</summary>
public sealed class FormSubmissionKernelTransactionPort(IFormSubmissionTransactionStore store)
    : IKernelTransactionPort<FormSubmissionCommit, FormSubmissionCommitResult>,
      IKernelPreparedTransactionPort<FormSubmissionCommit, FormSubmissionCommitResult>
{
    private readonly IFormSubmissionTransactionStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async ValueTask<IKernelTransaction<FormSubmissionCommit, FormSubmissionCommitResult>> BeginAsync(
        KernelOperationIdentity operation,
        CancellationToken cancellationToken = default) =>
        new Transaction(await _store.BeginTransactionAsync(cancellationToken).ConfigureAwait(false), operation);

    public async ValueTask<IKernelPreparedTransaction<FormSubmissionCommit, FormSubmissionCommitResult>> BeginAsync(
        CancellationToken cancellationToken = default) =>
        new Transaction(await _store.BeginTransactionAsync(cancellationToken).ConfigureAwait(false), null);

    private sealed class Transaction(
        IFormSubmissionTransactionScope store,
        KernelOperationIdentity? operation)
        : IKernelTransaction<FormSubmissionCommit, FormSubmissionCommitResult>,
          IKernelPreparedTransaction<FormSubmissionCommit, FormSubmissionCommitResult>
    {
        private FormSubmissionCommit? _commit;
        private KernelAuditEvidence? _audit;
        private KernelOperationIdentity? _operation = operation;

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (_operation is not null && _operation != operation)
                throw new InvalidOperationException("The Forms transaction operation cannot change after it is established.");
            _operation = operation;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRecordAsync(FormSubmissionCommit record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            var establishedOperation = _operation ?? throw new InvalidOperationException("The operation must be staged before its record.");
            if (!StringComparer.Ordinal.Equals(establishedOperation.CommandId, record.Submission.InstanceId.ToString())
                || !StringComparer.Ordinal.Equals(establishedOperation.IdempotencyKey, record.IdempotencyKey)
                || !StringComparer.Ordinal.Equals(establishedOperation.Fingerprint, record.Submission.RequestFingerprint))
                throw new InvalidOperationException("The Forms commit does not match its kernel operation identity.");
            _commit = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audit);
            var recordAudit = _commit?.Audit ?? throw new InvalidOperationException("The record must be staged before its audit.");
            if (!StringComparer.Ordinal.Equals(audit.AuditId, recordAudit.AuditId)
                || !StringComparer.Ordinal.Equals(audit.ActorId, recordAudit.ActorId)
                || audit.RecordedAt != recordAudit.RecordedAt
                || !audit.Payload.Span.SequenceEqual(recordAudit.Payload.Span))
                throw new InvalidOperationException("The Forms audit does not match the staged commit.");
            _audit = audit;
            return ValueTask.CompletedTask;
        }

        public ValueTask<FormSubmissionCommitResult> CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_commit is null || _audit is null) throw new InvalidOperationException("A complete record and audit set is required.");
            return store.CommitAsync(_commit, cancellationToken);
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            _commit = null;
            _audit = null;
            return store.RollbackAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            _commit = null;
            _audit = null;
            await store.DisposeAsync().ConfigureAwait(false);
        }
    }
}
