using Bunit;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LocaleProviderTests : BunitContext
{
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

    [Fact, Trait("ModuleConformance", "hlp.ui.locale-provider")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw);
        Assert.StartsWith("locale.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("en", Render<HarborlineLocaleProvider>().Instance.Locale);
    }
}
