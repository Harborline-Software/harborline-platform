using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.Assets.Common;

/// <summary>Opaque tenant identifier for multi-tenant data isolation.</summary>
[JsonConverter(typeof(TenantIdJsonConverter))]
public readonly record struct TenantId
{
    private const string ReservedPrefix = "__";

    /// <summary>Creates a non-sentinel tenant identifier.</summary>
    /// <exception cref="ArgumentException">The value is empty, whitespace, or reserved.</exception>
    public TenantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.StartsWith(ReservedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Tenant identifiers beginning with '__' are reserved for system sentinels.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>The opaque wire value.</summary>
    public string Value { get; init; }

    /// <summary>True for default values and reserved system sentinels.</summary>
    public bool IsSystemSentinel =>
        string.IsNullOrWhiteSpace(Value) || Value.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    /// <summary>The system/background-operation sentinel.</summary>
    public static TenantId System { get; } = new() { Value = "__system__" };

    /// <inheritdoc />
    public override string ToString() => Value ?? string.Empty;

    /// <summary>
    /// Parses a string into a tenant identifier, applying the same validation as the public
    /// constructor.
    /// </summary>
    /// <remarks>
    /// Prefer this over the implicit string conversion below at boundary points — endpoint binding,
    /// query-string parsing, deserialization — so the null, whitespace and reserved-prefix guards
    /// fire once, audibly, at a named call site. An implicit conversion applies the same guards but
    /// gives the reader nothing to search for when one of them throws.
    /// <para>
    /// This implements the existing <c>tenant.construct</c> operation under a name; it adds no
    /// behaviour, so the frozen interface revision is unchanged.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">The value is empty, whitespace, or reserved.</exception>
    public static TenantId FromString(string value) => new(value);

    // There is deliberately NO implicit string -> TenantId conversion. It ran the null, whitespace
    // and reserved-prefix guards invisibly: the validation was identical to FromString's, but when
    // it threw, the call site held no token a reader could search for. Construction is now always
    // spelled -- FromString at boundaries, the constructor elsewhere. Removed at interface revision
    // 2; the API's ADR 0091 reached the same conclusion and worked around it with a helper instead.

    /// <summary>Converts a tenant identifier to its wire value.</summary>
    /// <remarks>
    /// The outbound direction is kept: projecting a validated identifier to its string carries no
    /// validation and so cannot fail silently. Only the inbound direction was a hazard.
    /// </remarks>
    public static implicit operator string(TenantId id) => id.Value;
}

internal sealed class TenantIdJsonConverter : JsonConverter<TenantId>
{
    public override TenantId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("TenantId must be a non-null string.");
        }

        try
        {
            return new TenantId(reader.GetString()!);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, TenantId value, JsonSerializerOptions options)
    {
        if (value.IsSystemSentinel)
        {
            throw new JsonException("Default and system TenantId values cannot cross a tenant wire boundary.");
        }

        writer.WriteStringValue(value.Value);
    }
}
