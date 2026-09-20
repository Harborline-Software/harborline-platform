using Harborline.Contracts.Forms;

namespace Harborline.Foundation.Forms.UI;

/// <summary>Additive UI-only hints that never become canonical Forms wire authority.</summary>
public sealed record FormViewRendererHints(
    string? ValueKind = null,
    bool? Required = null,
    IReadOnlyList<FormViewOption>? Options = null,
    IReadOnlyDictionary<string, object?>? Config = null,
    bool? ReadOnly = null,
    PresentationOutcome? Presentation = null);

/// <summary>A caller-authored select option used only by a renderer adapter.</summary>
public sealed record FormViewOption(string Value, object? Label = null);

/// <summary>The canonical field plus normalized render hints; canonical rule evidence is retained.</summary>
public sealed record FormViewRenderField(
    FormViewField Canonical,
    string? ValueKind,
    bool Required,
    IReadOnlyList<FormViewOption>? Options,
    IReadOnlyDictionary<string, object?>? Config,
    bool ReadOnly,
    PresentationOutcome? Presentation);

/// <summary>Normalizes the canonical server rule projection without weakening redaction.</summary>
public static class FormViewBinding
{
    /// <summary>Returns renderer hints derived from the canonical field without changing its wire evidence.</summary>
    public static FormViewRenderField Normalize(FormViewField field, FormViewRendererHints? hints = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        hints ??= new();
        var rules = field.Rules.HasValue ? field.Rules.Value : null;
        var redacted = field.IsSensitive || !field.IsReadable;
        var readOnly = rules?.ReadOnly
            ?? hints.ReadOnly
            ?? (field.ReadOnly.HasValue && field.ReadOnly.Value);
        var basePresentation = hints.Presentation
            ?? (field.Presentation.HasValue ? field.Presentation.Value : null);
        var presentation = rules is null ? basePresentation : FromRules(rules, basePresentation);
        var required = rules?.Required ?? hints.Required ?? false;
        var safeCanonical = redacted && field.Value.HasValue
            ? field with { Value = default }
            : field;

        return new(
            safeCanonical,
            hints.ValueKind,
            required,
            hints.Options,
            hints.Config,
            ReadOnly: redacted || readOnly,
            Presentation: presentation);
    }

    private static PresentationOutcome? FromRules(FormViewFieldRules rules, PresentationOutcome? basis)
    {
        if (!rules.PresentationSeverity.HasValue && !rules.PresentationBadge.HasValue && !rules.PresentationStyleToken.HasValue) return basis;
        basis ??= new();
        return basis with
        {
            Severity = rules.PresentationSeverity.HasValue ? Optional<Severity?>.Some(rules.PresentationSeverity.Value) : basis.Severity,
            Badge = rules.PresentationBadge.HasValue ? Optional<InternationalizedText>.Some(rules.PresentationBadge.Value) : basis.Badge,
            StyleToken = rules.PresentationStyleToken.HasValue ? Optional<string>.Some(rules.PresentationStyleToken.Value) : basis.StyleToken,
        };
    }
}

/// <summary>Exact TypeScript-compatible resolution for localized form text.</summary>
public static class FormViewText
{
    /// <summary>Resolves exact locale tags, primary subtags, the declared default, first value, then fallback.</summary>
    public static string Resolve(InternationalizedText? text, IReadOnlyList<string> localeChain, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(localeChain);
        if (text is null) return fallback;
        foreach (var tag in localeChain)
        {
            if (text.Values.TryGetValue(tag, out var exact)) return exact;
            var separator = tag.IndexOf('-');
            var primary = separator < 0 ? tag : tag[..separator];
            if (primary.Length > 0 && text.Values.TryGetValue(primary, out var baseLanguage)) return baseLanguage;
        }

        if (text.DefaultLocale.Length > 0 && text.Values.TryGetValue(text.DefaultLocale, out var defaultText)) return defaultText;
        return text.Values.Values.FirstOrDefault() ?? fallback;
    }
}
