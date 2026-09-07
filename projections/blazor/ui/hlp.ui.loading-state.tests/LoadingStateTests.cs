using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LoadingStateNativeTests : BunitContext
{
    [Fact]
    public void PageAndInlinePreserveStatusLabelAndHostContext()
    {
        var page = Render<HarborlineLoadingState>(parameters => parameters
            .Add(component => component.Label, "Loading inspections")
            .Add(component => component.Class, "consumer")
            .AddUnmatched("lang", "en-US")
            .AddUnmatched("data-case", "native"));
        Assert.Equal("div", page.Find("[role=status]").TagName.ToLowerInvariant());
        Assert.Equal("polite", page.Find("[role=status]").GetAttribute("aria-live"));
        Assert.Equal("Loading inspections", page.Find("[role=status]").TextContent);
        Assert.Contains("hl-loading-state--page", page.Find("[role=status]").ClassList);
        Assert.Contains("consumer", page.Find("[role=status]").ClassList);
        // The indicator is drawn by CSS, not rendered. The gallery asserts this status region holds
        // no svg at all, and the component root IS that region, so an aria-hidden SVG still failed.
        Assert.Empty(page.FindAll("[role=status] svg"));
        Assert.Single(page.FindAll("[role=status]"));

        var inline = Render<HarborlineLoadingState>(parameters => parameters
            .Add(component => component.Label, "جارٍ التحميل")
            .Add(component => component.Variant, LoadingStateVariant.Inline)
            .AddUnmatched("lang", "ar-SA")
            .AddUnmatched("dir", "rtl"));
        Assert.Equal("p", inline.Find("[role=status]").TagName.ToLowerInvariant());
        Assert.Contains("hl-loading-state--inline", inline.Find("[role=status]").ClassList);
        Assert.Equal("rtl", inline.Find("[role=status]").GetAttribute("dir"));
        Assert.Equal("جارٍ التحميل", inline.Find("[role=status]").TextContent);
    }

    [Fact]
    public void LabelUpdateUsesTheSameStatusRoot()
    {
        var cut = Render<HarborlineLoadingState>(parameters => parameters.Add(component => component.Label, "Loading inspections"));
        Assert.Single(cut.FindAll("[role=status]"));
        cut.Render(parameters => parameters.Add(component => component.Label, "Loading attachments"));
        Assert.Single(cut.FindAll("[role=status]"));
        Assert.Equal("Loading attachments", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void EmptyLabelFailsClosed()
    {
        var error = Assert.ThrowsAny<Exception>(() => Render<HarborlineLoadingState>());
        Assert.Contains("label-required", error.ToString());
    }
}

public sealed class LoadingStateConformanceTests : BunitContext
{
    [Fact]
    [Trait("ModuleConformance", "hlp.ui.loading-state")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw!);
        var id = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("loading-state.", id);

        if (id == "loading-state.variant-classes")
        {
            // 282 s5: these three selectors used to live only in the error-card lane stylesheet
            // (projections/blazor/ui/hlp.ui.error-card/wwwroot/feedback.css), never in an authority
            // the other lane shared. Both lanes assert the spellings from this row.
            var expected = fixture.RootElement.GetProperty("expected");
            var page = Render<HarborlineLoadingState>(parameters => parameters.Add(component => component.Label, "Loading inspections"));
            Assert.Equal(Classes(expected, "pageClasses"), page.Find("[role=status]").ClassList);
            Assert.Equal(Classes(expected, "labelClasses"), page.Find(".hl-loading-state__label").ClassList);
            var inlineCut = Render<HarborlineLoadingState>(parameters => parameters
                .Add(component => component.Label, "Loading inspections")
                .Add(component => component.Variant, LoadingStateVariant.Inline));
            Assert.Equal(Classes(expected, "inlineClasses"), inlineCut.Find("[role=status]").ClassList);
            return;
        }

        var inline = id == "loading-state.inline";
        var label = id == "loading-state.locale-direction" ? "جارٍ التحميل" : "Loading inspections";
        var cut = Render<HarborlineLoadingState>(parameters =>
        {
            parameters.Add(component => component.Label, label);
            parameters.Add(component => component.Variant, inline ? LoadingStateVariant.Inline : LoadingStateVariant.Page);
            if (id == "loading-state.locale-direction")
            {
                parameters.AddUnmatched("lang", "ar-SA");
                parameters.AddUnmatched("dir", "rtl");
            }
            if (id == "loading-state.host-attributes")
            {
                parameters.Add(component => component.Class, "consumer");
                parameters.AddUnmatched("data-case", "shared");
                parameters.AddUnmatched("aria-atomic", "true");
            }
        });
        var status = cut.Find("[role=status]");
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal(label, status.TextContent);
        Assert.Equal(inline ? "p" : "div", status.TagName.ToLowerInvariant());
    }

    private static string[] Classes(System.Text.Json.JsonElement expected, string property) =>
        expected.GetProperty(property).EnumerateArray().Select(value => value.GetString()!).ToArray();
}
