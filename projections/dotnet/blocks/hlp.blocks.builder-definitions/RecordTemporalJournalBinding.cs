using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Harborline.Kernel.Core;

namespace Harborline.Blocks.BuilderDefinitions;

// This byte protocol is local integrity evidence, not authentication or a publication resolver.
internal static class RecordTemporalJournalBinding
{
    internal const string Format = "records-write-file-journal/3";
    internal const string IntentEncodingVersion = "records-temporal-intent/1";
    internal const string BindingEncodingVersion = "records-temporal-binding/1";

    internal static byte[] EncodeIntent(string tenant, string recordTypeId,
        RecordTemporalCommandKind kind, string? targetRecordId, RecordTemporalBasis? predecessor,
        DateTimeOffset effectiveFrom, DateTimeOffset? effectiveTo, string? reason,
        string? requestedVersion, JsonObject body)
    {
        if (kind is not (RecordTemporalCommandKind.Create or RecordTemporalCommandKind.Amend)
            || (kind == RecordTemporalCommandKind.Create && (targetRecordId is not null || predecessor is not null || reason is not null))
            || (kind == RecordTemporalCommandKind.Amend && (targetRecordId is null || predecessor is null || reason is null)))
            throw new InvalidOperationException("The temporal command shape is invalid.");
        using var output = new EncodingWriter();
        output.Raw("hlp.records.temporal-intent/1\0"u8);
        output.Byte((byte)kind);
        output.Text(tenant);
        output.Optional(targetRecordId, output.Text);
        output.Optional(predecessor, output.Basis);
        output.Time(effectiveFrom);
        output.OptionalTime(effectiveTo);
        output.Optional(reason, output.Text);
        output.Part(RecordWriteIntent.Encode(recordTypeId, requestedVersion, body));
        return output.Bytes();
    }

    internal static byte[] EncodeBinding(string key, KernelOperationIdentity operation,
        RecordWriteRow row, KernelAuditEvidence audit, RecordTemporalEvidence temporal)
    {
        using var output = new EncodingWriter();
        output.Raw("hlp.records.temporal-binding/1\0"u8);
        output.Part(RecordWriteJournalBinding.Encode(row));
        output.Text(key);
        output.Text(operation.CommandId);
        output.Text(operation.IdempotencyKey);
        output.Hash(operation.Fingerprint);
        output.Text(row.RecordId);
        output.Text(row.ActorId);
        output.Text(row.OperationId);
        output.Optional(row.RequestedVersion, output.Text);
        output.Text(row.IntentEncodingVersion);
        output.Hash(row.IntentDigest);
        output.Raw(SHA256.HashData(row.Body.Span));
        output.Time(row.RecordedAt);
        output.Optional(row.OriginatorId, output.Text);
        output.Optional(row.AcceptedCommandId, output.Text);
        output.Optional(row.AcceptanceAuditId, output.Text);
        output.Text(audit.AuditId);
        output.Text(audit.ActorId);
        output.Time(audit.RecordedAt);
        output.Raw(SHA256.HashData(audit.Payload.Span));
        output.Raw(SHA256.HashData(audit.AuthoritySnapshot.Span));
        output.Optional(audit.OriginatorId, output.Text);
        output.Revision(temporal.Revision);
        output.Optional(temporal.Predecessor, output.Basis);
        output.Count(temporal.Supersedes.Count);
        foreach (var segment in temporal.Supersedes)
        {
            output.Basis(segment.Revision);
            output.Time(segment.EffectiveFrom);
            output.OptionalTime(segment.EffectiveTo);
        }
        output.Time(temporal.EffectiveFrom);
        output.OptionalTime(temporal.EffectiveTo);
        output.Optional(temporal.Reason, output.Text);
        output.Time(temporal.AuthorityRecordedAt);
        return output.Bytes();
    }

    internal static string HashBinding(string key, KernelOperationIdentity operation,
        RecordWriteRow row, KernelAuditEvidence audit, RecordTemporalEvidence temporal)
        => Convert.ToHexStringLower(SHA256.HashData(EncodeBinding(key, operation, row, audit, temporal)));

    private sealed class EncodingWriter : IDisposable
    {
        private readonly MemoryStream _stream = new();
        internal void Raw(ReadOnlySpan<byte> bytes) => _stream.Write(bytes);
        internal void Byte(byte value) => _stream.WriteByte(value);
        internal byte[] Bytes() => _stream.ToArray();
        internal void Count(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value));
            Raw(bytes);
        }
        internal void Part(byte[] bytes) { Count(bytes.Length); Raw(bytes); }
        internal void Text(string value) => Part(RecordWriteIntent.Strict.GetBytes(value));
        internal void Optional<T>(T? value, Action<T> write) where T : class
        {
            Byte(value is null ? (byte)0 : (byte)1);
            if (value is not null) write(value);
        }
        internal void Hash(string value)
        {
            if (!RecordWriteJournalBinding.IsHash(value))
                throw new InvalidOperationException("The temporal hash is invalid.");
            Raw(Convert.FromHexString(value));
        }
        internal void Revision(long value)
        {
            if (value <= 0) throw new InvalidOperationException("The temporal revision is invalid.");
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)value));
            Raw(bytes);
        }
        internal void Basis(RecordTemporalBasis value) { Revision(value.Revision); Hash(value.Hash); }
        internal void Time(DateTimeOffset value)
        {
            Span<byte> bytes = stackalloc byte[10];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, checked((ulong)value.UtcTicks));
            BinaryPrimitives.WriteInt16BigEndian(bytes[8..], checked((short)value.Offset.TotalMinutes));
            Raw(bytes);
        }
        internal void OptionalTime(DateTimeOffset? value)
        {
            Byte(value.HasValue ? (byte)1 : (byte)0);
            if (value.HasValue) Time(value.Value);
        }
        public void Dispose() => _stream.Dispose();
    }
}
