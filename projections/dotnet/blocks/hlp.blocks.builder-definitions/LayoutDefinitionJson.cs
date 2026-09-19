using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.BuilderDefinitions;

/// <summary>Canonical, projection-neutral Layout definition JSON.</summary>
public static class LayoutDefinitionJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>Serializes a Layout definition to canonical UTF-8 JSON.</summary>
    /// <param name="definition">The definition to serialize.</param>
    /// <returns>The canonical JSON document with a trailing newline.</returns>
    public static byte[] SerializeCanonical(LayoutDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var source = JsonSerializer.SerializeToNode(definition, Options)
            ?? throw new JsonException("The Layout definition serialized to no JSON value.");
        var canonical = Canonicalize(source);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) canonical.WriteTo(writer, Options);
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>Deserializes a canonical or compatible Layout JSON document.</summary>
    /// <param name="json">The UTF-8 JSON document.</param>
    /// <returns>The projection-neutral Layout definition.</returns>
    public static LayoutDefinition Deserialize(ReadOnlySpan<byte> json)
    {
        var definition = JsonSerializer.Deserialize<LayoutDefinition>(json, Options)
            ?? throw new JsonException("The Layout definition payload is null.");
        return definition;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    private static JsonNode Canonicalize(JsonNode node) => node switch
    {
        JsonObject value => new JsonObject(value
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(
                property.Key,
                property.Value is null ? null : Canonicalize(property.Value)))),
        JsonArray value => new JsonArray(value
            .Select(item => item is null ? null : Canonicalize(item))
            .ToArray()),
        _ => node.DeepClone(),
    };
}
