using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Model;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>The renderer-neutral state of an actual authoring/runtime preview result.</summary>
public enum RulesPreviewOutcomeKind
{
    Value,
    Validity,
    Visibility,
    Presentation,
    Refusal,
    Pending,
    Uncomputable,
}

/// <summary>
/// A named preview payload. Values are carried as canonical JSON text; refusal and uncomputable
/// states preserve the producer's stable code instead of manufacturing a successful value.
/// </summary>
public sealed record RulesPreviewOutcome(
    RulesPreviewOutcomeKind Kind,
    string RuleName,
    string MemberName,
    string? Value = null,
    string? Validity = null,
    string? Visibility = null,
    string? Presentation = null,
    string? Code = null);

/// <summary>Projects the real engine preview outcome without interpreting operators in a UI.</summary>
public static class RulesPreviewContract
{
    public static RulesPreviewOutcome FromPreview(PreviewResult preview, string ruleName, string memberName)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        var outcome = preview.Outcome;
        if (outcome is null) return new(RulesPreviewOutcomeKind.Uncomputable, ruleName, memberName,
            Code: "rule.uncomputable");
        return outcome.OutputType switch
        {
            OutputType.Value => Value(outcome.Value!, ruleName, memberName),
            OutputType.Validity => new(RulesPreviewOutcomeKind.Validity, ruleName, memberName,
                Validity: outcome.Validity!.Ok ? "valid" : "invalid", Code: outcome.Validity.Error?.Code),
            OutputType.Visibility => new(RulesPreviewOutcomeKind.Visibility, ruleName, memberName,
                Visibility: $"visible={outcome.Visibility!.Visible};required={outcome.Visibility.Required};readOnly={outcome.Visibility.ReadOnly}"),
            OutputType.Presentation => new(RulesPreviewOutcomeKind.Presentation, ruleName, memberName,
                Presentation: $"severity={outcome.Presentation!.Severity?.ToString() ?? "none"};style={outcome.Presentation.StyleToken ?? ""}"),
            OutputType.Options => Options(outcome.Options!, ruleName, memberName),
            _ => new(RulesPreviewOutcomeKind.Uncomputable, ruleName, memberName, Code: "rule.uncomputable"),
        };
    }

    public static RulesPreviewOutcome FromDiagnostic(RuleIntentDiagnostic diagnostic, string ruleName, string memberName)
        => new(RulesPreviewOutcomeKind.Uncomputable, ruleName, memberName, Code: diagnostic.Code);

    private static RulesPreviewOutcome Value(ComputedValue value, string ruleName, string memberName) => value.State switch
    {
        ValueState.Resolved => new(RulesPreviewOutcomeKind.Value, ruleName, memberName,
            Value: value.Value?.ToJsonString() ?? "null"),
        ValueState.Pending => new(RulesPreviewOutcomeKind.Pending, ruleName, memberName),
        ValueState.Error => new(RulesPreviewOutcomeKind.Refusal, ruleName, memberName, Code: value.Error?.Code),
        _ => new(RulesPreviewOutcomeKind.Uncomputable, ruleName, memberName, Code: "rule.uncomputable"),
    };

    private static RulesPreviewOutcome Options(OptionsOutcome options, string ruleName, string memberName) => options.State switch
    {
        ValueState.Resolved => new(RulesPreviewOutcomeKind.Value, ruleName, memberName,
            Value: new JsonArray((options.Options ?? Array.Empty<JsonNode?>()).Select(item => item?.DeepClone()).ToArray()).ToJsonString()),
        ValueState.Pending => new(RulesPreviewOutcomeKind.Pending, ruleName, memberName),
        ValueState.Error => new(RulesPreviewOutcomeKind.Refusal, ruleName, memberName, Code: options.Error?.Code),
        _ => new(RulesPreviewOutcomeKind.Uncomputable, ruleName, memberName, Code: "rule.uncomputable"),
    };
}
