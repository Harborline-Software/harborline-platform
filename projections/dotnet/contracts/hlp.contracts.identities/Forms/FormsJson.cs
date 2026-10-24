#nullable enable
#pragma warning disable CS1591 // Maintained wire bindings are documented by the module interface.

using System.Text.Json;
using System.Text.Json.Serialization;
using Harborline.Contracts.Authorization;
using Harborline.Contracts.Serialization;

namespace Harborline.Contracts.Forms;

public sealed class ControlHintJsonConverter : JsonConverter<ControlHint>
{
    public override ControlHint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? new(reader.GetString()!) : throw new JsonException("ControlHint must be a string.");
    public override void Write(Utf8JsonWriter writer, ControlHint value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

public sealed class LayoutGapJsonConverter : JsonConverter<LayoutGap>
{
    public override LayoutGap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) && LayoutGap.AllowedValues.Contains(value)
            ? new(value) : throw new JsonException("Unknown closed LayoutGap value.");
    public override void Write(Utf8JsonWriter writer, LayoutGap value, JsonSerializerOptions options) => writer.WriteNumberValue(value.Value);
}

public sealed class FormsWireException(string code, string type, string? detail = null)
    : JsonException($"{code}: {type}{(detail is null ? string.Empty : $" ({detail})")}")
{
    public string Code { get; } = code;
    public string WireType { get; } = type;
    public string? Detail { get; } = detail;
}

public static partial class FormsJson
{
    private static readonly JsonDocument Model = JsonDocument.Parse(ModelJson);

    public static JsonSerializerOptions CreateOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
    };

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static T Deserialize<T>(string json)
    {
        using var document = JsonDocument.Parse(json);
        Validate(typeof(T).Name, document.RootElement);
        return JsonSerializer.Deserialize<T>(document.RootElement, Options)
            ?? throw new FormsWireException("invalid-null", typeof(T).Name);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static void Validate(string type, JsonElement value)
    {
        if (!Model.RootElement.GetProperty("declarations").TryGetProperty(type, out var shape))
            throw new FormsWireException("unknown-type", type);
        ValidateShape(shape, value, type);
    }

    private static void ValidateShape(JsonElement shape, JsonElement value, string type)
    {
        var kind = shape.GetProperty("kind").GetString();
        switch (kind)
        {
            case "ref":
                Validate(shape.GetProperty("name").GetString()!, value);
                return;
            case "string":
            case "openString":
                Require(value.ValueKind == JsonValueKind.String, "invalid-string", type);
                return;
            case "number":
                Require(value.ValueKind == JsonValueKind.Number, "invalid-number", type);
                return;
            case "boolean":
                Require(value.ValueKind is JsonValueKind.True or JsonValueKind.False, "invalid-boolean", type);
                return;
            case "unknown":
                return;
            case "literal":
                Require(JsonElement.DeepEquals(value, shape.GetProperty("value")), "unknown-closed-value", type);
                return;
            case "enum":
                Require(shape.GetProperty("values").EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)), "unknown-closed-value", type);
                return;
            case "nullable":
                if (value.ValueKind != JsonValueKind.Null) ValidateShape(shape.GetProperty("value"), value, type);
                return;
            case "union":
                foreach (var member in shape.GetProperty("members").EnumerateArray())
                {
                    try { ValidateShape(member, value, type); return; } catch (FormsWireException) { }
                }
                throw new FormsWireException("unknown-closed-value", type);
            case "array":
                Require(value.ValueKind == JsonValueKind.Array, "invalid-array", type);
                foreach (var item in value.EnumerateArray()) ValidateShape(shape.GetProperty("element"), item, type);
                return;
            case "record":
                Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
                foreach (var property in value.EnumerateObject()) ValidateShape(shape.GetProperty("value"), property.Value, type);
                return;
            case "object":
                ValidateObject(shape, value, type);
                return;
            case "discriminatedUnion":
                ValidateDiscriminatedUnion(shape, value, type);
                return;
            case "exclude":
                ValidateShape(shape.GetProperty("source"), value, type);
                var excluded = shape.GetProperty("excluded");
                if (excluded.GetProperty("kind").GetString() == "literal")
                    Require(!JsonElement.DeepEquals(excluded.GetProperty("value"), value), "excluded-closed-value", type);
                else
                    Require(!excluded.GetProperty("values").EnumerateArray().Any(candidate => JsonElement.DeepEquals(candidate, value)), "excluded-closed-value", type);
                return;
            case "constant":
                Require(JsonElement.DeepEquals(value, shape.GetProperty("value")), "constant-mismatch", type);
                return;
            default:
                throw new FormsWireException("unsupported-shape", type, kind);
        }
    }

    private static void ValidateObject(JsonElement shape, JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
        foreach (var property in shape.GetProperty("properties").EnumerateObject())
        {
            var present = value.TryGetProperty(property.Name, out var propertyValue);
            if (!present)
            {
                Require(property.Value.GetProperty("optional").GetBoolean(), "missing-required-property", type, property.Name);
                continue;
            }
            ValidateShape(property.Value.GetProperty("shape"), propertyValue, $"{type}.{property.Name}");
        }
    }

    private static void ValidateDiscriminatedUnion(JsonElement shape, JsonElement value, string type)
    {
        Require(value.ValueKind == JsonValueKind.Object, "invalid-object", type);
        var discriminatorName = shape.GetProperty("discriminator").GetString()!;
        Require(value.TryGetProperty(discriminatorName, out var discriminator) && discriminator.ValueKind == JsonValueKind.String, "unknown-discriminator", type);
        var discriminatorValue = discriminator.GetString()!;
        var variants = shape.GetProperty("variants");
        Require(variants.TryGetProperty(discriminatorValue, out var variant), "unknown-discriminator", type, discriminatorValue);
        var selected = variant.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var reserved = variants.EnumerateObject().SelectMany(candidate => candidate.Value.GetProperty("properties").EnumerateObject().Select(property => property.Name)).ToHashSet(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (property.Name != discriminatorName && reserved.Contains(property.Name) && !selected.Contains(property.Name))
                throw new FormsWireException("variant-member-mismatch", type, property.Name);
        ValidateObject(variant, value, type);
    }


    private static void Require(bool condition, string code, string type, string? detail = null)
    {
        if (!condition) throw new FormsWireException(code, type, detail);
    }
}
