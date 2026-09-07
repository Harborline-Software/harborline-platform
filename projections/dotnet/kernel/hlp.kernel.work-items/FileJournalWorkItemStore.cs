using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Kernel.WorkItems;

/// <summary>
/// Single-process checksummed append-only journal adapter. Every committed frame contains the complete
/// materialized kernel state, making recovery deterministic while this local-shadow format remains small.
/// </summary>
public sealed class FileJournalWorkItemStore : IWorkItemStore, IWorkItemJournalReader, IDisposable, IAsyncDisposable
{
    private const string Magic = "HLWI1";
    private const int MaximumFrameBytes = 16 * 1024 * 1024;
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _commitGate = new(1, 1);
    private readonly InMemoryWorkItemStore _inner = new();
    private bool _disposed;

    /// <summary>Opens and recovers one exclusive journal file.</summary>
    public FileJournalWorkItemStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.Asynchronous);
        try
        {
            Recover();
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Task<WorkItemSnapshot?> GetAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(tenantId, workItemId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemSnapshot>> ListOpenAsync(string tenantId, CancellationToken cancellationToken = default) =>
        _inner.ListOpenAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public Task<WorkItemIdempotencyReceipt?> GetReceiptAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        _inner.GetReceiptAsync(tenantId, idempotencyKey, cancellationToken);

    /// <inheritdoc />
    public Task<WorkItemStoreResult> CommitCreateAsync(WorkItemCommit commit, CancellationToken cancellationToken = default) =>
        CommitAsync(commit, create: true, cancellationToken);

    /// <inheritdoc />
    public Task<WorkItemStoreResult> CommitTransitionAsync(WorkItemCommit commit, CancellationToken cancellationToken = default) =>
        CommitAsync(commit, create: false, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemEvent>> ReadEventsAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default) =>
        _inner.ReadEventsAsync(tenantId, workItemId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemAudit>> ReadAuditAsync(string tenantId, string workItemId, CancellationToken cancellationToken = default) =>
        _inner.ReadAuditAsync(tenantId, workItemId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkItemOutboxMessage>> ReadOutboxAsync(string tenantId, CancellationToken cancellationToken = default) =>
        _inner.ReadOutboxAsync(tenantId, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stream.Dispose();
        _commitGate.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _stream.DisposeAsync().ConfigureAwait(false);
        _commitGate.Dispose();
    }

    private async Task<WorkItemStoreResult> CommitAsync(WorkItemCommit commit, bool create, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _commitGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var candidate = new InMemoryWorkItemStore();
            candidate.ImportState(_inner.ExportState());
            var result = create
                ? await candidate.CommitCreateAsync(commit, cancellationToken).ConfigureAwait(false)
                : await candidate.CommitTransitionAsync(commit, cancellationToken).ConfigureAwait(false);
            if (result.Disposition != WorkItemMutationDisposition.Committed) return result;

            var payload = JsonSerializer.SerializeToUtf8Bytes(candidate.ExportState());
            if (payload.Length > MaximumFrameBytes) throw new InvalidOperationException("Work-item journal frame exceeds the configured resource bound.");
            await AppendFrameAsync(payload, cancellationToken).ConfigureAwait(false);
            _inner.ImportState(candidate.ExportState());
            return result;
        }
        finally
        {
            _commitGate.Release();
        }
    }

    private async Task AppendFrameAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var frameStart = _stream.Length;
        _stream.Position = frameStart;
        var checksum = Convert.ToHexStringLower(SHA256.HashData(payload));
        var header = Encoding.ASCII.GetBytes($"{Magic} {payload.Length} {checksum}\n");
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            _stream.Flush(flushToDisk: true);
        }
        catch
        {
            _stream.SetLength(frameStart);
            _stream.Flush(flushToDisk: true);
            throw;
        }
    }

    private void Recover()
    {
        _stream.Position = 0;
        WorkItemJournalState? latest = null;
        while (_stream.Position < _stream.Length)
        {
            var frameStart = _stream.Position;
            var header = ReadLine();
            if (header is null)
            {
                TruncateIncomplete(frameStart);
                break;
            }

            var fields = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3 || fields[0] != Magic || !int.TryParse(fields[1], out var length) || length < 0 || length > MaximumFrameBytes)
                throw new InvalidDataException("Unknown or malformed work-item journal frame header.");
            if (_stream.Length - _stream.Position < length + 1L)
            {
                TruncateIncomplete(frameStart);
                break;
            }

            var payload = new byte[length];
            _stream.ReadExactly(payload);
            if (_stream.ReadByte() != (byte)'\n') throw new InvalidDataException("Work-item journal frame terminator is corrupt.");
            var checksum = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (!StringComparer.Ordinal.Equals(checksum, fields[2])) throw new InvalidDataException("Work-item journal frame checksum is corrupt.");
            latest = JsonSerializer.Deserialize<WorkItemJournalState>(payload)
                ?? throw new InvalidDataException("Work-item journal frame payload is empty.");
        }

        if (latest is not null) _inner.ImportState(latest);
        _stream.Position = _stream.Length;
    }

    private string? ReadLine()
    {
        var bytes = new List<byte>();
        while (_stream.Position < _stream.Length)
        {
            var value = _stream.ReadByte();
            if (value == (byte)'\n') return Encoding.ASCII.GetString([.. bytes]);
            bytes.Add((byte)value);
            if (bytes.Count > 256) throw new InvalidDataException("Work-item journal header exceeds its resource bound.");
        }
        return null;
    }

    private void TruncateIncomplete(long frameStart)
    {
        _stream.SetLength(frameStart);
        _stream.Flush(flushToDisk: true);
    }
}
