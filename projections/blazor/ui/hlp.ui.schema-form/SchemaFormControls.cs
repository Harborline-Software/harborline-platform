using Microsoft.AspNetCore.Components;

namespace Harborline.UIAdapters.Blazor.Components.Forms;

/// <summary>The built-in controls that schema forms render fields with, keyed by control hint.</summary>
public static class SchemaFormControls
{
    private static readonly string[] Hints =
    [
        "text", "textarea", "number", "integer", "select", "multiselect", "checkbox", "boolean",
        "boolean-toggle", "date", "datetime", "time", "currency", "percentage", "phone", "email",
        "url", "readonly", "hidden",
    ];

    private static readonly IReadOnlyDictionary<string, SchemaFormControlRenderer> Defaults =
        Hints.ToDictionary(hint => hint, BuiltIn, StringComparer.OrdinalIgnoreCase);

    /// <summary>The control hints the built-in controls understand.</summary>
    public static IReadOnlyCollection<string> BuiltInHints => Hints;

    /// <summary>Builds a control registry from the built-ins, with optional replacements by hint.</summary>
    public static SchemaFormControlRegistry CreateRegistry(
        IReadOnlyDictionary<string, SchemaFormControlRenderer>? overrides = null)
    {
        var merged = new Dictionary<string, SchemaFormControlRenderer>(Defaults, StringComparer.OrdinalIgnoreCase);
        if (overrides is not null)
        {
            foreach (var pair in overrides) merged[pair.Key] = pair.Value;
        }

        var text = overrides is not null && overrides.TryGetValue("text", out var customText)
            ? customText
            : Defaults["text"];
        return new SchemaFormControlRegistry(text, merged);
    }

    /// <summary>Whether a control hint can display the given value.</summary>
    public static bool AcceptsValue(string hint, object? value)
    {
        if (value is null || hint.Equals("hidden", StringComparison.OrdinalIgnoreCase)) return true;
        if (hint is "checkbox" or "boolean" or "boolean-toggle") return value is bool;
        if (hint.Equals("multiselect", StringComparison.OrdinalIgnoreCase))
        {
            return value is System.Collections.IEnumerable values
                && value is not string
                && values.Cast<object?>().All(IsScalar);
        }

        return value is string || IsNumber(value);
    }

    private static bool IsScalar(object? value) => value is string or bool || value is not null && IsNumber(value);

    private static bool IsNumber(object value) => value is
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    private static SchemaFormControlRenderer BuiltIn(string hint) => args => builder =>
    {
        builder.OpenComponent<HarborlineSchemaControl>(0);
        builder.AddAttribute(1, nameof(HarborlineSchemaControl.Hint), hint);
        builder.AddAttribute(2, nameof(HarborlineSchemaControl.Args), args);
        builder.CloseComponent();
    };
}
