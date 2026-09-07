using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class CardTests : BunitContext
{
    [Fact]
    public void SurfaceRegionsLogicalSeparatorsAndHeadingRemainComposable()
    {
        var cut = Render<HarborlineCard>(p => p.Add(x => x.Orientation, CardOrientation.Horizontal).Add(x => x.Separators, true).Add(x => x.Class, "consumer").AddChildContent<HarborlineCardHeader>(h => h.AddChildContent<HarborlineCardTitle>(t => t.Add(x => x.Level, 2).AddChildContent("Title"))).AddChildContent<HarborlineCardContent>(c => c.AddChildContent("Body")));
        Assert.Equal("horizontal", cut.Find(".hl-card").GetAttribute("data-hl-orientation"));
        Assert.Equal("true", cut.Find(".hl-card").GetAttribute("data-hl-separators"));
        Assert.Equal("Title", cut.Find("h2").TextContent);
        Assert.Equal("Body", cut.Find(".hl-card__content").TextContent);
        Assert.Null(cut.Find(".hl-card").GetAttribute("role"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.card")]
    public void SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("card.", fixture.RootElement.GetProperty("id").GetString());
        Assert.Equal("outlined", Render<HarborlineCard>().Find(".hl-card").GetAttribute("data-hl-variant"));
    }
}
