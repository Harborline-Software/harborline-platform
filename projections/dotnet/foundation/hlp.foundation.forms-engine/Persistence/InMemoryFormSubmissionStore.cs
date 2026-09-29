using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Represents the in memory form submission state contract used by this package.</summary>
public sealed class InMemoryFormSubmissionState
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal readonly Dictionary<(TenantId Tenant, string Instance), FormSubmissionRecord> Submissions = new();
    internal readonly Dictionary<(TenantId Tenant, string Form, string Key), FormSubmissionCommit> Idempotency = new();
    internal readonly Dictionary<string, FormMutationAuditEnvelope> Audits = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, FormProjectionEnvelope> Pending = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Completed = new(StringComparer.Ordinal);
    internal readonly HashSet<string> Leased = new(StringComparer.Ordinal);
}

/// <summary>Represents the in memory form submission store contract used by this package.</summary>
public sealed class InMemoryFormSubmissionStore : IFormSubmissionTransactionStore
{
    private readonly InMemoryFormSubmissionState _state;
    private readonly Func<FormSubmissionCommit, Exception?>? _commitFailure;

    /// <summary>Initializes the in memory form submission store instance with the supplied dependencies.</summary>
    public InMemoryFormSubmissionStore(InMemoryFormSubmissionState? state = null)
        : this(state ?? new InMemoryFormSubmissionState(), null) { }

    internal InMemoryFormSubmissionStore(
        InMemoryFormSubmissionState state,
        Func<FormSubmissionCommit, Exception?>? commitFailure)
    {
        _state = state;
        _commitFailure = commitFailure;
    }

    /// <summary>Executes the commit async contract.</summary>
    public async ValueTask<FormSubmissionCommitResult> CommitAsync(
        FormSubmissionCommit commit,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return await transaction.CommitAsync(commit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Executes the begin transaction async contract.</summary>
    public async ValueTask<IFormSubmissionTransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Transaction(_state, _commitFailure);
    }

    private sealed class Transaction(
        InMemoryFormSubmissionState state,
        Func<FormSubmissionCommit, Exception?>? commitFailure) : IFormSubmissionTransactionScope
    {
        private bool _commitAttempted;
        private bool _released;

        /// <summary>Executes the commit async contract.</summary>
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
                    ? new FormSubmissionCommitResult(FormSubmissionCommitDisposition.Replayed, existing.Receipt)
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
            return ValueTask.FromResult(new FormSubmissionCommitResult(FormSubmissionCommitDisposition.Created, snapshot.Receipt));
        }

        /// <summary>Executes the rollback async contract.</summary>
        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            Release();
            return ValueTask.CompletedTask;
        }

        /// <summary>Executes the dispose async contract.</summary>
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

    /// <summary>Calculates the get async contract.</summary>
    public async ValueTask<FormSubmissionRecord?> GetAsync(
        TenantId tenant,
        EntityId instanceId,
        CancellationToken cancellationToken = default)
    {
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _state.Submissions.TryGetValue((tenant, instanceId.ToString()), out var value) ? FormSubmissionStoreModel.Clone(value) : null; }
        finally { _state.Gate.Release(); }
    }

    /// <summary>Executes the lease pending async contract.</summary>
    public async ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(
        int maximum,
        CancellationToken cancellationToken = default)
    {
        if (maximum is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximum));
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = _state.Pending.Values.Where(row => !_state.Leased.Contains(row.OutboxId))
                .OrderBy(row => row.SubmittedAt).ThenBy(row => row.OutboxId, StringComparer.Ordinal).Take(maximum).ToArray();
            foreach (var row in rows) _state.Leased.Add(row.OutboxId);
            return rows.Select(FormSubmissionStoreModel.Clone).ToArray();
        }
        finally { _state.Gate.Release(); }
    }

    /// <summary>Executes the release projection lease async contract.</summary>
    public async ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _state.Leased.Remove(outboxId); }
        finally { _state.Gate.Release(); }
    }

    /// <summary>Executes the complete projection async contract.</summary>
    public async ValueTask CompleteProjectionAsync(string outboxId, IReadOnlyList<FormProjectionSkip> skips, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skips);
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.Leased.Remove(outboxId);
            if (!_state.Pending.Remove(outboxId)) return;
            _state.Completed.Add(outboxId);
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

    /// <summary>Executes the retry projection async contract.</summary>
    public async ValueTask RetryProjectionAsync(string outboxId, string stableErrorCode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableErrorCode);
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state.Leased.Remove(outboxId);
            if (_state.Pending.TryGetValue(outboxId, out var value))
                _state.Pending[outboxId] = value with { Attempts = checked(value.Attempts + 1), LastErrorCode = stableErrorCode };
        }
        finally { _state.Gate.Release(); }
    }

    internal async ValueTask<(int Submissions, int Audits, int Pending, int Completed)> CountsAsync()
    {
        await _state.Gate.WaitAsync().ConfigureAwait(false);
        try { return (_state.Submissions.Count, _state.Audits.Count, _state.Pending.Count, _state.Completed.Count); }
        finally { _state.Gate.Release(); }
    }

}
