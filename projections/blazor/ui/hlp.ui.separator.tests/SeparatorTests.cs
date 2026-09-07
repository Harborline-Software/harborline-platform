using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class SeparatorTests : BunitContext
{
    [Fact]
    public void DecorativeSemanticAndLabeledModesStayDistinct()
    {
        var decorative = Render<HarborlineSeparator>();
        Assert.Null(decorative.Find(".hl-separator").GetAttribute("role"));
        Assert.Equal("true", decorative.Find(".hl-separator").GetAttribute("data-hl-rule"));
        var semantic = Render<HarborlineSeparator>(p => p.Add(x => x.Decorative, false).Add(x => x.Orientation, SeparatorOrientation.Vertical).Add(x => x.Label, "or").Add(x => x.Class, "consumer"));
        Assert.Equal("separator", semantic.Find(".hl-separator").GetAttribute("role"));
        Assert.Equal("vertical", semantic.Find(".hl-separator").GetAttribute("aria-orientation"));
        Assert.Equal(2, semantic.FindAll(".hl-separator__rule").Count);
        Assert.Equal("or", semantic.Find(".hl-separator__label").TextContent);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.separator")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("separator.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Null(Render<HarborlineSeparator>().Find(".hl-separator").GetAttribute("role"));
    }
}
