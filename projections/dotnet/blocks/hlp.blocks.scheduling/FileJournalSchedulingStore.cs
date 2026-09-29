using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>Options for the single-process scheduling journal adapter.</summary>
public sealed class FileJournalSchedulingStoreOptions
{
    public string JournalPath { get; set; } = string.Empty;
    public int MaximumFrameBytes { get; set; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(JournalPath);
        if (!Path.IsPathRooted(JournalPath))
            throw new ArgumentException("The scheduling journal path must be absolute.", nameof(JournalPath));
        if (MaximumFrameBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes));
    }
}

/// <summary>
/// Single-process durable scheduling store. It holds an exclusive sibling lock for its lifetime.
/// Every mutation is one versioned, authenticated-header, checksummed append-only frame, flushed
/// before indexes change. This adapter does not claim multi-process or multi-node coordination.
/// </summary>
public sealed class FileJournalSchedulingStore : IDisposable
{
    private const uint Magic = 0x4A534C48; // HLSJ, little endian.
    private const byte FormatVersion = 1;
    private const int HeaderBytes = 12;
    private const int DigestBytes = 32;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, SchedulingCalendarEntity> calendars = new(StringComparer.Ordinal);
    private readonly FileJournalSchedulingStoreOptions options;
    private readonly FileStream processLock;
    private readonly FileStream journal;
    private bool disposed;

    public FileJournalSchedulingStore(FileJournalSchedulingStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        var directory = Path.GetDirectoryName(options.JournalPath)!;
        Directory.CreateDirectory(directory);
        processLock = new FileStream(options.JournalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            journal = new FileStream(options.JournalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            Replay();
            journal.Position = journal.Length;
        }
        catch
        {
            processLock.Dispose();
            throw;
        }
    }

    public async Task SaveCalendarEntityAsync(SchedulingCalendarEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ValidateText(entity.TenantId); ValidateText(entity.EntityId); ValidateText(entity.PayloadJson);
        if (!Enum.IsDefined(entity.Kind)) throw new ArgumentOutOfRangeException(nameof(entity));
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AppendAsync(RecordType.CalendarUpserted, entity, cancellationToken).ConfigureAwait(false);
            calendars[CalendarKey(entity.TenantId, entity.Kind, entity.EntityId)] = entity;
        }
        finally { gate.Release(); }
    }

    public async Task<SchedulingCalendarEntity?> GetCalendarEntityAsync(string tenantId,
        SchedulingCalendarEntityKind kind, string entityId, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { return calendars.GetValueOrDefault(CalendarKey(tenantId, kind, entityId)); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<SchedulingCalendarEntity>> ListCalendarEntitiesAsync(string tenantId,
        SchedulingCalendarEntityKind kind, CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return calendars.Values.Where(x => x.TenantId == tenantId && x.Kind == kind)
                .OrderBy(x => x.EntityId, StringComparer.Ordinal).ToArray();
        }
        finally { gate.Release(); }
    }

    public long JournalLength => journal.Length;

    private async ValueTask AppendAsync<T>(RecordType type, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (payload.Length > options.MaximumFrameBytes) throw new InvalidOperationException("Scheduling journal frame exceeds its configured bound.");
        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        header[4] = FormatVersion; header[5] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
        var headerDigest = SHA256.HashData(header);
        var framed = new byte[header.Length + payload.Length];
        header.CopyTo(framed, 0); payload.CopyTo(framed, header.Length);
        var frameDigest = SHA256.HashData(framed);
        var start = journal.Length;
        journal.Position = start;
        try
        {
            await journal.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await journal.WriteAsync(headerDigest, cancellationToken).ConfigureAwait(false);
            await journal.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await journal.WriteAsync(frameDigest, cancellationToken).ConfigureAwait(false);
            journal.Flush(flushToDisk: true);
        }
        catch
        {
            try { journal.SetLength(start); journal.Flush(flushToDisk: true); } catch { }
            throw;
        }
    }

    private void Replay()
    {
        journal.Position = 0;
        while (journal.Position < journal.Length)
        {
            var start = journal.Position;
            if (journal.Length - start < HeaderBytes) { Truncate(start); return; }
            var header = new byte[HeaderBytes]; journal.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != Magic) throw new InvalidDataException("Scheduling journal frame marker is invalid.");
            if (header[4] != FormatVersion) throw new InvalidDataException("Scheduling journal version is unsupported.");
            if (journal.Length - journal.Position < DigestBytes) { Truncate(start); return; }
            var headerDigest = new byte[DigestBytes]; journal.ReadExactly(headerDigest);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(header), headerDigest)) throw new InvalidDataException("Scheduling journal header checksum is invalid.");
            var type = (RecordType)header[5];
            if (!Enum.IsDefined(type)) throw new InvalidDataException("Scheduling journal record type is unsupported.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            if (length < 0 || length > options.MaximumFrameBytes) throw new InvalidDataException("Scheduling journal frame length is invalid.");
            if (journal.Length - journal.Position < length + DigestBytes) { Truncate(start); return; }
            var payload = new byte[length]; var digest = new byte[DigestBytes];
            journal.ReadExactly(payload); journal.ReadExactly(digest);
            var framed = new byte[HeaderBytes + length]; header.CopyTo(framed, 0); payload.CopyTo(framed, HeaderBytes);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(framed), digest)) throw new InvalidDataException("Scheduling journal frame checksum is invalid.");
            try { Apply(type, payload); }
            catch (Exception error) when (error is not InvalidDataException) { throw new InvalidDataException("Scheduling journal payload is invalid.", error); }
        }
    }

    private void Apply(RecordType type, byte[] payload)
    {
        switch (type)
        {
            // DES-0022 ruling 8 retired L491's integer revisions. Scheduling definitions live in the shared
            // versioned-definition store; a journal still holding a draft frame is refused, never replayed.
            case RecordType.RetiredIntegerRevisionDraft:
                throw new InvalidDataException("Scheduling journal holds a retired integer-revision definition draft; "
                    + "move it to the shared versioned-definition store before opening this journal.");
            case RecordType.CalendarUpserted:
                var entity = Deserialize<SchedulingCalendarEntity>(payload);
                calendars[CalendarKey(entity.TenantId, entity.Kind, entity.EntityId)] = entity;
                break;
            default: throw new InvalidDataException("Scheduling journal record type is unsupported.");
        }
    }

    private void Truncate(long start) { journal.SetLength(start); journal.Position = start; journal.Flush(flushToDisk: true); }
    private static T Deserialize<T>(byte[] value) => JsonSerializer.Deserialize<T>(value) ?? throw new InvalidDataException("Scheduling journal payload is empty.");
    private static string CalendarKey(string tenantId, SchedulingCalendarEntityKind kind, string id) => tenantId + "\u001f" + (byte)kind + "\u001f" + id;
    private static void ValidateText(string value) => ArgumentException.ThrowIfNullOrWhiteSpace(value);
    private async ValueTask EnterAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(token).ConfigureAwait(false);
        if (!disposed) return;
        gate.Release(); throw new ObjectDisposedException(nameof(FileJournalSchedulingStore));
    }

    public void Dispose()
    {
        gate.Wait();
        try { if (disposed) return; disposed = true; journal.Dispose(); processLock.Dispose(); }
        finally { gate.Release(); }
        GC.SuppressFinalize(this);
    }

    // Value 1 stays reserved so a retired draft frame is recognised and refused rather than misread.
    private enum RecordType : byte { RetiredIntegerRevisionDraft = 1, CalendarUpserted = 2 }
}
