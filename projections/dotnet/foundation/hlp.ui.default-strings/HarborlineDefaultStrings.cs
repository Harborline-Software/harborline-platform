using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Harborline.Foundation.Localization;

/// <summary>The exact pinned English catalog and neutral single-brace interpolation behavior.</summary>
public static partial class HarborlineDefaultStrings
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Catalog = new(LoadCatalog);

    /// <summary>The complete immutable 713-key English fallback catalog.</summary>
    public static IReadOnlyDictionary<string, string> Values => Catalog.Value;

    /// <summary>Returns the exact default value for a known key.</summary>
    public static string Get(string key) => Values.TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException($"Unknown Harborline string key '{key}'.");

    /// <summary>Interpolates supplied string or numeric values and leaves unknown placeholders visible.</summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, object?>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (variables is null) return template;
        return Placeholder().Replace(template, match => variables.TryGetValue(match.Groups[1].Value, out var value)
            ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            : match.Value);
    }

    private static IReadOnlyDictionary<string, string> LoadCatalog()
    {
        const string suffix = ".default-strings.json";
        var assembly = typeof(HarborlineDefaultStrings).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidOperationException("Default string catalog resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException("Default string catalog could not be read.");
    }

    [GeneratedRegex("\\{(\\w+)\\}")]
    private static partial Regex Placeholder();
}
