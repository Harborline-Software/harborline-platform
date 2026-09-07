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

    /// <summary>Selects the cardinal plural category for this scope.</summary>
    public HarborlinePluralCategory PluralCategory(decimal count)
    {
        var primary = Locale.Split('-', 2)[0].ToLowerInvariant();
        var integer = decimal.Truncate(count);
        var isInteger = count == integer;
        var n = Math.Abs(integer);
        return primary switch
        {
            "ar" when !isInteger => HarborlinePluralCategory.Other,
            "ar" when n == 0 => HarborlinePluralCategory.Zero,
            "ar" when n == 1 => HarborlinePluralCategory.One,
            "ar" when n == 2 => HarborlinePluralCategory.Two,
            "ar" when n % 100 is >= 3 and <= 10 => HarborlinePluralCategory.Few,
            "ar" when n % 100 is >= 11 and <= 99 => HarborlinePluralCategory.Many,
            "ar" => HarborlinePluralCategory.Other,
            "pl" when isInteger && n == 1 => HarborlinePluralCategory.One,
            "pl" when isInteger && n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) => HarborlinePluralCategory.Few,
            "pl" when isInteger && (n != 1 && (n % 10 is 0 or 1 || n % 10 is >= 5 and <= 9 || n % 100 is >= 12 and <= 14)) => HarborlinePluralCategory.Many,
            "pl" => HarborlinePluralCategory.Other,
            "ru" when isInteger && n % 10 == 1 && n % 100 != 11 => HarborlinePluralCategory.One,
            "ru" when isInteger && n % 10 is >= 2 and <= 4 && n % 100 is not (>= 12 and <= 14) => HarborlinePluralCategory.Few,
            "ru" when isInteger && (n % 10 == 0 || n % 10 is >= 5 and <= 9 || n % 100 is >= 11 and <= 14) => HarborlinePluralCategory.Many,
            "ru" => HarborlinePluralCategory.Other,
            "ja" => HarborlinePluralCategory.Other,
            _ when count == 1 => HarborlinePluralCategory.One,
            _ => HarborlinePluralCategory.Other,
        };
    }

    /// <summary>Resolves the category key, then other, then the invariant count.</summary>
    public string ResolvePlural(decimal count, IReadOnlyDictionary<HarborlinePluralCategory, string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var category = PluralCategory(count);
        if (keys.TryGetValue(category, out var key) || keys.TryGetValue(HarborlinePluralCategory.Other, out key))
            return Resolve(key);
        return count.ToString(CultureInfo.InvariantCulture);
    }

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
