using Harborline.Foundation.Localization;

namespace Harborline.UIAdapters.Blazor.Localization;

/// <summary>Locale, text direction and translations for the current UI scope, wrapped for components.</summary>
public sealed class HarborlineLocaleContext
{
    internal const string LoadingKey = "common.loading";
    internal HarborlineLocaleContext(HarborlineLocaleScope scope) => Scope = scope;
    internal HarborlineLocaleScope Scope { get; }
/// <summary>The active locale code.</summary>
    public string Locale => Scope.Locale;
/// <summary>The text direction of the active locale, ltr or rtl.</summary>
    public string Direction => Scope.Direction;
/// <summary>The translation catalog of the active locale, by key.</summary>
    public IReadOnlyDictionary<string, string> Catalog => Scope.Catalog;
/// <summary>Returns the translation for a key.</summary>
    public string Translate(string key) => Scope.Resolve(key);
/// <summary>Resolves a key to text, filling variables and honouring an instance override.</summary>
    public string ResolveString(string key, IReadOnlyDictionary<string, object?>? variables = null, string? instanceOverride = null) => Scope.ResolveString(key, variables, instanceOverride);
/// <summary>Returns the plural category of a count under the rules of the active locale.</summary>
    public HarborlinePluralCategory PluralCategory(decimal count) => Scope.PluralCategory(count);
/// <summary>Picks the plural form of a message for a count from the supplied keys.</summary>
    public string ResolvePlural(decimal count, IReadOnlyDictionary<HarborlinePluralCategory, string> keys) => Scope.ResolvePlural(count, keys);
/// <summary>Formats a number for the active locale, optionally with a fraction-digit range or another locale.</summary>
    public string FormatNumber(decimal value, int? minimumFractionDigits = null, int? maximumFractionDigits = null, string? locale = null) => Scope.FormatNumber(value, minimumFractionDigits, maximumFractionDigits, locale);
}
