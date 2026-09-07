using Harborline.Contracts.Forms;

namespace Harborline.Foundation.RuleEngine;

internal static class RuleDefinitionFactory
{
    public static RuleDefinition Create(
        string Id,
        RuleTier Tier,
        RuleScope Scope,
        string ScopeTarget,
        string Expression,
        RuleActionKind Action,
        InternationalizedText? ErrorMessage = null,
        PresentationHint? Presentation = null) => new()
    {
        Id = Id,
        Tier = Tier,
        Scope = Scope,
        ScopeTarget = ScopeTarget,
        Expression = Expression,
        Action = Action,
        ErrorMessage = ErrorMessage is null ? default : Optional<InternationalizedText>.Some(ErrorMessage),
        Presentation = Presentation is null ? default : Optional<PresentationHint>.Some(Presentation),
    };

    public static InternationalizedText Text(string DefaultLocale, IReadOnlyDictionary<string, string> Values) => new()
    {
        DefaultLocale = DefaultLocale,
        Values = Values,
    };

    public static PresentationHint Hint(
        string? Severity = null,
        InternationalizedText? Badge = null,
        string? StyleToken = null) => new()
    {
        Severity = Severity switch
        {
            "info" => PresentationHintSeverityValue.Info,
            "warn" => PresentationHintSeverityValue.Warn,
            "error" => PresentationHintSeverityValue.Error,
            null => default(Optional<PresentationHintSeverityValue>),
            _ => throw new ArgumentOutOfRangeException(nameof(Severity)),
        },
        Badge = Badge is null ? default : Optional<InternationalizedText>.Some(Badge),
        StyleToken = StyleToken is null ? default : Optional<string>.Some(StyleToken),
    };
}
