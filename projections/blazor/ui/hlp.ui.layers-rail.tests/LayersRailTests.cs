using Bunit;
using Harborline.Foundation.Builder;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class LayersRailTests : BunitContext
{
    public LayersRailTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    private static readonly IReadOnlyList<AspectLens> Lenses=[new("layout","Layout",LensTone.Accent,AspectLensKind.Colorize,_=>new(true)),new("rules","Rules",LensTone.Warning,AspectLensKind.Filter,id=>new(id=="child"))];
    private static readonly IReadOnlyList<CanvasNode> Nodes=[new("root","group","Root",0,null,HasChildren:true),new("child","field","Child",1,"root")];
    private IRenderedComponent<HarborlineLayersRail> RenderRail(Action<LayersState>? changed=null,Action<string>? selected=null)=>Render<HarborlineLayersRail>(p=>p.Add(x=>x.Lenses,Lenses).Add(x=>x.State,LayersState.Initialize(Lenses.Select(x=>x.Id),"layout")).Add(x=>x.StateChanged,state=>changed?.Invoke(state)).Add(x=>x.Canvas,new CanvasModel(Nodes,"child",selected??(_=>{}))));
    [Fact] public void StructureAndTreeLevelsMatch(){var cut=RenderRail();Assert.Equal("complementary",cut.Find("aside").LocalName=="aside"?"complementary":"missing");Assert.Equal(2,cut.FindAll("[role=treeitem]").Count);Assert.Equal("2",cut.FindAll("[role=treeitem]")[1].GetAttribute("aria-level"));}
    // T-962: the tree is one Tab stop, and its initial stop is the selected node when it is visible,
    // else the first node, as React's initial state (APG treeview: focus entering a single-select tree
    // lands on the selected node). Blazor chose the first node, so the two lanes walked different tab
    // stops. Both lanes keep the roving stop where focus last was after that, so a later selection
    // made outside the tree does not move it.
    [Fact] public void TheTreeTabStopStartsOnTheSelectedNodeOrTheFirst(){
        string StopOf(IRenderedComponent<HarborlineLayersRail> cut)=>Assert.Single(cut.FindAll(".hl-layers-rail__node-control[tabindex='0']")).Closest("[data-node-id]")!.GetAttribute("data-node-id")!;
        Assert.Equal("child",StopOf(RenderRail()));
        var unselected=Render<HarborlineLayersRail>(p=>p.Add(x=>x.Lenses,Lenses).Add(x=>x.State,LayersState.Initialize(Lenses.Select(x=>x.Id),"layout")).Add(x=>x.Canvas,new CanvasModel(Nodes,null,_=>{})));
        Assert.Equal("root",StopOf(unselected));
    }
    // T-962: a culture's negative sign need not be U+002D (ar, he and fa add a direction mark; sv, nb,
    // fi, lt, sl, hr and eu use U+2212), and HTML integer parsing refuses it, so every other tree node
    // became a tab stop. The tabindex is invariant.
    [Theory, InlineData("ar-SA"), InlineData("he-IL"), InlineData("fa-IR"), InlineData("sv-SE"), InlineData("nb-NO")] public void TreeTabIndexIsInvariantUnderEveryCulture(string culture){var previous=System.Globalization.CultureInfo.CurrentCulture;try{System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo(culture);var cut=RenderRail();Assert.Equal(new[]{"-1","0"},cut.FindAll(".hl-layers-rail__node-control").Select(x=>x.GetAttribute("tabindex") ?? "").ToArray());}finally{System.Globalization.CultureInfo.CurrentCulture=previous;}}
    [Fact] public void LensAndSelectionCallbacksRemainHostOwned(){LayersState? state=null;var selected=new List<string>();var cut=RenderRail(next=>state=next,selected.Add);cut.FindAll(".hl-layers-rail__lens-activate")[1].Click();Assert.Equal("rules",state?.ActiveId);cut.Find("[data-node-id=child]").Click();Assert.Equal(["child"],selected);}
    [Theory,Trait("ModuleConformance","hlp.ui.layers-rail")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.layers-rail", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("layers-rail.");Assert.NotNull(RenderRail());
        });
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
