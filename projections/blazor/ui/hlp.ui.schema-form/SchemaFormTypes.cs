using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Forms;

public sealed record SchemaFormText(string DefaultLocale, IReadOnlyDictionary<string, string> Values)
{
    public static SchemaFormText From(string value, string locale = "en") =>
        new(locale, new Dictionary<string, string>(StringComparer.Ordinal) { [locale] = value });
}

public static class SchemaFormTextResolver
{
    public static string Resolve(SchemaFormText? text, IReadOnlyList<string> localeChain, string fallback = "")
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

        if (text.Values.TryGetValue(text.DefaultLocale, out var declaredDefault)) return declaredDefault;
        return text.Values.Values.FirstOrDefault() ?? fallback;
    }
}

public sealed record SchemaFormOption(string Value, SchemaFormText Label, bool Disabled = false);

public sealed record SchemaFormPresentation(
    SchemaFormText? Badge = null,
    string? Severity = null,
    string? StyleToken = null);

public sealed record SchemaFormField
{
    public required string Name { get; init; }
    public required SchemaFormText Label { get; init; }
    public SchemaFormText? HelpText { get; init; }
    public bool IsSensitive { get; init; }
    public bool IsReadable { get; init; } = true;
    public string? ControlHint { get; init; } = "text";
    public string? ValueKind { get; init; }
    public bool Required { get; init; }
    public bool ReadOnly { get; init; }
    public IReadOnlyList<SchemaFormOption> Options { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Config { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
    public SchemaFormPresentation? Presentation { get; init; }
}

public abstract record SchemaFormItem(string Key);

public sealed record SchemaFormFieldItem(string ItemKey, SchemaFormField Field) : SchemaFormItem(ItemKey);

public sealed record SchemaFormGroupItem(
    string ItemKey,
    IReadOnlyList<SchemaFormItem> Items,
    SchemaFormText? Title = null) : SchemaFormItem(ItemKey);

public sealed record SchemaFormCardinality(int Min = 0, int? Max = null);

public sealed record SchemaFormCollectionItem(
    string ItemKey,
    IReadOnlyList<SchemaFormItem> Items,
    SchemaFormText? Title = null,
    SchemaFormCardinality? Cardinality = null) : SchemaFormItem(ItemKey);

public enum SchemaFormContentKind
{
    Paragraph,
    Heading,
}

public sealed record SchemaFormContentNode(SchemaFormContentKind Kind, SchemaFormText Text);

public sealed record SchemaFormContentItem(
    string ItemKey,
    IReadOnlyList<SchemaFormContentNode> Content) : SchemaFormItem(ItemKey);

public enum SchemaFormActionKind
{
    ScrollToSection,
    OpenUrl,
}

public sealed record SchemaFormAction(
    SchemaFormActionKind Kind,
    SchemaFormText Label,
    string? SectionId = null,
    string? Url = null);

public sealed record SchemaFormActionItem(string ItemKey, SchemaFormAction Action) : SchemaFormItem(ItemKey);

public sealed record SchemaFormSection
{
    public required string Id { get; init; }
    public required SchemaFormText Title { get; init; }
    public IReadOnlyList<SchemaFormField> Fields { get; init; } = [];
    public IReadOnlyList<SchemaFormItem>? Items { get; init; }
}

public sealed record SchemaFormView
{
    public required string FormId { get; init; }
    public required string Version { get; init; }
    public SchemaFormText? Title { get; init; }
    public SchemaFormText? Description { get; init; }
    public required IReadOnlyList<SchemaFormSection> Sections { get; init; }
}

public sealed record SchemaFormValidationError(string JsonPointer, string Message, string Kind = "Schema");

public sealed record SchemaFormValidationResult(bool IsValid, IReadOnlyList<SchemaFormValidationError> Errors)
{
    public static SchemaFormValidationResult Valid { get; } = new(true, []);
}

public sealed record SchemaFormStrings
{
    public string Submit { get; init; } = "Submit";
    public string Submitting { get; init; } = "Submitting…";
    public string ErrorSummaryTitle { get; init; } = "Please fix the following errors";
    public string Redacted { get; init; } = "Hidden";
    public string Empty { get; init; } = "This form has no fields to display.";
    public string AddItem { get; init; } = "Add";
    public string RemoveItem { get; init; } = "Remove";
    public string ItemLabel { get; init; } = "Item {n}";
    public string ControlUnavailable { get; init; } = "Control unavailable: {control}";
    public string SubmitBlocked { get; init; } = "Submission is unavailable while rules are pending or errored.";
}

public enum SchemaFormRuleValueState
{
    Resolved,
    Pending,
    Error,
}

public sealed record SchemaFormRuleValue(SchemaFormRuleValueState State, object? Value = null);

public sealed record SchemaFormVisibility(bool Visible = true, bool Required = false, bool ReadOnly = false);

public sealed record SchemaFormRuleEvaluation
{
    public IReadOnlyDictionary<string, SchemaFormVisibility> Visibility { get; init; } =
        new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, SchemaFormRuleValue> Values { get; init; } =
        new Dictionary<string, SchemaFormRuleValue>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, SchemaFormPresentation> Presentations { get; init; } =
        new Dictionary<string, SchemaFormPresentation>(StringComparer.Ordinal);
    public bool HasPending { get; init; }
    public bool IsSaveBlocked { get; init; }
}

public sealed record SchemaFormRuleInstance(
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyDictionary<string, object?> Tables);

public interface ISchemaFormRuleGraph
{
    SchemaFormRuleEvaluation EvaluateInstance(SchemaFormRuleInstance instance);
}

public sealed record SchemaFormControlArgs(
    SchemaFormField Field,
    IReadOnlyList<string> LocaleChain,
    object? Value,
    string StringValue,
    bool HasError,
    bool Required,
    bool Disabled,
    SchemaFormStrings Strings,
    EventCallback<object?> ValueChanged,
    string LabelId,
    string? DescribedBy);

public delegate RenderFragment SchemaFormControlRenderer(SchemaFormControlArgs args);

public sealed class SchemaFormControlRegistry
{
    private readonly IReadOnlyDictionary<string, SchemaFormControlRenderer> renderers;

    public SchemaFormControlRegistry(
        SchemaFormControlRenderer text,
        IReadOnlyDictionary<string, SchemaFormControlRenderer>? renderers = null)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        var merged = new Dictionary<string, SchemaFormControlRenderer>(StringComparer.OrdinalIgnoreCase);
        if (renderers is not null)
        {
            foreach (var pair in renderers) merged[pair.Key] = pair.Value;
        }

        merged["text"] = Text;
        this.renderers = merged;
    }

    public SchemaFormControlRenderer Text { get; }

    public bool TryGet(string hint, out SchemaFormControlRenderer renderer) =>
        renderers.TryGetValue(hint, out renderer!);
}

public static class SchemaFormErrorCodes
{
    public const string UnavailableValue = "schema-form.unavailable-value";
    public const string SubmitBlocked = "schema-form.submit-blocked";
}
