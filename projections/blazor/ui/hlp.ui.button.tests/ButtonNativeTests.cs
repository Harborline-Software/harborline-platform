using Bunit;
using Harborline.Foundation.Enums;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using System.Text.Json;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ButtonNativeTests : BunitContext
{
    [Fact]
    public void PublicDefaultsMatchTheLegacyProjection()
    {
        var cut = Render<HarborlineButton>(parameters => parameters.AddChildContent("Save"));
        Assert.Equal(ButtonVariant.Secondary, cut.Instance.Variant);
        Assert.Equal(ButtonSize.Medium, cut.Instance.Size);
        Assert.Equal(FillMode.Solid, cut.Instance.FillMode);
        Assert.Equal(RoundedMode.Medium, cut.Instance.Rounded);
    }

    [Fact]
    public void NamedIconOnlyUseIsAllowed()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.LeadingIcon, builder => builder.AddContent(0, "+"))
            .AddUnmatched("aria-label", "Create item"));
        Assert.Equal("Create item", cut.Find("button").GetAttribute("aria-label"));
    }

    [Fact]
    public void ConsumerStyleAndClassAreComposed()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Class, "consumer")
            .Add(component => component.Style, "min-width:4rem")
            .AddChildContent("Save"));
        Assert.Contains("consumer", cut.Find("button").ClassList);
        Assert.Contains("min-width:4rem", cut.Find("button").GetAttribute("style"));
    }

    [Fact]
    public void PublicThemeTokensAndThemeMetadataSurviveHostComposition()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Style, "--hl-button-primary:#123456")
            .AddUnmatched("data-theme", "dark")
            .AddChildContent("Ship"));
        Assert.Contains("--hl-button-primary:#123456", cut.Find("button").GetAttribute("style"));
        Assert.Equal("dark", cut.Find("button").GetAttribute("data-theme"));
    }

    [Fact]
    public void LoadingReplacesOnlyTheLeadingIcon()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Loading, true)
            .Add(component => component.LeadingIcon, builder => builder.AddContent(0, "L"))
            .Add(component => component.TrailingIcon, builder => builder.AddContent(0, "T"))
            .AddChildContent("Save"));
        Assert.DoesNotContain("L", cut.Find("button").TextContent);
        Assert.Contains("SaveT", string.Concat(cut.Find("button").TextContent.Where(character => !char.IsWhiteSpace(character))));
    }

    [Fact]
    public void LegacyIconAliasRemainsSupported()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Icon, builder => builder.AddContent(0, "+"))
            .AddUnmatched("aria-label", "Create item"));
        Assert.Contains("+", cut.Find("button").TextContent);
    }

    [Fact]
    public void DisabledButtonDoesNotEmitActivation()
    {
        var count = 0;
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Enabled, false)
            .Add(component => component.OnClick, _ => count++)
            .AddChildContent("Save"));
        cut.Find("button").Click();
        Assert.Equal(0, count);
    }

    [Fact]
    public void LoadingButtonDoesNotEmitActivation()
    {
        var count = 0;
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Loading, true)
            .Add(component => component.OnClick, _ => count++)
            .AddChildContent("Save"));
        cut.Find("button").Click();
        Assert.Equal(0, count);
    }

    [Fact]
    public void LoadingExposesTheDefaultAccessibleStatus()
    {
        var cut = Render<HarborlineButton>(parameters => parameters
            .Add(component => component.Loading, true)
            .AddChildContent("Saving"));
        Assert.Equal("Loading", cut.Find("[role=status]").GetAttribute("aria-label"));
        Assert.Equal("Loading", cut.Find("[role=status]").TextContent);
        Assert.Equal("Saving", cut.Find("button").TextContent.Trim());
    }

    [Fact]
    public void PublicLocaleProviderPropagatesLanguageDirectionAndOwnedStrings()
    {
        IReadOnlyDictionary<string, string> catalog = new Dictionary<string, string>
        {
            ["common.loading"] = "جارٍ التحميل",
        };
        var cut = Render<HarborlineLocaleProvider>(parameters => parameters
            .Add(component => component.Locale, "ar-SA")
            .Add(component => component.Catalog, catalog)
            .AddChildContent<HarborlineButton>(button => button
                .Add(component => component.Loading, true)
                .AddChildContent("جارٍ الحفظ")));

        Assert.Equal("ar-SA", cut.Find("button").GetAttribute("lang"));
        Assert.Equal("rtl", cut.Find("button").GetAttribute("dir"));
        Assert.Equal("جارٍ التحميل", cut.Find("[role=status]").GetAttribute("aria-label"));
        Assert.Equal("جارٍ التحميل", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void EveryFillModeMapsToAClass()
    {
        foreach (var fill in Enum.GetValues<FillMode>())
        {
            var cut = Render<HarborlineButton>(parameters => parameters
                .Add(component => component.FillMode, fill)
                .AddChildContent("Save"));
            Assert.Contains($"hl-button--fill-{fill.ToString().ToLowerInvariant()}", cut.Find("button").ClassList);
        }
    }

    [Fact]
    public void EveryRoundedModeMapsToAClass()
    {
        foreach (var rounded in Enum.GetValues<RoundedMode>())
        {
            var cut = Render<HarborlineButton>(parameters => parameters
                .Add(component => component.Rounded, rounded)
                .AddChildContent("Save"));
            Assert.Contains($"hl-button--rounded-{rounded.ToString().ToLowerInvariant()}", cut.Find("button").ClassList);
        }
    }

    [Fact]
    public void AssemblyIdentityIsHarborlineUiAdaptersBlazor()
    {
        Assert.Equal("Harborline.UIAdapters.Blazor", typeof(HarborlineButton).Assembly.GetName().Name);
        Assert.Equal("Harborline.Foundation", typeof(ButtonVariant).Assembly.GetName().Name);
    }

    [Fact]
    public void LegacyDisposableContractRemainsCallableAndIdempotent()
    {
        var cut = Render<HarborlineButton>(parameters => parameters.AddChildContent("Save"));
        var disposable = Assert.IsAssignableFrom<IDisposable>(cut.Instance);

        disposable.Dispose();
        disposable.Dispose();
    }

    [Fact]
    public void EveryLegacyProviderClassFlowsThroughTheBoundedPublicHostBridge()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "compatibility", "legacy-button-provider-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var fixtures = document.RootElement.GetProperty("fixtures");

        Assert.Equal(900, fixtures.GetArrayLength());
        foreach (var fixture in fixtures.EnumerateArray())
        {
            var providerClass = fixture.GetProperty("className").GetString();
            Assert.False(string.IsNullOrWhiteSpace(providerClass));
            using var cut = Render<HarborlineButton>(parameters => parameters
                .Add(component => component.Class, providerClass)
                .AddChildContent("Save"));

            var renderedClass = cut.Find("button").GetAttribute("class");
            Assert.Contains(providerClass, renderedClass);
            Assert.Contains("hl-button", cut.Find("button").ClassList);
        }
    }
}
