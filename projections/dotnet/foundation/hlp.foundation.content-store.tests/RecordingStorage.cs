using Harborline.Foundation.Assets.Common;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.ContentStore.Tests;

internal sealed record BookkeepingImage(ContentStoreItem[] Items, ContentStoreItem[] Staged,
    KeyValuePair<(TenantId Tenant, string? Record), long>[] Quota, KernelAuditEvidence[] Audits, IReadOnlyList<ReclamationEntry> Reclamations, IReadOnlyList<ContentIntegrityEntry> Integrity);

// The in-memory backing behind a decorator that records the order of what reaches each port, and can pause, fail or
// crash at a named step. It decides nothing itself, so what it shows is the store's order and the backing's conditions.
internal sealed class RecordingStorage : IContentStoreStorage
{
    public InMemoryContentBookkeeping Memory { get; } = new();
    public InMemoryContentByteStore MemoryBytes { get; } = new();
    public List<string> Events { get; } = [];
    public List<string> EpochEvents { get; } = [];
    public List<ContentStoreOperation> StagedRecords { get; } = [];
    public List<int> SegmentReads { get; } = [];
    public List<KernelOperationIdentity> StagedOperations { get; } = [];
    private int _transactionsBegun;
    public int TransactionsBegun => Volatile.Read(ref _transactionsBegun);
    public List<(ContentStoreOperation Operation, BookkeepingImage Image)> Commits { get; } = [];
    public bool FailNextCommit { get; set; }
    public IOException? Failure { get; private set; }
    public Exception? ThrowOnBegin { get; set; }
    public Exception? ThrowOnDelete { get; set; }
    public Action? BeforeFindStaged { get; set; }
    public Action<int>? BeforeSegmentRead { get; set; }
    public Action? BeforeTryCreate { get; set; }
    public Action? AfterCommit { get; set; }
    public Func<Task>? BeforeBegin { get; set; }
    public Func<Task>? BeforeFindItemForUpdate { get; set; }

    public RecordingStorage()
    {
        Bookkeeping = new RecordingBookkeeping(this);
        Bytes = new RecordingBytes(this);
        Epochs = new RecordingEpochs(this);
    }

    public IContentBookkeeping Bookkeeping { get; }
    public IContentByteStore Bytes { get; }
    public IContentEpochKeyStore Epochs { get; }
    public InMemoryContentEpochKeyStore MemoryEpochs { get; } = new();
    public Action<string>? OnEpochStep { get; set; }
    public Func<Task>? BeforeFirstSetCurrent { get; set; }
    public Func<Task>? BeforePin { get; set; }
    public Func<Task>? BeforeEpochGet { get; set; }

    public static BookkeepingImage Capture(InMemoryContentBookkeeping memory)
    {
        var (items, staged, quota, audits) = memory.Snapshot();
        return new(items, staged, quota, audits, memory.Reclamations, memory.IntegrityEntries);
    }

    private void Log(string entry)
    {
        lock (Events) Events.Add(entry);
    }

    private void ThrowIfFailing(ContentStoreOperation operation)
    {
        if (!FailNextCommit) return;
        FailNextCommit = false;
        Failure = new IOException("The backing could not write the unit.");
        lock (Commits) Commits.Add((operation, Capture(Memory)));
        throw Failure;
    }

    private sealed class RecordingBookkeeping(RecordingStorage owner) : IContentBookkeeping
    {
        public async ValueTask<IContentBookkeepingTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            if (owner.ThrowOnBegin is { } crash) throw crash;
            if (owner.BeforeBegin is { } before)
            {
                owner.BeforeBegin = null;
                await before();
            }

            Interlocked.Increment(ref owner._transactionsBegun);
            return new Transaction(owner, await owner.Memory.BeginTransactionAsync(cancellationToken));
        }

        public ValueTask<IReadOnlyList<ContentStoreItem>> StagedCreatedAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => owner.Memory.StagedCreatedAtOrBeforeAsync(cutoff, cancellationToken);
        public ValueTask<bool> NamesBytesAsync(Guid itemKey, CancellationToken cancellationToken = default) => owner.Memory.NamesBytesAsync(itemKey, cancellationToken);
        public ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default) => owner.Memory.FindItemAsync(key, cancellationToken);
        public ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default) => owner.Memory.FindIntegrityAsync(tenant, itemKey, cancellationToken);
        public IReadOnlyList<ReclamationEntry> Reclamations => owner.Memory.Reclamations;
        public IReadOnlyList<ContentIntegrityEntry> IntegrityEntries => owner.Memory.IntegrityEntries;
        public int StagedCount => owner.Memory.StagedCount;
        public int CommittedCount => owner.Memory.CommittedCount;
    }

    private sealed class Transaction(RecordingStorage owner, IContentBookkeepingTransaction inner) : IContentBookkeepingTransaction
    {
        private IContentBookkeepingSession Session => inner;
        private ContentStoreOperation _operation;

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            lock (owner.StagedOperations) owner.StagedOperations.Add(operation);
            return Session.StageOperationAsync(operation, cancellationToken);
        }

        public ValueTask StageRecordAsync(ContentStoreOperation record, CancellationToken cancellationToken = default)
        {
            _operation = record;
            lock (owner.StagedRecords) owner.StagedRecords.Add(record);
            return Session.StageRecordAsync(record, cancellationToken);
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default) => Session.StageAuditAsync(audit, cancellationToken);
        public ValueTask<ContentStoreItem?> FindItemAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default) => inner.FindItemAsync(key, cancellationToken);

        public async ValueTask<ContentStoreItem?> FindItemForUpdateAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default)
        {
            if (owner.BeforeFindItemForUpdate is { } hook)
            {
                owner.BeforeFindItemForUpdate = null;
                await hook();
            }

            return await inner.FindItemForUpdateAsync(key, cancellationToken);
        }

        public ValueTask<IReadOnlyList<ContentStoreItem>> ItemsUnderEpochAsync(TenantId tenant, Guid epochId, int limit, CancellationToken cancellationToken = default) => inner.ItemsUnderEpochAsync(tenant, epochId, limit, cancellationToken);
        public async ValueTask<bool> PinEpochAsync(TenantId tenant, Guid epochId, CancellationToken cancellationToken = default)
        {
            if (owner.BeforePin is { } hook)
            {
                owner.BeforePin = null;
                await hook();
            }

            return await inner.PinEpochAsync(tenant, epochId, cancellationToken);
        }

        public ValueTask<bool> RetireEpochIfUnnamedAsync(TenantId tenant, Guid epochId, CancellationToken cancellationToken = default) => inner.RetireEpochIfUnnamedAsync(tenant, epochId, cancellationToken);

        public ValueTask<ContentStoreItem?> FindStagedForUpdateAsync(Guid stageId, CancellationToken cancellationToken = default)
        {
            owner.BeforeFindStaged?.Invoke();
            return inner.FindStagedForUpdateAsync(stageId, cancellationToken);
        }
        public ValueTask<bool> ReplaceWrapAsync(ContentStoreItem item, Guid epochId, byte[] wrappedDataKey, byte[] wrapNonce, byte[] wrapTag, CancellationToken cancellationToken = default) =>
            inner.ReplaceWrapAsync(item, epochId, wrappedDataKey, wrapNonce, wrapTag, cancellationToken);

        public ValueTask<ContentStoreItem?> FindStagedAsync(Guid stageId, CancellationToken cancellationToken = default)
        {
            owner.BeforeFindStaged?.Invoke();
            return inner.FindStagedAsync(stageId, cancellationToken);
        }

        public ValueTask<ContentIntegrityEntry?> FindIntegrityAsync(TenantId tenant, Guid itemKey, CancellationToken cancellationToken = default) => inner.FindIntegrityAsync(tenant, itemKey, cancellationToken);

        public ValueTask AddStagedAsync(ContentStoreItem item, CancellationToken cancellationToken = default)
        {
            owner.Log("staged-entry");
            return inner.AddStagedAsync(item, cancellationToken);
        }

        public ValueTask<bool> RemoveStagedAsync(Guid stageId, CancellationToken cancellationToken = default) => inner.RemoveStagedAsync(stageId, cancellationToken);
        public ValueTask<ContentQuotaReservation> TryReserveQuotaAsync(TenantId tenant, string record, long bytes, long recordLimit, long tenantLimit, CancellationToken cancellationToken = default) =>
            inner.TryReserveQuotaAsync(tenant, record, bytes, recordLimit, tenantLimit, cancellationToken);
        public ValueTask ReleaseQuotaAsync(TenantId tenant, string record, long bytes, CancellationToken cancellationToken = default) => inner.ReleaseQuotaAsync(tenant, record, bytes, cancellationToken);

        public ValueTask<ContentStoreItem?> TryCreateItemAsync(ContentStoreItem item, CancellationToken cancellationToken = default)
        {
            owner.BeforeTryCreate?.Invoke();
            return inner.TryCreateItemAsync(item, cancellationToken);
        }

        public ValueTask<bool> AddOwnerAsync(ContentStorageItemKey key, ContentOwner added, CancellationToken cancellationToken = default) => inner.AddOwnerAsync(key, added, cancellationToken);
        public ValueTask<bool> RemoveOwnerAsync(ContentStorageItemKey key, ContentOwner removed, CancellationToken cancellationToken = default) => inner.RemoveOwnerAsync(key, removed, cancellationToken);
        public ValueTask<bool> DeleteItemIfUnownedAsync(ContentStorageItemKey key, CancellationToken cancellationToken = default) => inner.DeleteItemIfUnownedAsync(key, cancellationToken);
        public ValueTask AppendReclamationAsync(ReclamationEntry entry, CancellationToken cancellationToken = default) => inner.AppendReclamationAsync(entry, cancellationToken);
        public ValueTask SetIntegrityAsync(ContentIntegrityEntry entry, CancellationToken cancellationToken = default) => inner.SetIntegrityAsync(entry, cancellationToken);

        public async ValueTask<bool> CommitAsync(CancellationToken cancellationToken = default)
        {
            owner.ThrowIfFailing(_operation);
            var result = await inner.CommitAsync(cancellationToken);
            lock (owner.Commits) owner.Commits.Add((_operation, Capture(owner.Memory)));
            owner.Log("unit-commit");
            owner.AfterCommit?.Invoke();
            return result;
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RecordingEpochs(RecordingStorage owner) : IContentEpochKeyStore
    {
        public ValueTask<ContentEpochKey?> GetCurrentAsync(TenantId tenant, CancellationToken cancellationToken = default) => owner.MemoryEpochs.GetCurrentAsync(tenant, cancellationToken);
        public async ValueTask<ContentEpochKey?> GetAsync(TenantId tenant, Guid epochId, CancellationToken cancellationToken = default)
        {
            if (owner.BeforeEpochGet is { } hook)
            {
                owner.BeforeEpochGet = null;
                await hook();
            }

            return await owner.MemoryEpochs.GetAsync(tenant, epochId, cancellationToken);
        }

        public async ValueTask CreateAsync(TenantId tenant, ContentEpochKey epoch, CancellationToken cancellationToken = default)
        {
            await owner.MemoryEpochs.CreateAsync(tenant, epoch, cancellationToken);
            lock (owner.EpochEvents) owner.EpochEvents.Add("epoch-created");
            owner.OnEpochStep?.Invoke("epoch-created");
        }

        public async ValueTask<bool> TrySetCurrentAsync(TenantId tenant, Guid? expectedCurrent, Guid epochId, CancellationToken cancellationToken = default)
        {
            if (owner.BeforeFirstSetCurrent is { } before)
            {
                owner.BeforeFirstSetCurrent = null;
                await before();
            }

            owner.OnEpochStep?.Invoke("before-switch");
            var switched = await owner.MemoryEpochs.TrySetCurrentAsync(tenant, expectedCurrent, epochId, cancellationToken);
            if (switched) owner.OnEpochStep?.Invoke("epoch-switched");
            return switched;
        }

        public async ValueTask DestroyAsync(TenantId tenant, Guid epochId, CancellationToken cancellationToken = default)
        {
            owner.OnEpochStep?.Invoke("before-destroy");
            await owner.MemoryEpochs.DestroyAsync(tenant, epochId, cancellationToken);
            lock (owner.EpochEvents) owner.EpochEvents.Add("epoch-destroyed");
        }
    }

    private sealed class RecordingBytes(RecordingStorage owner) : IContentByteStore
    {
        public async ValueTask<IContentByteWriter> OpenWriteAsync(Guid itemKey, DateTimeOffset startedUtc, CancellationToken cancellationToken = default) =>
            new Writer(owner, await owner.MemoryBytes.OpenWriteAsync(itemKey, startedUtc, cancellationToken));

        public ValueTask<ContentCiphertextSegment?> ReadSegmentAsync(Guid itemKey, int index, CancellationToken cancellationToken = default)
        {
            lock (owner.SegmentReads) owner.SegmentReads.Add(index);
            owner.BeforeSegmentRead?.Invoke(index);
            return owner.MemoryBytes.ReadSegmentAsync(itemKey, index, cancellationToken);
        }

        public ValueTask DeleteAsync(Guid itemKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.ThrowOnDelete is { } failure) throw failure;
            owner.Log("bytes-delete");
            return owner.MemoryBytes.DeleteAsync(itemKey, cancellationToken);
        }

        public ValueTask<IReadOnlyList<Guid>> WrittenAtOrBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => owner.MemoryBytes.WrittenAtOrBeforeAsync(cutoff, cancellationToken);

        public async ValueTask<bool> DeleteIfWrittenAtOrBeforeAsync(Guid itemKey, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.ThrowOnDelete is { } failure) throw failure;
            var deleted = await owner.MemoryBytes.DeleteIfWrittenAtOrBeforeAsync(itemKey, cutoff, cancellationToken);
            if (deleted) owner.Log("bytes-delete");
            return deleted;
        }
    }

    private sealed class Writer(RecordingStorage owner, IContentByteWriter inner) : IContentByteWriter
    {
        public ValueTask AppendAsync(ContentCiphertextSegment segment, CancellationToken cancellationToken = default) => inner.AppendAsync(segment, cancellationToken);

        public async ValueTask CompleteAsync(DateTimeOffset durableUtc, CancellationToken cancellationToken = default)
        {
            await inner.CompleteAsync(durableUtc, cancellationToken);
            owner.Log("bytes-durable");
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
