using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Harborline.Kernel.Core;
using Harborline.Kernel.SchemaValidation;

namespace Harborline.Blocks.BuilderDefinitions;
/// <summary>A file-backed ordinary and temporal Records transaction port, composed into no host.</summary>
/// <remarks>
/// Writers coordinate in this process only. Cross-process fencing and power-loss durability remain host obligations.
/// Open, receipt lookup and commit remain uncapped; current, effective, history and target reads share the 16 MiB spine.
/// Ordinary /1-/2 semantics and raw legacy provenance remain frozen. Temporal Created alone first publishes /3;
/// later ordinary and accepted Created writes preserve /3 and all prior evidence. Read/replay/refusal never converts.
/// Every completed candidate is decoded before replacement. The unkeyed hashes are integrity evidence, not authentication.
/// All older writers, including preloaded /2 writers, must be stopped, drained and fenced before any /3 cutover.
/// No operational cutover, automatic downgrade, cross-process exclusion or T-619 power-loss guarantee is supplied.
/// </remarks>
public sealed class FileJournalRecordWriteStore : IKernelPreparedTransactionPort<RecordWriteRow, RecordWriteCommit>,
    IRecordWriteReceiptReader, IRecordTemporalReadStore, IRecordTemporalWritePort, IRecordTemporalReceiptReader
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

    private readonly TimeProvider? _authorityClock;

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

    /// <summary>Opens the same journal with an explicitly configured acceptance clock.</summary>
    /// <param name="path">Journal path.</param>
    /// <param name="authorityClock">Trusted accepting authority source, never a person-effective timestamp.</param>
    public FileJournalRecordWriteStore(string path, TimeProvider authorityClock) : this(ClockPath(path, authorityClock))
    {
        _authorityClock = authorityClock;
    }

    internal FileJournalRecordWriteStore(string path, TimeProvider authorityClock,
        Action<long>? currentReadProgress, Action? beforeReplace) : this(path, authorityClock)
    {
        _currentReadProgress = currentReadProgress;
        _beforeReplace = beforeReplace;
    }

    private static string ClockPath(string path, TimeProvider authorityClock)
    {
        ArgumentNullException.ThrowIfNull(authorityClock);
        return path;
    }

    /// <inheritdoc />
    public bool HasAuthorityClock => _authorityClock is not null;

    ValueTask<IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>>
        IKernelPreparedTransactionPort<RecordTemporalWrite, RecordTemporalCommit>.BeginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>>(new TemporalTransaction(this));
    }

    /// <inheritdoc />
    public async ValueTask<RecordTemporalCommittedOperation?> FindTemporalOperationAsync(RecordWriteOperationKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = Load(cancellationToken).SingleOrDefault(item => item.Key == key.IdempotencyKey);
            return entry is null ? null : TemporalStored(entry);
        }
        finally { Mutation.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<RecordTemporalLookup> FindAtAsync(string tenant, string recordId, DateTimeOffset effectiveAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return SelectAt(await LoadForCurrentReadAsync(cancellationToken).ConfigureAwait(false), tenant, recordId, effectiveAt);
        }
        finally { Mutation.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<RecordTemporalHistory> HistoryAsync(string tenant, string recordId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var matches = Identity(await LoadForCurrentReadAsync(cancellationToken).ConfigureAwait(false), tenant, recordId);
            if (matches.Count == 0) return new(RecordTemporalHistoryKind.NotFound, Array.Empty<RecordTemporalVersion>());
            if (matches[0].Temporal is null)
                return new(RecordTemporalHistoryKind.NonTemporal, Array.Empty<RecordTemporalVersion>());
            return new(RecordTemporalHistoryKind.Temporal, Array.AsReadOnly(matches.Select(CopyVersion).ToArray()));
        }
        finally { Mutation.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<RecordTemporalTarget?> FindTemporalTargetAsync(string tenant, string recordId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var matches = Identity(await LoadForCurrentReadAsync(cancellationToken).ConfigureAwait(false), tenant, recordId);
            return matches.Count == 0 ? null : new(matches[0].Row.RecordTypeId, matches[0].Temporal is not null);
        }
        finally { Mutation.Release(); }
    }

    private static List<Entry> Identity(List<Entry> entries, string tenant, string recordId)
    {
        var matches = entries.Where(entry => string.Equals(entry.Row.Tenant, tenant, StringComparison.Ordinal)
            && string.Equals(entry.Row.RecordId, recordId, StringComparison.Ordinal)).ToList();
        if (matches.Count > 1 && matches.Any(entry => entry.Temporal is null))
            throw new InvalidDataException("Records journal has ambiguous record identities.");
        return matches;
    }

    private static RecordTemporalLookup SelectAt(List<Entry> entries, string tenant, string recordId, DateTimeOffset at)
    {
        var matches = Identity(entries, tenant, recordId);
        if (matches.Count == 0) return new(RecordTemporalLookupKind.NotFound, null, null);
        if (matches[0].Temporal is null)
            return new(RecordTemporalLookupKind.NonTemporal, matches[0].Row with { Body = matches[0].Row.Body.ToArray() }, null);
        var selected = matches.LastOrDefault(entry => Covers(entry.Temporal!, at.UtcTicks));
        return selected is null ? new(RecordTemporalLookupKind.NoEffectiveRevision, null, null)
            : new(RecordTemporalLookupKind.Temporal, null, CopyVersion(selected));
    }

    private static bool Covers(RecordTemporalEvidence temporal, long ticks)
        => temporal.EffectiveFrom.UtcTicks <= ticks && (temporal.EffectiveTo is null || ticks < temporal.EffectiveTo.Value.UtcTicks);

    private static RecordTemporalEvidence CopyTemporal(RecordTemporalEvidence value)
        => value with
        {
            Predecessor = value.Predecessor is null ? null : new(value.Predecessor.Revision, value.Predecessor.Hash),
            Supersedes = Array.AsReadOnly(value.Supersedes.Select(segment => new RecordTemporalSupersededSegment(
                new(segment.Revision.Revision, segment.Revision.Hash), segment.EffectiveFrom, segment.EffectiveTo)).ToArray()),
        };

    private static RecordTemporalVersion CopyVersion(Entry entry)
        => new(entry.Row with { Body = entry.Row.Body.ToArray() }, CopyTemporal(entry.Temporal!), entry.TemporalHash!);

    private static RecordTemporalCommittedOperation TemporalStored(Entry entry)
    {
        var receipt = entry.Stored().Receipt;
        return new(entry.Row.IntentEncodingVersion, entry.Row.IntentDigest,
            entry.Temporal is null || receipt is null ? null : new(receipt with { }, CopyTemporal(entry.Temporal), entry.TemporalHash!));
    }

    private async ValueTask<RecordTemporalCommit> CommitTemporalAsync(KernelOperationIdentity operation,
        RecordTemporalWrite write, KernelAuditEvidence audit, CancellationToken ct)
    {
        await Mutation.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var entries = Load(out var legacySource, ct);
            var prior = entries.SingleOrDefault(entry => entry.Key == operation.IdempotencyKey);
            if (prior is not null)
            {
                var stored = TemporalStored(prior);
                var disposition = stored.IntentEncodingVersion == RecordTemporalJournalBinding.IntentEncodingVersion
                    && write.Row.IntentEncodingVersion == RecordTemporalJournalBinding.IntentEncodingVersion
                    && stored.IntentDigest == operation.Fingerprint
                    ? RecordTemporalCommitDisposition.Replayed : RecordTemporalCommitDisposition.Conflict;
                return new(disposition, null) { Stored = stored };
            }
            var matches = Identity(entries, write.Row.Tenant, write.Row.RecordId);
            Entry? head = null;
            if (write.Kind == RecordTemporalCommandKind.Create)
            {
                if (matches.Count != 0) throw new InvalidOperationException("The new temporal record identity already exists.");
            }
            else
            {
                if (matches.Count > 0 && matches[0].Temporal is null)
                    return new(RecordTemporalCommitDisposition.UnsupportedLegacyBasis, null);
                head = matches.LastOrDefault();
                if (head is null || write.ExpectedPredecessor is not { } expected
                    || expected.Revision != head.Temporal!.Revision || expected.Hash != head.TemporalHash)
                    return new(RecordTemporalCommitDisposition.BasisMismatch, null);
                if (head.Row.RecordTypeId != write.Row.RecordTypeId)
                    throw new InvalidOperationException("A temporal amendment changed its logical type.");
            }
            if (_authorityClock is null) return new(RecordTemporalCommitDisposition.TemporalClockRequired, null);
            if (head?.Temporal?.Revision == long.MaxValue)
                throw new InvalidOperationException("The temporal revision cannot be incremented.");
            var revision = head is null ? 1 : checked(head.Temporal!.Revision + 1);
            var authorityAt = _authorityClock.GetUtcNow();
            var temporal = new RecordTemporalEvidence(revision,
                head is null ? null : new(head.Temporal!.Revision, head.TemporalHash!),
                VisibleSegments(matches, write.EffectiveFrom, write.EffectiveTo),
                write.EffectiveFrom, write.EffectiveTo, write.Reason, authorityAt);
            var row = write.Row;
            var receipt = new RecordWriteReceipt(row.RecordId, row.OperationId, row.RecordTypeId, row.Version,
                row.DefinitionDigest, row.Schema, row.RecordedAt);
            var appended = new Entry(operation.IdempotencyKey, operation, row, audit, JsonSerializer.SerializeToElement(receipt, Json))
                { Temporal = temporal };
            appended = appended with
            {
                TemporalHash = RecordTemporalJournalBinding.HashBinding(appended.Key, operation, row, audit, temporal),
            };
            ValidateBound(appended);
            ValidateTemporalChains(entries.Append(appended).ToList(), ct);
            if (legacySource is not null)
            {
                try { RecordWriteJournalBinding.ValidateUnicode(legacySource); }
                catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { throw Corrupt(); }
                using var original = JsonDocument.Parse(legacySource);
                var originals = original.RootElement.GetProperty("Entries").EnumerateArray().ToArray();
                for (var i = 0; i < entries.Count; i++)
                    entries[i] = entries[i] with { Raw = RecordWriteIntent.Strict.GetBytes(originals[i].GetRawText()) };
            }
            else if (entries.Count != 0) ValidateTemporalUnicode(entries[0].Source);
            await PublishTemporalAsync(entries, appended, ct).ConfigureAwait(false);
            return new(RecordTemporalCommitDisposition.Created, new(receipt, CopyTemporal(temporal), appended.TemporalHash!));
        }
        finally { Mutation.Release(); }
    }

    private async ValueTask PublishTemporalAsync(List<Entry> entries, Entry appended, CancellationToken ct)
    {
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
                    writer.WriteString("Format", RecordTemporalJournalBinding.Format);
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
                    writer.WriteString("Tag", "temporal/3");
                    using var element = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(appended, Json));
                    foreach (var property in element.RootElement.EnumerateObject()) property.WriteTo(writer);
                    writer.WriteStartObject("Binding");
                    writer.WriteString("Encoding", RecordWriteJournalBinding.EncodingName);
                    writer.WriteString("Hash", RecordWriteJournalBinding.Hash(appended.Row));
                    writer.WriteEndObject();
                    writer.WritePropertyName("Temporal");
                    WriteTemporal(writer, appended.Temporal!);
                    writer.WriteStartObject("TemporalBinding");
                    writer.WriteString("Encoding", RecordTemporalJournalBinding.BindingEncodingVersion);
                    writer.WriteString("Hash", appended.TemporalHash);
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
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { throw Corrupt(); }
            _beforeReplace?.Invoke();
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static IReadOnlyList<RecordTemporalSupersededSegment> VisibleSegments(List<Entry> prior,
        DateTimeOffset from, DateTimeOffset? to)
    {
        var boundaries = new SortedSet<long> { from.UtcTicks };
        if (to is { } end) boundaries.Add(end.UtcTicks);
        foreach (var entry in prior)
        {
            if (entry.Temporal is not { } temporal) throw Corrupt();
            boundaries.Add(temporal.EffectiveFrom.UtcTicks);
            if (temporal.EffectiveTo is { } until) boundaries.Add(until.UtcTicks);
        }
        var points = boundaries.ToArray();
        var segments = new List<RecordTemporalSupersededSegment>();
        for (var i = 0; i < points.Length; i++)
        {
            var start = points[i];
            if (start < from.UtcTicks || to is { } limit && start >= limit.UtcTicks) continue;
            var selected = prior.LastOrDefault(entry => Covers(entry.Temporal!, start));
            if (selected is null) continue;
            DateTimeOffset? finish = i + 1 < points.Length ? new DateTimeOffset(points[i + 1], TimeSpan.Zero) : null;
            var basis = new RecordTemporalBasis(selected.Temporal!.Revision, selected.TemporalHash!);
            if (segments.Count > 0 && segments[^1].Revision == basis
                && segments[^1].EffectiveTo is { } previous && previous.UtcTicks == start)
                segments[^1] = segments[^1] with { EffectiveTo = finish };
            else segments.Add(new(basis, new(start, TimeSpan.Zero), finish));
        }
        return Array.AsReadOnly(segments.ToArray());
    }

    private static void WriteTemporal(Utf8JsonWriter writer, RecordTemporalEvidence temporal)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Revision", temporal.Revision);
        writer.WritePropertyName("Predecessor");
        WriteBasis(writer, temporal.Predecessor);
        writer.WriteStartArray("Supersedes");
        foreach (var segment in temporal.Supersedes)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Revision");
            WriteBasis(writer, segment.Revision);
            WriteTime(writer, "EffectiveFrom", segment.EffectiveFrom);
            WriteTime(writer, "EffectiveTo", segment.EffectiveTo);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        WriteTime(writer, "EffectiveFrom", temporal.EffectiveFrom);
        WriteTime(writer, "EffectiveTo", temporal.EffectiveTo);
        writer.WriteString("Reason", temporal.Reason);
        WriteTime(writer, "AuthorityRecordedAt", temporal.AuthorityRecordedAt);
        writer.WriteEndObject();
    }

    private static void WriteBasis(Utf8JsonWriter writer, RecordTemporalBasis? value)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteNumber("Revision", value.Revision);
        writer.WriteString("Hash", value.Hash);
        writer.WriteEndObject();
    }

    private static void WriteTime(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static RecordTemporalEvidence ReadTemporal(JsonElement value)
    {
        Members(value, "Revision", "Predecessor", "Supersedes", "EffectiveFrom", "EffectiveTo", "Reason", "AuthorityRecordedAt");
        var segments = value.GetProperty("Supersedes");
        if (segments.ValueKind != JsonValueKind.Array) throw Corrupt();
        var result = new List<RecordTemporalSupersededSegment>();
        foreach (var segment in segments.EnumerateArray())
        {
            Members(segment, "Revision", "EffectiveFrom", "EffectiveTo");
            result.Add(new(ReadBasis(segment.GetProperty("Revision")) ?? throw Corrupt(),
                ReadTime(segment.GetProperty("EffectiveFrom")), OptionalTime(segment.GetProperty("EffectiveTo"))));
        }
        var reason = value.GetProperty("Reason");
        if (reason.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw Corrupt();
        return new(ReadRevision(value.GetProperty("Revision")), ReadBasis(value.GetProperty("Predecessor")),
            Array.AsReadOnly(result.ToArray()), ReadTime(value.GetProperty("EffectiveFrom")), OptionalTime(value.GetProperty("EffectiveTo")),
            reason.GetString(), ReadTime(value.GetProperty("AuthorityRecordedAt")));
    }

    private static long ReadRevision(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var revision) || revision <= 0
            || value.GetRawText().IndexOfAny(new[] { '.', 'e', 'E' }) >= 0) throw Corrupt();
        return revision;
    }

    private static RecordTemporalBasis? ReadBasis(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        Members(value, "Revision", "Hash");
        var hash = value.GetProperty("Hash").GetString();
        if (!RecordWriteJournalBinding.IsHash(hash)) throw Corrupt();
        return new(ReadRevision(value.GetProperty("Revision")), hash!);
    }

    private static DateTimeOffset? OptionalTime(JsonElement value)
        => value.ValueKind == JsonValueKind.Null ? null : ReadTime(value);

    private static DateTimeOffset ReadTime(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) throw Corrupt();
        var text = value.GetString()!;
        if (text.Length != 33 || !DateTimeOffset.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz",
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var result)
            || result.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz", System.Globalization.CultureInfo.InvariantCulture) != text)
            throw Corrupt();
        return result;
    }

    private static void ValidateTemporalChains(List<Entry> entries, CancellationToken ct)
    {
        if (!entries.Any(entry => entry.Temporal is not null)) throw Corrupt();
        var identities = new Dictionary<(string Tenant, string Id), List<Entry>>();
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var row = entry.Row;
            if (TemporalBlank(row.Tenant) || TemporalBlank(row.RecordId) || TemporalBlank(row.RecordTypeId)) throw Corrupt();
            var identity = (row.Tenant, row.RecordId);
            if (!identities.TryGetValue(identity, out var prior)) identities.Add(identity, prior = []);
            if (entry.Temporal is not { } temporal)
            {
                if (prior.Count != 0) throw Corrupt();
                prior.Add(entry);
                continue;
            }
            if (prior.Any(item => item.Temporal is null) || prior.Any(item => item.Row.RecordTypeId != row.RecordTypeId)) throw Corrupt();
            ValidateBound(entry);
            if (row.IntentEncodingVersion != RecordTemporalJournalBinding.IntentEncodingVersion
                || !RecordWriteJournalBinding.IsHash(row.IntentDigest)
                || entry.Key != new RecordWriteOperationKey(row.Tenant, row.ActorId, row.OperationId).IdempotencyKey
                || row.OriginatorId is not null || row.AcceptedCommandId is not null || row.AcceptanceAuditId is not null
                || entry.Audit.OriginatorId is not null || temporal.Revision <= 0
                || temporal.EffectiveTo is { } end && end.UtcTicks <= temporal.EffectiveFrom.UtcTicks
                || !RecordWriteJournalBinding.IsHash(entry.TemporalHash)
                || RecordTemporalJournalBinding.HashBinding(entry.Key, entry.Operation, row, entry.Audit, temporal) != entry.TemporalHash)
                throw Corrupt();
            if (prior.Count == 0)
            {
                if (temporal.Revision != 1 || temporal.Predecessor is not null || temporal.Reason is not null
                    || temporal.Supersedes.Count != 0) throw Corrupt();
            }
            else
            {
                var head = prior[^1];
                if (head.Temporal!.Revision == long.MaxValue || temporal.Revision != head.Temporal.Revision + 1
                    || temporal.Predecessor is not { } predecessor || predecessor.Revision != head.Temporal.Revision
                    || predecessor.Hash != head.TemporalHash || TemporalBlank(temporal.Reason)) throw Corrupt();
                var expected = VisibleSegments(prior, temporal.EffectiveFrom, temporal.EffectiveTo);
                if (expected.Count != temporal.Supersedes.Count) throw Corrupt();
                for (var i = 0; i < expected.Count; i++)
                    if (expected[i].Revision != temporal.Supersedes[i].Revision
                        || !expected[i].EffectiveFrom.EqualsExact(temporal.Supersedes[i].EffectiveFrom)
                        || !SameTime(expected[i].EffectiveTo, temporal.Supersedes[i].EffectiveTo)) throw Corrupt();
            }
            prior.Add(entry);
        }
    }

    private static bool SameTime(DateTimeOffset? left, DateTimeOffset? right)
        => left is null ? right is null : right is { } value && left.Value.EqualsExact(value);

    private static bool TemporalBlank(string? value) => value is null || value.Length == 0 || value.All(c =>
        c is >= '\u0009' and <= '\u000d' or '\u0020' or '\u0085' or '\u00a0' or '\u1680'
            or >= '\u2000' and <= '\u200a' or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000');

    private static void ValidateTemporalUnicode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) || !System.Text.Unicode.Utf8.IsValid(bytes)) throw Corrupt();
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = BoundJournalMaxDepth });
        while (reader.Read())
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
            var raw = reader.ValueSpan;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != (byte)'\\') continue;
                if (++i >= raw.Length) throw Corrupt();
                if (raw[i] != (byte)'u') continue;
                var code = Hex(raw.Slice(i + 1, 4));
                i += 4;
                if (code is >= 0xdc00 and <= 0xdfff) throw Corrupt();
                if (code is < 0xd800 or > 0xdbff) continue;
                if (i + 6 >= raw.Length || raw[i + 1] != (byte)'\\' || raw[i + 2] != (byte)'u'
                    || Hex(raw.Slice(i + 3, 4)) is < 0xdc00 or > 0xdfff) throw Corrupt();
                i += 6;
            }
            _ = RecordWriteIntent.Strict.GetByteCount(reader.GetString()!);
        }
        static int Hex(ReadOnlySpan<byte> value)
        {
            var result = 0;
            foreach (var c in value)
            {
                var digit = c is >= (byte)'0' and <= (byte)'9' ? c - '0'
                    : c is >= (byte)'a' and <= (byte)'f' ? c - 'a' + 10
                    : c is >= (byte)'A' and <= (byte)'F' ? c - 'A' + 10 : throw Corrupt();
                result = result * 16 + digit;
            }
            return result;
        }
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
    /// <inheritdoc />
    public async ValueTask<RecordWriteRow?> FindAsync(string tenant, string recordId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);
        await Mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await LoadForCurrentReadAsync(cancellationToken).ConfigureAwait(false);
            var matches = Identity(entries, tenant, recordId);
            if (matches.Count == 0) return null;
            if (matches[0].Temporal is null) return matches[0].Row with { Body = matches[0].Row.Body.ToArray() };
            if (_authorityClock is null)
                throw new NotSupportedException("Temporal point reads require an explicit authority clock.");
            return SelectAt(entries, tenant, recordId, _authorityClock.GetUtcNow()).Temporal?.Row;
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

            if (entries.Any(entry => entry.Temporal is not null)
                && entries.Any(entry => entry.Row.Tenant == row.Tenant && entry.Row.RecordId == row.RecordId))
                throw new InvalidOperationException("The ordinary record identity already exists in the temporal journal.");
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
                        writer.WriteString("Format", entries.Any(entry => entry.Temporal is not null)
                            ? RecordTemporalJournalBinding.Format : RecordWriteJournalBinding.Format);
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
        var temporalFormat = format == RecordTemporalJournalBinding.Format;
        if (!temporalFormat && format != RecordWriteJournalBinding.Format) throw Corrupt();
        if (temporalFormat) ValidateTemporalUnicode(bytes);
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
                var tag = item.GetProperty("Tag").GetString();
                var isTemporal = temporalFormat && tag == "temporal/3";
                if (isTemporal) Members(item, "Tag", "Key", "Operation", "Row", "Audit", "Receipt", "Binding", "Temporal", "TemporalBinding");
                else
                {
                    Members(item, "Tag", "Key", "Operation", "Row", "Audit", "Receipt", "Binding");
                    if (tag != "bound/2") throw Corrupt();
                }
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
                if (isTemporal)
                {
                    var temporalBinding = item.GetProperty("TemporalBinding");
                    Members(temporalBinding, "Encoding", "Hash");
                    var temporalHash = temporalBinding.GetProperty("Hash").GetString();
                    if (temporalBinding.GetProperty("Encoding").GetString() != RecordTemporalJournalBinding.BindingEncodingVersion
                        || !RecordWriteJournalBinding.IsHash(temporalHash)) throw Corrupt();
                    entry = entry with { Temporal = ReadTemporal(item.GetProperty("Temporal")), TemporalHash = temporalHash };
                }
            }
            entries.Add(entry with { Raw = RecordWriteIntent.Strict.GetBytes(item.GetRawText()), Wrapped = true, Source = bytes });
        }
        var validated = ValidateJournal(new(JournalFormat, entries), cancellationToken);
        if (temporalFormat) ValidateTemporalChains(validated, cancellationToken);
        return validated;
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
        [JsonIgnore] public RecordTemporalEvidence? Temporal { get; init; }
        [JsonIgnore] public string? TemporalHash { get; init; }
        [JsonIgnore] public byte[] Source { get; init; } = [];
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

    private sealed class TemporalTransaction(FileJournalRecordWriteStore owner)
        : IKernelPreparedTransaction<RecordTemporalWrite, RecordTemporalCommit>
    {
        private KernelOperationIdentity? _operation;
        private RecordTemporalWrite? _write;
        private KernelAuditEvidence? _audit;
        private bool _finished;

        private void Open(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_finished) throw new InvalidOperationException("Records temporal transaction is closed.");
        }

        public ValueTask StageOperationAsync(KernelOperationIdentity operation, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(operation);
            if (_operation is not null) throw new InvalidOperationException("Records temporal operation is already staged.");
            _operation = operation.Validate();
            return ValueTask.CompletedTask;
        }

        public ValueTask StageRecordAsync(RecordTemporalWrite record, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(record);
            ArgumentNullException.ThrowIfNull(record.Row);
            if (_write is not null) throw new InvalidOperationException("Records temporal declaration is already staged.");
            if (record.Kind is not (RecordTemporalCommandKind.Create or RecordTemporalCommandKind.Amend)
                || record.EffectiveTo is { } end && end.UtcTicks <= record.EffectiveFrom.UtcTicks
                || record.Kind == RecordTemporalCommandKind.Create && (record.ExpectedPredecessor is not null || record.Reason is not null)
                || record.Kind == RecordTemporalCommandKind.Amend && (record.ExpectedPredecessor is not { } basis
                    || basis.Revision <= 0 || !RecordWriteJournalBinding.IsHash(basis.Hash) || TemporalBlank(record.Reason))
                || record.Row.IntentEncodingVersion != RecordTemporalJournalBinding.IntentEncodingVersion
                || !RecordWriteJournalBinding.IsHash(record.Row.IntentDigest)
                || record.Row.OriginatorId is not null || record.Row.AcceptedCommandId is not null || record.Row.AcceptanceAuditId is not null)
                throw new InvalidOperationException("The prepared temporal declaration is invalid.");
            if (record.Reason is not null) _ = RecordWriteIntent.Strict.GetByteCount(record.Reason);
            _ = RecordWriteJournalBinding.Encode(record.Row);
            _write = record with
            {
                Row = record.Row with { Body = record.Row.Body.ToArray() },
                ExpectedPredecessor = record.ExpectedPredecessor is null ? null
                    : new(record.ExpectedPredecessor.Revision, record.ExpectedPredecessor.Hash),
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask StageAuditAsync(KernelAuditEvidence audit, CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            ArgumentNullException.ThrowIfNull(audit);
            audit.Validate();
            if (_audit is not null) throw new InvalidOperationException("Records temporal audit is already staged.");
            _audit = audit with { Payload = audit.Payload.ToArray(), AuthoritySnapshot = audit.AuthoritySnapshot.ToArray() };
            return ValueTask.CompletedTask;
        }

        public async ValueTask<RecordTemporalCommit> CommitAsync(CancellationToken cancellationToken = default)
        {
            Open(cancellationToken);
            if (_operation is null || _write is null || _audit is null)
                throw new InvalidOperationException("Records temporal transaction is incomplete.");
            _finished = true;
            try { return await owner.CommitTemporalAsync(_operation, _write, _audit, cancellationToken).ConfigureAwait(false); }
            finally { _operation = null; _write = null; _audit = null; }
        }

        public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
        {
            _finished = true;
            _operation = null;
            _write = null;
            _audit = null;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => RollbackAsync();
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
