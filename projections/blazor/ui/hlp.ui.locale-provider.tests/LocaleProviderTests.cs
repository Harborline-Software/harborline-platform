using System.Globalization;
using System.Text.Json;
using Bunit;
using Harborline.Foundation.Localization;
using Harborline.UIAdapters.Blazor.Localization;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LocaleProviderTests : BunitContext
{
    private static readonly decimal[] UkrainianCounts = [21m, 22m, 25m, 1.5m];

    [Fact]
    public void ProviderDirectionAndEmptyOverrideReachLeafComponents()
    {
        Assert.Equal("rtl", HarborlineLocaleProvider.DirectionForLocale("HE-il"));
        var cut = Render<HarborlineLocaleProvider>(parameters => parameters
            .Add(component => component.Locale, "ar-SA")
            .Add(component => component.Catalog, new Dictionary<string, string> { ["common.loading"] = "" })
            .AddChildContent<Components.Buttons.HarborlineButton>(button => button
                .Add(component => component.Loading, true)
                .AddChildContent("Save")));
        Assert.Equal("ar-SA", cut.Find("button").GetAttribute("lang"));
        Assert.Equal("rtl", cut.Find("button").GetAttribute("dir"));
        Assert.Equal("", cut.Find("[role=status]").GetAttribute("aria-label"));
    }

    [Fact]
    public void CldrPluralCategoriesReachComponentsThroughTheCascadedContext()
    {
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "conformance", "hlp.ui.locale-provider", "fixtures.yaml")));
        AssertPluralCategories(fixtures.RootElement.GetProperty("cases").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == "locale.plural-categories"));
    }

    [Fact]
    public void PluralKeySelectionUsesCldrCategories()
    {
        var context = ContextFor("uk");
        var keys = new Dictionary<HarborlinePluralCategory, string>
        {
            [HarborlinePluralCategory.One] = "fixture.one",
            [HarborlinePluralCategory.Few] = "fixture.few",
            [HarborlinePluralCategory.Many] = "fixture.many",
            [HarborlinePluralCategory.Other] = "fixture.other",
        };
        Assert.Equal(["fixture.one", "fixture.few", "fixture.many", "fixture.other"],
            UkrainianCounts.Select(count => context.ResolvePlural(count, keys)));
        Assert.Equal(HarborlinePluralCategory.Other, ContextFor("zh").PluralCategory(1));
        var en = ContextFor("en");
        Assert.Equal(HarborlinePluralCategory.One, en.PluralCategory(1.0m));
        Assert.Equal(HarborlinePluralCategory.Other, en.PluralCategory(1m, 1));
        Assert.Equal("fixture.other", en.ResolvePlural(1m, 1, keys));
        Assert.Equal(HarborlinePluralCategory.Two, ContextFor("iw").PluralCategory(2));
    }

    private void AssertPluralCategories(JsonElement fixture)
    {
        Assert.Equal("47", fixture.GetProperty("input").GetProperty("cldr").GetString());
        var rows = fixture.GetProperty("input").GetProperty("rows").EnumerateArray().ToArray();
        var expected = fixture.GetProperty("expected").GetProperty("categories").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(expected.Length, rows.Length);
        var actual = rows.Select(row =>
        {
            var context = ContextFor(row.GetProperty("locale").GetString()!);
            var count = decimal.Parse(row.GetProperty("count").GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            var category = row.TryGetProperty("visibleFractionDigits", out var digits)
                ? context.PluralCategory(count, digits.GetInt32())
                : context.PluralCategory(count);
            return category.ToString().ToLowerInvariant();
        }).ToArray();
        Assert.Equal(expected, actual);
    }

    private HarborlineLocaleContext ContextFor(string locale)
    {
        var cut = Render<HarborlineLocaleProvider>(parameters => parameters
            .Add(component => component.Locale, locale)
            .AddChildContent<LocaleProbe>());
        return cut.FindComponent<LocaleProbe>().Instance.Context ?? throw new InvalidOperationException("No cascaded locale context.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "repository.yaml"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed class LocaleProbe : ComponentBase
    {
        [CascadingParameter] public HarborlineLocaleContext? Context { get; set; }
    }

    [Theory, Trait("ModuleConformance", "hlp.ui.locale-provider")]
    [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.locale-provider", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            var raw = SharedFixtureBatch.Current;
            if (string.IsNullOrWhiteSpace(raw)) return;
            using var fixture = JsonDocument.Parse(raw);
            Assert.StartsWith("locale.", fixture.RootElement.GetProperty("id").GetString());
            Assert.Equal("en", Render<HarborlineLocaleProvider>().Instance.Locale);
            if (fixture.RootElement.GetProperty("id").GetString() == "locale.plural-categories") AssertPluralCategories(fixture.RootElement);
        });
    }
}
