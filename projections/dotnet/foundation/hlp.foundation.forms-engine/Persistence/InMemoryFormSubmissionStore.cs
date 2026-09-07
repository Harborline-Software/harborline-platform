using Harborline.Foundation.Assets.Common;

namespace Harborline.Foundation.Forms.Engine.Persistence;

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

public sealed class InMemoryFormSubmissionStore : IFormSubmissionTransactionStore
{
    private readonly InMemoryFormSubmissionState _state;
    private readonly Func<FormSubmissionCommit, Exception?>? _commitFailure;

    public InMemoryFormSubmissionStore(InMemoryFormSubmissionState? state = null)
        : this(state ?? new InMemoryFormSubmissionState(), null) { }

    internal InMemoryFormSubmissionStore(
        InMemoryFormSubmissionState state,
        Func<FormSubmissionCommit, Exception?>? commitFailure)
    {
        _state = state;
        _commitFailure = commitFailure;
    }

    public async ValueTask<FormSubmissionCommitResult> CommitAsync(
        FormSubmissionCommit commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        FormSubmissionStoreModel.ValidateAtomicEnvelope(commit);
        var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);

        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.Idempotency.TryGetValue(key, out var existing))
            {
                return string.Equals(existing.Submission.RequestFingerprint, commit.Submission.RequestFingerprint, StringComparison.Ordinal)
                    ? new(FormSubmissionCommitDisposition.Replayed, existing.Receipt)
                    : new(FormSubmissionCommitDisposition.Conflict, null);
            }

            if (_state.Submissions.ContainsKey((commit.Submission.Tenant, commit.Submission.InstanceId.ToString()))
                || _state.Audits.ContainsKey(commit.Audit.AuditId)
                || _state.Pending.ContainsKey(commit.Projection.OutboxId)
                || _state.Completed.Contains(commit.Projection.OutboxId))
                throw new ArgumentException("Submission, audit, and outbox identities must be unique.", nameof(commit));
            if (_commitFailure?.Invoke(commit) is { } failure) throw failure;
            var snapshot = FormSubmissionStoreModel.Clone(commit);
            _state.Submissions.Add((snapshot.Submission.Tenant, snapshot.Submission.InstanceId.ToString()), snapshot.Submission);
            _state.Audits.Add(snapshot.Audit.AuditId, snapshot.Audit);
            _state.Pending.Add(snapshot.Projection.OutboxId, snapshot.Projection);
            _state.Idempotency.Add(key, snapshot);
            _state.Leased.Add(snapshot.Projection.OutboxId);
            return new(FormSubmissionCommitDisposition.Created, snapshot.Receipt);
        }
        finally { _state.Gate.Release(); }
    }

    public async ValueTask<FormSubmissionRecord?> GetAsync(
        TenantId tenant,
        EntityId instanceId,
        CancellationToken cancellationToken = default)
    {
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _state.Submissions.TryGetValue((tenant, instanceId.ToString()), out var value) ? FormSubmissionStoreModel.Clone(value) : null; }
        finally { _state.Gate.Release(); }
    }

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

    public async ValueTask ReleaseProjectionLeaseAsync(string outboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await _state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _state.Leased.Remove(outboxId); }
        finally { _state.Gate.Release(); }
    }

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
