using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Canonical, provider-neutral JSON for the released navigation definition.</summary>
public static class ReleasedNavigationDefinitionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Serializes a definition as canonical UTF-8 JSON with a trailing newline.</summary>
    public static byte[] SerializeCanonical(ReleasedNavigationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var source = JsonSerializer.SerializeToNode(definition, Options)
            ?? throw new JsonException("The released navigation definition serialized to no JSON value.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Canonicalize(source).WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Deserializes a released navigation definition from provider-neutral JSON.</summary>
    public static ReleasedNavigationDefinition Deserialize(ReadOnlySpan<byte> json)
        => JsonSerializer.Deserialize<ReleasedNavigationDefinition>(json, Options)
            ?? throw new JsonException("The released navigation definition payload is null.");

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value.Select(item => item is null ? null : Canonicalize(item)).ToArray()),
        _ => node.DeepClone(),
    };
}
