using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>A file-backed Records transaction port and receipt reader, composed into no host.</summary>
/// <remarks>
/// Coordinates writers in this process only; cross-process locking awaits an owner decision.
/// Replacement is crash-safe, not power-loss-safe: the rename has no directory fsync on Unix or write-through on Windows.
/// A lost Created result followed by a retry could create a second record; BCL-only code cannot close this T-619 adapter gap.
/// A missing journal directory reads as empty and commit recreates it; this remains an owner decision.
/// External Windows handles can refuse replacement; the commit fails without changing the journal.
/// Constructor/open, receipt lookup and commit still load the whole journal without a byte cap.
/// Only current-row lookup is bounded to 16 MiB; a larger journal refuses as corrupt even if otherwise valid.
/// Current lookup remains an O(N) scan and neither refusal nor cancellation resets or rewrites the journal.
/// New commits write /2; legacy /1 reads/replays remain unchanged. Before conversion every old writer must be stopped,
/// drained and fenced from restart. Raw legacy entries retain their original provenance and receipts. The unkeyed
/// binding is integrity evidence, not authentication or valid-downgrade detection; no host cutover is performed here.
/// </remarks>
public sealed class FileJournalRecordWriteStore : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>, IRecordWriteReceiptReader, IRecordReadStore
{
    // /1 remains a read/replay format; every successful new commit writes /2.
    private const string JournalFormat = "records-write-file-journal/1";
    // Only the current-row read has this resource bound; the existing journal write format is unchanged.
    private const long MaxCurrentReadBytes = 16 * 1024 * 1024;
    private const int LegacyJournalMaxDepth = 64;
    private const int BoundJournalMaxDepth = 65;
    private static readonly SemaphoreSlim Mutation = new(1, 1);
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        Converters = { new SchemaConverter() },
    };
    private static readonly JsonSerializerOptions LegacyJson = LegacyOptions();

    private static JsonSerializerOptions LegacyOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(RecordWriteRow)) return;
            var property = info.Properties.Single(item => item.Name == nameof(RecordWriteRow.PublicationVersionId));
            info.Properties.Remove(property);
        });
        return new(Json) { TypeInfoResolver = resolver, MaxDepth = LegacyJournalMaxDepth };
    }
    private readonly string _path;
    private readonly Action<long>? _currentReadProgress;
    private readonly Action? _beforeReplace;

    /// <summary>Opens the journal, refusing corrupt content without resetting it.</summary>
    public FileJournalRecordWriteStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        Mutation.Wait();
        try { _ = Load(); }
        finally { Mutation.Release(); }
    }

    /// <summary>Observes positive actual async-read progress for isolated current-read verification.</summary>
    /// <param name="path">The same real journal path the public constructor opens.</param>
    /// <param name="currentReadProgress">Per-instance observation after positive current-read IO, before its token check.</param>
    internal FileJournalRecordWriteStore(string path, Action<long> currentReadProgress) : this(path)
    {
        _currentReadProgress = currentReadProgress ?? throw new ArgumentNullException(nameof(currentReadProgress));
    }

    internal FileJournalRecordWriteStore(string path, Action beforeReplace) : this(path)
    {
        _beforeReplace = beforeReplace ?? throw new ArgumentNullException(nameof(beforeReplace));
    }

    /// <inheritdoc />
    public ValueTask<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>> BeginAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>>(new Transaction(this));
    }

    /// <inheritdoc />
    public async ValueTask<RecordWriteCommittedOperation?> FindAsync(RecordWriteOperationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return Load(cancellationToken).SingleOrDefault(entry => entry.Key == key.IdempotencyKey)?.Stored(); }
        finally { Mutation.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadForCurrentReadAsync(cancellationToken).ConfigureAwait(false);
            RecordWriteRow? match = null;
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(entry.Row.Tenant, tenant, StringComparison.Ordinal)
                    || !string.Equals(entry.Row.RecordId, recordId, StringComparison.Ordinal)) continue;
                if (match is not null) throw new InvalidDataException("Records journal has ambiguous record identities.");
                match = entry.Row;
            }
            return match is null ? null : match with { Body = match.Body.ToArray() };
        }
        finally { Mutation.Release(); }
    }

    private async ValueTask<RecordWriteCommit> CommitAsync(KernelOperationIdentity operation, RecordWriteRow row,
        KernelAuditEvidence audit, CancellationToken ct)
    {
        await Mutation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = Load(out var legacySource, ct);
            var prior = entries.SingleOrDefault(entry => entry.Key == operation.IdempotencyKey);
            if (prior is not null)
            {
                var stored = prior.Stored();
                var disposition = stored.IntentEncodingVersion == RecordWriteIntent.EncodingVersion
                    && row.IntentEncodingVersion == RecordWriteIntent.EncodingVersion && stored.IntentDigest == operation.Fingerprint
                    ? RecordWriteCommitDisposition.Replayed : RecordWriteCommitDisposition.Conflict;
                return new(disposition, stored.Receipt) { Stored = stored };
            }

            var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                row.DefinitionDigest, row.Schema, row.RecordedAt);
            var appended = new Entry(operation.IdempotencyKey, operation, row, audit, JsonSerializer.SerializeToElement(receipt, Json));
            var hash = RecordWriteJournalBinding.Hash(row);
            try
            {
                ValidateBound(appended);
                RecordWriteJournalBinding.ValidateUnicode(JsonSerializer.SerializeToUtf8Bytes(appended, Json));
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidDataException or InvalidOperationException)
            {
                throw RecordWriteJournalBinding.InvalidBinding();
            }
            if (legacySource is not null)
            {
                // Scan the complete source, including ignored root/nested members, before creating any file.
                try { RecordWriteJournalBinding.ValidateUnicode(legacySource); }
                catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
                { throw Corrupt(); }
                using var original = JsonDocument.Parse(legacySource);
                var originals = original.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
                for (var i = 0; i < entries.Count; i++)
                    entries[i] = entries[i] with { Raw = RecordWriteIntent.Strict.GetBytes(originals[i].GetRawText()) };
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { MaxDepth = BoundJournalMaxDepth }))
                    {
                        writer.WriteStartObject();
                        writer.WriteString("Format", RecordWriteJournalBinding.Format);
                        writer.WriteNumber("LegacyEntryCount", entries.Count(entry => entry.IsLegacy));
                        writer.WriteStartArray("Entries");
                        foreach (var entry in entries)
                        {
                            ct.ThrowIfCancellationRequested();
                            if (entry.Wrapped) writer.WriteRawValue(entry.Raw, skipInputValidation: false);
                            else
                            {
                                writer.WriteStartObject();
                                writer.WriteString("Tag", "legacy/1");
                                writer.WritePropertyName("Legacy");
                                writer.WriteRawValue(entry.Raw, skipInputValidation: false);
                                writer.WriteEndObject();
                            }
                        }
                        writer.WriteStartObject();
                        writer.WriteString("Tag", "bound/2");
                        using var element = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(appended, Json));
                        foreach (var property in element.RootElement.EnumerateObject()) property.WriteTo(writer);
                        writer.WriteStartObject("Binding");
                        writer.WriteString("Encoding", RecordWriteJournalBinding.EncodingName);
                        writer.WriteString("Hash", hash);
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                        writer.WriteEndArray();
                        writer.WriteEndObject();
                        await writer.FlushAsync(ct).ConfigureAwait(false);
                    }
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
            ct.ThrowIfCancellationRequested();
            try
            {
                _ = Decode(File.ReadAllBytes(temporary), out var legacyOutput, ct);
                if (legacyOutput) throw Corrupt();
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            { throw Corrupt(); }
            _beforeReplace?.Invoke();
                ct.ThrowIfCancellationRequested();
                File.Move(temporary, _path, overwrite: true);
            }
            finally { File.Delete(temporary); }
            return new(RecordWriteCommitDisposition.Created, receipt);
        }
        finally { Mutation.Release(); }
    }

    private List<Entry> Load(CancellationToken cancellationToken = default) => Load(out _, cancellationToken);

    private List<Entry> Load(out byte[]? legacySource, CancellationToken cancellationToken = default)
    {
        legacySource = null;
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        try { bytes = File.ReadAllBytes(_path); }
        catch (FileNotFoundException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        try
        {
            var entries = Decode(bytes, out var legacy, cancellationToken);
            if (legacy) legacySource = bytes;
            return entries;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidDataException("Records journal is invalid.");
        }
    }

    private async ValueTask<List<Entry>> LoadForCurrentReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileStream stream;
        try
        {
            stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException) { cancellationToken.ThrowIfCancellationRequested(); return []; }
        catch (DirectoryNotFoundException) { cancellationToken.ThrowIfCancellationRequested(); return []; }
        await using (stream)
        {
            if (stream.Length > MaxCurrentReadBytes) throw new InvalidDataException("Records journal is invalid.");
            await using var bounded = new CurrentReadStream(stream, MaxCurrentReadBytes, _currentReadProgress);
            try
            {
                using var bytes = new MemoryStream();
                await bounded.CopyToAsync(bytes, cancellationToken).ConfigureAwait(false);
                return Decode(bytes.ToArray(), out _, cancellationToken);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            {
                throw new InvalidDataException("Records journal is invalid.");
            }
        }
    }

    private static InvalidDataException Corrupt() => new("Records journal is invalid.");

    private static List<Entry> Decode(byte[] bytes, out bool legacy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = BoundJournalMaxDepth });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw Corrupt();
        if (!root.TryGetProperty("Format", out var formatElement)) throw Corrupt();
        var format = formatElement.GetString();
        legacy = format == JournalFormat;
        if (legacy)
        {
            var journal = JsonSerializer.Deserialize<Journal>(bytes, LegacyJson);
            var result = ValidateJournal(journal, cancellationToken);
            // An ignored additive property in an old object cannot manufacture provenance.
            return result.Select(entry => entry with { Row = entry.Row with { PublicationVersionId = null }, IsLegacy = true }).ToList();
        }
        if (format != RecordWriteJournalBinding.Format) throw Corrupt();
        if (!System.Text.Unicode.Utf8.IsValid(bytes) || bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) throw Corrupt();
        Members(root, "Format", "LegacyEntryCount", "Entries");
        var countToken = root.GetProperty("LegacyEntryCount");
        if (countToken.ValueKind != JsonValueKind.Number || !countToken.TryGetInt32(out var count)
            || countToken.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) throw Corrupt();
        var array = root.GetProperty("Entries");
        if (array.ValueKind != JsonValueKind.Array || count < 0 || count >= array.GetArrayLength()) throw Corrupt();
        var entries = new List<Entry>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry entry;
            if (index++ < count)
            {
                Members(item, "Tag", "Legacy");
                if (item.GetProperty("Tag").GetString() != "legacy/1") throw Corrupt();
                var original = item.GetProperty("Legacy");
                if (original.ValueKind != JsonValueKind.Object) throw Corrupt();
                entry = original.Deserialize<Entry>(LegacyJson) ?? throw Corrupt();
                if (entry.Row is null) throw Corrupt();
                entry = entry with { Row = entry.Row with { PublicationVersionId = null }, IsLegacy = true };
            }
            else
            {
                Members(item, "Tag", "Key", "Operation", "Row", "Audit", "Receipt", "Binding");
                if (item.GetProperty("Tag").GetString() != "bound/2") throw Corrupt();
                RecordWriteJournalBinding.ValidateUnicode(RecordWriteIntent.Strict.GetBytes(item.GetRawText()));
                Members(item.GetProperty("Operation"), "CommandId", "IdempotencyKey", "Fingerprint");
                Members(item.GetProperty("Row"), "Tenant", "RecordId", "RecordTypeId", "Version", "DefinitionDigest", "Schema",
                    "ActorId", "OperationId", "RequestedVersion", "IntentEncodingVersion", "IntentDigest", "Body", "RecordedAt",
                    "OriginatorId", "AcceptedCommandId", "AcceptanceAuditId", "PublicationVersionId");
                Members(item.GetProperty("Audit"), "AuditId", "ActorId", "RecordedAt", "Payload", "AuthoritySnapshot", "OriginatorId");
                Members(item.GetProperty("Receipt"), "RecordId", "OperationId", "RecordTypeId", "Version", "DefinitionDigest", "Schema", "RecordedAt");
                Members(item.GetProperty("Binding"), "Encoding", "Hash");
                if (item.GetProperty("Row").GetProperty("Body").ValueKind != JsonValueKind.String
                    || item.GetProperty("Audit").GetProperty("Payload").ValueKind != JsonValueKind.String
                    || item.GetProperty("Audit").GetProperty("AuthoritySnapshot").ValueKind != JsonValueKind.String) throw Corrupt();
                // Deserialize only the six original fields; Tag and Binding belong to this format's dispatch.
                entry = new(item.GetProperty("Key").GetString()!, item.GetProperty("Operation").Deserialize<KernelOperationIdentity>(Json)!,
                    item.GetProperty("Row").Deserialize<RecordWriteRow>(Json)!, item.GetProperty("Audit").Deserialize<KernelAuditEvidence>(Json)!,
                    item.GetProperty("Receipt").Clone());
                ValidateBound(entry);
                var binding = item.GetProperty("Binding");
                var hash = binding.GetProperty("Hash").GetString();
                if (binding.GetProperty("Encoding").GetString() != RecordWriteJournalBinding.EncodingName
                    || !RecordWriteJournalBinding.IsHash(hash) || RecordWriteJournalBinding.Hash(entry.Row) != hash) throw Corrupt();
            }
            entries.Add(entry with { Raw = RecordWriteIntent.Strict.GetBytes(item.GetRawText()), Wrapped = true });
        }
        return ValidateJournal(new(JournalFormat, entries), cancellationToken);
    }

    private static void Members(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Corrupt();
        var remaining = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!remaining.Remove(property.Name)) throw Corrupt();
        if (remaining.Count != 0) throw Corrupt();
    }

    private static void ValidateBound(Entry entry)
    {
        if (entry.Operation is null || entry.Row is null || entry.Audit is null) throw Corrupt();
        var row = entry.Row;
        foreach (var text in new[] { entry.Key, entry.Operation.CommandId, entry.Operation.IdempotencyKey,
            entry.Operation.Fingerprint, row.Tenant, row.RecordId, row.RecordTypeId, row.Version, row.DefinitionDigest,
            row.Schema.Value, row.ActorId, row.OperationId, row.RequestedVersion, row.IntentEncodingVersion, row.IntentDigest,
            row.OriginatorId, row.AcceptedCommandId, row.AcceptanceAuditId, row.PublicationVersionId,
            entry.Audit.AuditId, entry.Audit.ActorId, entry.Audit.OriginatorId })
            if (text is not null) _ = RecordWriteIntent.Strict.GetByteCount(text);
        var receipt = entry.Receipt?.Deserialize<RecordWriteReceipt>(Json) ?? throw Corrupt();
        if (receipt.RecordId != row.RecordId || receipt.OperationId != row.OperationId || receipt.RecordTypeId != row.RecordTypeId
            || receipt.Version != row.Version || receipt.DefinitionDigest != row.DefinitionDigest || receipt.Schema != row.Schema
            || !receipt.RecordedAt.EqualsExact(row.RecordedAt) || entry.Key != entry.Operation.IdempotencyKey
            || entry.Operation.CommandId != row.RecordId || entry.Operation.Fingerprint != row.IntentDigest
            || entry.Audit.ActorId != row.ActorId || !entry.Audit.RecordedAt.EqualsExact(row.RecordedAt)
            || entry.Audit.OriginatorId != row.OriginatorId) throw Corrupt();
        entry.Operation.Validate();
        entry.Audit.Validate();
        _ = RecordWriteJournalBinding.Encode(row);
    }

    private static List<Entry> ValidateJournal(Journal? journal, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (journal?.Format != JournalFormat || journal.Entries is null) throw new InvalidDataException("Records journal is invalid.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in journal.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null || string.IsNullOrWhiteSpace(entry.Key) || !keys.Add(entry.Key)
                || entry.Operation is null || entry.Row is null || entry.Audit is null
                || entry.Operation.IdempotencyKey != entry.Key || string.IsNullOrWhiteSpace(entry.Row.IntentEncodingVersion)
                || string.IsNullOrWhiteSpace(entry.Row.IntentDigest) || string.IsNullOrWhiteSpace(entry.Row.Schema.Value))
                throw new InvalidDataException("Records journal is invalid.");
            entry.Operation.Validate();
            entry.Audit.Validate();
        }
        return journal.Entries;
    }

    // Bound bytes actually consumed as well as the opening length, including a journal that grows
    // externally while read. The extra byte distinguishes an exactly-at-limit EOF from oversize.
    private sealed class CurrentReadStream(Stream inner, long limit, Action<long>? progress) : Stream
    {
        private long _consumed;
        private int _bomBytesMatched;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = inner.Read(buffer[..(int)Math.Min(buffer.Length, limit - _consumed + 1)]);
            return Count(buffer[..count]);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, limit - _consumed + 1)], cancellationToken)
                .ConfigureAwait(false);
            if (count > 0) progress?.Invoke(_consumed + count);
            cancellationToken.ThrowIfCancellationRequested();
            return Count(buffer.Span[..count]);
        }
        private int Count(ReadOnlySpan<byte> bytes)
        {
            _consumed += bytes.Length;
            if (_consumed > limit) throw new InvalidDataException("Records journal is invalid.");
            // DeserializeAsync can consume a BOM that the existing byte-array Load rejects.
            // Match only the initial three bytes, retaining progress across short reads.
            ReadOnlySpan<byte> bom = [0xef, 0xbb, 0xbf];
            foreach (var value in bytes)
            {
                if (_bomBytesMatched < 0) break;
                if (value != bom[_bomBytesMatched]) { _bomBytesMatched = -1; break; }
                if (++_bomBytesMatched == bom.Length) throw new InvalidDataException("Records journal is invalid.");
            }
            return bytes.Length;
        }
    }

    private sealed record Journal(string Format, List<Entry> Entries);
    private sealed record Entry(string Key, KernelOperationIdentity Operation, RecordWriteRow Row, KernelAuditEvidence Audit, JsonElement? Receipt = null)
    {
        [JsonIgnore] public byte[] Raw { get; init; } = [];
        [JsonIgnore] public bool IsLegacy { get; init; }
        [JsonIgnore] public bool Wrapped { get; init; }
        public RecordWriteCommittedOperation Stored()
        {
            RecordWriteReceipt? receipt = null;
            try { receipt = Receipt?.Deserialize<RecordWriteReceipt>(Json); }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { }
            return new(Row.IntentEncodingVersion, Row.IntentDigest, receipt);
        }
    }

    private sealed class SchemaConverter : JsonConverter<SchemaId>
    {
        public override SchemaId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, SchemaId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }

    private sealed class Transaction(FileJournalRecordWriteStore owner) : IKernelPreparedTransaction<RecordWriteRow, RecordWriteCommit>
    {
        private KernelOperationIdentity? _operation;
        private RecordWriteRow? _row;
        private KernelAuditEvidence? _audit;
        private bool _finished;

        private void Open(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_finished) throw new InvalidOperationException("Records transaction is closed.");
        }

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(operation);
            if (_operation is not null) throw new InvalidOperationException("Records operation is already staged.");
            _operation = operation.Validate();
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRecordAsync(RecordWriteRow record, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(record);
            if (_row is not null) throw new InvalidOperationException("Records row is already staged.");
            ArgumentException.ThrowIfNullOrWhiteSpace(record.IntentEncodingVersion);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.IntentDigest);
            ArgumentException.ThrowIfNullOrWhiteSpace(record.Schema.Value);
            _ = RecordWriteJournalBinding.Encode(record);
            _row = record with { Body = record.Body.ToArray() };
            return ValueTask.CompletedTask;
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(audit);
            audit.Validate();
            if (_audit is not null) throw new InvalidOperationException("Records audit is already staged.");
            _audit = audit with { Payload = audit.Payload.ToArray(), AuthoritySnapshot = audit.AuthoritySnapshot.ToArray() };
            return ValueTask.CompletedTask;
        }

        public async ValueTask<RecordWriteCommit> CommitAsync(CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            if (_operation is null || _row is null || _audit is null)
                throw new InvalidOperationException("Records transaction is incomplete.");
            _finished = true;
            try { return await owner.CommitAsync(_operation, _row, _audit, cancellationToken).ConfigureAwait(false); }
            finally { _operation = null; _row = null; _audit = null; }
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            _finished = true;
            _operation = null;
            _row = null;
            _audit = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => RollbackAsync();
    }
}
