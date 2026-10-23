using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Blocks.BuilderDefinitions;

// Q28/Q41: this is a versioned byte protocol, not a schema-ID implementation or an authorization mechanism.
internal static class RecordWriteJournalBinding
{
    internal const string EncodingName = "hlp.records.publication-binding/2";
    internal const string Format = "records-write-file-journal/2";

    internal static byte[] Encode(RecordWriteRow row)
    {
        using var output = new MemoryStream();
        output.Write("hlp.records.publication-binding/2\0"u8);
        Text(row.Tenant);
        Text("records");
        Text(row.RecordTypeId);
        Text(row.PublicationVersionId);
        Text(row.Version);
        Text("sha256-utf8-body/1");
        if (!IsHash(row.DefinitionDigest)) throw InvalidBinding();
        Part(Convert.FromHexString(row.DefinitionDigest));
        Text(row.Schema.Value);
        return output.ToArray();

        void Text(string? value)
        {
            if (value is null || value.Length == 0 || value.All(FrozenWhitespace)) throw InvalidBinding();
            try { Part(RecordWriteIntent.Strict.GetBytes(value)); }
            catch (EncoderFallbackException) { throw InvalidBinding(); }
        }
        void Part(byte[] value)
        {
            Span<byte> prefix = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(prefix, checked((uint)value.Length));
            output.Write(prefix);
            output.Write(value);
        }
    }

    internal static string Hash(RecordWriteRow row) => Convert.ToHexStringLower(SHA256.HashData(Encode(row)));

    internal static bool IsHash(string? value)
        => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FrozenWhitespace(char c) => c is >= '\u0009' and <= '\u000d' or '\u0020' or '\u0085'
        or '\u00a0' or '\u1680' or >= '\u2000' and <= '\u200a' or '\u2028' or '\u2029' or '\u202f'
        or '\u205f' or '\u3000';

    internal static void ValidatePublication(RecordWriteRow row, RecordTypePublication publication)
    {
        var document = publication.Revision.Document;
        if (document.Key.Kind != DefinitionKind.Records || document.Key.Tenant != row.Tenant
            || document.Key.DefinitionId != row.RecordTypeId || document.VersionId != row.PublicationVersionId
            || document.Version != row.Version || publication.Revision.Digest != row.DefinitionDigest
            || publication.Schema.Id != row.Schema) throw InvalidBinding();
        string digest;
        try { digest = Convert.ToHexStringLower(SHA256.HashData(RecordWriteIntent.Strict.GetBytes(document.BodyJson))); }
        catch (EncoderFallbackException) { throw InvalidBinding(); }
        if (digest != row.DefinitionDigest) throw InvalidBinding();
        _ = Encode(row);
    }

    internal static InvalidOperationException InvalidBinding() => new("The admitted Records publication binding is invalid.");

    // Inspect every token, including ignored members when converting /1. Never decode application payloads as JSON.
    internal static void ValidateUnicode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) || !System.Text.Unicode.Utf8.IsValid(bytes))
            throw new InvalidDataException("Records journal is invalid.");
        var reader = new Utf8JsonReader(bytes);
        while (reader.Read())
        {
            if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName)) continue;
            var raw = reader.ValueSpan;
            for (var i = 0; i < raw.Length; i++)
            {
                if (raw[i] != (byte)'\\') continue;
                if (++i >= raw.Length) throw new JsonException();
                if (raw[i] != (byte)'u') continue;
                var code = Hex(raw.Slice(i + 1, 4));
                i += 4;
                if (code is >= 0xdc00 and <= 0xdfff) throw new JsonException();
                if (code is < 0xd800 or > 0xdbff) continue;
                if (i + 6 >= raw.Length || raw[i + 1] != (byte)'\\' || raw[i + 2] != (byte)'u'
                    || Hex(raw.Slice(i + 3, 4)) is < 0xdc00 or > 0xdfff) throw new JsonException();
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
                    : c is >= (byte)'A' and <= (byte)'F' ? c - 'A' + 10 : throw new JsonException();
                result = result * 16 + digit;
            }
            return result;
        }
    }
}
