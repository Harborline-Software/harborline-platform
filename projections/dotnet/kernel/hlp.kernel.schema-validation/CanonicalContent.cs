using System.Security.Cryptography;
using System.Text.Json;

namespace Harborline.Kernel.SchemaValidation;

internal static class CanonicalContent
{
    internal static byte[] Serialize(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            Write(root, writer);
        }

        return stream.ToArray();
    }

    internal static string ContentAddress(ReadOnlySpan<byte> content)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(content, digest);
        Span<byte> cid = stackalloc byte[36];
        cid[0] = 0x01;
        cid[1] = 0x55;
        cid[2] = 0x12;
        cid[3] = 0x20;
        digest.CopyTo(cid[4..]);
        return $"b{Base32(cid)}";
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) Write(item, writer);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string Base32(ReadOnlySpan<byte> bytes)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var output = new char[(bytes.Length * 8 + 4) / 5];
        var buffer = 0;
        var bitsLeft = 0;
        var outputIndex = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output[outputIndex++] = alphabet[(buffer >> (bitsLeft - 5)) & 0x1f];
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0) output[outputIndex] = alphabet[(buffer << (5 - bitsLeft)) & 0x1f];
        return new string(output);
    }
}
