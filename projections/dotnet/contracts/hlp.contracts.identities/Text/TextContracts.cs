#nullable enable
#pragma warning disable CS1591 // Maintained wire bindings are documented by the module interface.

using System.Text.Json.Serialization;

namespace Harborline.Contracts.Text;

public sealed record InternationalizedText
{
    [JsonPropertyName("defaultLocale")]
    [JsonRequired]
    public required string DefaultLocale { get; init; }

    [JsonPropertyName("values")]
    [JsonRequired]
    public required IReadOnlyDictionary<string, string> Values { get; init; }

}
