using Harborline.Foundation.Localization;

namespace Harborline.UIAdapters.Blazor.Localization;

public sealed class HarborlineLocaleContext
{
    internal const string LoadingKey = "common.loading";
    internal HarborlineLocaleContext(HarborlineLocaleScope scope) => Scope = scope;
    internal HarborlineLocaleScope Scope { get; }
    public string Locale => Scope.Locale;
    public string Direction => Scope.Direction;
    public IReadOnlyDictionary<string, string> Catalog => Scope.Catalog;
    public string Translate(string key) => Scope.Resolve(key);
    public string ResolveString(string key, IReadOnlyDictionary<string, object?>? variables = null, string? instanceOverride = null) => Scope.ResolveString(key, variables, instanceOverride);
    public HarborlinePluralCategory PluralCategory(decimal count) => Scope.PluralCategory(count);
    public string ResolvePlural(decimal count, IReadOnlyDictionary<HarborlinePluralCategory, string> keys) => Scope.ResolvePlural(count, keys);
    public string FormatNumber(decimal value, int? minimumFractionDigits = null, int? maximumFractionDigits = null, string? locale = null) => Scope.FormatNumber(value, minimumFractionDigits, maximumFractionDigits, locale);
}
