using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Foundation.Assets.Common;

/// <summary>Canonical identifier for an entity in the Harborline asset model.</summary>
[JsonConverter(typeof(EntityIdJsonConverter))]
public readonly record struct EntityId(string Scheme, string Authority, string LocalPart)
{
    /// <inheritdoc />
    public override string ToString() => $"{Scheme}:{Authority}/{LocalPart}";

    /// <summary>Parses <c>{scheme}:{authority}/{localPart}</c>.</summary>
    public static EntityId Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var colon = value.IndexOf(':', StringComparison.Ordinal);
        var slash = colon < 0 ? -1 : value.IndexOf('/', colon + 1);
        if (colon <= 0 || slash <= colon + 1 || slash == value.Length - 1)
        {
            throw Invalid(value);
        }

        var scheme = value[..colon];
        var authority = value[(colon + 1)..slash];
        var localPart = value[(slash + 1)..];
        if (string.IsNullOrWhiteSpace(scheme)
            || string.IsNullOrWhiteSpace(authority)
            || string.IsNullOrWhiteSpace(localPart))
        {
            throw Invalid(value);
        }

        return new EntityId(scheme, authority, localPart);
    }

    /// <summary>Attempts to parse a canonical entity identifier.</summary>
    public static bool TryParse(string? value, out EntityId id)
    {
        if (value is null)
        {
            id = default;
            return false;
        }

        try
        {
            id = Parse(value);
            return true;
        }
        catch (FormatException)
        {
            id = default;
            return false;
        }
    }

    private static FormatException Invalid(string value) =>
        new($"EntityId '{value}' must be of form scheme:authority/localPart with non-empty segments.");
}

internal sealed class EntityIdJsonConverter : JsonConverter<EntityId>
{
    public override EntityId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("EntityId must be a non-null string.");
        }

        try
        {
            return EntityId.Parse(reader.GetString()!);
        }
        catch (FormatException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, EntityId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
