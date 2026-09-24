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

    [Fact(DisplayName = "layout-auth-13..16: the editor binds a block to any of the five kinds and names it from the catalogue")]
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

    [Fact(DisplayName = "layout-auth-31: the editor marks an unbound block as needing a binding instead of removing it")]
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

    [Fact(DisplayName = "layout-auth-18: a repeating block binds a collection and its row subtree is authored once")]
    public void RepeatingBlockBindsACollectionAndItsRowSubtreeIsAuthoredOnce()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("lines", "layout.table", new("query", "views.invoice-lines")),
            new("amount", "layout.table", new("record_field", "line.amount"), ParentId: "lines"),
            new("total", "layout.table", new("measure", "invoice.total")),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        // Only a collection binding (a query or a record field) can repeat; a measure cannot.
        Assert.Empty(cut.FindAll("[aria-label='Block 3 repeats per row']"));
        cut.Find("[aria-label='Block 1 repeats per row']").Change(true);
        Assert.Equal([blocks[0] with { Repeating = true }, blocks[1], blocks[2]], changed!.Blocks);

        // The row subtree is authored once, as the repeating block's children: a block may be
        // placed inside it, and never inside itself or its own descendant.
        var parentOfLines = cut.Find("[aria-label='Block 1 parent']").TextContent;
        Assert.DoesNotContain("Block 1", parentOfLines, StringComparison.Ordinal);
        Assert.DoesNotContain("Block 2", parentOfLines, StringComparison.Ordinal);
        Assert.Contains("Block 3", parentOfLines, StringComparison.Ordinal);
        cut.Find("[aria-label='Block 3 parent']").Change("lines");
        Assert.Equal([blocks[0], blocks[1], blocks[2] with { ParentId = "lines" }], changed.Blocks);
    }

    [Fact(DisplayName = "layout-auth-18: rebinding a repeating block to a kind that is not a collection stops it repeating")]
    public void RebindingARepeatingBlockToANonCollectionKindStopsItRepeating()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("lines", "layout.table", new("query", "views.invoice-lines"), Repeating: true)] })
            .Add(x => x.Catalogue, Catalogue())
            .Add(x => x.ValueChanged, value => changed = value));

        Assert.True(cut.Find("[aria-label='Block 1 repeats per row']").HasAttribute("checked"));
        cut.Find("[aria-label='Block 1 binding kind']").Change("measure");
        Assert.Equal(new LayoutAuthoringBlock("lines", "layout.table", new("measure", "")), changed!.Blocks.Single());
    }

    [Fact(DisplayName = "layout-auth-19: a related block traverses a declared Records relationship and persists only its key")]
    public void RelatedBlockTraversesADeclaredRelationshipAndPersistsOnlyItsKey()
    {
        LayoutAuthoringDraft? changed = null;
        LayoutAuthoringBlock[] blocks =
        [
            new("supplier", "layout.table", new("record_field", "supplier.name")),
            new("entry", "layout.table", new("record_field", "invoice.reference"), Intent: "capture"),
        ];
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = blocks })
            .Add(x => x.Catalogue, Catalogue() with { Relationships = [new("invoice.supplier", "Invoice supplier")] })
            .Add(x => x.ValueChanged, value => changed = value));

        // A related block observes; a capture block is never offered a traversal.
        Assert.Empty(cut.FindAll("[aria-label='Block 2 related record']"));
        // Only relationships Records declares are offered, and the block stores the key alone.
        var related = cut.Find("[aria-label='Block 1 related record']");
        Assert.Equal(["Not related", "Invoice supplier"], related.QuerySelectorAll("option").Select(option => option.TextContent));
        Assert.Equal(["", "invoice.supplier"], related.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        related.Change("invoice.supplier");
        Assert.Equal([blocks[0] with { RelatedRelationship = "invoice.supplier" }, blocks[1]], changed!.Blocks);
    }

    [Fact(DisplayName = "layout-auth-19: a related block that stops observing drops its relationship")]
    public void RelatedBlockThatStopsObservingDropsItsRelationship()
    {
        LayoutAuthoringDraft? changed = null;
        var cut = Render<HarborlineLayoutAuthoringEditor>(parameters => parameters
            .Add(x => x.Value, LayoutAuthoringDraft.Empty with { Blocks = [new("supplier", "layout.table", new("record_field", "supplier.name"), RelatedRelationship: "invoice.supplier")] })
            .Add(x => x.Catalogue, Catalogue() with { Relationships = [new("invoice.supplier", "Invoice supplier")] })
            .Add(x => x.ValueChanged, value => changed = value));

        cut.Find("[aria-label='Block 1 intent']").Change("capture");
        Assert.Equal(new LayoutAuthoringBlock("supplier", "layout.table", new("record_field", "supplier.name"), Intent: "capture"), changed!.Blocks.Single());
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
