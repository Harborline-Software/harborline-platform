using System.Text.Json;
using System.Text.Json.Serialization;

namespace Harborline.Blocks.Aggregates;

/// <summary>Normative revision-1 JSON serializer configuration.</summary>
public static class AggregateJson
{
    /// <summary>Creates serializer options with camel-case names, closed string enums, and finite numbers.</summary><returns>A new mutable options instance.</returns>
    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
