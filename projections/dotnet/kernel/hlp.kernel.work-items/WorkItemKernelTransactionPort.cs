using System.Text;
using Harborline.Kernel.Core;

namespace Harborline.Kernel.WorkItems;

internal enum WorkItemCommitKind { Create, Transition }

internal sealed record WorkItemAtomicCommit(WorkItemCommitKind Kind, WorkItemCommit Commit);

internal sealed class WorkItemKernelTransactionPort(IWorkItemStore store)
    : IKernelTransactionPort<WorkItemAtomicCommit, WorkItemStoreResult>
{
    private readonly IWorkItemStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public ValueTask<IKernelTransaction<WorkItemAtomicCommit, WorkItemStoreResult>> BeginAsync(
        KernelOperationIdentity operation,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IKernelTransaction<WorkItemAtomicCommit, WorkItemStoreResult>>(
            new Transaction(_store, operation));

    private sealed class Transaction(IWorkItemStore store, KernelOperationIdentity operation)
        : IKernelTransaction<WorkItemAtomicCommit, WorkItemStoreResult>
    {
        private WorkItemAtomicCommit? _record;
        private KernelAuditEvidence? _audit;

        public ValueTask StageRecordAsync(WorkItemAtomicCommit record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (!StringComparer.Ordinal.Equals(operation.CommandId, record.Commit.Snapshot.Id)
                || !StringComparer.Ordinal.Equals(operation.IdempotencyKey, record.Commit.IdempotencyKey)
                || !StringComparer.Ordinal.Equals(operation.Fingerprint, record.Commit.Fingerprint))
                throw new InvalidOperationException("The work-item commit does not match its kernel operation identity.");
            _record = record;
            return ValueTask.CompletedTask;
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(audit);
            var commit = _record?.Commit ?? throw new InvalidOperationException("The record must be staged before its audit.");
            if (!StringComparer.Ordinal.Equals(audit.ActorId, commit.ActorId)
                || audit.RecordedAt != commit.Event.OccurredAt
                || !audit.Payload.Span.SequenceEqual(Encoding.UTF8.GetBytes(commit.Event.DataJson)))
                throw new InvalidOperationException("The work-item audit does not match the staged commit.");
            _audit = audit;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<WorkItemStoreResult> CommitAsync(CancellationToken cancellationToken = default)
        {
            var record = _record ?? throw new InvalidOperationException("A complete record and audit set is required.");
            if (_audit is null) throw new InvalidOperationException("A complete record and audit set is required.");
            return record.Kind == WorkItemCommitKind.Create
                ? await store.CommitCreateAsync(record.Commit, cancellationToken).ConfigureAwait(false)
                : await store.CommitTransitionAsync(record.Commit, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            _record = null;
            _audit = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _record = null;
            _audit = null;
            return ValueTask.CompletedTask;
        }
    }
}
