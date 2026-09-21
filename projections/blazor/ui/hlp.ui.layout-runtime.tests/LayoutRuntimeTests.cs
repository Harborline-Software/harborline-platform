using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LayoutRuntimeTests : BunitContext
{
    [Fact]
    public void ScreenFlowKeepsOrderStaticRegionsAndVersionProvenance()
    {
        var plan = new LayoutRuntimePlan("invoice", "version-7", "screen", [new("title", "layout.text", 0), new("table", "layout.table", 1)], [new("header", "layout.text", 0, "header.center")]);
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan));
        Assert.Single(cut.FindAll("[data-fragmentainer=screen]"));
        Assert.Equal(["title", "table"], cut.FindAll("[data-layout-block]").Select(node => node.GetAttribute("data-layout-block")));
        Assert.Single(cut.FindAll("[data-layout-static-region=header\\.center]"));
        Assert.Contains("version-7", cut.Markup);
    }

    [Fact]
    public void PersistedInvalidValueRendersDiagnosticWithoutClamp()
    {
        var plan = new LayoutRuntimePlan("invoice", "version-7", "page", [], [], [new("layout.numeric.out_of_range", "/blocks/0/placement/span")]);
        var cut = Render<HarborlineLayoutRuntime>(parameters => parameters.Add(x => x.Plan, plan));
        Assert.Contains("layout.numeric.out_of_range at /blocks/0/placement/span", cut.Markup);
        Assert.Single(cut.FindAll("[role=alert]"));
    }

    [Fact]
    public void EditorOffersTheClosedLayoutGrammarWithoutAReadingOrderOrNumericControl()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Medium = "page", Blocks = [new("dashboard", "layout.dashboard-widget")], PageRuns = [new("run-1", "letter", "letter-master")] })
            .Add(x => x.Catalogue, new LayoutAuthoringCatalogue([new("layout.table", "Table"), new("layout.dashboard-widget", "Dashboard widget")], ["header.center"], [new("letter", "Letter")], [new("letter-master", "Letter master")], ["header.center"], [new("revenue-glance", "Revenue glance")]))
            .Add(x => x.ValueChanged, value => changed = value));
        Assert.Contains("sm", cut.Find("[aria-label='Collapse below']").TextContent);
        Assert.DoesNotContain("xl", cut.Find("[aria-label='Collapse below']").TextContent);
        Assert.Contains("capture", cut.Find("[aria-label='Default intent']").TextContent);
        Assert.Contains("areas", cut.Find("[aria-label='Container flow']").TextContent);
        Assert.Contains("Revenue glance", cut.Find("[aria-label='Block 1 Helm widget']").TextContent);
        Assert.Contains("Letter", cut.Find("[aria-label='Page run 1 layout']").TextContent);
        Assert.DoesNotContain("reading order", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("input[type=number]"));
        cut.FindAll("button").Single(button => button.TextContent == "Add block").Click();
        Assert.Equal("layout.table", changed!.Blocks.Last().Kind);
    }
}
