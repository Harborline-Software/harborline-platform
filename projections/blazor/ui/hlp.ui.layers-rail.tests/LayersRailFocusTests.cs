using Bunit;
using Harborline.Foundation.Builder;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: focus is never stranded and the tree is one tab stop. bUnit sees the module requests; the
// Chromium probe layers-rail-focus.mjs checks where focus actually lands.
public sealed class LayersRailFocusTests : BunitContext
{
    private readonly BunitJSModuleInterop module;
    public LayersRailFocusTests() { module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/layers-rail.js"); module.Mode = JSRuntimeMode.Loose; }

    [Fact]
    public void ExitLensMovesFocusToTheLensThatWasActiveBeforeTheStatusIsRemoved()
    {
        var cut = Render<LayersRailHost>();
        cut.Find(".hl-layers-rail__status-close").Click();
        Assert.Empty(cut.FindAll("[role=status]"));
        Assert.Equal("layout", Assert.Single(module.Invocations["focusLens"]).Arguments[1]);
        Assert.Equal("layout", cut.Find(".hl-layers-rail__lens-activate").GetAttribute("data-lens-id"));
    }

    [Fact]
    public void TheTreeIsOneTabStopAndDisclosuresAreNotTabStops()
    {
        var cut = Render<LayersRailHost>();
        Assert.All(cut.FindAll(".hl-layers-rail__disclosure"), d => Assert.Equal("-1", d.GetAttribute("tabindex")));
        Assert.Single(cut.FindAll("[role=tree] button"), b => b.GetAttribute("tabindex") != "-1");
    }

    [Fact]
    public void ACollapsedNodeStaysCollapsedWhenTheHostReRendersAfterASelection()
    {
        var cut = Render<LayersRailHost>();
        cut.Find(".hl-layers-rail__disclosure").Click();
        Assert.Empty(cut.FindAll("[data-node-id=child]"));
        cut.Find("[data-node-id=sibling] .hl-layers-rail__node-control").Click();
        Assert.Empty(cut.FindAll("[data-node-id=child]"));
    }

    [Fact]
    public void BlankCallerLabelsKeepTheDefaultsAndNoNameIsEmpty()
    {
        var d = DefaultRailLabels.Value;
        var blank = d with { ExitLens = " ", Insert = "", CollapseNode = "  ", ActivateLens = _ => " ", RailRegion = "" };
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Labels, blank).Add(x => x.OnInsert, () => { }));
        Assert.Equal(d.ExitLens, cut.Find(".hl-layers-rail__status-close").GetAttribute("aria-label"));
        Assert.Equal(d.Insert, cut.Find(".hl-layers-rail__insert").GetAttribute("aria-label"));
        Assert.Equal("Layers", cut.Find("aside").GetAttribute("aria-label"));
        Assert.Equal(d.ActivateLens("Layout"), cut.Find(".hl-layers-rail__lens-activate").GetAttribute("aria-label"));
        foreach (var button in cut.FindAll("button")) Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("aria-label") ?? button.TextContent), button.OuterHtml);
    }

    [Fact]
    public void CarriesArabicAndPseudoLocalizedCallerText()
    {
        var arabic = DefaultRailLabels.Value with { RailRegion = "الطبقات", ExitLens = "الخروج من العدسة" };
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Labels, arabic));
        Assert.Equal("الطبقات", cut.Find("aside").GetAttribute("aria-label"));
        Assert.Equal("الخروج من العدسة", cut.Find(".hl-layers-rail__status-close").GetAttribute("aria-label"));
        const string pseudo = "⟦ Ļåÿëřš ··· ⟧";
        Assert.Equal(pseudo, Render<LayersRailHost>(p => p.Add(x => x.Labels, DefaultRailLabels.Value with { RailRegion = pseudo })).Find("aside").GetAttribute("aria-label"));
    }
}
