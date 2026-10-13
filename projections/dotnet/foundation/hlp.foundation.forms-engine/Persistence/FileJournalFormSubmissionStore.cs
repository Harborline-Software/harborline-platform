using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;
using Harborline.Kernel.Core;

namespace Harborline.Foundation.Forms.Engine.Persistence;

/// <summary>Configures the absolute journal path and maximum durable frame size.</summary>
public sealed class FileJournalFormSubmissionStoreOptions
{
    /// <summary>Defines the absolute path of the append-only submission journal.</summary>
    public string JournalPath { get; set; } = string.Empty;
    /// <summary>Defines the maximum serialized frame size accepted by the journal.</summary>
    public int MaximumFrameBytes { get; set; } = 16 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(JournalPath);
        if (!Path.IsPathRooted(JournalPath))
            throw new ArgumentException("The Forms submission journal path must be absolute.", nameof(JournalPath));
        if (MaximumFrameBytes is < 1024 or > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(MaximumFrameBytes));
    }
}

/// <summary>
/// Single-process durable Forms submission store backed by a checksummed append-only journal.
/// One exclusive lock is held for the adapter lifetime; multi-node leasing is intentionally not claimed.
/// </summary>
public sealed class FileJournalFormSubmissionStore : IFormSubmissionTransactionStore, IFormAcceptedCommandStore, IDisposable
{
    private const uint Magic = 0x4A464C48; // HLFJ in little-endian byte order.
    // Format 2 adds the D3 binding members (schema reference and record type target) to the submission and projection
    // rows. Format 1 frames still load, with those members absent; a format-1-only reader refuses a format 2 frame
    // instead of silently dropping the binding.
    private const byte FormatVersion = 2;
    private const byte LegacyFormatVersion = 1;
    // Format 3 (ADR-0110, plan W6) carries an accepted Records command: a commit frame whose projection row holds the
    // command and whose audit holds the acceptance authority snapshot, and the claim, cancel and refuse frames of its
    // lifecycle. Only those frames are written as format 3, so a journal with no accepted command stays readable by a
    // format-2 reader, and a format-2 reader refuses a journal that holds one instead of silently dropping the command or
    // its fence. Revision 16 also adds the binding-failure, quarantine and abandonment frames (review R2-M1, R2-L1) and
    // the resolution frame (review R3-L1), all format 3 only; format 3 is unreleased, so no released reader meets them.
    private const byte CommandFormatVersion = 3;
    private const int HeaderBytes = 12;
    private const int HeaderChecksumBytes = 32;
    private const int ChecksumBytes = 32;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<(TenantId Tenant, string Instance), FormSubmissionRecord> _submissions = new();
    private readonly Dictionary<(TenantId Tenant, string Form, string Key), FormSubmissionCommit> _idempotency = new();
    private readonly Dictionary<string, FormMutationAuditEnvelope> _audits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FormProjectionEnvelope> _pending = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _leased = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (TenantId Tenant, FormAcceptedRecordCommand Command)> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EntityId> _commandInstances = new(StringComparer.Ordinal);
    // Review R2-L4: each pending row's queue position, issued in frame order, so replay rebuilds the same lease order.
    private readonly Dictionary<string, long> _queuePositions = new(StringComparer.Ordinal);
    private long _queueSequence;
    private readonly FileJournalFormSubmissionStoreOptions _options;
    private readonly FileStream _lock;
    private readonly FileStream _journal;
    private bool _disposed;

    /// <summary>Opens or creates the configured journal and rebuilds its indexes.</summary>
    public FileJournalFormSubmissionStore(FileJournalFormSubmissionStoreOptions options)
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
        return new Transaction(this);
    }

    /// <inheritdoc />
    public async ValueTask<FormSubmissionRecord?> GetAsync(
        TenantId tenant,
        EntityId instanceId,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { return _submissions.TryGetValue((tenant, instanceId.ToString()), out var value) ? FormSubmissionStoreModel.Clone(value) : null; }
        finally { _gate.Release(); }
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
            var rows = FormSubmissionStoreModel.LeaseOrder(_pending.Values
                .Where(row => !_leased.Contains(row.OutboxId) && FormSubmissionStoreModel.LeaseAdmits(row, kinds)), _queuePositions, maximum);
            foreach (var row in rows) _leased.Add(row.OutboxId);
            return rows.Select(FormSubmissionStoreModel.Clone).ToArray();
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormProjectionEnvelope?> LeasePendingProjectionAsync(TenantId tenant, string outboxId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_pending.TryGetValue(outboxId, out var row) || row.Tenant != tenant || row.AcceptedCommand is not null || !_leased.Add(outboxId))
                return null;
            return FormSubmissionStoreModel.Clone(row);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseProjectionLeaseAsync(
        string outboxId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { _leased.Remove(outboxId); }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask CompleteProjectionAsync(
        string outboxId,
        IReadOnlyList<FormProjectionSkip> skips,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        ArgumentNullException.ThrowIfNull(skips);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Review R3-L1: only a claimed command completes here; a quarantined one only through its audited resolution.
            if (_pending.TryGetValue(outboxId, out var row) && row.AcceptedCommand is { State: not FormAcceptedCommandState.Claimed })
                throw new InvalidOperationException("An accepted Records command completes only after its execution claim.");
            _leased.Remove(outboxId);
            if (!_pending.ContainsKey(outboxId)) return;
            var snapshot = skips.Select(skip => new FormProjectionSkip(skip.Reason, skip.FieldPointer, skip.Target)).ToArray();
            await AppendAsync(JournalRecordType.ProjectionCompleted, new CompletionDto(outboxId, snapshot.Select(ToDto).ToArray()), cancellationToken).ConfigureAwait(false);
            ApplyCompletion(outboxId, snapshot);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask RetryProjectionAsync(
        string outboxId,
        string stableErrorCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stableErrorCode);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _leased.Remove(outboxId);
            if (!_pending.ContainsKey(outboxId)) return;
            if (_pending[outboxId].Attempts == int.MaxValue)
                throw new InvalidOperationException("The Forms projection retry count is exhausted.");
            await AppendAsync(JournalRecordType.ProjectionRetried, new RetryDto(outboxId, stableErrorCode), cancellationToken).ConfigureAwait(false);
            ApplyRetry(outboxId, stableErrorCode);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedRecordCommand?> GetAcceptedCommandAsync(TenantId tenant, string commandId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _commands.TryGetValue(commandId, out var entry) && entry.Tenant == tenant
                ? FormSubmissionStoreModel.Clone(entry.Command)
                : null;
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> ClaimAcceptedCommandAsync(string commandId, string workerId, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Pending) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            // The claim is durable before it is visible and before any effect: the fence a cancel contends on.
            await AppendAsync(JournalRecordType.CommandClaimed, new ClaimDto(commandId, workerId, Format(at)), cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyClaim(commandId, workerId, at), true);
        }
        finally { _gate.Release(); }
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
            if (!_commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Pending) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            // Review M1: the cancellation and its audit are one durable frame.
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: true);
            await AppendAsync(JournalRecordType.CommandCancelled, new CancelDto(commandId, tenant.Value, ToDto(envelope)),
                cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyCancel(commandId, envelope), true);
        }
        finally { _gate.Release(); }
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
            if (!_commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != expectedState) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            await AppendAsync(JournalRecordType.CommandRefused, new RefuseDto(commandId, (int)expectedState, refusalCode, ToDto(envelope)),
                cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyRefuse(commandId, refusalCode, envelope), true);
        }
        finally { _gate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<FormAcceptedCommandTransition?> RecordBindingFailureAsync(string commandId, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Claimed || !_pending.TryGetValue(commandId, out var row))
                return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            if (row.Attempts == int.MaxValue || entry.Command.BindingFailures == int.MaxValue)
                throw new InvalidOperationException("The Forms projection retry count is exhausted.");
            // Review R2-L1: the binding failure and its attempt are one durable frame.
            await AppendAsync(JournalRecordType.CommandBindingFailed, new BindingFailureDto(commandId, code), cancellationToken, CommandFormatVersion)
                .ConfigureAwait(false);
            return new(ApplyBindingFailure(commandId, code), true);
        }
        finally { _gate.Release(); }
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
            if (!_commands.TryGetValue(commandId, out var entry)) return null;
            if (entry.Command.State != FormAcceptedCommandState.Claimed) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            // Review R2-M1: the quarantine and its audit are one durable frame.
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            await AppendAsync(JournalRecordType.CommandQuarantined, new QuarantineDto(commandId, code, ToDto(envelope)),
                cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyQuarantine(commandId, code, envelope), true);
        }
        finally { _gate.Release(); }
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
            if (!_commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Quarantined) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: true);
            await AppendAsync(JournalRecordType.CommandAbandoned, new CancelDto(commandId, tenant.Value, ToDto(envelope)),
                cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyCancel(commandId, envelope), true);
        }
        finally { _gate.Release(); }
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
            if (!_commands.TryGetValue(commandId, out var entry) || entry.Tenant != tenant) return null;
            if (entry.Command.State != FormAcceptedCommandState.Quarantined) return new(FormSubmissionStoreModel.Clone(entry.Command), false);
            // Review R3-L1: the resolution and its audit are one durable frame.
            var envelope = AuditOf(commandId, entry.Tenant, audit, snapshotRequired: false);
            await AppendAsync(JournalRecordType.CommandResolved, new CancelDto(commandId, tenant.Value, ToDto(envelope)),
                cancellationToken, CommandFormatVersion).ConfigureAwait(false);
            return new(ApplyResolve(commandId, envelope), true);
        }
        finally { _gate.Release(); }
    }

    // The lifecycle transition's audit envelope, validated before anything is appended (review M1).
    private FormMutationAuditEnvelope AuditOf(string commandId, TenantId tenant, KernelAuditEvidence audit, bool snapshotRequired)
    {
        var envelope = FormSubmissionStoreModel.LifecycleAudit(audit, _commandInstances[commandId], tenant, snapshotRequired);
        if (_audits.ContainsKey(envelope.AuditId)) throw new ArgumentException("Audit identities must be unique.", nameof(audit));
        return envelope;
    }

    internal async ValueTask<(int Submissions, int Audits, int Pending, int Completed)> CountsAsync()
    {
        await EnterAsync(default).ConfigureAwait(false);
        try { return (_submissions.Count, _audits.Count, _pending.Count, _completed.Count); }
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

    private async ValueTask EnterAsync(CancellationToken cancellationToken)
    {
        // Review M5: never re-entered from the flow that holds this store's scope; that fails at once instead of waiting.
        FormStoreReentrancy.ThrowIfHeld();
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_disposed) return;
        _gate.Release();
        throw new ObjectDisposedException(nameof(FileJournalFormSubmissionStore));
    }

    private sealed class Transaction(FileJournalFormSubmissionStore store) : IFormSubmissionTransactionScope
    {
        private bool _commitAttempted;
        private bool _released;

        /// <inheritdoc />
        public async ValueTask<FormSubmissionCommitResult> CommitAsync(FormSubmissionCommit commit, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (_commitAttempted) throw new InvalidOperationException("A Forms submission scope can commit only once.");
            _commitAttempted = true;
            FormSubmissionStoreModel.ValidateAtomicEnvelope(commit);
            var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);
            if (store._idempotency.TryGetValue(key, out var existing))
                return string.Equals(existing.Submission.RequestFingerprint, commit.Submission.RequestFingerprint, StringComparison.Ordinal)
                    ? new(FormSubmissionCommitDisposition.Replayed, existing.Receipt, FormSubmissionStoreModel.Clone(existing))
                    : new(FormSubmissionCommitDisposition.Conflict, null);

            var snapshot = FormSubmissionStoreModel.Clone(commit);
            store.EnsureCommitCanApply(snapshot);
            var version = snapshot.Projection.AcceptedCommand is not null || !snapshot.Audit.AuthoritySnapshot.IsEmpty
                ? CommandFormatVersion : FormatVersion;
            await store.AppendAsync(JournalRecordType.Commit, ToDto(snapshot), cancellationToken, version).ConfigureAwait(false);
            store.ApplyCommit(snapshot, lease: true);
            return new(FormSubmissionCommitDisposition.Created, snapshot.Receipt);
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
            store._gate.Release();
        }
    }

    private async ValueTask AppendAsync<T>(JournalRecordType type, T record, CancellationToken cancellationToken, byte version = FormatVersion)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToUtf8Bytes(record);
        if (payload.Length > _options.MaximumFrameBytes)
            throw new InvalidOperationException("The Forms submission journal record exceeds its configured bound.");
        var header = new byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), Magic);
        header[4] = version;
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
            await _journal.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _journal.WriteAsync(headerChecksum, cancellationToken).ConfigureAwait(false);
            await _journal.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _journal.WriteAsync(checksum, cancellationToken).ConfigureAwait(false);
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
            catch { }
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
                throw new InvalidDataException("The Forms submission journal contains an invalid frame marker.");
            if (header[4] is not (CommandFormatVersion or FormatVersion or LegacyFormatVersion))
                throw new InvalidDataException("The Forms submission journal version is unsupported.");
            if (_journal.Length - _journal.Position < HeaderChecksumBytes)
            {
                TruncateTail(start);
                return;
            }
            var headerChecksum = new byte[HeaderChecksumBytes];
            _journal.ReadExactly(headerChecksum);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(header), headerChecksum))
                throw new InvalidDataException("The Forms submission journal header checksum is invalid.");
            var type = (JournalRecordType)header[5];
            if (!Enum.IsDefined(type)) throw new InvalidDataException("The Forms submission journal record type is unsupported.");
            if (type is JournalRecordType.CommandClaimed or JournalRecordType.CommandCancelled or JournalRecordType.CommandRefused
                    or JournalRecordType.CommandQuarantined or JournalRecordType.CommandBindingFailed or JournalRecordType.CommandAbandoned
                    or JournalRecordType.CommandResolved
                && header[4] != CommandFormatVersion)
                throw new InvalidDataException("The Forms submission journal record type is unsupported.");
            var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
            if (payloadLength < 0 || payloadLength > _options.MaximumFrameBytes)
                throw new InvalidDataException("The Forms submission journal record length is invalid.");
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
                throw new InvalidDataException("The Forms submission journal checksum is invalid.");
            try { Apply(type, payload, header[4]); }
            catch (Exception exception) when (exception is not InvalidDataException)
            {
                throw new InvalidDataException("The Forms submission journal record is invalid.", exception);
            }
        }
    }

    private void Apply(JournalRecordType type, ReadOnlySpan<byte> payload, byte version)
    {
        switch (type)
        {
            case JournalRecordType.Commit:
                var committed = FromDto(Deserialize<CommitDto>(payload));
                // A commit that carries an accepted command or an acceptance snapshot is a format 3 frame, never older.
                if ((committed.Projection.AcceptedCommand is not null || !committed.Audit.AuthoritySnapshot.IsEmpty) && version != CommandFormatVersion)
                    throw new InvalidDataException("The Forms submission journal commit carries an accepted command outside format 3.");
                ApplyCommit(committed, lease: false);
                break;
            case JournalRecordType.ProjectionRetried:
                var retry = Deserialize<RetryDto>(payload);
                if (!_pending.ContainsKey(retry.OutboxId)) throw new InvalidDataException("Retry record names no pending projection.");
                ApplyRetry(retry.OutboxId, retry.StableErrorCode);
                break;
            case JournalRecordType.ProjectionCompleted:
                var completion = Deserialize<CompletionDto>(payload);
                if (!_pending.TryGetValue(completion.OutboxId, out var completed)) throw new InvalidDataException("Completion record names no pending projection.");
                if (completed.AcceptedCommand is { State: not FormAcceptedCommandState.Claimed })
                    throw new InvalidDataException("Completion record names an accepted command that was never claimed.");
                ApplyCompletion(completion.OutboxId, completion.Skips.Select(FromDto).ToArray());
                break;
            case JournalRecordType.CommandClaimed:
                var claim = Deserialize<ClaimDto>(payload);
                if (!_commands.TryGetValue(claim.CommandId, out var claimed) || claimed.Command.State != FormAcceptedCommandState.Pending)
                    throw new InvalidDataException("Claim record names no pending accepted command.");
                ApplyClaim(claim.CommandId, claim.WorkerId, Parse(claim.At));
                break;
            case JournalRecordType.CommandCancelled:
                var cancel = Deserialize<CancelDto>(payload);
                if (!_commands.TryGetValue(cancel.CommandId, out var cancelled) || cancelled.Command.State != FormAcceptedCommandState.Pending
                    || cancelled.Tenant != new TenantId(cancel.Tenant))
                    throw new InvalidDataException("Cancel record names no pending accepted command of its tenant.");
                var cancelAudit = FromDto(cancel.Audit);
                if (_audits.ContainsKey(cancelAudit.AuditId) || cancelAudit.AuthoritySnapshot.IsEmpty)
                    throw new InvalidDataException("Cancel record carries no new audited authority snapshot.");
                ApplyCancel(cancel.CommandId, cancelAudit);
                break;
            case JournalRecordType.CommandRefused:
                var refusal = Deserialize<RefuseDto>(payload);
                if (!_commands.TryGetValue(refusal.CommandId, out var refused)
                    || (int)refused.Command.State != refusal.ExpectedState
                    || refused.Command.State is not (FormAcceptedCommandState.Pending or FormAcceptedCommandState.Claimed))
                    throw new InvalidDataException("Refusal record names no open accepted command in its expected state.");
                var refusalAudit = FromDto(refusal.Audit);
                if (_audits.ContainsKey(refusalAudit.AuditId)) throw new InvalidDataException("Refusal record reuses an audit identity.");
                ApplyRefuse(refusal.CommandId, refusal.RefusalCode, refusalAudit);
                break;
            case JournalRecordType.CommandBindingFailed:
                var failure = Deserialize<BindingFailureDto>(payload);
                if (string.IsNullOrWhiteSpace(failure.Code) || !_commands.TryGetValue(failure.CommandId, out var failing)
                    || failing.Command.State != FormAcceptedCommandState.Claimed || !_pending.ContainsKey(failure.CommandId))
                    throw new InvalidDataException("Binding-failure record names no pending claimed accepted command.");
                ApplyBindingFailure(failure.CommandId, failure.Code);
                break;
            case JournalRecordType.CommandQuarantined:
                var quarantine = Deserialize<QuarantineDto>(payload);
                if (string.IsNullOrWhiteSpace(quarantine.Code) || !_commands.TryGetValue(quarantine.CommandId, out var quarantined)
                    || quarantined.Command.State != FormAcceptedCommandState.Claimed)
                    throw new InvalidDataException("Quarantine record names no claimed accepted command.");
                var quarantineAudit = FromDto(quarantine.Audit);
                if (_audits.ContainsKey(quarantineAudit.AuditId)) throw new InvalidDataException("Quarantine record reuses an audit identity.");
                ApplyQuarantine(quarantine.CommandId, quarantine.Code, quarantineAudit);
                break;
            case JournalRecordType.CommandAbandoned:
                var abandonment = Deserialize<CancelDto>(payload);
                if (!_commands.TryGetValue(abandonment.CommandId, out var abandoned) || abandoned.Command.State != FormAcceptedCommandState.Quarantined
                    || abandoned.Tenant != new TenantId(abandonment.Tenant))
                    throw new InvalidDataException("Abandonment record names no quarantined accepted command of its tenant.");
                var abandonAudit = FromDto(abandonment.Audit);
                if (_audits.ContainsKey(abandonAudit.AuditId) || abandonAudit.AuthoritySnapshot.IsEmpty)
                    throw new InvalidDataException("Abandonment record carries no new audited authority snapshot.");
                ApplyCancel(abandonment.CommandId, abandonAudit);
                break;
            case JournalRecordType.CommandResolved:
                var resolution = Deserialize<CancelDto>(payload);
                if (!_commands.TryGetValue(resolution.CommandId, out var resolving) || resolving.Command.State != FormAcceptedCommandState.Quarantined
                    || resolving.Tenant != new TenantId(resolution.Tenant))
                    throw new InvalidDataException("Resolution record names no quarantined accepted command of its tenant.");
                var resolveAudit = FromDto(resolution.Audit);
                if (_audits.ContainsKey(resolveAudit.AuditId)) throw new InvalidDataException("Resolution record reuses an audit identity.");
                ApplyResolve(resolution.CommandId, resolveAudit);
                break;
            default:
                throw new InvalidDataException("The Forms submission journal record type is unsupported.");
        }
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> payload) =>
        JsonSerializer.Deserialize<T>(payload) ?? throw new InvalidDataException("The Forms submission journal payload is empty.");

    private void ApplyCommit(FormSubmissionCommit commit, bool lease)
    {
        FormSubmissionStoreModel.ValidateAtomicEnvelope(commit);
        EnsureCommitCanApply(commit);
        var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);
        _submissions.Add((commit.Submission.Tenant, commit.Submission.InstanceId.ToString()), commit.Submission);
        _audits.Add(commit.Audit.AuditId, commit.Audit);
        _pending.Add(commit.Projection.OutboxId, commit.Projection);
        _queuePositions[commit.Projection.OutboxId] = ++_queueSequence;
        _idempotency.Add(key, commit);
        if (lease) _leased.Add(commit.Projection.OutboxId);
        if (commit.Projection.AcceptedCommand is { } command)
        {
            _commands.Add(command.CommandId, (commit.Submission.Tenant, command));
            _commandInstances.Add(command.CommandId, commit.Submission.InstanceId);
        }
    }

    private FormAcceptedRecordCommand ApplyClaim(string commandId, string workerId, DateTimeOffset at) =>
        ApplyTransition(commandId, command => command with
        {
            State = FormAcceptedCommandState.Claimed, StateActorId = workerId, StateChangedAt = at,
        }, settle: false);

    private FormAcceptedRecordCommand ApplyCancel(string commandId, FormMutationAuditEnvelope audit)
    {
        var next = ApplyTransition(commandId, command => command with
        {
            State = FormAcceptedCommandState.Cancelled, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt,
            StateSnapshot = audit.AuthoritySnapshot.ToArray(),
        }, settle: true);
        _audits.Add(audit.AuditId, audit);
        return next;
    }

    private FormAcceptedRecordCommand ApplyRefuse(string commandId, string refusalCode, FormMutationAuditEnvelope audit)
    {
        var next = ApplyTransition(commandId, command => command with
        {
            State = FormAcceptedCommandState.Refused, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt, RefusalCode = refusalCode,
        }, settle: true);
        _audits.Add(audit.AuditId, audit);
        return next;
    }

    private FormAcceptedRecordCommand ApplyBindingFailure(string commandId, string code)
    {
        var (tenant, current) = _commands[commandId];
        var next = current with { BindingFailures = checked(current.BindingFailures + 1) };
        _commands[commandId] = (tenant, next);
        var row = _pending[commandId];
        _pending[commandId] = row with { Attempts = checked(row.Attempts + 1), LastErrorCode = code, AcceptedCommand = next };
        _queuePositions[commandId] = ++_queueSequence;
        _leased.Remove(commandId);
        return FormSubmissionStoreModel.Clone(next);
    }

    private FormAcceptedRecordCommand ApplyResolve(string commandId, FormMutationAuditEnvelope audit)
    {
        var next = ApplyTransition(commandId, command => command with
        {
            State = FormAcceptedCommandState.Completed, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt,
        }, settle: true);
        _audits.Add(audit.AuditId, audit);
        return next;
    }

    private FormAcceptedRecordCommand ApplyQuarantine(string commandId, string code, FormMutationAuditEnvelope audit)
    {
        var next = ApplyTransition(commandId, command => command with
        {
            State = FormAcceptedCommandState.Quarantined, StateActorId = audit.ActorId, StateChangedAt = audit.RecordedAt, RefusalCode = code,
        }, settle: false);
        _leased.Remove(commandId);
        _audits.Add(audit.AuditId, audit);
        return next;
    }

    // One accepted-command transition applied to the indexes after its frame is durable: the command's new state on its row
    // and, for a terminal transition, the row leaves the pending pool and the stored receipt reports the outcome.
    private FormAcceptedRecordCommand ApplyTransition(string commandId, Func<FormAcceptedRecordCommand, FormAcceptedRecordCommand> transition, bool settle)
    {
        var (tenant, current) = _commands[commandId];
        var next = transition(current);
        _commands[commandId] = (tenant, next);
        if (_pending.TryGetValue(commandId, out var row))
        {
            if (settle)
            {
                _pending.Remove(commandId);
                _queuePositions.Remove(commandId);
                _leased.Remove(commandId);
                _completed.Add(commandId);
            }
            else
            {
                _pending[commandId] = row with { AcceptedCommand = next };
            }
        }
        if (settle)
        {
            foreach (var (key, commit) in _idempotency.ToArray())
            {
                if (!string.Equals(commit.Projection.OutboxId, commandId, StringComparison.Ordinal)) continue;
                _idempotency[key] = commit with
                {
                    Receipt = commit.Receipt with { ProjectionStatus = FormSubmissionStoreModel.ReceiptStatus(next.State) },
                };
                break;
            }
        }
        return FormSubmissionStoreModel.Clone(next);
    }

    private void EnsureCommitCanApply(FormSubmissionCommit commit)
    {
        var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);
        if (_idempotency.ContainsKey(key)
            || _submissions.ContainsKey((commit.Submission.Tenant, commit.Submission.InstanceId.ToString()))
            || _audits.ContainsKey(commit.Audit.AuditId)
            || _pending.ContainsKey(commit.Projection.OutboxId)
            || _completed.Contains(commit.Projection.OutboxId))
            throw new ArgumentException("Submission, audit, and outbox identities must be unique.", nameof(commit));
    }

    private void ApplyRetry(string outboxId, string stableErrorCode)
    {
        _leased.Remove(outboxId);
        var value = _pending[outboxId];
        _pending[outboxId] = value with { Attempts = checked(value.Attempts + 1), LastErrorCode = stableErrorCode };
        _queuePositions[outboxId] = ++_queueSequence;
    }

    private void ApplyCompletion(string outboxId, IReadOnlyList<FormProjectionSkip> skips)
    {
        _leased.Remove(outboxId);
        _pending.Remove(outboxId);
        _queuePositions.Remove(outboxId);
        _completed.Add(outboxId);
        if (_commands.TryGetValue(outboxId, out var entry))
            _commands[outboxId] = (entry.Tenant, entry.Command with { State = FormAcceptedCommandState.Completed });
        foreach (var (key, commit) in _idempotency.ToArray())
        {
            if (!string.Equals(commit.Projection.OutboxId, outboxId, StringComparison.Ordinal)) continue;
            _idempotency[key] = commit with
            {
                Receipt = commit.Receipt with
                {
                    ProjectionStatus = FormProjectionStatus.Complete,
                    ProjectionSkips = skips.ToArray(),
                },
            };
            break;
        }
    }

    private void TruncateTail(long start)
    {
        _journal.SetLength(start);
        _journal.Position = start;
        _journal.Flush(flushToDisk: true);
    }

    private static CommitDto ToDto(FormSubmissionCommit value) => new(
        value.IdempotencyKey,
        new(value.Submission.InstanceId.ToString(), value.Submission.Tenant.Value, value.Submission.PartyId.ToString("D"), value.Submission.ActorId,
            value.Submission.FormId.Value, value.Submission.DefinitionVersion.Major, value.Submission.DefinitionVersion.Minor,
            value.Submission.DefinitionVersion.Patch, value.Submission.RequestFingerprint, value.Submission.ProtectedAcceptedCandidate.ToArray(), Format(value.Submission.SubmittedAt),
            value.Submission.SchemaRef?.Value, value.Submission.RecordTypeId),
        new(value.Audit.AuditId, value.Audit.InstanceId.ToString(), value.Audit.Tenant.Value, value.Audit.ActorId, value.Audit.Payload.ToArray(), Format(value.Audit.RecordedAt),
            value.Audit.AuthoritySnapshot.IsEmpty ? null : value.Audit.AuthoritySnapshot.ToArray()),
        new(value.Projection.OutboxId, value.Projection.InstanceId.ToString(), value.Projection.Tenant.Value, value.Projection.PartyId.ToString("D"),
            value.Projection.ActorId, value.Projection.FormId.Value, value.Projection.DefinitionVersion.Major, value.Projection.DefinitionVersion.Minor,
            value.Projection.DefinitionVersion.Patch, value.Projection.CaseReference, value.Projection.ProtectedAcceptedValues.ToArray(),
            Format(value.Projection.SubmittedAt), value.Projection.Attempts, value.Projection.LastErrorCode,
            value.Projection.SchemaRef?.Value, value.Projection.RecordTypeId,
            value.Projection.AcceptedCommand is { } command ? ToDto(command) : null),
        new(value.Receipt.InstanceId.ToString(), Format(value.Receipt.SubmittedAt), (int)value.Receipt.ProjectionStatus, value.Receipt.ProjectionSkips.Select(ToDto).ToArray()));

    private static FormSubmissionCommit FromDto(CommitDto value)
    {
        var submission = new FormSubmissionRecord(EntityId.Parse(value.Submission.InstanceId), new(value.Submission.Tenant), Guid.Parse(value.Submission.PartyId),
            value.Submission.ActorId, new(value.Submission.FormId), new(value.Submission.VersionMajor, value.Submission.VersionMinor, value.Submission.VersionPatch),
            value.Submission.RequestFingerprint, value.Submission.ProtectedAcceptedCandidate, Parse(value.Submission.SubmittedAt),
            SchemaOrNull(value.Submission.SchemaRef), value.Submission.RecordTypeId);
        var audit = new FormMutationAuditEnvelope(value.Audit.AuditId, EntityId.Parse(value.Audit.InstanceId), new(value.Audit.Tenant),
            value.Audit.ActorId, value.Audit.Payload, Parse(value.Audit.RecordedAt))
        {
            AuthoritySnapshot = value.Audit.AuthoritySnapshot is { } authoritySnapshot ? authoritySnapshot : ReadOnlyMemory<byte>.Empty,
        };
        var projection = new FormProjectionEnvelope(value.Projection.OutboxId, EntityId.Parse(value.Projection.InstanceId), new(value.Projection.Tenant),
            Guid.Parse(value.Projection.PartyId), value.Projection.ActorId, new(value.Projection.FormId),
            new(value.Projection.VersionMajor, value.Projection.VersionMinor, value.Projection.VersionPatch), value.Projection.CaseReference,
            value.Projection.ProtectedAcceptedValues, Parse(value.Projection.SubmittedAt), value.Projection.Attempts, value.Projection.LastErrorCode,
            SchemaOrNull(value.Projection.SchemaRef), value.Projection.RecordTypeId)
        {
            AcceptedCommand = value.Projection.AcceptedCommand is { } command ? FromDto(command) : null,
        };
        if (!Enum.IsDefined((FormProjectionStatus)value.Receipt.ProjectionStatus))
            throw new InvalidDataException("The Forms submission receipt status is invalid.");
        var receipt = new FormSubmitReceipt(EntityId.Parse(value.Receipt.InstanceId), Parse(value.Receipt.SubmittedAt),
            (FormProjectionStatus)value.Receipt.ProjectionStatus, value.Receipt.Skips.Select(FromDto).ToArray());
        return new(value.IdempotencyKey, submission, audit, projection, receipt);
    }

    // SchemaId converts implicitly from string, so a bare `null` arm would bind to that conversion; return the nullable
    // explicitly instead.
    private static SchemaId? SchemaOrNull(string? value)
    {
        if (value is null) return default(SchemaId?);
        return new SchemaId(value);
    }
    private static SkipDto ToDto(FormProjectionSkip value) => new(value.Reason, value.FieldPointer, value.Target);
    private static AcceptedCommandDto ToDto(FormAcceptedRecordCommand value) => new(value.CommandId, value.OperationId, value.RecordTypeId,
        value.PayloadFingerprint, Format(value.AcceptedAt), value.AcceptanceSnapshot.ToArray(), value.AcceptanceAuditId,
        value.AcceptanceDecidedAt is { } decidedAt ? Format(decidedAt) : null, value.BodyDigestEncoding, value.BodyDigest);
    private static FormAcceptedRecordCommand FromDto(AcceptedCommandDto value) => new(value.CommandId, value.OperationId, value.RecordTypeId,
        value.PayloadFingerprint, Parse(value.AcceptedAt), value.AcceptanceSnapshot)
    {
        AcceptanceAuditId = value.AcceptanceAuditId,
        AcceptanceDecidedAt = value.AcceptanceDecidedAt is { } decidedAt ? Parse(decidedAt) : null,
        BodyDigestEncoding = value.BodyDigestEncoding,
        BodyDigest = value.BodyDigest,
    };
    private static AuditDto ToDto(FormMutationAuditEnvelope value) => new(value.AuditId, value.InstanceId.ToString(), value.Tenant.Value,
        value.ActorId, value.Payload.ToArray(), Format(value.RecordedAt), value.AuthoritySnapshot.IsEmpty ? null : value.AuthoritySnapshot.ToArray());
    private static FormMutationAuditEnvelope FromDto(AuditDto value) => new(value.AuditId, EntityId.Parse(value.InstanceId), new(value.Tenant),
        value.ActorId, value.Payload, Parse(value.RecordedAt))
    {
        AuthoritySnapshot = value.AuthoritySnapshot is { } authoritySnapshot ? authoritySnapshot : ReadOnlyMemory<byte>.Empty,
    };
    private static FormProjectionSkip FromDto(SkipDto value) => new(value.Reason, value.FieldPointer, value.Target);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private enum JournalRecordType : byte
    {
        Commit = 1, ProjectionRetried = 2, ProjectionCompleted = 3, CommandClaimed = 4, CommandCancelled = 5, CommandRefused = 6,
        CommandQuarantined = 7, CommandBindingFailed = 8, CommandAbandoned = 9, CommandResolved = 10,
    }
    private sealed record CommitDto(string IdempotencyKey, SubmissionDto Submission, AuditDto Audit, ProjectionDto Projection, ReceiptDto Receipt);
    private sealed record SubmissionDto(string InstanceId, string Tenant, string PartyId, string ActorId, string FormId, int VersionMajor, int VersionMinor, int VersionPatch, string RequestFingerprint, byte[] ProtectedAcceptedCandidate, string SubmittedAt, string? SchemaRef = null, string? RecordTypeId = null);
    // The format-3 members are omitted when absent, so a frame without an accepted command keeps its format-2 bytes.
    private sealed record AuditDto(string AuditId, string InstanceId, string Tenant, string ActorId, byte[] Payload, string RecordedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] byte[]? AuthoritySnapshot = null);
    private sealed record ProjectionDto(string OutboxId, string InstanceId, string Tenant, string PartyId, string ActorId, string FormId, int VersionMajor, int VersionMinor, int VersionPatch, string? CaseReference, byte[] ProtectedAcceptedValues, string SubmittedAt, int Attempts, string? LastErrorCode, string? SchemaRef = null, string? RecordTypeId = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AcceptedCommandDto? AcceptedCommand = null);
    private sealed record AcceptedCommandDto(string CommandId, string OperationId, string RecordTypeId, string PayloadFingerprint, string AcceptedAt, byte[] AcceptanceSnapshot,
        string? AcceptanceAuditId = null, string? AcceptanceDecidedAt = null, string? BodyDigestEncoding = null, string? BodyDigest = null);
    private sealed record ClaimDto(string CommandId, string WorkerId, string At);
    private sealed record CancelDto(string CommandId, string Tenant, AuditDto Audit);
    private sealed record RefuseDto(string CommandId, int ExpectedState, string RefusalCode, AuditDto Audit);
    private sealed record QuarantineDto(string CommandId, string Code, AuditDto Audit);
    private sealed record BindingFailureDto(string CommandId, string Code);
    private sealed record ReceiptDto(string InstanceId, string SubmittedAt, int ProjectionStatus, SkipDto[] Skips);
    private sealed record SkipDto(string Reason, string FieldPointer, string? Target);
    private sealed record RetryDto(string OutboxId, string StableErrorCode);
    private sealed record CompletionDto(string OutboxId, SkipDto[] Skips);
}
