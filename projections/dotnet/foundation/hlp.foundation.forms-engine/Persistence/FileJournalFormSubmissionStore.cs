using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Forms.Models;

namespace Harborline.Foundation.Forms.Engine.Persistence;

public sealed class FileJournalFormSubmissionStoreOptions
{
    public string JournalPath { get; set; } = string.Empty;
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
public sealed class FileJournalFormSubmissionStore : IFormSubmissionTransactionStore, IDisposable
{
    private const uint Magic = 0x4A464C48; // HLFJ in little-endian byte order.
    private const byte FormatVersion = 1;
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
    private readonly FileJournalFormSubmissionStoreOptions _options;
    private readonly FileStream _lock;
    private readonly FileStream _journal;
    private bool _disposed;

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

    public async ValueTask<FormSubmissionCommitResult> CommitAsync(
        FormSubmissionCommit commit,
        CancellationToken cancellationToken = default)
    {
        FormSubmissionStoreModel.ValidateAtomicEnvelope(commit);
        var key = (commit.Submission.Tenant, commit.Submission.FormId.Value, commit.IdempotencyKey);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_idempotency.TryGetValue(key, out var existing))
            {
                return string.Equals(existing.Submission.RequestFingerprint, commit.Submission.RequestFingerprint, StringComparison.Ordinal)
                    ? new(FormSubmissionCommitDisposition.Replayed, existing.Receipt)
                    : new(FormSubmissionCommitDisposition.Conflict, null);
            }

            var snapshot = FormSubmissionStoreModel.Clone(commit);
            EnsureCommitCanApply(snapshot);
            await AppendAsync(JournalRecordType.Commit, ToDto(snapshot), cancellationToken).ConfigureAwait(false);
            ApplyCommit(snapshot, lease: true);
            return new(FormSubmissionCommitDisposition.Created, snapshot.Receipt);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<FormSubmissionRecord?> GetAsync(
        TenantId tenant,
        EntityId instanceId,
        CancellationToken cancellationToken = default)
    {
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { return _submissions.TryGetValue((tenant, instanceId.ToString()), out var value) ? FormSubmissionStoreModel.Clone(value) : null; }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<FormProjectionEnvelope>> LeasePendingAsync(
        int maximum,
        CancellationToken cancellationToken = default)
    {
        if (maximum is <= 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(maximum));
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = _pending.Values.Where(row => !_leased.Contains(row.OutboxId))
                .OrderBy(row => row.SubmittedAt).ThenBy(row => row.OutboxId, StringComparer.Ordinal).Take(maximum).ToArray();
            foreach (var row in rows) _leased.Add(row.OutboxId);
            return rows.Select(FormSubmissionStoreModel.Clone).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask ReleaseProjectionLeaseAsync(
        string outboxId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outboxId);
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try { _leased.Remove(outboxId); }
        finally { _gate.Release(); }
    }

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
            _leased.Remove(outboxId);
            if (!_pending.ContainsKey(outboxId)) return;
            var snapshot = skips.Select(skip => new FormProjectionSkip(skip.Reason, skip.FieldPointer, skip.Target)).ToArray();
            await AppendAsync(JournalRecordType.ProjectionCompleted, new CompletionDto(outboxId, snapshot.Select(ToDto).ToArray()), cancellationToken).ConfigureAwait(false);
            ApplyCompletion(outboxId, snapshot);
        }
        finally { _gate.Release(); }
    }

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

    internal async ValueTask<(int Submissions, int Audits, int Pending, int Completed)> CountsAsync()
    {
        await EnterAsync(default).ConfigureAwait(false);
        try { return (_submissions.Count, _audits.Count, _pending.Count, _completed.Count); }
        finally { _gate.Release(); }
    }

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_disposed) return;
        _gate.Release();
        throw new ObjectDisposedException(nameof(FileJournalFormSubmissionStore));
    }

    private async ValueTask AppendAsync<T>(JournalRecordType type, T record, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var payload = JsonSerializer.SerializeToUtf8Bytes(record);
        if (payload.Length > _options.MaximumFrameBytes)
            throw new InvalidOperationException("The Forms submission journal record exceeds its configured bound.");
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
            if (header[4] != FormatVersion)
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
            try { Apply(type, payload); }
            catch (Exception exception) when (exception is not InvalidDataException)
            {
                throw new InvalidDataException("The Forms submission journal record is invalid.", exception);
            }
        }
    }

    private void Apply(JournalRecordType type, ReadOnlySpan<byte> payload)
    {
        switch (type)
        {
            case JournalRecordType.Commit:
                ApplyCommit(FromDto(Deserialize<CommitDto>(payload)), lease: false);
                break;
            case JournalRecordType.ProjectionRetried:
                var retry = Deserialize<RetryDto>(payload);
                if (!_pending.ContainsKey(retry.OutboxId)) throw new InvalidDataException("Retry record names no pending projection.");
                ApplyRetry(retry.OutboxId, retry.StableErrorCode);
                break;
            case JournalRecordType.ProjectionCompleted:
                var completion = Deserialize<CompletionDto>(payload);
                if (!_pending.ContainsKey(completion.OutboxId)) throw new InvalidDataException("Completion record names no pending projection.");
                ApplyCompletion(completion.OutboxId, completion.Skips.Select(FromDto).ToArray());
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
        _idempotency.Add(key, commit);
        if (lease) _leased.Add(commit.Projection.OutboxId);
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
    }

    private void ApplyCompletion(string outboxId, IReadOnlyList<FormProjectionSkip> skips)
    {
        _leased.Remove(outboxId);
        _pending.Remove(outboxId);
        _completed.Add(outboxId);
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
            value.Submission.DefinitionVersion.Patch, value.Submission.RequestFingerprint, value.Submission.ProtectedAcceptedCandidate.ToArray(), Format(value.Submission.SubmittedAt)),
        new(value.Audit.AuditId, value.Audit.InstanceId.ToString(), value.Audit.Tenant.Value, value.Audit.ActorId, value.Audit.Payload.ToArray(), Format(value.Audit.RecordedAt)),
        new(value.Projection.OutboxId, value.Projection.InstanceId.ToString(), value.Projection.Tenant.Value, value.Projection.PartyId.ToString("D"),
            value.Projection.ActorId, value.Projection.FormId.Value, value.Projection.DefinitionVersion.Major, value.Projection.DefinitionVersion.Minor,
            value.Projection.DefinitionVersion.Patch, value.Projection.CaseReference, value.Projection.ProtectedAcceptedValues.ToArray(),
            Format(value.Projection.SubmittedAt), value.Projection.Attempts, value.Projection.LastErrorCode),
        new(value.Receipt.InstanceId.ToString(), Format(value.Receipt.SubmittedAt), (int)value.Receipt.ProjectionStatus, value.Receipt.ProjectionSkips.Select(ToDto).ToArray()));

    private static FormSubmissionCommit FromDto(CommitDto value)
    {
        var submission = new FormSubmissionRecord(EntityId.Parse(value.Submission.InstanceId), new(value.Submission.Tenant), Guid.Parse(value.Submission.PartyId),
            value.Submission.ActorId, new(value.Submission.FormId), new(value.Submission.VersionMajor, value.Submission.VersionMinor, value.Submission.VersionPatch),
            value.Submission.RequestFingerprint, value.Submission.ProtectedAcceptedCandidate, Parse(value.Submission.SubmittedAt));
        var audit = new FormMutationAuditEnvelope(value.Audit.AuditId, EntityId.Parse(value.Audit.InstanceId), new(value.Audit.Tenant),
            value.Audit.ActorId, value.Audit.Payload, Parse(value.Audit.RecordedAt));
        var projection = new FormProjectionEnvelope(value.Projection.OutboxId, EntityId.Parse(value.Projection.InstanceId), new(value.Projection.Tenant),
            Guid.Parse(value.Projection.PartyId), value.Projection.ActorId, new(value.Projection.FormId),
            new(value.Projection.VersionMajor, value.Projection.VersionMinor, value.Projection.VersionPatch), value.Projection.CaseReference,
            value.Projection.ProtectedAcceptedValues, Parse(value.Projection.SubmittedAt), value.Projection.Attempts, value.Projection.LastErrorCode);
        if (!Enum.IsDefined((FormProjectionStatus)value.Receipt.ProjectionStatus))
            throw new InvalidDataException("The Forms submission receipt status is invalid.");
        var receipt = new FormSubmitReceipt(EntityId.Parse(value.Receipt.InstanceId), Parse(value.Receipt.SubmittedAt),
            (FormProjectionStatus)value.Receipt.ProjectionStatus, value.Receipt.Skips.Select(FromDto).ToArray());
        return new(value.IdempotencyKey, submission, audit, projection, receipt);
    }

    private static SkipDto ToDto(FormProjectionSkip value) => new(value.Reason, value.FieldPointer, value.Target);
    private static FormProjectionSkip FromDto(SkipDto value) => new(value.Reason, value.FieldPointer, value.Target);
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string value) => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private enum JournalRecordType : byte { Commit = 1, ProjectionRetried = 2, ProjectionCompleted = 3 }
    private sealed record CommitDto(string IdempotencyKey, SubmissionDto Submission, AuditDto Audit, ProjectionDto Projection, ReceiptDto Receipt);
    private sealed record SubmissionDto(string InstanceId, string Tenant, string PartyId, string ActorId, string FormId, int VersionMajor, int VersionMinor, int VersionPatch, string RequestFingerprint, byte[] ProtectedAcceptedCandidate, string SubmittedAt);
    private sealed record AuditDto(string AuditId, string InstanceId, string Tenant, string ActorId, byte[] Payload, string RecordedAt);
    private sealed record ProjectionDto(string OutboxId, string InstanceId, string Tenant, string PartyId, string ActorId, string FormId, int VersionMajor, int VersionMinor, int VersionPatch, string? CaseReference, byte[] ProtectedAcceptedValues, string SubmittedAt, int Attempts, string? LastErrorCode);
    private sealed record ReceiptDto(string InstanceId, string SubmittedAt, int ProjectionStatus, SkipDto[] Skips);
    private sealed record SkipDto(string Reason, string FieldPointer, string? Target);
    private sealed record RetryDto(string OutboxId, string StableErrorCode);
    private sealed record CompletionDto(string OutboxId, SkipDto[] Skips);
}
