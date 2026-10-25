using Bunit;
using Microsoft.AspNetCore.Components;
using Harborline.Foundation.Localization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ScrollAffordanceComponentTests : BunitContext
{
    private const string LiveStatusCase = "scroll-affordance.component-live-status";

    public ScrollAffordanceComponentTests() => JSInterop.Mode = JSRuntimeMode.Loose;
    [Fact] public void NamedRegionPreservesContentAndOrientation() { var cut = Render<HarborlineScrollAffordance>(p => p.Add(x => x.AriaLabel, "Results").Add(x => x.Orientation, Browser.ScrollAffordanceOrientation.Vertical).Add(x => x.ChildContent, Text("Body"))); var host = cut.Find("[role=group]"); Assert.Equal("Results", host.GetAttribute("aria-label")); Assert.Contains("vertical", host.ClassName); Assert.Equal("Body", host.TextContent); }
    [Theory, Trait("ModuleConformance", "hlp.ui.scroll-affordance")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.scroll-affordance", MemberType = typeof(SharedFixtureBatch))]
    public Task SharedFixtureConforms(string caseId, string? rawFixture)
    {
        return SharedFixtureBatch.RunAsync(caseId, rawFixture, async () => {
            AssertFixture("scroll-affordance.component-");
            if (caseId == LiveStatusCase) await AssertLiveStatusIsLocalizedAsync();
            else Assert.NotNull(Render<HarborlineScrollAffordance>(p => p.Add(x => x.AriaLabel, "Items")));
        });
    }

    // T-1106: the position status resolves the host catalog first, then the default catalog, the order
    // React's t() follows. Each row is a measurement the frozen policy maps to one announcement key.
    public static TheoryData<string, double, double, double, int, string> HostCatalogStatuses => new()
    {
        { "en-XA", 0, 800, 400, 8, "⟦ Šçřøļļåƀļë, šhøŵîñĝ 1 ţø 4 øƒ 8 ······ ⟧" },
        { "en-XA", 0, 800, 100, 8, "⟦ Šçřøļļåƀļë, šhøŵîñĝ 1 øƒ 8 ······ ⟧" },
        { "en-XA", 0, 700, 300, 0, "⟦ Møřë çøñţëñţ åṽåîļåƀļë; ûšë åřřøŵ ķëÿš ········ ⟧" },
        { "en-XA", 200, 700, 300, 0, "⟦ Šçřøļļåƀļë řëĝîøñ, 50% šçřøļļëđ ······ ⟧" },
        { "en-XA", 400, 700, 300, 0, "⟦ Šçřøļļåƀļë řëĝîøñ, ëñđ řëåçhëđ ······ ⟧" },
        { "ar-SA", 0, 800, 400, 8, "قابل للتمرير، عرض 1 إلى 4 من 8" },
        { "ar-SA", 0, 800, 100, 8, "قابل للتمرير، عرض 1 من 8" },
        { "ar-SA", 0, 700, 300, 0, "منطقة قابلة للتمرير، يتوفر المزيد من المحتوى" },
        { "ar-SA", -200, 700, 300, 0, "منطقة قابلة للتمرير، تم تمرير 50%" },
        { "ar-SA", -400, 700, 300, 0, "منطقة قابلة للتمرير، تم الوصول إلى النهاية" },
    };

    [Theory, MemberData(nameof(HostCatalogStatuses))]
    public async Task HostCatalogLocalizesThePositionStatus(string locale, double rawPosition, double scrollSize, double clientSize, int itemCount, string expected)
    {
        var rtl = HarborlineLocaleScope.DirectionForLocale(locale) == "rtl";
        var provider = RenderUnderProvider(locale, CatalogFor(locale), itemCount == 0 ? null : itemCount);
        await MeasureAsync(provider, new(rawPosition, scrollSize, clientSize, rtl));
        AssertStatus(provider, expected);
    }

    [Fact]
    public async Task CatalogChangeRelocalizesTheStatusAndAMissingKeyFallsBackToTheDefaultCatalog()
    {
        var provider = RenderUnderProvider("en-XA", PseudoCatalog, 8);
        await MeasureAsync(provider, new(0, 800, 400));
        AssertStatus(provider, "⟦ Šçřøļļåƀļë, šhøŵîñĝ 1 ţø 4 øƒ 8 ······ ⟧");

        provider.Render(p => p.Add(x => x.Locale, "ar-SA").Add(x => x.Direction, "rtl").Add(x => x.Catalog, ArabicCatalog));
        AssertStatus(provider, "قابل للتمرير، عرض 1 إلى 4 من 8");

        provider.Render(p => p.Add(x => x.Locale, "fr-FR").Add(x => x.Direction, "ltr").Add(x => x.Catalog, new Dictionary<string, string> { ["common.close"] = "Fermer" }));
        AssertStatus(provider, DefaultShowingRange(1, 4, 8));
    }

    [Fact]
    public async Task WithoutAProviderTheStatusUsesTheDefaultCatalog()
    {
        var cut = Render<HarborlineScrollAffordance>(p => p.Add(x => x.AriaLabel, "Items").Add(x => x.ItemCount, 8));
        await cut.InvokeAsync(() => cut.Instance.OnMeasuredAsync(new(0, 800, 400)));
        var status = cut.Find(".hl-scroll-affordance__status");
        Assert.Equal(DefaultShowingRange(1, 4, 8), status.TextContent);
        Assert.Equal("Scrollable, showing 1 to 4 of 8", status.TextContent);
    }

    private async Task AssertLiveStatusIsLocalizedAsync()
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(SharedFixtureBatch.Current!);
        var input = fixture.RootElement.GetProperty("input");
        var expected = fixture.RootElement.GetProperty("expected");
        Assert.True(input.GetProperty("overflow").GetBoolean());
        Assert.True(expected.GetProperty("localized").GetBoolean());
        var itemCount = input.GetProperty("itemCount").GetInt32();
        var provider = RenderUnderProvider("en-XA", PseudoCatalog, itemCount);
        await MeasureAsync(provider, new(0, itemCount * 100, 300));
        var status = provider.Find(".hl-scroll-affordance__status");
        Assert.Equal(expected.GetProperty("live").GetString(), status.GetAttribute("aria-live"));
        Assert.Equal(expected.GetProperty("atomic").GetString(), status.GetAttribute("aria-atomic"));
        Assert.Equal($"⟦ Šçřøļļåƀļë, šhøŵîñĝ 1 ţø 3 øƒ {itemCount} ······ ⟧", status.TextContent);
    }

    private IRenderedComponent<HarborlineLocaleProvider> RenderUnderProvider(string locale, IReadOnlyDictionary<string, string> catalog, int? itemCount) =>
        Render<HarborlineLocaleProvider>(p => p
            .Add(x => x.Locale, locale)
            .Add(x => x.Direction, HarborlineLocaleScope.DirectionForLocale(locale))
            .Add(x => x.Catalog, catalog)
            .AddChildContent<HarborlineScrollAffordance>(region => region.Add(x => x.AriaLabel, "Items").Add(x => x.ItemCount, itemCount)));

    private static async Task MeasureAsync(IRenderedComponent<HarborlineLocaleProvider> provider, ScrollAffordanceMeasurement measurement)
    {
        var region = provider.FindComponent<HarborlineScrollAffordance>();
        await region.InvokeAsync(() => region.Instance.OnMeasuredAsync(measurement));
    }

    private static void AssertStatus(IRenderedComponent<HarborlineLocaleProvider> provider, string expected)
    {
        var status = provider.Find(".hl-scroll-affordance__status");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal("true", status.GetAttribute("aria-atomic"));
        Assert.Equal(expected, status.TextContent);
    }

    private static string DefaultShowingRange(int start, int end, int total) => HarborlineDefaultStrings.Interpolate(
        HarborlineDefaultStrings.Get("scrollAffordance.showingRange"),
        new Dictionary<string, object?> { ["start"] = start, ["end"] = end, ["total"] = total });

    private static IReadOnlyDictionary<string, string> CatalogFor(string locale) => locale == "ar-SA" ? ArabicCatalog : PseudoCatalog;

    private static readonly IReadOnlyDictionary<string, string> PseudoCatalog = new Dictionary<string, string>
    {
        ["scrollAffordance.showingOne"] = "⟦ Šçřøļļåƀļë, šhøŵîñĝ {index} øƒ {total} ······ ⟧",
        ["scrollAffordance.showingRange"] = "⟦ Šçřøļļåƀļë, šhøŵîñĝ {start} ţø {end} øƒ {total} ······ ⟧",
        ["scrollAffordance.endReached"] = "⟦ Šçřøļļåƀļë řëĝîøñ, ëñđ řëåçhëđ ······ ⟧",
        ["scrollAffordance.moreAvailable"] = "⟦ Møřë çøñţëñţ åṽåîļåƀļë; ûšë åřřøŵ ķëÿš ········ ⟧",
        ["scrollAffordance.percentScrolled"] = "⟦ Šçřøļļåƀļë řëĝîøñ, {percent}% šçřøļļëđ ······ ⟧",
    };

    private static readonly IReadOnlyDictionary<string, string> ArabicCatalog = new Dictionary<string, string>
    {
        ["scrollAffordance.showingOne"] = "قابل للتمرير، عرض {index} من {total}",
        ["scrollAffordance.showingRange"] = "قابل للتمرير، عرض {start} إلى {end} من {total}",
        ["scrollAffordance.endReached"] = "منطقة قابلة للتمرير، تم الوصول إلى النهاية",
        ["scrollAffordance.moreAvailable"] = "منطقة قابلة للتمرير، يتوفر المزيد من المحتوى",
        ["scrollAffordance.percentScrolled"] = "منطقة قابلة للتمرير، تم تمرير {percent}%",
    };

    private static RenderFragment Text(string value) => builder => builder.AddContent(0, value);
    private static void AssertFixture(string prefix) { var raw = SharedFixtureBatch.Current; if (string.IsNullOrWhiteSpace(raw)) return; using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith(prefix, fixture.RootElement.GetProperty("id").GetString()); }
}
