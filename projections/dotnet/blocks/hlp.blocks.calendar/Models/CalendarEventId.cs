using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.Calendar.Models;

/// <summary>Strongly-typed identifier for a <see cref="CalendarEvent"/>. UUIDv7.</summary>
[JsonConverter(typeof(CalendarEventIdJsonConverter))]
public readonly record struct CalendarEventId(Guid Value)
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString();
    /// <summary>Creates a new time-sortable event identifier.</summary>
    public static CalendarEventId NewId() => new(Guid.CreateVersion7());
    /// <summary>Converts the value object to its underlying GUID.</summary>
    public static implicit operator Guid(CalendarEventId id) => id.Value;
}

internal sealed class CalendarEventIdJsonConverter : JsonConverter<CalendarEventId>
{
    public override CalendarEventId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString() ?? throw new JsonException("CalendarEventId must be a non-null string.");
        return new CalendarEventId(Guid.Parse(str));
    }

    public override void Write(Utf8JsonWriter writer, CalendarEventId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString());
}
