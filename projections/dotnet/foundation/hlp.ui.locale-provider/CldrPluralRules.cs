using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Harborline.Foundation.Localization;

/// <summary>
/// The plural operands of UTS #35 Part 3 section 5.1.1 ("Plural Operand Meanings") for one number,
/// read from its visible decimal digits so that 1 and 1.0 stay distinct (v = 0 versus v = 1).
/// </summary>
internal readonly record struct CldrPluralOperands(
    decimal N, decimal I, int V, int W, decimal F, decimal T, int E)
{
    /// <summary>
    /// Operands of a decimal as a default formatter shows it: trailing fraction zeros are removed,
    /// so an incidental scale (1.0m from numeric(18,2) or qty * 0.50m) selects like 1.
    /// </summary>
    public static CldrPluralOperands FromDecimal(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (text.Contains('.', StringComparison.Ordinal)) text = text.TrimEnd('0').TrimEnd('.');
        return Parse(text);
    }

    /// <summary>
    /// Operands of a decimal displayed with exactly <paramref name="visibleFractionDigits"/> fraction
    /// digits (rounded half away from zero, as Intl's halfExpand does), so 1 shown as "1.0" has v = 1.
    /// </summary>
    public static CldrPluralOperands FromDecimal(decimal value, int visibleFractionDigits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleFractionDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(visibleFractionDigits, 28);
        var rounded = Math.Round(value, visibleFractionDigits, MidpointRounding.AwayFromZero);
        return Parse(rounded.ToString("F" + visibleFractionDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Operands of a source number in CLDR sample syntax: optional sign, digits, an optional
    /// fraction, and an optional compact exponent written c or e (1.2c3 is 1200 with e = 3).
    /// </summary>
    public static CldrPluralOperands Parse(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var text = source.Trim();
        if (text[0] is '-' or '+') text = text[1..];
        var exponent = 0;
        var marker = text.IndexOfAny(['c', 'e']);
        if (marker >= 0)
        {
            exponent = int.Parse(text[(marker + 1)..], NumberStyles.None, CultureInfo.InvariantCulture);
            text = text[..marker];
        }
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = point < 0 ? text : text[..point];
        var fractionDigits = point < 0 ? string.Empty : text[(point + 1)..];
        if (integerDigits.Length == 0 || !integerDigits.All(char.IsAsciiDigit) || !fractionDigits.All(char.IsAsciiDigit)
            || (point >= 0 && fractionDigits.Length == 0))
            throw new FormatException($"'{source}' is not a CLDR plural operand source number.");

        // A compact exponent moves the decimal point right; the moved digits become integer digits.
        var shift = Math.Min(exponent, fractionDigits.Length);
        integerDigits += fractionDigits[..shift] + new string('0', exponent - shift);
        fractionDigits = fractionDigits[shift..];

        var trimmed = fractionDigits.TrimEnd('0');
        var integer = decimal.Parse(integerDigits, NumberStyles.None, CultureInfo.InvariantCulture);
        var fraction = fractionDigits.Length == 0 ? 0m : decimal.Parse(fractionDigits, NumberStyles.None, CultureInfo.InvariantCulture);
        var visible = trimmed.Length == 0 ? 0m : decimal.Parse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture);
        var n = fractionDigits.Length == 0
            ? integer
            : decimal.Parse($"{integerDigits}.{fractionDigits}", NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        return new CldrPluralOperands(n, integer, fractionDigits.Length, trimmed.Length, fraction, visible, exponent);
    }

    /// <summary>The value of one operand letter; c is the synonym of e.</summary>
    public decimal Get(char operand) => operand switch
    {
        'n' => N,
        'i' => I,
        'v' => V,
        'w' => W,
        'f' => F,
        't' => T,
        'c' or 'e' => E,
        _ => throw new ArgumentOutOfRangeException(nameof(operand), operand, "Unknown CLDR plural operand."),
    };
}

/// <summary>One relation: operand, optional modulus, = or !=, and a list of values and inclusive ranges.</summary>
internal sealed record CldrPluralRelation(char Operand, decimal? Modulus, bool Negated, IReadOnlyList<(decimal Low, decimal High)> Ranges)
{
    public bool Matches(in CldrPluralOperands operands)
    {
        var value = operands.Get(Operand);
        if (Modulus is { } modulus) value %= modulus;
        // UTS #35: "x = 2..4" is "x = 2,3,4", so a range only ever holds integers.
        var inList = value == decimal.Truncate(value) && Ranges.Any(range => value >= range.Low && value <= range.High);
        return inList != Negated;
    }
}

/// <summary>
/// One parsed CLDR plural condition in disjunctive form: or-separated groups of and-joined relations
/// ("and" binds tighter than "or"). An empty condition is the always-true other rule.
/// </summary>
internal sealed class CldrPluralCondition
{
    private readonly IReadOnlyList<IReadOnlyList<CldrPluralRelation>> _alternatives;

    private CldrPluralCondition(IReadOnlyList<IReadOnlyList<CldrPluralRelation>> alternatives) => _alternatives = alternatives;

    public bool IsEmpty => _alternatives.Count == 0;

    public bool Matches(in CldrPluralOperands operands)
    {
        if (IsEmpty) return true;
        foreach (var conjunction in _alternatives)
        {
            var all = true;
            foreach (var relation in conjunction)
            {
                if (!relation.Matches(operands)) { all = false; break; }
            }
            if (all) return true;
        }
        return false;
    }

    /// <summary>Parses rule text; everything from the first @integer or @decimal sample list is ignored.</summary>
    public static CldrPluralCondition Parse(string rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var at = rule.IndexOf('@', StringComparison.Ordinal);
        var tokens = Tokenize(at < 0 ? rule : rule[..at]);
        var position = 0;
        var alternatives = new List<IReadOnlyList<CldrPluralRelation>>();
        if (tokens.Count == 0) return new CldrPluralCondition(alternatives);
        while (true)
        {
            var conjunction = new List<CldrPluralRelation> { ParseRelation(tokens, ref position) };
            while (position < tokens.Count && tokens[position] == "and")
            {
                position++;
                conjunction.Add(ParseRelation(tokens, ref position));
            }
            alternatives.Add(conjunction);
            if (position == tokens.Count) break;
            Expect(tokens, ref position, "or");
        }
        return new CldrPluralCondition(alternatives);
    }

    private static CldrPluralRelation ParseRelation(List<string> tokens, ref int position)
    {
        var operand = Next(tokens, ref position);
        if (operand.Length != 1 || "niwvftce".IndexOf(operand[0], StringComparison.Ordinal) < 0)
            throw new FormatException($"Expected a plural operand, found '{operand}'.");
        decimal? modulus = null;
        if (Peek(tokens, position) == "%")
        {
            position++;
            modulus = ParseValue(Next(tokens, ref position));
            if (modulus == 0) throw new FormatException("A plural rule modulus must not be 0.");
        }
        var op = Next(tokens, ref position);
        if (op is not ("=" or "!=")) throw new FormatException($"Expected = or !=, found '{op}'.");
        var ranges = new List<(decimal, decimal)>();
        while (true)
        {
            var low = ParseValue(Next(tokens, ref position));
            var high = low;
            if (Peek(tokens, position) == "..")
            {
                position++;
                high = ParseValue(Next(tokens, ref position));
            }
            ranges.Add((low, high));
            if (Peek(tokens, position) != ",") break;
            position++;
        }
        return new CldrPluralRelation(operand[0], modulus, op == "!=", ranges);
    }

    private static decimal ParseValue(string token) =>
        token.Length > 0 && token.All(char.IsAsciiDigit)
            ? decimal.Parse(token, NumberStyles.None, CultureInfo.InvariantCulture)
            : throw new FormatException($"Expected a value, found '{token}'.");

    private static string? Peek(List<string> tokens, int position) => position < tokens.Count ? tokens[position] : null;

    private static string Next(List<string> tokens, ref int position) =>
        position < tokens.Count ? tokens[position++] : throw new FormatException("Unexpected end of plural rule.");

    private static void Expect(List<string> tokens, ref int position, string expected)
    {
        var actual = Next(tokens, ref position);
        if (actual != expected) throw new FormatException($"Expected '{expected}', found '{actual}'.");
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current)) { index++; continue; }
            if (char.IsAsciiDigit(current))
            {
                var start = index;
                while (index < text.Length && char.IsAsciiDigit(text[index])) index++;
                tokens.Add(text[start..index]);
            }
            else if (char.IsAsciiLetterLower(current))
            {
                var start = index;
                while (index < text.Length && char.IsAsciiLetterLower(text[index])) index++;
                tokens.Add(text[start..index]);
            }
            else if (current == '.' && index + 1 < text.Length && text[index + 1] == '.') { tokens.Add(".."); index += 2; }
            else if (current == '!' && index + 1 < text.Length && text[index + 1] == '=') { tokens.Add("!="); index += 2; }
            else if (current is '=' or '%' or ',') { tokens.Add(current.ToString()); index++; }
            else throw new FormatException($"Unexpected '{current}' in plural rule '{text}'.");
        }
        return tokens;
    }
}

/// <summary>The parsed cardinal rules of one CLDR locale, evaluated in category order.</summary>
internal sealed class CldrPluralRuleSet
{
    private static readonly (string Suffix, HarborlinePluralCategory Category)[] Order =
    [
        ("zero", HarborlinePluralCategory.Zero),
        ("one", HarborlinePluralCategory.One),
        ("two", HarborlinePluralCategory.Two),
        ("few", HarborlinePluralCategory.Few),
        ("many", HarborlinePluralCategory.Many),
    ];

    private readonly IReadOnlyList<(HarborlinePluralCategory Category, CldrPluralCondition Condition)> _rules;

    private CldrPluralRuleSet(string locale, IReadOnlyList<(HarborlinePluralCategory, CldrPluralCondition)> rules)
    {
        Locale = locale;
        _rules = rules;
    }

    /// <summary>The CLDR data key these rules came from, or root when none applied.</summary>
    public string Locale { get; }

    public static CldrPluralRuleSet Root { get; } = new("root", []);

    private const string RulePrefix = "pluralRule-count-";

    public static CldrPluralRuleSet Parse(string locale, IReadOnlyDictionary<string, string> rules)
    {
        foreach (var name in rules.Keys)
        {
            var suffix = name.StartsWith(RulePrefix, StringComparison.Ordinal) ? name[RulePrefix.Length..] : null;
            if (suffix != "other" && !Order.Any(entry => entry.Suffix == suffix))
                throw new FormatException($"CLDR rule {name} for {locale} is not a cardinal plural category.");
        }
        var parsed = new List<(HarborlinePluralCategory, CldrPluralCondition)>();
        foreach (var (suffix, category) in Order)
        {
            if (!rules.TryGetValue(RulePrefix + suffix, out var text)) continue;
            var condition = CldrPluralCondition.Parse(text);
            // Only other may be unconditional; an empty zero..many rule would swallow every number.
            if (condition.IsEmpty) throw new FormatException($"CLDR {suffix} rule for {locale} has no condition.");
            parsed.Add((category, condition));
        }
        if (rules.TryGetValue("pluralRule-count-other", out var other) && !CldrPluralCondition.Parse(other).IsEmpty)
            throw new FormatException($"CLDR other rule for {locale} must carry samples only.");
        return new CldrPluralRuleSet(locale, parsed);
    }

    public HarborlinePluralCategory Select(in CldrPluralOperands operands)
    {
        foreach (var (category, condition) in _rules)
        {
            if (condition.Matches(operands)) return category;
        }
        return HarborlinePluralCategory.Other;
    }
}

/// <summary>
/// CLDR cardinal plural rules (UTS #35 Part 3 "Language Plural Rules") from the embedded
/// cldr-core supplemental/plurals.json. Locale lookup is exact tag, then CLDR truncation
/// inheritance (pt-PT-x falls to pt-PT, then pt), else root, which has only the other category.
/// Each data key's rules are parsed once and cached; caller tags are resolved per call and never cached.
/// </summary>
internal static class CldrPluralRules
{
    internal const string ResourceName = "Harborline.Foundation.Localization.cldr.plurals.json";

    private static readonly Lazy<CldrPluralData> Data = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
    // Keyed only by CLDR data keys, never by caller tags: a tag can come from user input
    // (Accept-Language on Blazor Server), so caching by tag would grow without bound.
    private static readonly ConcurrentDictionary<string, CldrPluralRuleSet> ByKey = new(StringComparer.Ordinal);

    /// <summary>Legacy language codes replaced before lookup: iw is he, ji is yi, in is id.</summary>
    internal static readonly IReadOnlyDictionary<string, string> LegacyLanguageAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["iw"] = "he", ["ji"] = "yi", ["in"] = "id" };

    /// <summary>Parsed rule sets held in the cache; bounded by the number of data keys.</summary>
    internal static int CachedRuleSetCount => ByKey.Count;

    /// <summary>The CLDR release the embedded data declares.</summary>
    public static string CldrVersion => Data.Value.CldrVersion;

    /// <summary>Every locale key present in the embedded cardinal data.</summary>
    public static IReadOnlyList<string> Locales => [.. Data.Value.Rules.Keys];

    /// <summary>The raw rule text for one data key, as CLDR spells it.</summary>
    public static IReadOnlyDictionary<string, string> RawRules(string dataKey) => Data.Value.Rules[dataKey];

    public static HarborlinePluralCategory Select(string locale, decimal count) =>
        ForLocale(locale).Select(CldrPluralOperands.FromDecimal(count));

    public static HarborlinePluralCategory Select(string locale, decimal count, int visibleFractionDigits) =>
        ForLocale(locale).Select(CldrPluralOperands.FromDecimal(count, visibleFractionDigits));

    public static HarborlinePluralCategory Select(string locale, string sourceNumber) =>
        ForLocale(locale).Select(CldrPluralOperands.Parse(sourceNumber));

    /// <summary>Resolves a BCP-47 tag on every call, then returns the rule set cached for its data key.</summary>
    public static CldrPluralRuleSet ForLocale(string locale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        var key = ResolveKey(locale);
        return key is null ? CldrPluralRuleSet.Root : ByKey.GetOrAdd(key, static k => CldrPluralRuleSet.Parse(k, Data.Value.Rules[k]));
    }

    /// <summary>The data key a tag resolves to by exact match then truncation, or null for root.</summary>
    public static string? ResolveKey(string locale)
    {
        var candidate = locale.Trim().Replace('_', '-');
        // Legacy ISO 639 codes that Intl canonicalises and old Android/Java Accept-Language still sends.
        var first = candidate.IndexOf('-', StringComparison.Ordinal);
        var language = first < 0 ? candidate : candidate[..first];
        if (LegacyLanguageAliases.TryGetValue(language, out var replacement))
            candidate = replacement + (first < 0 ? string.Empty : candidate[first..]);
        while (candidate.Length > 0)
        {
            if (Data.Value.CanonicalKeys.TryGetValue(candidate, out var key)) return key;
            var dash = candidate.LastIndexOf('-');
            if (dash < 0) break;
            candidate = candidate[..dash];
        }
        return null;
    }

    private static CldrPluralData Load()
    {
        var assembly = typeof(CldrPluralRules).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("CLDR plural rule resource is missing; plural selection fails closed rather than guessing.");
        using var document = JsonDocument.Parse(stream);
        var supplemental = document.RootElement.GetProperty("supplemental");
        var version = supplemental.GetProperty("version").GetProperty("_cldrVersion").GetString()
            ?? throw new InvalidOperationException("CLDR plural rule resource has no version.");
        var rules = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var locale in supplemental.GetProperty("plurals-type-cardinal").EnumerateObject())
        {
            rules[locale.Name] = locale.Value.EnumerateObject().ToDictionary(rule => rule.Name, rule => rule.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        }
        return new CldrPluralData(version, rules, rules.Keys.ToDictionary(key => key, key => key, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record CldrPluralData(
        string CldrVersion,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Rules,
        IReadOnlyDictionary<string, string> CanonicalKeys);
}
