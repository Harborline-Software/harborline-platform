using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.Assistance;

/// <summary>The untrusted pilot.proposal/3 wire envelope, never a dispatch receipt.</summary>
public sealed record AssistanceProposalEnvelope(string Schema, string Surface, string Command, JsonElement Args);

/// <summary>Computes a content digest without carrying proposal content into a delegation actor.</summary>
public static class AssistanceProposalDigest
{
    /// <summary>Returns sha256: followed by the lowercase hex SHA-256 of canonical UTF-8, without a newline.</summary>
    public static string Compute(AssistanceProposalEnvelope proposal) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(AssistanceDelegationJson.SerializeCanonical(proposal)));
}

/// <summary>Strict snake_case wire codecs; object keys use RFC 8785 ordinal UTF-16 order.</summary>
public static class AssistanceDelegationJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
    };

    /// <summary>Deserializes a supported wire shape and refuses invalid or unknown members.</summary>
    public static T Deserialize<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("A wire value is required.");
        Validate(value);
        return value;
    }

    /// <summary>Serializes a supported wire shape as canonical UTF-8 with no trailing newline.</summary>
    public static byte[] SerializeCanonical<T>(T value)
    {
        Validate(value);
        return Encoding.UTF8.GetBytes(Canonical(JsonSerializer.SerializeToElement(value, Options)));
    }

    private static void Validate(object? value)
    {
        if (value is not AssistanceProposalEnvelope proposal || proposal.Schema != "pilot.proposal/3"
            || proposal.Surface is null || proposal.Command is null || proposal.Args.ValueKind == JsonValueKind.Undefined)
            throw new JsonException("Invalid proposal envelope.");
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => Quote(property.Name) + ":" + Canonical(property.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => Quote(value.GetString()!),
        JsonValueKind.Number => Number(value.GetDouble()),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => throw new JsonException("Invalid JSON value."),
    };

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsSurrogate(character))
            {
                if (!char.IsHighSurrogate(character) || index + 1 == value.Length || !char.IsLowSurrogate(value[index + 1]))
                    throw new JsonException("Invalid JSON string.");
                result.Append(character).Append(value[++index]);
                continue;
            }
            result.Append(character switch
            {
                '"' => "\\\"", '\\' => "\\\\", '\b' => "\\b", '\t' => "\\t", '\n' => "\\n", '\f' => "\\f", '\r' => "\\r",
                < ' ' => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }
        return result.Append('"').ToString();
    }

    private static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new JsonException("A finite JSON number is required.");
        if (value == 0) return "0";
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E');
        var exponent = exponentIndex < 0 ? 0 : int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var point = mantissa.IndexOf('.');
        var fractionLength = point < 0 ? 0 : mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        var significant = digits.TrimEnd('0');
        var position = digits.Length + exponent - fractionLength;
        var body = position switch
        {
            > 0 and <= 21 when significant.Length <= position => significant + new string('0', position - significant.Length),
            > 0 and <= 21 => significant.Insert(position, "."),
            > -6 and <= 0 => "0." + new string('0', -position) + significant,
            _ => significant[0] + (significant.Length > 1 ? "." + significant[1..] : "") + "e"
                + (position > 0 ? "+" : "-") + Math.Abs(position - 1).ToString(CultureInfo.InvariantCulture),
        };
        return value < 0 ? "-" + body : body;
    }
}
