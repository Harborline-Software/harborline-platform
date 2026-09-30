using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>Text with translations by locale, and the default locale to fall back to.</summary>
public sealed record SchemaFormText(string DefaultLocale, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Creates text in a single locale.</summary>
    public static SchemaFormText From(string value, string locale = "en") =>
        new(locale, new Dictionary<string, string>(StringComparer.Ordinal) { [locale] = value });
}

/// <summary>Picks the translation of a piece of schema form text.</summary>
public static class SchemaFormTextResolver
{
    /// <summary>Returns the text for the first locale in the chain that has it, otherwise the fallback.</summary>
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

/// <summary>A choice in a select field: its value, label and whether it is disabled.</summary>
public sealed record SchemaFormOption(string Value, SchemaFormText Label, bool Disabled = false);

/// <summary>Rule-driven presentation of a field: an optional badge, severity and style token.</summary>
public sealed record SchemaFormPresentation(
    SchemaFormText? Badge = null,
    string? Severity = null,
    string? StyleToken = null);

/// <summary>One field of a schema form: name, label, control, value kind, options, and read and required state.</summary>
public sealed record SchemaFormField
{
    /// <summary>The field name, used to bind and submit its value.</summary>
    public required string Name { get; init; }
    /// <summary>The label shown for the field.</summary>
    public required SchemaFormText Label { get; init; }
    /// <summary>Help text shown under the field.</summary>
    public SchemaFormText? HelpText { get; init; }
    /// <summary>Whether the value is sensitive and hidden unless the viewer is allowed to see it.</summary>
    public bool IsSensitive { get; init; }
    /// <summary>Whether the viewer may read the value; when false it is shown as redacted.</summary>
    public bool IsReadable { get; init; } = true;
    /// <summary>The control used to edit the value, text by default.</summary>
    public string? ControlHint { get; init; } = "text";
    /// <summary>The kind of value the field holds, used to check what the control accepts.</summary>
    public string? ValueKind { get; init; }
    /// <summary>Whether the field must have a value.</summary>
    public bool Required { get; init; }
    /// <summary>Whether the field cannot be edited.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>The choices offered by a select or choice control.</summary>
    public IReadOnlyList<SchemaFormOption> Options { get; init; } = [];
    /// <summary>The values the field may take, when the set is restricted.</summary>
    public IReadOnlyList<string>? PermittedValues { get; init; }
    /// <summary>Extra settings passed to the control of the field.</summary>
    public IReadOnlyDictionary<string, object?> Config { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
    /// <summary>The presentation the rules set for this field, such as a badge.</summary>
    public SchemaFormPresentation? Presentation { get; init; }
}

/// <summary>Base type for an item in a schema form section, identified by key.</summary>
public abstract record SchemaFormItem(string Key);

/// <summary>A section item that shows one field.</summary>
public sealed record SchemaFormFieldItem(string ItemKey, SchemaFormField Field) : SchemaFormItem(ItemKey);

/// <summary>A section item that groups other items under an optional title.</summary>
public sealed record SchemaFormGroupItem(
    string ItemKey,
    IReadOnlyList<SchemaFormItem> Items,
    SchemaFormText? Title = null) : SchemaFormItem(ItemKey);

/// <summary>How many entries a collection may hold: a minimum and an optional maximum.</summary>
public sealed record SchemaFormCardinality(int Min = 0, int? Max = null);

/// <summary>A section item holding a repeatable set of items, within an optional entry count.</summary>
public sealed record SchemaFormCollectionItem(
    string ItemKey,
    IReadOnlyList<SchemaFormItem> Items,
    SchemaFormText? Title = null,
    SchemaFormCardinality? Cardinality = null) : SchemaFormItem(ItemKey);

/// <summary>The kind of static content block shown in a schema form.</summary>
public enum SchemaFormContentKind
{
    /// <summary>Renders the content as body text.</summary>
    Paragraph,
    /// <summary>Renders the content as a section heading.</summary>
    Heading,
}

/// <summary>One piece of read-only content: its kind and text.</summary>
public sealed record SchemaFormContentNode(SchemaFormContentKind Kind, SchemaFormText Text);

/// <summary>A section item that shows read-only content such as paragraphs and headings.</summary>
public sealed record SchemaFormContentItem(
    string ItemKey,
    IReadOnlyList<SchemaFormContentNode> Content) : SchemaFormItem(ItemKey);

/// <summary>What a schema form action does when activated.</summary>
public enum SchemaFormActionKind
{
    /// <summary>Scrolls the form to the target section when activated.</summary>
    ScrollToSection,
    /// <summary>Opens the target URL when activated.</summary>
    OpenUrl,
}

/// <summary>A link-style action in a form: what it does, its label and the section or URL it targets.</summary>
public sealed record SchemaFormAction(
    SchemaFormActionKind Kind,
    SchemaFormText Label,
    string? SectionId = null,
    string? Url = null);

/// <summary>A section item that shows one action.</summary>
public sealed record SchemaFormActionItem(string ItemKey, SchemaFormAction Action) : SchemaFormItem(ItemKey);

/// <summary>One section of a schema form: id, title, fields and items.</summary>
public sealed record SchemaFormSection
{
    /// <summary>The section id, used to scroll to it.</summary>
    public required string Id { get; init; }
    /// <summary>The section title.</summary>
    public required SchemaFormText Title { get; init; }
    /// <summary>The fields shown in the section.</summary>
    public IReadOnlyList<SchemaFormField> Fields { get; init; } = [];
    /// <summary>The section items in order, which replace the plain field list when present.</summary>
    public IReadOnlyList<SchemaFormItem>? Items { get; init; }
}

/// <summary>A schema form to render: id, version, title, description and sections.</summary>
public sealed record SchemaFormView
{
    /// <summary>Id of the form.</summary>
    public required string FormId { get; init; }
    /// <summary>Version of the form definition.</summary>
    public required string Version { get; init; }
    /// <summary>The form title.</summary>
    public SchemaFormText? Title { get; init; }
    /// <summary>The form description.</summary>
    public SchemaFormText? Description { get; init; }
    /// <summary>The sections that make up the form.</summary>
    public required IReadOnlyList<SchemaFormSection> Sections { get; init; }
}

/// <summary>A validation error: the JSON pointer of the field, the message and the kind of error.</summary>
public sealed record SchemaFormValidationError(string JsonPointer, string Message, string Kind = "Schema");

/// <summary>The outcome of validating a form: whether it is valid and the errors found.</summary>
public sealed record SchemaFormValidationResult(bool IsValid, IReadOnlyList<SchemaFormValidationError> Errors)
{
    /// <summary>A result with no errors.</summary>
    public static SchemaFormValidationResult Valid { get; } = new(true, []);
}

/// <summary>The fixed text a schema form shows, such as button labels and messages.</summary>
public sealed record SchemaFormStrings
{
    /// <summary>Label of the submit button.</summary>
    public string Submit { get; init; } = "Submit";
    /// <summary>Label of the submit button while submitting.</summary>
    public string Submitting { get; init; } = "Submitting…";
    /// <summary>Heading of the error summary.</summary>
    public string ErrorSummaryTitle { get; init; } = "Please fix the following errors";
    /// <summary>Text shown in place of a hidden value.</summary>
    public string Redacted { get; init; } = "Hidden";
    /// <summary>Message shown when a form has no fields.</summary>
    public string Empty { get; init; } = "This form has no fields to display.";
    /// <summary>Label of the button that adds a collection entry.</summary>
    public string AddItem { get; init; } = "Add";
    /// <summary>Label of the button that removes a collection entry.</summary>
    public string RemoveItem { get; init; } = "Remove";
    /// <summary>Label of a collection entry, with {n} replaced by its number.</summary>
    public string ItemLabel { get; init; } = "Item {n}";
    /// <summary>Message shown when the control of a field is not registered, with {control} replaced by its hint.</summary>
    public string ControlUnavailable { get; init; } = "Control unavailable: {control}";
    /// <summary>Message shown when submitting is blocked because rules are pending or have errors.</summary>
    public string SubmitBlocked { get; init; } = "Submission is unavailable while rules are pending or errored.";
}

/// <summary>Whether a schema form rule's value is resolved, still pending, or failed.</summary>
public enum SchemaFormRuleValueState
{
    /// <summary>The rule input has a value and the rule can be evaluated.</summary>
    Resolved,
    /// <summary>The rule input is still loading, so the result is not final.</summary>
    Pending,
    /// <summary>The rule input failed to resolve, so the rule cannot be evaluated.</summary>
    Error,
}

/// <summary>A value computed by a rule: whether it is known, pending or in error, and the value.</summary>
public sealed record SchemaFormRuleValue(SchemaFormRuleValueState State, object? Value = null);

/// <summary>Rule-computed visibility of a field: whether it is visible, required and read-only.</summary>
public sealed record SchemaFormVisibility(bool Visible = true, bool Required = false, bool ReadOnly = false);

/// <summary>The result of evaluating the rules for a form: field visibility, computed values, presentations and blocking state.</summary>
public sealed record SchemaFormRuleEvaluation
{
    /// <summary>Visibility, required and read-only state per field.</summary>
    public IReadOnlyDictionary<string, SchemaFormVisibility> Visibility { get; init; } =
        new Dictionary<string, SchemaFormVisibility>(StringComparer.Ordinal);
    /// <summary>Values computed by rules, by field.</summary>
    public IReadOnlyDictionary<string, SchemaFormRuleValue> Values { get; init; } =
        new Dictionary<string, SchemaFormRuleValue>(StringComparer.Ordinal);
    /// <summary>Rule-driven presentations, by field.</summary>
    public IReadOnlyDictionary<string, SchemaFormPresentation> Presentations { get; init; } =
        new Dictionary<string, SchemaFormPresentation>(StringComparer.Ordinal);
    /// <summary>Whether any rule is still being evaluated.</summary>
    public bool HasPending { get; init; }
    /// <summary>Whether saving is blocked because a rule is pending or in error.</summary>
    public bool IsSaveBlocked { get; init; }
}

/// <summary>The form data a rule evaluation reads: field values and table values.</summary>
public sealed record SchemaFormRuleInstance(
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyDictionary<string, object?> Tables);

/// <summary>Evaluates the rules of a form against its current values.</summary>
public interface ISchemaFormRuleGraph
{
    /// <summary>Evaluates the rules for the given form instance and returns the result.</summary>
    SchemaFormRuleEvaluation EvaluateInstance(SchemaFormRuleInstance instance);
}

/// <summary>Everything a control renderer receives for a field: value, error, required and disabled state, strings and callbacks.</summary>
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

/// <summary>Represents a callback used by the Blazor UI contract.</summary>
public delegate RenderFragment SchemaFormControlRenderer(SchemaFormControlArgs args);

/// <summary>Holds the controls a schema form can render, found by control hint.</summary>
public sealed class SchemaFormControlRegistry
{
    private readonly IReadOnlyDictionary<string, SchemaFormControlRenderer> renderers;

    /// <summary>Creates a registry from the text control, which is always present, and further controls by hint.</summary>
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

    /// <summary>The plain text control, used when no control matches a hint.</summary>
    public SchemaFormControlRenderer Text { get; }

    /// <summary>Finds the control for a hint, ignoring case.</summary>
    public bool TryGet(string hint, out SchemaFormControlRenderer renderer) =>
        renderers.TryGetValue(hint, out renderer!);
}

/// <summary>Error codes a schema form reports.</summary>
public static class SchemaFormErrorCodes
{
    /// <summary>Code for a value that could not be shown or read.</summary>
    public const string UnavailableValue = "schema-form.unavailable-value";
    /// <summary>Code for a submit stopped because rules are pending or have errors.</summary>
    public const string SubmitBlocked = "schema-form.submit-blocked";
}
