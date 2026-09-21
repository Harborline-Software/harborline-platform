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

    [Fact]
    public void EditorBindsABlockToAnyOfTheFiveKindsAndNamesItFromTheCatalogue()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("total", "layout.table", new("measure", "invoice.total"))] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        var kind = cut.Find("[aria-label='Block 1 binding kind']");
        foreach (var label in new[] { "Record field", "Query", "Measure", "Template", "Static content" })
            Assert.Contains(label, kind.TextContent, StringComparison.Ordinal);
        // The name list follows the chosen kind and offers nothing from another kind.
        Assert.Contains("Invoice total", cut.Find("[aria-label='Block 1 binding name']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Open invoices", cut.Find("[aria-label='Block 1 binding name']").TextContent, StringComparison.Ordinal);

        // Rebinding to another kind preserves the block and clears the name (layout-auth-31).
        kind.Change("query");
        Assert.Equal(new LayoutAuthoringBinding("query", ""), changed!.Blocks.Single().Binding);
        Assert.Equal("total", changed.Blocks.Single().Id);
    }

    [Fact]
    public void EditorMarksAnUnboundBlockAsNeedingABindingInsteadOfRemovingIt()
    {
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("orphan", "layout.table", new("query", ""))] })
            .Add(x => x.Catalogue, Catalogue()));

        Assert.Equal("Block 1 needs a binding", cut.Find("[role=status]").TextContent);
        Assert.Single(cut.FindAll("[aria-label='Block 1 binding kind']"));
    }

    [Fact]
    public void EditorAuthorsStaticContentOnTheBlockRatherThanLookingItUp()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("notice", "layout.table", new("static", ""))] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.Empty(cut.FindAll("[aria-label='Block 1 binding name']"));
        cut.Find("[aria-label='Block 1 binding content']").Change("Registered office: Leeds");
        Assert.Equal(new LayoutAuthoringBinding("static", "Registered office: Leeds"), changed!.Blocks.Single().Binding);
    }

    private static LayoutAuthoringCatalogue Catalogue() => new(
        [new("layout.table", "Table")],
        ["header.center"],
        Bindables: new Dictionary<string, IReadOnlyList<LayoutAuthoringOption>>(StringComparer.Ordinal)
        {
            ["record_field"] = [new("invoice.supplier", "Supplier")],
            ["query"] = [new("views.open-invoices", "Open invoices")],
            ["measure"] = [new("invoice.total", "Invoice total")],
            ["template"] = [new("tpl.remittance", "Remittance")],
        });
}
