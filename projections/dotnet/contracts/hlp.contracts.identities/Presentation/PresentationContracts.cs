#nullable enable
#pragma warning disable CS1591 // Maintained wire bindings are documented by the module interface.

using System.Text.Json.Serialization;
using Harborline.Contracts.Serialization;
using Harborline.Contracts.Text;

namespace Harborline.Contracts.Presentation;

public sealed record PresentationHint
{
    [JsonPropertyName("severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PresentationHintSeverityValue> Severity { get; init; }

    [JsonPropertyName("badge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<InternationalizedText> Badge { get; init; }

    [JsonPropertyName("styleToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> StyleToken { get; init; }

}

[JsonConverter(typeof(ClosedEnumJsonConverter<PresentationHintSeverityValue>))]
public enum PresentationHintSeverityValue
{
    [JsonStringEnumMemberName("info")]
    Info,
    [JsonStringEnumMemberName("warn")]
    Warn,
    [JsonStringEnumMemberName("error")]
    Error,
}
