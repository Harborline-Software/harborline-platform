#nullable enable
#pragma warning disable CS1591 // Maintained wire bindings are documented by the module interface.

using System.Text.Json.Serialization;
using Harborline.Contracts.Serialization;
using Harborline.Contracts.Presentation;
using Harborline.Contracts.Text;

namespace Harborline.Contracts.Rules;

[JsonConverter(typeof(ClosedEnumJsonConverter<RuleTier>))]
public enum RuleTier
{
    [JsonStringEnumMemberName("JsonSchema")]
    JsonSchema,
    [JsonStringEnumMemberName("JsonLogic")]
    JsonLogic,
    [JsonStringEnumMemberName("PowerFx")]
    PowerFx,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<RuleScope>))]
public enum RuleScope
{
    [JsonStringEnumMemberName("Field")]
    Field,
    [JsonStringEnumMemberName("Section")]
    Section,
    [JsonStringEnumMemberName("Schema")]
    Schema,
    [JsonStringEnumMemberName("Row")]
    Row,
    [JsonStringEnumMemberName("Table")]
    Table,
}

[JsonConverter(typeof(ClosedEnumJsonConverter<RuleActionKind>))]
public enum RuleActionKind
{
    [JsonStringEnumMemberName("Visibility")]
    Visibility,
    [JsonStringEnumMemberName("Required")]
    Required,
    [JsonStringEnumMemberName("ReadOnly")]
    ReadOnly,
    [JsonStringEnumMemberName("Validate")]
    Validate,
    [JsonStringEnumMemberName("Compute")]
    Compute,
    [JsonStringEnumMemberName("Presentation")]
    Presentation,
    [JsonStringEnumMemberName("Options")]
    Options,
}

public sealed record RuleDefinition
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public required string Id { get; init; }

    [JsonPropertyName("tier")]
    [JsonRequired]
    public required RuleTier Tier { get; init; }

    [JsonPropertyName("scope")]
    [JsonRequired]
    public required RuleScope Scope { get; init; }

    [JsonPropertyName("scopeTarget")]
    [JsonRequired]
    public required string ScopeTarget { get; init; }

    [JsonPropertyName("expression")]
    [JsonRequired]
    public required string Expression { get; init; }

    [JsonPropertyName("action")]
    [JsonRequired]
    public required RuleActionKind Action { get; init; }

    [JsonPropertyName("errorMessage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<InternationalizedText> ErrorMessage { get; init; }

    [JsonPropertyName("presentation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<PresentationHint> Presentation { get; init; }

}
