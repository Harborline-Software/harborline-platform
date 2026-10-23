using System.Globalization;

namespace Harborline.Foundation.Localization;

/// <summary>Cardinal plural categories used by the locale contract.</summary>
public enum HarborlinePluralCategory
{
    /// <summary>Zero category.</summary>
    Zero,
    /// <summary>One category.</summary>
    One,
    /// <summary>Two category.</summary>
    Two,
    /// <summary>Few category.</summary>
    Few,
    /// <summary>Many category.</summary>
    Many,
    /// <summary>Other category.</summary>
    Other,
}

/// <summary>One provider-neutral number-format request.</summary>
/// <param name="Value">Number to format.</param>
/// <param name="Locale">Effective request locale.</param>
/// <param name="MinimumFractionDigits">Optional minimum fraction digits.</param>
/// <param name="MaximumFractionDigits">Optional maximum fraction digits.</param>
public sealed record HarborlineNumberFormatRequest(
    decimal Value,
    string Locale,
    int? MinimumFractionDigits = null,
    int? MaximumFractionDigits = null);

/// <summary>Formats a number for a locale scope.</summary>
public delegate string HarborlineNumberFormatter(HarborlineNumberFormatRequest request);

/// <summary>Provider-neutral localization scope for the Harborline App UI contract.</summary>
public sealed class HarborlineLocaleScope
{
    private static readonly HashSet<string> RtlPrimarySubtags =
        new(["ar", "he", "fa", "ur", "ps", "sd", "ug", "yi", "dv"], StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyDictionary<string, string> _catalog;
    private readonly HarborlineNumberFormatter? _numberFormatter;

    /// <summary>Creates an independent locale scope; only the formatter may inherit from a parent.</summary>
    public HarborlineLocaleScope(
        string locale = "en",
        string? direction = null,
        IReadOnlyDictionary<string, string>? catalog = null,
        HarborlineNumberFormatter? numberFormatter = null,
        HarborlineLocaleScope? parent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        Locale = locale;
        Direction = direction ?? DirectionForLocale(locale);
        _catalog = catalog ?? new Dictionary<string, string>();
        _numberFormatter = numberFormatter ?? parent?._numberFormatter;
    }

    /// <summary>Effective BCP-47 locale tag.</summary>
    public string Locale { get; }
    /// <summary>Effective ltr or rtl direction.</summary>
    public string Direction { get; }
    /// <summary>Scope-owned replacement catalog.</summary>
    public IReadOnlyDictionary<string, string> Catalog => _catalog;

    /// <summary>Derives direction from the exact supported RTL primary subtags.</summary>
    public static string DirectionForLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var primary = locale.Split('-', 2)[0];
        return RtlPrimarySubtags.Contains(primary) ? "rtl" : "ltr";
    }

    /// <summary>Resolves an override, frozen English default, or visibly returns the key.</summary>
    public string Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_catalog.TryGetValue(key, out var value)) return value;
        return HarborlineDefaultStrings.Values.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>Resolves and interpolates a string with optional instance precedence.</summary>
    public string ResolveString(
        string key,
        IReadOnlyDictionary<string, object?>? variables = null,
        string? instanceOverride = null) =>
        HarborlineDefaultStrings.Interpolate(instanceOverride ?? Resolve(key), variables);

    /// <summary>
    /// Selects the CLDR cardinal plural category for this scope's locale, evaluating the count as a
    /// default formatter shows it: trailing fraction zeros are removed, so 1.0m selects like 1 (as
    /// React's tn does for a JavaScript number, which has no scale).
    /// </summary>
    public HarborlinePluralCategory PluralCategory(decimal count) => CldrPluralRules.Select(Locale, count);

    /// <summary>
    /// Selects the CLDR cardinal plural category for a count displayed with exactly
    /// <paramref name="visibleFractionDigits"/> fraction digits (rounded half away from zero), using
    /// exact CLDR visible-digit operands: in en, 1 shown as "1.0" is other.
    /// </summary>
    public HarborlinePluralCategory PluralCategory(decimal count, int visibleFractionDigits) =>
        CldrPluralRules.Select(Locale, count, visibleFractionDigits);

    /// <summary>Resolves the category key, then other, then the invariant count without trailing fraction zeros.</summary>
    public string ResolvePlural(decimal count, IReadOnlyDictionary<HarborlinePluralCategory, string> keys) =>
        SelectKey(PluralCategory(count), keys) ?? TrimFractionZeros(count.ToString(CultureInfo.InvariantCulture));

    /// <summary>Resolves the category key for a count shown with fixed fraction digits, then other, then that count text.</summary>
    public string ResolvePlural(decimal count, int visibleFractionDigits, IReadOnlyDictionary<HarborlinePluralCategory, string> keys) =>
        SelectKey(PluralCategory(count, visibleFractionDigits), keys)
            ?? Math.Round(count, visibleFractionDigits, MidpointRounding.AwayFromZero)
                .ToString("F" + visibleFractionDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private string? SelectKey(HarborlinePluralCategory category, IReadOnlyDictionary<HarborlinePluralCategory, string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.TryGetValue(category, out var key) || keys.TryGetValue(HarborlinePluralCategory.Other, out key)
            ? Resolve(key)
            : null;
    }

    private static string TrimFractionZeros(string text) =>
        text.Contains('.', StringComparison.Ordinal) ? text.TrimEnd('0').TrimEnd('.') : text;

    /// <summary>Formats through the inherited host formatter or the projection runtime.</summary>
    public string FormatNumber(
        decimal value,
        int? minimumFractionDigits = null,
        int? maximumFractionDigits = null,
        string? locale = null)
    {
        var effectiveLocale = string.IsNullOrWhiteSpace(locale) ? Locale : locale;
        var request = new HarborlineNumberFormatRequest(value, effectiveLocale!, minimumFractionDigits, maximumFractionDigits);
        if (_numberFormatter is not null) return _numberFormatter(request);

        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(effectiveLocale!); }
        catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo("en"); }
        var min = Math.Clamp(minimumFractionDigits ?? 0, 0, 99);
        var max = Math.Clamp(maximumFractionDigits ?? Math.Max(min, 3), min, 99);
        var wire = value.ToString($"N{max}", culture);
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var index = wire.LastIndexOf(separator, StringComparison.Ordinal);
        while (index >= 0 && wire.EndsWith('0') && wire.Length - index - separator.Length > min)
            wire = wire[..^1];
        if (wire.EndsWith(separator, StringComparison.Ordinal)) wire = wire[..^separator.Length];
        return wire;
    }
}
