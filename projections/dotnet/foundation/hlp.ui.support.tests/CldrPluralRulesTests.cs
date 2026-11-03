using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Harborline.Foundation.Localization;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

/// <summary>CLDR cardinal plural rules per UTS #35 Part 3, checked against CLDR's own samples and Node's Intl.</summary>
public sealed class CldrPluralRulesTests
{
    private const string DataPath = "projections/dotnet/foundation/hlp.ui.locale-provider/cldr/plurals.json";

    [Theory]
    [InlineData("1", "1", "1", 0, 0, "0", "0", 0)]
    [InlineData("1.0", "1", "1", 1, 0, "0", "0", 0)]
    [InlineData("1.00", "1", "1", 2, 0, "0", "0", 0)]
    [InlineData("1.3", "1.3", "1", 1, 1, "3", "3", 0)]
    [InlineData("1.30", "1.3", "1", 2, 1, "30", "3", 0)]
    [InlineData("1.03", "1.03", "1", 2, 2, "3", "3", 0)]
    [InlineData("1.230", "1.23", "1", 3, 2, "230", "23", 0)]
    [InlineData("-2.5", "2.5", "2", 1, 1, "5", "5", 0)]
    [InlineData("1.2c3", "1200", "1200", 0, 0, "0", "0", 3)]
    [InlineData("1c6", "1000000", "1000000", 0, 0, "0", "0", 6)]
    [InlineData("1.0000001c6", "1000000.1", "1000000", 1, 1, "1", "1", 6)]
    [InlineData("123c2", "12300", "12300", 0, 0, "0", "0", 2)]
    public void OperandsFollowUts35(string source, string n, string i, int v, int w, string f, string t, int e)
    {
        var operands = CldrPluralOperands.Parse(source);
        Assert.Equal(decimal.Parse(n, CultureInfo.InvariantCulture), operands.N);
        Assert.Equal(decimal.Parse(i, CultureInfo.InvariantCulture), operands.I);
        Assert.Equal(v, operands.V);
        Assert.Equal(w, operands.W);
        Assert.Equal(decimal.Parse(f, CultureInfo.InvariantCulture), operands.F);
        Assert.Equal(decimal.Parse(t, CultureInfo.InvariantCulture), operands.T);
        Assert.Equal(e, operands.E);
        Assert.Equal(e, operands.Get('c'));
    }

    [Fact]
    public void DefaultSelectionIgnoresTrailingZerosAndExplicitDigitsAreExact()
    {
        Assert.Equal(CldrPluralOperands.Parse("1"), CldrPluralOperands.FromDecimal(1.0m));
        Assert.Equal(CldrPluralOperands.Parse("2.5"), CldrPluralOperands.FromDecimal(2.50m));
        Assert.Equal(CldrPluralOperands.Parse("100"), CldrPluralOperands.FromDecimal(100m));
        Assert.Equal(CldrPluralOperands.Parse("1.0"), CldrPluralOperands.FromDecimal(1m, 1));
        Assert.Equal(CldrPluralOperands.Parse("2.50"), CldrPluralOperands.FromDecimal(2.5m, 2));
        Assert.Equal(CldrPluralOperands.Parse("3"), CldrPluralOperands.FromDecimal(2.5m, 0));
        Assert.Equal(CldrPluralOperands.Parse("1.13"), CldrPluralOperands.FromDecimal(1.125m, 2));
        var en = new HarborlineLocaleScope("en");
        Assert.Equal(HarborlinePluralCategory.One, en.PluralCategory(1m));
        Assert.Equal(HarborlinePluralCategory.One, en.PluralCategory(1.0m));
        Assert.Equal(HarborlinePluralCategory.One, en.PluralCategory(0.50m * 2));
        Assert.Equal(HarborlinePluralCategory.Other, en.PluralCategory(1m, 1));
        Assert.Equal(HarborlinePluralCategory.Other, en.PluralCategory(1.0m, 1));
        Assert.Equal(HarborlinePluralCategory.One, en.PluralCategory(1.0m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => en.PluralCategory(1m, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => en.PluralCategory(1m, 29));
        var keys = new Dictionary<HarborlinePluralCategory, string> { [HarborlinePluralCategory.One] = "fixture.one", [HarborlinePluralCategory.Other] = "fixture.other" };
        Assert.Equal("fixture.one", en.ResolvePlural(1.00m, keys));
        Assert.Equal("fixture.other", en.ResolvePlural(1m, 2, keys));
        Assert.Equal("1", en.ResolvePlural(1.00m, new Dictionary<HarborlinePluralCategory, string>()));
        Assert.Equal("1.00", en.ResolvePlural(1m, 2, new Dictionary<HarborlinePluralCategory, string>()));
    }

    [Theory]
    // and binds tighter than or: "a or b and c" is "a or (b and c)".
    [InlineData("n = 1 or n = 2 and v = 1", "1", true)]
    [InlineData("n = 1 or n = 2 and v = 1", "2", false)]
    [InlineData("n = 1 or n = 2 and v = 1", "2.0", true)]
    [InlineData("n = 2 and v = 1 or n = 1", "1", true)]
    // Ranges are inclusive at both ends and only hold integers.
    [InlineData("n = 3..5", "3", true)]
    [InlineData("n = 3..5", "5", true)]
    [InlineData("n = 3..5", "6", false)]
    [InlineData("n = 3..5", "4.5", false)]
    [InlineData("n = 3..5", "4.0", true)]
    // Value lists, modulus, and negation.
    [InlineData("n % 10 = 2,4,7..8", "17", true)]
    [InlineData("n % 10 = 2,4,7..8", "13", false)]
    [InlineData("n % 100 != 11..19", "111", false)]
    [InlineData("n % 100 != 11..19", "121", true)]
    [InlineData("n%10=1", "21", true)]
    [InlineData("i = 0 and v != 0 @integer 0 @decimal 0.1", "0.5", true)]
    [InlineData("e = 0 and i % 1000000 = 0 or e != 0..5", "1c6", true)]
    [InlineData("e = 0 and i % 1000000 = 0 or e != 0..5", "1c3", false)]
    [InlineData("", "42", true)]
    public void ConditionsFollowUts35Syntax(string rule, string number, bool expected) =>
        Assert.Equal(expected, CldrPluralCondition.Parse(rule).Matches(CldrPluralOperands.Parse(number)));

    [Theory]
    [InlineData("n in 1")]
    [InlineData("n = ")]
    [InlineData("x = 1")]
    [InlineData("n = 1 or")]
    [InlineData("n = 1 xor n = 2")]
    [InlineData("n % 0 = 1")]
    [InlineData("i % 00 = 0 or n = 1")]
    public void MalformedConditionsAreRefused(string rule) =>
        Assert.Throws<FormatException>(() => CldrPluralCondition.Parse(rule));

    [Theory]
    // zero..many must carry a condition; only other may be unconditional.
    [InlineData("pluralRule-count-one", " @integer 1")]
    [InlineData("pluralRule-count-many", "")]
    // Unknown category suffixes and non-rule names are refused, not ignored.
    [InlineData("pluralRule-count-several", "n = 3")]
    [InlineData("pluralRule-count-One", "n = 1")]
    [InlineData("ordinal-count-one", "n = 1")]
    // other carries samples only.
    [InlineData("pluralRule-count-other", "n = 1")]
    public void MalformedRuleSetsAreRefused(string name, string rule)
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal) { ["pluralRule-count-other"] = " @integer 0~15", [name] = rule };
        Assert.Throws<FormatException>(() => CldrPluralRuleSet.Parse("xx", rules));
    }

    [Fact]
    public void WellFormedRuleSetParses()
    {
        var rules = new Dictionary<string, string>(StringComparer.Ordinal) { ["pluralRule-count-one"] = "n = 1 @integer 1", ["pluralRule-count-other"] = " @integer 0, 2~16" };
        var set = CldrPluralRuleSet.Parse("xx", rules);
        Assert.Equal(HarborlinePluralCategory.One, set.Select(CldrPluralOperands.Parse("1")));
        Assert.Equal(HarborlinePluralCategory.Other, set.Select(CldrPluralOperands.Parse("2")));
    }

    [Theory]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("PT-pt", "pt-PT")]
    [InlineData("pt-PT-x-harbor", "pt-PT")]
    [InlineData("pt_PT", "pt-PT")]
    [InlineData("pt", "pt")]
    [InlineData("pt-BR", "pt")]
    [InlineData("sr-Latn-RS", "sr")]
    [InlineData("en-US-u-nu-arab", "en")]
    [InlineData("zz", null)]
    [InlineData("not_a_locale", null)]
    // Legacy language codes are replaced before lookup.
    [InlineData("iw", "he")]
    [InlineData("IW-il", "he")]
    [InlineData("ji", "yi")]
    [InlineData("in-ID", "id")]
    [InlineData("in_ID", "id")]
    public void LocaleLookupIsExactTagThenTruncationThenRoot(string tag, string? key)
    {
        Assert.Equal(key, CldrPluralRules.ResolveKey(tag));
        Assert.Equal(key ?? "root", CldrPluralRules.ForLocale(tag).Locale);
        if (key is not null) Assert.Same(CldrPluralRules.ForLocale(tag), CldrPluralRules.ForLocale(key));
    }

    [Fact]
    public void ArbitraryCallerTagsNeverGrowTheRuleCache()
    {
        // Tags can be user input (Accept-Language on Blazor Server); only data keys may be cached.
        for (var index = 0; index < 10_000; index++)
        {
            CldrPluralRules.ForLocale($"en-x-{index}");
            CldrPluralRules.ForLocale($"zz-{index}");
            CldrPluralRules.ForLocale($"pt-PT-u-nu-{index}");
        }
        Assert.InRange(CldrPluralRules.CachedRuleSetCount, 1, CldrPluralRules.Locales.Count);
        Assert.Equal(HarborlinePluralCategory.One, new HarborlineLocaleScope("en-x-123456").PluralCategory(1));
    }

    [Fact]
    public void ParentLocaleRulesDifferFromLanguageRules()
    {
        Assert.Equal(HarborlinePluralCategory.One, new HarborlineLocaleScope("pt").PluralCategory(0));
        Assert.Equal(HarborlinePluralCategory.Other, new HarborlineLocaleScope("pt-PT").PluralCategory(0));
        Assert.Equal(HarborlinePluralCategory.Other, new HarborlineLocaleScope("pt-PT-x-harbor").PluralCategory(0));
        Assert.Equal(HarborlinePluralCategory.Other, new HarborlineLocaleScope("zz").PluralCategory(1));
    }

    [Fact]
    public void EmbeddedDataIsTheRecordedCldr47File()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), DataPath));
        Assert.Equal("83f10932abb4ce2c4681725c6d11e4c01097a758830677127142c80c79bd6322", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Assert.Equal("47", CldrPluralRules.CldrVersion);
        Assert.Equal(219, CldrPluralRules.Locales.Count);
        using var stream = typeof(HarborlineLocaleScope).Assembly.GetManifestResourceStream(CldrPluralRules.ResourceName);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        Assert.Equal(bytes, copy.ToArray());
    }

    [Theory]
    [InlineData("Harborline.Foundation.Localization.cldr.LICENSE", "LICENSE")]
    [InlineData("Harborline.Foundation.Localization.cldr.NOTICE.md", "NOTICE.md")]
    public void UnicodeLicenceAndNoticeTravelInsideTheDll(string resource, string file)
    {
        using var stream = typeof(HarborlineLocaleScope).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var expected = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "projections/dotnet/foundation/hlp.ui.locale-provider/cldr", file));
        Assert.Equal(expected, copy.ToArray());
        if (file == "LICENSE") Assert.Contains("SPDX-License-Identifier: Unicode-3.0", System.Text.Encoding.UTF8.GetString(expected), StringComparison.Ordinal);
    }

    [Fact]
    public void EverySampleEvaluatesToItsOwnCategory()
    {
        var failures = new List<string>();
        var checkedSamples = 0;
        foreach (var locale in CldrPluralRules.Locales)
        {
            foreach (var (name, text) in CldrPluralRules.RawRules(locale))
            {
                var expected = Enum.Parse<HarborlinePluralCategory>(name["pluralRule-count-".Length..], ignoreCase: true);
                foreach (var sample in Samples(text))
                {
                    checkedSamples++;
                    var actual = CldrPluralRules.Select(locale, sample);
                    if (actual != expected) failures.Add($"{locale} {sample}: expected {expected}, got {actual}");
                    if (!sample.Contains('c', StringComparison.Ordinal))
                    {
                        var viaScope = new HarborlineLocaleScope(locale).PluralCategory(Parse(sample), Scale(sample));
                        if (viaScope != expected) failures.Add($"{locale} {sample} via scope: expected {expected}, got {viaScope}");
                    }
                }
            }
        }
        Assert.Empty(failures);
        Assert.True(checkedSamples > 10000, $"only {checkedSamples} samples checked");
    }

    [Fact]
    public void CrossCheckFixtureCoversEveryDataLocale()
    {
        using var fixture = ReadJson("conformance/hlp.ui.locale-provider/plural-crosscheck.json");
        var root = fixture.RootElement;
        Assert.Equal("47", root.GetProperty("data").GetProperty("cldr").GetString());
        Assert.Equal("83f10932abb4ce2c4681725c6d11e4c01097a758830677127142c80c79bd6322", root.GetProperty("data").GetProperty("sha256").GetString());
        var compared = root.GetProperty("locales").EnumerateObject().Select(locale => locale.Name).ToArray();
        var unsupported = root.GetProperty("unsupported").EnumerateArray().Select(locale => locale.GetString()!).ToArray();
        var aliases = root.GetProperty("legacyAliases").EnumerateArray().Select(locale => locale.GetString()!).ToArray();
        // Only CLDR's und (no language) is outside Intl; nothing else may be skipped.
        Assert.Equal(["und"], unsupported);
        Assert.Equal(CldrPluralRules.LegacyLanguageAliases.Keys.Order(StringComparer.Ordinal), aliases);
        Assert.Equal(CldrPluralRules.Locales.Concat(aliases).Order(StringComparer.Ordinal), compared.Concat(unsupported).Order(StringComparer.Ordinal));
        Assert.Equal(compared, root.GetProperty("defaults").EnumerateObject().Select(locale => locale.Name));
        // Intl canonicalises the deprecated CLDR codes and the legacy aliases; the .NET lookup must
        // reach the same rules (the data keeps jw, mo, sh, tl itself; in, iw, ji go through the alias map).
        var canonicalised = root.GetProperty("canonicalised").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetString()!);
        Assert.Equal(new Dictionary<string, string> { ["in"] = "id", ["iw"] = "he", ["ji"] = "yi", ["jw"] = "jv", ["mo"] = "ro", ["sh"] = "sr", ["tl"] = "fil" }, canonicalised);
        foreach (var (alias, target) in CldrPluralRules.LegacyLanguageAliases) Assert.Equal(target, CldrPluralRules.ResolveKey(alias));
        Assert.Equal(443, root.GetProperty("numbers").GetArrayLength());
        Assert.Equal(21, root.GetProperty("defaultNumbers").GetArrayLength());
        Assert.Contains(root.GetProperty("numbers").EnumerateArray(), number => number.GetString() == "-2.50");
        Assert.Contains(root.GetProperty("numbers").EnumerateArray(), number => number.GetString() == "1.125");
    }

    [Fact]
    public void MatchesNodeIntlPluralRulesOrAnAllowListedCldrChange()
    {
        using var fixture = ReadJson("conformance/hlp.ui.locale-provider/plural-crosscheck.json");
        using var allowList = ReadJson("conformance/hlp.ui.locale-provider/plural-crosscheck-allowlist.json");
        var root = fixture.RootElement;
        var letters = root.GetProperty("categoryLetters").EnumerateObject()
            .ToDictionary(entry => entry.Value.GetString()![0], entry => Enum.Parse<HarborlinePluralCategory>(entry.Name, ignoreCase: true));
        var mismatches = new SortedSet<string>(StringComparer.Ordinal);
        var comparisons = 0;
        // Visible-digit mode: Intl with the source scale against PluralCategory(count, scale).
        comparisons += Compare(root.GetProperty("locales"), root.GetProperty("numbers"), letters, mismatches, "",
            (scope, source) => scope.PluralCategory(Parse(source), Scale(source)));
        // Default mode: plain Intl select(Number(source)) against PluralCategory(count).
        comparisons += Compare(root.GetProperty("defaults"), root.GetProperty("defaultNumbers"), letters, mismatches, "default ",
            (scope, source) => scope.PluralCategory(Parse(source)));
        Assert.Equal(221 * (443 + 21), comparisons);
        var (unexplained, stale) = Reconcile(mismatches, allowList.RootElement);
        var byLocale = string.Join(", ", unexplained.GroupBy(entry => entry[..entry.IndexOf(' ', StringComparison.Ordinal)], StringComparer.Ordinal)
            .Select(group => $"{group.Key}={group.Count()}"));
        Assert.True(unexplained.Length == 0 && stale.Length == 0,
            $"{unexplained.Length} unexplained disagreements in {comparisons} comparisons (by locale: {byLocale}); "
            + $"first: {string.Join("; ", unexplained.Take(40))}. Stale allow-list entries: {string.Join("; ", stale)}");
    }

    [Fact]
    public void AllowListSuppressesOnlyListedDisagreements()
    {
        using var allowList = JsonDocument.Parse("""
            {"entries":[{"locale":"xx","numbers":["2","3"],"node":"few","dotnet":"other",
              "cldr47Rule":"n = 2..3 (synthetic)","explanation":"synthetic entry for the mechanism test"}]}
            """);
        var listed = Key("xx", "2", HarborlinePluralCategory.Few, HarborlinePluralCategory.Other);
        var alsoListed = Key("xx", "3", HarborlinePluralCategory.Few, HarborlinePluralCategory.Other);
        var unlisted = Key("xx", "4", HarborlinePluralCategory.Few, HarborlinePluralCategory.Other);
        var wrongCategory = Key("xx", "2", HarborlinePluralCategory.Many, HarborlinePluralCategory.Other);

        var (unexplained, stale) = Reconcile([listed, alsoListed], allowList.RootElement);
        Assert.Empty(unexplained);
        Assert.Empty(stale);

        (unexplained, stale) = Reconcile([listed, alsoListed, unlisted, wrongCategory], allowList.RootElement);
        Assert.Equal([wrongCategory, unlisted], unexplained.Order(StringComparer.Ordinal));
        Assert.Empty(stale);

        (unexplained, stale) = Reconcile([listed], allowList.RootElement);
        Assert.Empty(unexplained);
        Assert.Equal([alsoListed], stale);

        using var unexplainedEntry = JsonDocument.Parse("""{"entries":[{"locale":"xx","numbers":["2"],"node":"few","dotnet":"other","cldr47Rule":"n = 2","explanation":" "}]}""");
        Assert.ThrowsAny<Exception>(() => Reconcile([listed], unexplainedEntry.RootElement));
    }

    /// <summary>Disagreements not on the allow-list, and allow-list entries that no longer occur.</summary>
    internal static (string[] Unexplained, string[] Stale) Reconcile(IEnumerable<string> mismatches, JsonElement allowList)
    {
        var allowed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in allowList.GetProperty("entries").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("explanation").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("cldr47Rule").GetString()));
            var node = Enum.Parse<HarborlinePluralCategory>(entry.GetProperty("node").GetString()!, ignoreCase: true);
            var dotnet = Enum.Parse<HarborlinePluralCategory>(entry.GetProperty("dotnet").GetString()!, ignoreCase: true);
            foreach (var number in entry.GetProperty("numbers").EnumerateArray())
                allowed.Add(Key(entry.GetProperty("locale").GetString()!, number.GetString()!, node, dotnet));
        }
        var found = new SortedSet<string>(mismatches, StringComparer.Ordinal);
        return ([.. found.Except(allowed, StringComparer.Ordinal)], [.. allowed.Except(found, StringComparer.Ordinal)]);
    }

    private static int Compare(JsonElement section, JsonElement numberList, Dictionary<char, HarborlinePluralCategory> letters,
        SortedSet<string> mismatches, string mode, Func<HarborlineLocaleScope, string, HarborlinePluralCategory> select)
    {
        var numbers = numberList.EnumerateArray().Select(number => number.GetString()!).ToArray();
        var comparisons = 0;
        foreach (var locale in section.EnumerateObject())
        {
            var categories = locale.Value.GetString()!;
            Assert.Equal(numbers.Length, categories.Length);
            var scope = new HarborlineLocaleScope(locale.Name);
            for (var index = 0; index < numbers.Length; index++)
            {
                comparisons++;
                var node = letters[categories[index]];
                var dotnet = select(scope, numbers[index]);
                if (node != dotnet) mismatches.Add(Key(locale.Name, mode + numbers[index], node, dotnet));
            }
        }
        return comparisons;
    }

    private static decimal Parse(string source) =>
        decimal.Parse(source, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static int Scale(string source) =>
        source.Contains('.', StringComparison.Ordinal) ? source.Length - source.IndexOf('.', StringComparison.Ordinal) - 1 : 0;

    private static string Key(string locale, string number, HarborlinePluralCategory node, HarborlinePluralCategory dotnet) =>
        $"{locale} {number}: node {node.ToString().ToLowerInvariant()}, dotnet {dotnet.ToString().ToLowerInvariant()}";

    /// <summary>Expands a rule's @integer and @decimal sample lists; a~b steps by the last digit's place.</summary>
    internal static IEnumerable<string> Samples(string rule)
    {
        var at = rule.IndexOf('@', StringComparison.Ordinal);
        if (at < 0) yield break;
        foreach (var list in rule[at..].Split('@', StringSplitOptions.RemoveEmptyEntries))
        {
            var body = list.Trim();
            var space = body.IndexOf(' ', StringComparison.Ordinal);
            Assert.True(body[..space] is "integer" or "decimal", body);
            foreach (var raw in body[(space + 1)..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw is "…" or "...") continue;
                var tilde = raw.IndexOf('~', StringComparison.Ordinal);
                if (tilde < 0) { yield return raw; continue; }
                var low = raw[..tilde];
                var high = raw[(tilde + 1)..];
                var scale = low.Contains('.', StringComparison.Ordinal) ? low.Length - low.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
                var step = 1m / (decimal)Math.Pow(10, scale);
                var end = decimal.Parse(high, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                for (var value = decimal.Parse(low, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture); value <= end; value += step)
                    yield return value.ToString("F" + scale.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            }
        }
    }

    private static JsonDocument ReadJson(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)));

    internal static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
