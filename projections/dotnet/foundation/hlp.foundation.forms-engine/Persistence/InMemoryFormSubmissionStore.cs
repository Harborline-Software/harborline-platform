using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Holds in-memory submission, audit, idempotency, lease, and projection state.</summary>
public sealed class InMemoryFormSubmissionState
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal readonly Dictionary<(TenantId Tenant, string Instance), FormSubmissionRecord> Submissions = new();
    internal readonly Dictionary<(TenantId Tenant, string Form, string Key), FormSubmissionCommit> Idempotency = new();
    internal readonly Dictionary<string, FormMutationAuditEnvelope> Audits = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, FormProjectionEnvelope> Pending = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Completed = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Leased = new(StringComparer.Ordinal);
    // Accepted Records commands by command id, with their tenant, in their current lifecycle state (ADR-0110).
    internal readonly Dictionary<string, (TenantId Tenant, FormAcceptedRecordCommand Command)> Commands = new(StringComparer.Ordinal);
    // The submission instance each accepted command belongs to, so a terminal transition's audit names it (review M1).
    internal readonly Dictionary<string, EntityId> CommandInstances = new(StringComparer.Ordinal);
    // Review R2-L4: each pending row's queue position (least recently enqueued leases first) and the sequence that issues them.
    internal readonly Dictionary<string, long> QueuePositions = new(StringComparer.Ordinal);
    internal long QueueSequence;

    // Places the row at the back of the lease queue: at its commit and after each failed attempt.
    internal void Enqueue(string outboxId) => QueuePositions[outboxId] = ++QueueSequence;
}

/// <summary>Provides a process-local transactional submission store for non-production hosts.</summary>
public sealed class InMemoryFormSubmissionStore : IFormSubmissionTransactionStore, IFormAcceptedCommandStore
{
    private readonly InMemoryFormSubmissionState _state;
    private readonly Func<FormSubmissionCommit, Exception?>? _commitFailure;

    /// <summary>Creates a process-local store with optional shared state for test isolation or inspection.</summary>
    public InMemoryFormSubmissionStore(InMemoryFormSubmissionState? state = null)
        : this(state ?? new InMemoryFormSubmissionState(), null) { }

    /// <summary>Creates the internal store variant with an injectable commit failure.</summary>
    internal InMemoryFormSubmissionStore(
        InMemoryFormSubmissionState state,
        Func<FormSubmissionCommit, Exception?>? commitFailure)
    {
        _state = state;
        _commitFailure = commitFailure;
    }

    /// <inheritdoc />
    public async ValueTask<FormSubmissionCommitResult> CommitAsync(
        FormSubmissionCommit commit,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return await transaction.CommitAsync(commit, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        return new Transaction(_state, _commitFailure);
    }

    private sealed class Transaction(
        InMemoryFormSubmissionState state,
        Func<FormSubmissionCommit, Exception?>? commitFailure) : IFormSubmissionTransactionScope
    {
        private bool _commitAttempted;
        private bool _released;

        /// <inheritdoc />
        public ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (_commitAttempted) throw new InvalidOperationException("A Forms submission scope can commit only once.");
            _commitAttempted = true;
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(commit);
            FormSubmissionStoreModel.ValidateAtomicEnvelope(commit);
            var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);
            if (state.Idempotency.TryGetValue(key, out var existing))
                return ValueTask.FromResult(string.Equals(existing.Submission.RequestFingerprint, commit.Submission.RequestFingerprint, StringComparison.Ordinal)
                    ? new FormSubmissionCommitResult(FormSubmissionCommitDisposition.Replayed, existing.Receipt, FormSubmissionStoreModel.Clone(existing))
                    : new FormSubmissionCommitResult(FormSubmissionCommitDisposition.Conflict, null));

            if (state.Submissions.ContainsKey((commit.Submission.Tenant, commit.Submission.InstanceId.ToString()))
                || state.Audits.ContainsKey(commit.Audit.AuditId)
                || state.Pending.ContainsKey(commit.Projection.OutboxId)
                || state.Completed.Contains(commit.Projection.OutboxId))
                throw new ArgumentException("Submission, audit, and outbox identities must be unique.", nameof(commit));
            if (commitFailure?.Invoke(commit) is { } failure) throw failure;
            var snapshot = FormSubmissionStoreModel.Clone(commit);
            state.Submissions.Add((snapshot.Submission.Tenant, snapshot.Submission.InstanceId.ToString()), snapshot.Submission);
            state.Audits.Add(snapshot.Audit.AuditId, snapshot.Audit);
            state.Pending.Add(snapshot.Projection.OutboxId, snapshot.Projection);
            state.Idempotency.Add(key, snapshot);
            state.Leased.Add(snapshot.Projection.OutboxId);
            state.Enqueue(snapshot.Projection.OutboxId);
            if (snapshot.Projection.AcceptedCommand is { } command)
            {
                state.Commands.Add(command.CommandId, (snapshot.Submission.Tenant, FormSubmissionStoreModel.Clone(command)));
                state.CommandInstances.Add(command.CommandId, snapshot.Submission.InstanceId);
            }
            return ValueTask.FromResult(new FormSubmissionCommitResult(FormSubmissionCommitDisposition.Created, snapshot.Receipt));
        }

        /// <inheritdoc />
        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            Release();
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            Release();
            return ValueTask.CompletedTask;
        }

        private void Release()
        {
            if (_released) return;
            _released = true;
            state.Gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<FormSubmissionRecord?> GetAsync(
        TenantId tenant,
        EntityId instanceId,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { return _state.Submissions.TryGetValue((tenant, instanceId.ToString()), out var value) ? FormSubmissionStoreModel.Clone(value) : null; }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(
        int maximum,
        CancellationToken cancellationToken = default) =>
        LeasePendingAsync(maximum, FormProjectionLeaseKinds.All, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(
        int maximum,
        FormProjectionLeaseKinds kinds,
        CancellationToken cancellationToken = default)
    {
        if (maximum is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximum));
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = FormSubmissionStoreModel.LeaseOrder(_state.Pending.Values
                .Where(row => !_state.Leased.Contains(row.OutboxId) && FormSubmissionStoreModel.LeaseAdmits(row, kinds)), _state.QueuePositions, maximum);
            foreach (var row in rows) _state.Leased.Add(row.OutboxId);
            return rows.Select(FormSubmissionStoreModel.Clone).ToArray();
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Pending.TryGetValue(outboxId, out var row) || row.Tenant != tenant || row.AcceptedCommand is not null
                || !_state.Leased.Add(outboxId))
                return null;
            return FormSubmissionStoreModel.Clone(row);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { _state.Leased.Remove(outboxId); }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skips);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Review R3-L1: only a claimed command completes here; a quarantined one only through its audited resolution.
            if (_state.Pending.TryGetValue(outboxId, out var row) && row.AcceptedCommand is { State: not FormAcceptedCommandState.Claimed })
                throw new InvalidOperationException("An accepted Records command completes only after its execution claim.");
            _state.Leased.Remove(outboxId);
            if (!_state.Pending.Remove(outboxId)) return;
            _state.QueuePositions.Remove(outboxId);
            _state.Completed.Add(outboxId);
            if (_state.Commands.TryGetValue(outboxId, out var entry))
                _state.Commands[outboxId] = (entry.Tenant, entry.Command with { State = FormAcceptedCommandState.Completed });
            foreach (var (key, commit) in _state.Idempotency.ToArray())
            {
                if (commit.Projection.OutboxId != outboxId) continue;
                var receipt = commit.Receipt with { ProjectionStatus = FormProjectionStatus.Complete, ProjectionSkips = skips.ToArray() };
                _state.Idempotency[key] = commit with { Receipt = receipt };
                break;
            }
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableErrorCode);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.Leased.Remove(outboxId);
            if (_state.Pending.TryGetValue(outboxId, out var value))
            {
                _state.Pending[outboxId] = value with { Attempts = checked(value.Attempts + 1), LastErrorCode = stableErrorCode };
                _state.Enqueue(outboxId);
            }
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _state.Commands.TryGetValue(commandId, out var entry) && entry.Tenant == tenant
                ? FormSubmissionStoreModel.Clone(entry.Command)
                : null;
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Pending) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            return new(Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Claimed, StateActorId = workerId, StateChangedAt = at,
            }, settle: false), true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> CancelAcceptedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(audit);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Pending) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: true);
            var next = Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Cancelled, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt,
                StateSnapshot = audit.AuthoritySnapshot.ToArray(),
            }, settle: true);
            _state.Audits.Add(envelope.AuditId, envelope);
            return new(next, true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> RefuseAcceptedCommandAsync(string commandId, FormAcceptedCommandState expectedState,
        string refusalCode, KernelAuditEvidence audit, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refusalCode);
        ArgumentNullException.ThrowIfNull(audit);
        if (expectedState is not (FormAcceptedCommandState.Pending or FormAcceptedCommandState.Claimed))
            throw new ArgumentOutOfRangeException(nameof(expectedState));
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != expectedState) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            var next = Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Refused, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt, RefusalCode = refusalCode,
            }, settle: true);
            _state.Audits.Add(envelope.AuditId, envelope);
            return new(next, true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Claimed || !_state.Pending.TryGetValue(commandId, out var row))
                return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var next = entry.Command with { BindingFailures = checked(entry.Command.BindingFailures + 1) };
            _state.Commands[commandId] = (entry.Tenant, next);
            _state.Pending[commandId] = row with { Attempts = checked(row.Attempts + 1), LastErrorCode = code, AcceptedCommand = next };
            _state.Enqueue(commandId);
            _state.Leased.Remove(commandId);
            return new(FormSubmissionStoreModel.Clone(next), true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> QuarantineAcceptedCommandAsync(string commandId, string code, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(audit);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Claimed) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            var next = Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Quarantined, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt, RefusalCode = code,
            }, settle: false);
            _state.Leased.Remove(commandId);
            _state.Audits.Add(envelope.AuditId, envelope);
            return new(next, true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> AbandonQuarantinedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(audit);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Quarantined) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: true);
            var next = Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Cancelled, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt,
                StateSnapshot = audit.AuthoritySnapshot.ToArray(),
            }, settle: true);
            _state.Audits.Add(envelope.AuditId, envelope);
            return new(next, true);
        }
        finally { _state.Gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> ResolveQuarantinedCommandAsync(TenantId tenant, string commandId, KernelAuditEvidence audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentNullException.ThrowIfNull(audit);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.Commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Quarantined) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            var next = Transition(entry, entry.Command with
            {
                State = FormAcceptedCommandState.Completed, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt,
            }, settle: true);
            _state.Audits.Add(envelope.AuditId, envelope);
            return new(next, true);
        }
        finally { _state.Gate.Release(); }
    }

    // The lifecycle transition's audit envelope, validated before anything changes (review M1).
    private FormMutationAuditEnvelope AuditOf(string commandId, TenantId tenant, KernelAuditEvidence audit, bool snapshotRequired)
    {
        var envelope = FormSubmissionStoreModel.LifecycleAudit(audit, _state.CommandInstances[commandId], tenant, snapshotRequired);
        if (_state.Audits.ContainsKey(envelope.AuditId))
            throw new ArgumentException("Audit identities must be unique.", nameof(audit));
        return envelope;
    }

    // Review M5: a built-in store is never re-entered from the flow that holds its scope; that fails at once.
    private ValueTask EnterAsync(CancellationToken cancellationToken)
    {
        FormStoreReentrancy.ThrowIfHeld();
        return new(_state.Gate.WaitAsync(cancellationToken));
    }

    // One transition under the gate: the command's new state on its row and, for a terminal transition, the row leaves the
    // pending pool and the stored receipt reports the outcome.
    private FormAcceptedRecordCommand Transition((TenantId Tenant, FormAcceptedRecordCommand Command) entry, FormAcceptedRecordCommand next, bool settle)
    {
        var commandId = next.CommandId;
        _state.Commands[commandId] = (entry.Tenant, next);
        if (_state.Pending.TryGetValue(commandId, out var row))
        {
            if (settle)
            {
                _state.Pending.Remove(commandId);
                _state.QueuePositions.Remove(commandId);
                _state.Leased.Remove(commandId);
                _state.Completed.Add(commandId);
            }
            else
            {
                _state.Pending[commandId] = row with { AcceptedCommand = next };
            }
        }
        if (settle)
        {
            foreach (var (key, commit) in _state.Idempotency.ToArray())
            {
                if (commit.Projection.OutboxId != commandId) continue;
                _state.Idempotency[key] = commit with
                {
                    Receipt = commit.Receipt with { ProjectionStatus = FormSubmissionStoreModel.ReceiptStatus(next.State) },
                };
                break;
            }
        }
        return FormSubmissionStoreModel.Clone(next);
    }

    internal async ValueTask<(int Submissions, int Audits, int Pending, int Completed)> CountsAsync()
    {
        await EnterAsync(default).ConfigureAwait(false);
        try { return (_state.Submissions.Count, _state.Audits.Count, _state.Pending.Count, _state.Completed.Count); }
        finally { _state.Gate.Release(); }
    }

}
