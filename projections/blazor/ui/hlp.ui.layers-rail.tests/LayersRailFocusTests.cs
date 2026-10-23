using Bunit;
using Harborline.Foundation.Builder;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Contract: focus is never stranded and the tree is one tab stop, the treeitem itself (APG treeview).
// bUnit sees the module requests and LayersRailScript runs the module's selectors; a real-browser probe
// kept with the batch evidence (outside this repository) checks where focus actually lands.
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
    public void TheTreeIsOneTabStopTheTreeitemAndItsButtonsAreNotTabStops()
    {
        var cut = Render<LayersRailHost>();
        Assert.All(cut.FindAll("[role=tree] button"), b => Assert.Equal("-1", b.GetAttribute("tabindex")));
        var stop = Assert.Single(cut.FindAll("[role=tree] [tabindex]"), e => e.GetAttribute("tabindex") != "-1");
        Assert.Equal("treeitem", stop.GetAttribute("role"));
        Assert.Equal("0", stop.GetAttribute("tabindex"));
    }

    [Fact]
    public void TheFirstTabStopIsTheSelectedNode()
    {
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Selected, "child"));
        Assert.Equal("0", cut.Find("[role=treeitem][data-node-id=child]").GetAttribute("tabindex"));
        Assert.Equal("-1", cut.Find("[role=treeitem][data-node-id=root]").GetAttribute("tabindex"));
    }

    [Fact]
    public void TheModuleFocusesTheTreeitemAndPreventsScrollingForTheTreeKeysOnIt()
    {
        var cut = Render<LayersRailHost>();
        var result = LayersRailScript.Run("child", "ArrowDown", "ArrowUp", "ArrowLeft", "ArrowRight", "Home", "End", "Enter", " ", "a");
        var target = cut.Find("aside").QuerySelector(result.FocusSelector!);
        Assert.Equal(cut.Find("[role=treeitem][data-node-id=child]").OuterHtml, target?.OuterHtml);
        var treeitem = cut.Find("[role=treeitem][data-node-id=child]");
        foreach (var key in result.KeyEvents)
        {
            Assert.Equal(key.Key != "a", key.Prevented);
            // Every selector the listener checks on the event target describes the treeitem, not its buttons.
            Assert.All(key.Queried, selector => { Assert.True(treeitem.Matches(selector), selector); Assert.False(treeitem.QuerySelector(".hl-layers-rail__node-control")!.Matches(selector), selector); });
        }
    }

    [Fact]
    public void ClickingTheNodeButtonOrDisclosureHandsFocusToTheTreeitemAndTheDisclosureDoesNotSelect()
    {
        var selected = new List<string>();
        var cut = Render<LayersRailHost>(p => p.Add(x => x.OnSelect, selected.Add));
        cut.Find("[data-node-id=sibling] .hl-layers-rail__node-control").Click();
        Assert.Equal("sibling", module.Invocations["focusNode"].Last().Arguments[1]);
        cut.Find("[data-node-id=root] .hl-layers-rail__disclosure").Click();
        Assert.Equal("root", module.Invocations["focusNode"].Last().Arguments[1]);
        Assert.Equal(["sibling"], selected);
        Assert.Equal("false", cut.Find("[data-node-id=root]").GetAttribute("aria-selected"));
    }

    [Fact]
    public void EachTreeitemIsNamedByItsLabelAloneAndDescribedByItsProvenance()
    {
        var cut = Render<LayersRailHost>();
        var root = cut.Find("[role=treeitem][data-node-id=root]");
        var label = root.QuerySelector(".hl-layers-rail__node-label")!;
        Assert.Equal(label.Id, root.GetAttribute("aria-labelledby"));
        Assert.Equal("Root", label.TextContent);
        Assert.Equal(root.QuerySelector(".hl-layers-rail__source")!.Id, root.GetAttribute("aria-describedby"));
        // Ids are unique across rail instances.
        var other = Render<LayersRailHost>().Find("[role=treeitem][data-node-id=root]");
        Assert.NotEqual(root.GetAttribute("aria-labelledby"), other.GetAttribute("aria-labelledby"));
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
    public void ActivatingTheTreeitemItselfSelectsOnceAndModifierChordsAreLeftToTheBrowser()
    {
        var selected = new List<string>();
        var cut = Render<LayersRailHost>(p => p.Add(x => x.OnSelect, selected.Add));
        cut.Find("[role=treeitem][data-node-id=child]").Click();
        Assert.Equal(["child"], selected);
        foreach (var chord in new[] { new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", CtrlKey = true }, new() { Key = " ", MetaKey = true }, new() { Key = "Enter", AltKey = true } })
            cut.Find("[role=treeitem][data-node-id=child]").KeyDown(chord);
        Assert.Equal(["child"], selected);
        // The pointer-only buttons never take mouse focus, so a key can't start on them before focus returns to the treeitem.
        Assert.All(cut.FindAll("[role=treeitem] button"), button => Assert.Contains(button.Attributes, attribute => attribute.Name == "blazor:onmousedown:preventdefault"));
    }

    [Fact]
    public void EnterAndSpaceOnTheTreeitemSelectExactlyOnceEach()
    {
        var selected = new List<string>();
        var cut = Render<LayersRailHost>(p => p.Add(x => x.OnSelect, selected.Add));
        cut.Find("[role=treeitem][data-node-id=child]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(["child"], selected);
        cut.Find("[role=treeitem][data-node-id=child]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        Assert.Equal(["child", "child"], selected);
        Assert.Single(module.Invocations["observeTreeKeys"]);
    }

    [Fact]
    public void ADerivedParentStartsExpandedAsAnExplicitOneDoes()
    {
        IReadOnlyList<CanvasNode> nodes = [new("root", "section", "Root", 0, null), new("child", "field", "Child", 1, "root")];
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Nodes, nodes));
        Assert.Equal("true", cut.Find("[data-node-id=root]").GetAttribute("aria-expanded"));
        Assert.NotNull(cut.Find("[data-node-id=child]"));
    }

    [Fact]
    public async Task ACyclicOrSelfParentedTreeFailsClosedInsteadOfHanging()
    {
        IReadOnlyList<CanvasNode> nodes = [new("a", "field", "A", 1, "a", HasChildren: true)];
        // A hang fails the test after 10 seconds instead of blocking the run.
        var error = await Task.Run(() => Assert.Throws<InvalidOperationException>(() => Render<LayersRailHost>(p => p.Add(x => x.Nodes, nodes)))).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("invalid-outline-tree", error.Message);
    }

    [Fact]
    public void WhenTheHostRemovesTheFocusedNodeFocusFollowsOnlyIfItWasLost()
    {
        IReadOnlyList<CanvasNode> before = [new("a", "field", "A", 0, null), new("b", "field", "B", 0, null)];
        IReadOnlyList<CanvasNode> after = [new("b", "field", "B", 0, null)];
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Nodes, before));
        cut.Render(p => p.Add(x => x.Nodes, after));
        var request = module.Invocations["focusNode"].Last();
        Assert.Equal("b", request.Arguments[1]);
        Assert.Equal(true, request.Arguments[2]);
        Assert.Equal("0", cut.Find("[role=treeitem][data-node-id=b]").GetAttribute("tabindex"));
    }

    [Fact]
    public void BlankCallerLabelsKeepTheDefaultsAndNoNameIsEmpty()
    {
        var d = DefaultRailLabels.Value;
        var blank = d with { ExitLens = " ", Insert = "", CollapseNode = "  ", ActivateLens = _ => " ", RailRegion = "", LensesHeading = " ", OutlineHeading = "" };
        var cut = Render<LayersRailHost>(p => p.Add(x => x.Labels, blank).Add(x => x.OnInsert, () => { }));
        Assert.Equal(d.ExitLens, cut.Find(".hl-layers-rail__status-close").GetAttribute("aria-label"));
        Assert.Equal(d.Insert, cut.Find(".hl-layers-rail__insert").GetAttribute("aria-label"));
        Assert.Equal("Layers", cut.Find("aside").GetAttribute("aria-label"));
        Assert.Equal(d.LensesHeading, cut.Find("[role=group]").GetAttribute("aria-label"));
        Assert.Equal(d.OutlineHeading, cut.Find("[role=tree]").GetAttribute("aria-label"));
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
