using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Harborline.Foundation.Assistance;

/// <summary>The untrusted pilot.proposal/3 wire envelope, never a dispatch receipt.</summary>
public sealed record AssistanceProposalEnvelope(string Schema, string Surface, string Command, JsonElement Args);

/// <summary>Identifies the composing binding and proposal digest, never proposal content or context.</summary>
public sealed record AssistanceDelegationActor(string BindingKey, string BindingVersion, string ProviderId, string ModelVersion, string ProposalDigest);

/// <summary>The surface and nonempty command scope covered by an approval.</summary>
public sealed record AssistanceApprovalScope(string Surface, IReadOnlyList<string> Commands);

/// <summary>A per-act decision or the standing approval covering the act.</summary>
public sealed record AssistanceDisposition(
    string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ApprovalId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Approver = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AssistanceApprovalScope? Scope = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpiresAt = null);

/// <summary>Distinguishes delegated acts from hand-performed acts, whose actor is null.</summary>
public sealed record AssistanceDelegationRecord(string Subject, AssistanceDelegationActor? Actor, AssistanceDisposition Disposition);

/// <summary>A user-owned scoped approval; expiry and revocation coverage are checked at act time, never in a pack.</summary>
public sealed record AssistanceStandingApproval(string Id, string Approver, string BindingKey, AssistanceApprovalScope Scope, string? Schedule, string ExpiresAt, string? RevokedAt);

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
        using var document = JsonDocument.Parse(json);
        Canonical(document.RootElement);
        var value = JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("A wire value is required.");
        Validate(value);
        var disposition = value is AssistanceDelegationRecord ? document.RootElement.GetProperty("disposition") : document.RootElement;
        if ((value is AssistanceDisposition || value is AssistanceDelegationRecord)
            && disposition.GetProperty("kind").GetString() != "standing" && disposition.EnumerateObject().Count() != 1)
            throw new JsonException("A per-act disposition carries only kind.");
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
        switch (value)
        {
            case AssistanceProposalEnvelope proposal:
                if (proposal.Schema != "pilot.proposal/3" || proposal.Surface is null || proposal.Command is null || proposal.Args.ValueKind == JsonValueKind.Undefined)
                    throw new JsonException("Invalid proposal envelope.");
                Quote(proposal.Surface);
                Quote(proposal.Command);
                break;
            case AssistanceDelegationActor actor:
                Text(actor.BindingKey); Text(actor.BindingVersion); Text(actor.ProviderId); Text(actor.ModelVersion);
                if (actor.ProposalDigest is null || !Regex.IsMatch(actor.ProposalDigest, @"\Asha256:[0-9a-f]{64}\z", RegexOptions.CultureInvariant))
                    throw new JsonException("Invalid proposal digest.");
                break;
            case AssistanceApprovalScope scope:
                Text(scope.Surface);
                if (scope.Commands is null || scope.Commands.Count == 0) throw new JsonException("Scope commands are required.");
                foreach (var command in scope.Commands) Text(command);
                break;
            case AssistanceDisposition disposition:
                if (disposition.Kind == "standing")
                {
                    Text(disposition.ApprovalId); Text(disposition.Approver); Validate(disposition.Scope); Instant(disposition.ExpiresAt);
                }
                else if (disposition.Kind is not ("approve" or "dismiss" or "clarify" or "reject")
                    || disposition.ApprovalId is not null || disposition.Approver is not null || disposition.Scope is not null || disposition.ExpiresAt is not null)
                    throw new JsonException("Invalid disposition.");
                break;
            case AssistanceDelegationRecord record:
                Text(record.Subject);
                if (record.Actor is not null) Validate(record.Actor);
                Validate(record.Disposition);
                break;
            case AssistanceStandingApproval approval:
                Text(approval.Id); Text(approval.Approver); Text(approval.BindingKey); Validate(approval.Scope); Instant(approval.ExpiresAt);
                if (approval.Schedule is not null) Text(approval.Schedule);
                if (approval.RevokedAt is not null) Instant(approval.RevokedAt);
                break;
            default:
                throw new JsonException("A supported wire value is required.");
        }
    }

    private static void Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new JsonException("A nonempty wire string is required.");
        Quote(value);
    }

    private static void Instant(string? value)
    {
        if (value is null || !Regex.IsMatch(value, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|\+00:00)\z", RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParseExact(value,
            ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant) || instant.Offset != TimeSpan.Zero)
            throw new JsonException("A UTC ISO 8601 instant is required.");
    }

    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => CanonicalObject(value),
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.String => Quote(value.GetString()!),
        JsonValueKind.Number => Number(value),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => throw new JsonException("Invalid JSON value."),
    };

    private static string CanonicalObject(JsonElement value)
    {
        var members = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        if (members.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != members.Length)
            throw new JsonException("Duplicate JSON members refuse.");
        return "{" + string.Join(",", members.Select(property => Quote(property.Name) + ":" + Canonical(property.Value))) + "}";
    }

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

    private static string Number(JsonElement value)
    {
        var raw = value.GetRawText();
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var exponent = exponentIndex < 0 ? BigInteger.Zero : BigInteger.Parse(raw[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var fractionLength = point < 0 ? 0 : mantissa.Length - point - 1;
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal).TrimStart('-', '0');
        if (digits.Length == 0) return "0";
        var significant = digits.TrimEnd('0');
        var power = exponent - fractionLength + digits.Length - significant.Length;
        if (power < 0) return Number(value.GetDouble());
        if (significant.Length + power > 16) throw new JsonException("args-integer-out-of-range");
        var integer = long.Parse(significant + new string('0', (int)power), CultureInfo.InvariantCulture);
        if (integer > 9007199254740991L) throw new JsonException("args-integer-out-of-range");
        return (raw[0] == '-' ? "-" : "") + integer.ToString(CultureInfo.InvariantCulture);
    }

    private static string Number(double value)
    {
        if (!double.IsFinite(value)) throw new JsonException("A finite JSON number is required.");
        if (value == 0) return "0";
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentIndex = text.IndexOf('E', StringComparison.Ordinal);
        var exponent = exponentIndex < 0 ? 0 : int.Parse(text[(exponentIndex + 1)..], CultureInfo.InvariantCulture);
        var mantissa = exponentIndex < 0 ? text : text[..exponentIndex];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
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
