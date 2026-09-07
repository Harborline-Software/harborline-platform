namespace Harborline.Kernel.WorkItems;

/// <summary>Thread-safe atomic adapter for tests and development composition.</summary>
public sealed class InMemoryWorkItemStore : IWorkItemStore, IWorkItemJournalReader
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkItemSnapshot> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkItemIdempotencyReceipt> _receipts = new(StringComparer.Ordinal);
    private readonly List<TenantEvent> _events = [];
    private readonly List<TenantAudit> _audit = [];
    private readonly List<TenantOutbox> _outbox = [];

    /// <inheritdoc />
    public Task<WorkItemSnapshot?> GetAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_items.GetValueOrDefault(ItemKey(tenantId, workItemId)));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemSnapshot>> ListOpenAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<WorkItemSnapshot> result = _items
                .Where(row => TenantFromKey(row.Key) == tenantId && row.Value.Status is WorkItemStatus.Running or WorkItemStatus.Parked)
                .Select(row => row.Value)
                .OrderByDescending(row => row.UpdatedAt)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<WorkItemIdempotencyReceipt?> GetReceiptAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return Task.FromResult(_receipts.GetValueOrDefault(ReceiptKey(tenantId, idempotencyKey)));
    }

    /// <inheritdoc />
    public Task<WorkItemStoreResult> CommitCreateAsync(WorkItemCommit commit, CancellationToken cancellationToken = default) =>
        CommitAsync(commit, create: true, cancellationToken);

    /// <inheritdoc />
    public Task<WorkItemStoreResult> CommitTransitionAsync(WorkItemCommit commit, CancellationToken cancellationToken = default) =>
        CommitAsync(commit, create: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemEvent>> ReadEventsAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<WorkItemEvent> result = _events.Where(row => row.TenantId == tenantId && row.Value.WorkItemId == workItemId)
                .Select(row => row.Value).OrderBy(row => row.Sequence).ToArray();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemAudit>> ReadAuditAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<WorkItemAudit> result = _audit.Where(row => row.TenantId == tenantId && row.Value.WorkItemId == workItemId)
                .Select(row => row.Value).OrderBy(row => row.WorkItemVersion).ToArray();
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemOutboxMessage>> ReadOutboxAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            IReadOnlyList<WorkItemOutboxMessage> result = _outbox.Where(row => row.TenantId == tenantId).Select(row => row.Value).ToArray();
            return Task.FromResult(result);
        }
    }

    internal WorkItemJournalState ExportState()
    {
        lock (_gate)
        {
            return new WorkItemJournalState
            {
                Items = _items.Select(row => new TenantSnapshot(TenantFromKey(row.Key), row.Value)).ToList(),
                Receipts = _receipts.Select(row => new TenantReceipt(TenantFromKey(row.Key), row.Value)).ToList(),
                Events = [.. _events],
                Audit = [.. _audit],
                Outbox = [.. _outbox],
            };
        }
    }

    internal void ImportState(WorkItemJournalState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            _items.Clear();
            foreach (var row in state.Items) _items.Add(ItemKey(row.TenantId, row.Value.Id), row.Value);
            _receipts.Clear();
            foreach (var row in state.Receipts) _receipts.Add(ReceiptKey(row.TenantId, row.Value.Key), row.Value);
            _events.Clear(); _events.AddRange(state.Events);
            _audit.Clear(); _audit.AddRange(state.Audit);
            _outbox.Clear(); _outbox.AddRange(state.Outbox);
        }
    }

    private Task<WorkItemStoreResult> CommitAsync(WorkItemCommit commit, bool create, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var receiptKey = ReceiptKey(commit.TenantId, commit.IdempotencyKey);
            if (_receipts.TryGetValue(receiptKey, out var prior))
            {
                return Task.FromResult(new WorkItemStoreResult(
                    prior.Fingerprint == commit.Fingerprint ? WorkItemMutationDisposition.Replayed : WorkItemMutationDisposition.IdempotencyConflict,
                    prior.Fingerprint == commit.Fingerprint ? prior : null));
            }

            var itemKey = ItemKey(commit.TenantId, commit.Snapshot.Id);
            _items.TryGetValue(itemKey, out var current);
            if (create)
            {
                if (current is not null) return Task.FromResult(new WorkItemStoreResult(WorkItemMutationDisposition.VersionConflict, null));
            }
            else if (current is null)
            {
                return Task.FromResult(new WorkItemStoreResult(WorkItemMutationDisposition.NotFound, null));
            }
            else if (current.Version != commit.ExpectedVersion || !StringComparer.Ordinal.Equals(current.CurrentStep, commit.ExpectedStep))
            {
                return Task.FromResult(new WorkItemStoreResult(WorkItemMutationDisposition.VersionConflict, null));
            }

            var receipt = new WorkItemIdempotencyReceipt(commit.IdempotencyKey, commit.Fingerprint, commit.Snapshot, commit.ResultJson);
            _items[itemKey] = commit.Snapshot;
            _receipts.Add(receiptKey, receipt);
            _events.Add(new TenantEvent(commit.TenantId, commit.Event));
            _audit.Add(new TenantAudit(commit.TenantId, new WorkItemAudit(
                commit.Snapshot.Id, commit.Snapshot.Version, commit.ActorId, commit.Event.Action, commit.Event.OccurredAt)));
            foreach (var message in commit.Outbox) _outbox.Add(new TenantOutbox(commit.TenantId, message));
            return Task.FromResult(new WorkItemStoreResult(WorkItemMutationDisposition.Committed, receipt));
        }
    }

    private const char Separator = '\u001f';
    private static string ItemKey(string tenantId, string id) => $"{tenantId}{Separator}{id}";
    private static string ReceiptKey(string tenantId, string id) => $"{tenantId}{Separator}{id}";
    private static string TenantFromKey(string key) => key[..key.IndexOf(Separator, StringComparison.Ordinal)];
}

internal sealed record TenantSnapshot(string TenantId, WorkItemSnapshot Value);
internal sealed record TenantReceipt(string TenantId, WorkItemIdempotencyReceipt Value);
internal sealed record TenantEvent(string TenantId, WorkItemEvent Value);
internal sealed record TenantAudit(string TenantId, WorkItemAudit Value);
internal sealed record TenantOutbox(string TenantId, WorkItemOutboxMessage Value);

internal sealed class WorkItemJournalState
{
    public List<TenantSnapshot> Items { get; init; } = [];
    public List<TenantReceipt> Receipts { get; init; } = [];
    public List<TenantEvent> Events { get; init; } = [];
    public List<TenantAudit> Audit { get; init; } = [];
    public List<TenantOutbox> Outbox { get; init; } = [];
}
