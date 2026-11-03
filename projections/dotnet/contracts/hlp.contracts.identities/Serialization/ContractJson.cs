#nullable enable
#pragma warning disable CS1591 // Maintained wire bindings are documented by the module interface.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Contracts.Serialization;

#pragma warning disable CA1716, CA1000 // Preserve the existing Optional<T> construction API.
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    private readonly T? _value;

    private Optional(T? value)
    {
        HasValue = true;
        _value = value;
    }

    public bool HasValue { get; }
    public T? Value => HasValue ? _value : throw new InvalidOperationException("The optional value is absent.");
    public static Optional<T> Some(T? value) => new(value);
    public static implicit operator Optional<T>(T? value) => Some(value);
}
#pragma warning restore CA1716, CA1000

public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
}

public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Optional<T>.Some(JsonSerializer.Deserialize<T>(ref reader, options));

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (!value.HasValue) throw new JsonException("An absent Optional value must be omitted by its containing property.");
        JsonSerializer.Serialize(writer, value.Value, options);
    }
}

public sealed class ClosedEnumJsonConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> FromWire = typeof(TEnum)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(field => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name, field => (TEnum)field.GetValue(null)!);
    private static readonly Dictionary<TEnum, string> ToWire = FromWire.ToDictionary(row => row.Value, row => row.Key);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !FromWire.TryGetValue(reader.GetString()!, out var value))
            throw new JsonException($"Unknown closed {typeof(TEnum).Name} value.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!ToWire.TryGetValue(value, out var wire)) throw new JsonException($"Unknown closed {typeof(TEnum).Name} value.");
        writer.WriteStringValue(wire);
    }
}
