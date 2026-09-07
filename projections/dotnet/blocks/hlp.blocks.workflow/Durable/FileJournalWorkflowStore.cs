using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

namespace Harborline.Blocks.Workflow.Durable;

/// <summary>Options for the file-journal durable workflow store.</summary>
public sealed class FileJournalWorkflowStoreOptions
{
    /// <summary>Absolute path of the journal file. A sibling <c>.lock</c> file is created next to it.</summary>
    public string JournalPath { get; set; } = string.Empty;

    /// <summary>Maximum bytes a single journal frame payload may occupy (1 KiB … 64 MiB).</summary>
    public int MaximumFrameBytes { get; set; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(JournalPath);
        if (!Path.IsPathRooted(JournalPath))
            throw new ArgumentException("The workflow journal path must be absolute.", nameof(JournalPath));
        if (MaximumFrameBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes));
    }
}

/// <summary>
/// The unit-of-work handle a <see cref="WorkflowEffect"/> stages onto during a file-journal
/// atomic advance. The staged payloads ride the SAME journal frame as the outcome event, the
/// idempotency row, and the instance position — a staging failure aborts the whole advance
/// before anything is appended (the atomic-advance contract, build invariant #1).
/// </summary>
public sealed class FileJournalWorkflowUnitOfWork
{
    private readonly List<string> _payloads = new();

    /// <summary>Stages one opaque effect payload to co-commit with the advance.</summary>
    public void StageEffectPayload(string payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _payloads.Add(payload);
    }

    internal IReadOnlyList<string> Staged => _payloads;
}

/// <summary>
/// Single-process durable <see cref="IWorkflowStore"/> backed by a checksummed append-only
/// journal — the Harborline replacement for the earlier source EF/SQLite <c>NodeEfWorkflowStore</c>
/// at the narrowed durable-process seam, following the Forms file-journal precedent.
/// One exclusive lock is held for the adapter lifetime; multi-node leasing is intentionally
/// not claimed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomic advance.</b> <see cref="AdvanceAsync"/> first invokes the optional effect's
/// staging closure with a <see cref="FileJournalWorkflowUnitOfWork"/>; a staging throw aborts
/// with nothing appended. It then appends ONE frame carrying {staged effect payloads, outcome
/// event with the next per-instance monotonic Seq, the (instance, iteration, step)
/// idempotency row, the instance position/status} and flushes it to durable storage BEFORE
/// updating the in-memory indexes. A crash inside the window leaves the journal exactly as
/// before — the resume finds no idempotency row and re-runs cleanly with no double-effect.
/// </para>
/// <para>
/// <b>Recovery.</b> Construction replays the journal: an authenticated incomplete final frame
/// is truncated; a corrupt complete frame (bad marker, version, checksum, or length) fails
/// startup with <see cref="InvalidDataException"/> — fail-closed, never a silent skip.
/// </para>
/// </remarks>
public sealed class FileJournalWorkflowStore : IWorkflowStore, IDisposable
{
    private const uint Magic = 0x4A574C48; // HLWJ in little-endian byte order.
    private const byte FormatVersion = 1;
    private const int HeaderBytes = 12;
    private const int HeaderChecksumBytes = 32;
    private const int ChecksumBytes = 32;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, WorkflowInstanceRecord> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkflowStepIdempotencyRecord> _steps = new(StringComparer.Ordinal);
    private readonly List<WorkflowEventRecord> _events = new();
    private readonly Dictionary<string, List<string>> _effects = new(StringComparer.Ordinal);
    private readonly FileJournalWorkflowStoreOptions _options;
    private readonly FileStream _lock;
    private readonly FileStream _journal;
    private bool _disposed;

    /// <summary>Opens (or creates) the journal, replays it, and takes the exclusive process lock.</summary>
    public FileJournalWorkflowStore(FileJournalWorkflowStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = new() { JournalPath = Path.GetFullPath(options.JournalPath), MaximumFrameBytes = options.MaximumFrameBytes };
        var directory = Path.GetDirectoryName(_options.JournalPath)!;
        Directory.CreateDirectory(directory);
        try
        {
            _lock = new FileStream(_options.JournalPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _journal = new FileStream(
                _options.JournalPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                4096,
                FileOptions.WriteThrough);
            Replay();
            _journal.Position = _journal.Length;
        }
        catch
        {
            _journal?.Dispose();
            _lock?.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowInstanceRecord?> LoadAsync(string instanceId, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try { return _instances.TryGetValue(instanceId, out var record) ? Clone(record) : null; }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task CreateInstanceAsync(WorkflowInstanceRecord instance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            if (_instances.ContainsKey(instance.Id))
                throw new InvalidOperationException($"A workflow instance '{instance.Id}' already exists (instances are create-once).");
            await AppendAsync(JournalRecordType.InstanceCreated, ToDto(instance), ct).ConfigureAwait(false);
            _instances.Add(instance.Id, Clone(instance));
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task<WorkflowStepIdempotencyRecord?> FindStepResultAsync(WorkflowStepKey key, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try { return _steps.TryGetValue(key.Value, out var record) ? record : null; }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        WorkflowStepKey key, WorkflowEffect? effect, string resultJson, string eventType,
        string eventDataJson, string nextStep, WorkflowStatus nextStatus, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_instances.TryGetValue(key.InstanceId, out var instance))
                throw new KeyNotFoundException($"No workflow instance '{key.InstanceId}' exists.");
            if (_steps.ContainsKey(key.Value))
                throw new InvalidOperationException($"Workflow step '{key.Value}' already advanced (idempotency row exists).");

            // Stage the effect BEFORE anything is appended — a staging throw aborts the whole advance
            // and the journal is untouched (atomic advance, build invariant #1).
            var unitOfWork = new FileJournalWorkflowUnitOfWork();
            if (effect is not null)
            {
                await effect.StageAsync(unitOfWork, ct).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            var seq = NextSeq(key.InstanceId);
            var dto = new AdvanceDto(
                key.InstanceId, key.Iteration, key.Step, key.Value,
                unitOfWork.Staged.ToArray(), resultJson,
                new EventDto(key.InstanceId, seq, key.Step, eventType, eventDataJson, Format(now)),
                nextStep, (int)nextStatus, Format(now));
            await AppendAsync(JournalRecordType.Advanced, dto, ct).ConfigureAwait(false);
            ApplyAdvance(dto);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async Task ParkAsync(string instanceId, string step, string reasonJson, int iteration = 0, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_instances.ContainsKey(instanceId))
                throw new KeyNotFoundException($"No workflow instance '{instanceId}' exists.");
            var now = DateTimeOffset.UtcNow;
            var dto = new ParkDto(
                instanceId, step, iteration,
                new EventDto(instanceId, NextSeq(instanceId), step, "Parked", reasonJson, Format(now)),
                Format(now));
            await AppendAsync(JournalRecordType.Parked, dto, ct).ConfigureAwait(false);
            ApplyPark(dto);
        }
        finally { _gate.Release(); }
    }

    /// <summary>The append-only per-instance event log (monotonic Seq), for durable-history proofs.</summary>
    public async Task<IReadOnlyList<WorkflowEventRecord>> EventsAsync(string instanceId, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try
        {
            return _events.Where(e => e.InstanceId == instanceId).OrderBy(e => e.Seq).ToArray();
        }
        finally { _gate.Release(); }
    }

    /// <summary>The effect payloads committed for an instance (in commit order) — the exactly-once witness.</summary>
    public async Task<IReadOnlyList<string>> CommittedEffectPayloadsAsync(string instanceId, CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try { return _effects.TryGetValue(instanceId, out var rows) ? rows.ToArray() : Array.Empty<string>(); }
        finally { _gate.Release(); }
    }

    /// <summary>Store-level counts (instances, events, idempotency rows, committed effect payloads).</summary>
    public async Task<(int Instances, int Events, int IdempotencyRows, int Effects)> CountsAsync(CancellationToken ct = default)
    {
        await EnterAsync(ct).ConfigureAwait(false);
        try { return (_instances.Count, _events.Count, _steps.Count, _effects.Values.Sum(rows => rows.Count)); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            _journal.Dispose();
            _lock.Dispose();
        }
        finally { _gate.Release(); }
        GC.SuppressFinalize(this);
    }

    private async ValueTask EnterAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        if (!_disposed) return;
        _gate.Release();
        throw new ObjectDisposedException(nameof(FileJournalWorkflowStore));
    }

    private long NextSeq(string instanceId)
        => _events.Where(e => e.InstanceId == instanceId).Select(e => e.Seq).DefaultIfEmpty(0).Max() + 1;

    private async ValueTask AppendAsync<T>(JournalRecordType type, T record, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToUtf8Bytes(record);
        if (payload.Length > _options.MaximumFrameBytes)
            throw new InvalidOperationException("The workflow journal record exceeds its configured bound.");
        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        header[4] = FormatVersion;
        header[5] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), payload.Length);
        var headerChecksum = SHA256.HashData(header);
        var prefix = new byte[HeaderBytes + payload.Length];
        header.CopyTo(prefix, 0);
        payload.CopyTo(prefix, HeaderBytes);
        var checksum = SHA256.HashData(prefix);
        var start = _journal.Length;
        _journal.Position = start;
        try
        {
            await _journal.WriteAsync(header, ct).ConfigureAwait(false);
            await _journal.WriteAsync(headerChecksum, ct).ConfigureAwait(false);
            await _journal.WriteAsync(payload, ct).ConfigureAwait(false);
            await _journal.WriteAsync(checksum, ct).ConfigureAwait(false);
            _journal.Flush(flushToDisk: true);
        }
        catch
        {
            try
            {
                _journal.SetLength(start);
                _journal.Position = start;
                _journal.Flush(flushToDisk: true);
            }
            catch
            {
                // The rollback itself failed — the truncation-on-open path repairs the tail.
            }
            throw;
        }
    }

    private void Replay()
    {
        _journal.Position = 0;
        while (_journal.Position < _journal.Length)
        {
            var start = _journal.Position;
            if (_journal.Length - start < HeaderBytes)
            {
                TruncateTail(start);
                return;
            }

            var header = new byte[HeaderBytes];
            _journal.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)) != Magic)
                throw new InvalidDataException("The workflow journal contains an invalid frame marker.");
            if (header[4] != FormatVersion)
                throw new InvalidDataException("The workflow journal version is unsupported.");
            if (_journal.Length - _journal.Position < HeaderChecksumBytes)
            {
                TruncateTail(start);
                return;
            }
            var headerChecksum = new byte[HeaderChecksumBytes];
            _journal.ReadExactly(headerChecksum);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(header), headerChecksum))
                throw new InvalidDataException("The workflow journal header checksum is invalid.");
            var type = (JournalRecordType)header[5];
            if (!Enum.IsDefined(type)) throw new InvalidDataException("The workflow journal record type is unsupported.");
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
            if (payloadLength < 0 || payloadLength > _options.MaximumFrameBytes)
                throw new InvalidDataException("The workflow journal record length is invalid.");
            if (_journal.Length - _journal.Position < payloadLength + ChecksumBytes)
            {
                TruncateTail(start);
                return;
            }

            var payload = new byte[payloadLength];
            var checksum = new byte[ChecksumBytes];
            _journal.ReadExactly(payload);
            _journal.ReadExactly(checksum);
            var prefix = new byte[HeaderBytes + payloadLength];
            header.CopyTo(prefix, 0);
            payload.CopyTo(prefix, HeaderBytes);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(prefix), checksum))
                throw new InvalidDataException("The workflow journal checksum is invalid.");
            try { Apply(type, payload); }
            catch (Exception exception) when (exception is not InvalidDataException)
            {
                throw new InvalidDataException("The workflow journal record is invalid.", exception);
            }
        }
    }

    private void Apply(JournalRecordType type, ReadOnlySpan<byte> payload)
    {
        switch (type)
        {
            case JournalRecordType.InstanceCreated:
                var created = Deserialize<InstanceDto>(payload);
                if (_instances.ContainsKey(created.Id))
                    throw new InvalidDataException("Instance-created record duplicates an existing instance.");
                _instances.Add(created.Id, FromDto(created));
                break;
            case JournalRecordType.Advanced:
                ApplyAdvance(Deserialize<AdvanceDto>(payload));
                break;
            case JournalRecordType.Parked:
                ApplyPark(Deserialize<ParkDto>(payload));
                break;
            default:
                throw new InvalidDataException("The workflow journal record type is unsupported.");
        }
    }

    private void ApplyAdvance(AdvanceDto dto)
    {
        if (!_instances.TryGetValue(dto.InstanceId, out var instance))
            throw new InvalidDataException("Advance record names no existing instance.");
        if (_steps.ContainsKey(dto.Key))
            throw new InvalidDataException("Advance record duplicates an idempotency row.");
        if (!Enum.IsDefined((WorkflowStatus)dto.NextStatus))
            throw new InvalidDataException("Advance record carries an invalid status.");

        _events.Add(FromDto(dto.Event));
        _steps.Add(dto.Key, new WorkflowStepIdempotencyRecord
        {
            Key = dto.Key,
            InstanceId = dto.InstanceId,
            Step = dto.Step,
            Iteration = dto.Iteration,
            ResultJson = dto.ResultJson,
            CompletedAt = Parse(dto.At),
        });
        if (dto.EffectPayloads.Length > 0)
        {
            if (!_effects.TryGetValue(dto.InstanceId, out var rows))
            {
                rows = new List<string>();
                _effects.Add(dto.InstanceId, rows);
            }
            rows.AddRange(dto.EffectPayloads);
        }
        instance.CurrentStep = dto.NextStep;
        instance.Status = (WorkflowStatus)dto.NextStatus;
        instance.UpdatedAt = Parse(dto.At);
    }

    private void ApplyPark(ParkDto dto)
    {
        if (!_instances.TryGetValue(dto.InstanceId, out var instance))
            throw new InvalidDataException("Park record names no existing instance.");
        _events.Add(FromDto(dto.Event));
        instance.CurrentStep = dto.Step;
        instance.Status = WorkflowStatus.Parked;
        // The durable iteration counter (ADR 0135 A0): persisted with the park, read back verbatim
        // on resume — never recomputed (bug-1337 class).
        instance.Iteration = dto.Iteration;
        instance.UpdatedAt = Parse(dto.At);
    }

    private void TruncateTail(long start)
    {
        _journal.SetLength(start);
        _journal.Position = start;
        _journal.Flush(flushToDisk: true);
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("The workflow journal payload is empty.");

    private static WorkflowInstanceRecord Clone(WorkflowInstanceRecord source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        DefinitionKey = source.DefinitionKey,
        DefinitionVersion = source.DefinitionVersion,
        CurrentStep = source.CurrentStep,
        Iteration = source.Iteration,
        Status = source.Status,
        StateJson = source.StateJson,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt,
    };

    private static InstanceDto ToDto(WorkflowInstanceRecord value) => new(
        value.Id, value.TenantId, value.DefinitionKey, value.DefinitionVersion, value.CurrentStep,
        value.Iteration, (int)value.Status, value.StateJson, Format(value.CreatedAt), Format(value.UpdatedAt));

    private static WorkflowInstanceRecord FromDto(InstanceDto value)
    {
        if (!Enum.IsDefined((WorkflowStatus)value.Status))
            throw new InvalidDataException("Instance record carries an invalid status.");
        return new()
        {
            Id = value.Id,
            TenantId = value.TenantId,
            DefinitionKey = value.DefinitionKey,
            DefinitionVersion = value.DefinitionVersion,
            CurrentStep = value.CurrentStep,
            Iteration = value.Iteration,
            Status = (WorkflowStatus)value.Status,
            StateJson = value.StateJson,
            CreatedAt = Parse(value.CreatedAt),
            UpdatedAt = Parse(value.UpdatedAt),
        };
    }

    private static WorkflowEventRecord FromDto(EventDto value) => new()
    {
        InstanceId = value.InstanceId,
        Seq = value.Seq,
        Step = value.Step,
        EventType = value.EventType,
        DataJson = value.DataJson,
        OccurredAt = Parse(value.At),
    };

    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private enum JournalRecordType : byte { InstanceCreated = 1, Advanced = 2, Parked = 3 }
    private sealed record InstanceDto(string Id, string TenantId, string DefinitionKey, string DefinitionVersion, string CurrentStep, int Iteration, int Status, string StateJson, string CreatedAt, string UpdatedAt);
    private sealed record EventDto(string InstanceId, long Seq, string Step, string EventType, string DataJson, string At);
    private sealed record AdvanceDto(string InstanceId, int Iteration, string Step, string Key, string[] EffectPayloads, string ResultJson, EventDto Event, string NextStep, int NextStatus, string At);
    private sealed record ParkDto(string InstanceId, string Step, int Iteration, EventDto Event, string At);
}

/// <summary>DI registration for the durable file-journal workflow store.</summary>
public static class FileJournalWorkflowStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="FileJournalWorkflowStore"/> as the
    /// <see cref="IWorkflowStore"/>. Refuses layering over an already-registered store —
    /// an ambiguous durable store is a wiring error, not a silent last-wins.
    /// </summary>
    public static IServiceCollection AddFileJournalWorkflowStore(
        this IServiceCollection services, FileJournalWorkflowStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IWorkflowStore)))
            throw new InvalidOperationException(
                "An IWorkflowStore is already registered — the durable workflow store must be the sole store registration.");
        services.AddSingleton(sp => new FileJournalWorkflowStore(options));
        services.AddSingleton<IWorkflowStore>(sp => sp.GetRequiredService<FileJournalWorkflowStore>());
        return services;
    }
}
