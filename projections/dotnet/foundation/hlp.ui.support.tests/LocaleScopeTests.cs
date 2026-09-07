using Harborline.Foundation.Localization;
using Xunit;

namespace Harborline.Foundation.UI.Tests;

public sealed class LocaleScopeTests
{
    [Fact]
    public void DirectionCatalogInterpolationAndNestedFormatterFollowFrozenScopeRules()
    {
        Assert.Equal("rtl", HarborlineLocaleScope.DirectionForLocale("HE-il"));
        Assert.Equal("ltr", HarborlineLocaleScope.DirectionForLocale("en-US"));
        var parent = new HarborlineLocaleScope("fr-FR", catalog: new Dictionary<string, string> { ["common.loading"] = "Chargement" }, numberFormatter: request => $"root({request.Locale}):{request.Value}");
        var child = new HarborlineLocaleScope(parent: parent);
        Assert.Equal("en", child.Locale);
        Assert.Equal("Loading", child.Resolve("common.loading"));
        Assert.Equal("root(ar-SA):12.5", child.FormatNumber(12.5m, locale: "ar-SA"));
        var empty = new HarborlineLocaleScope(catalog: new Dictionary<string, string> { ["common.loading"] = "" });
        Assert.Equal("", empty.Resolve("common.loading"));
        Assert.Equal("fixture.unknown", empty.Resolve("fixture.unknown"));
        Assert.Equal("Step 2 of {total}", empty.ResolveString("common.loading", new Dictionary<string, object?> { ["current"] = 2 }, "Step {current} of {total}"));
    }

    [Fact]
    public void PluralsAndNumbersMatchRepresentativeLocaleFixtures()
    {
        Assert.Equal(HarborlinePluralCategory.One, new HarborlineLocaleScope("en").PluralCategory(1));
        Assert.Equal(HarborlinePluralCategory.Few, new HarborlineLocaleScope("ar").PluralCategory(103));
        Assert.Equal(HarborlinePluralCategory.Many, new HarborlineLocaleScope("pl").PluralCategory(5));
        Assert.Equal(HarborlinePluralCategory.Other, new HarborlineLocaleScope("ja").PluralCategory(1));
        Assert.Equal("1.234,50", new HarborlineLocaleScope("de-DE").FormatNumber(1234.5m, 2, 2));
        Assert.Equal("12.5", new HarborlineLocaleScope("not_a_locale").FormatNumber(12.5m));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.locale-provider")]
    public void SharedFixtureConforms()
    {
        using var fixture = Fixture.Read("locale.");
        if (fixture is null) return;
        Assert.Contains(fixture.RootElement.GetProperty("id").GetString(), Fixture.CaseIds("hlp.ui.locale-provider"));
        Assert.Equal("en", new HarborlineLocaleScope().Locale);
    }
}
