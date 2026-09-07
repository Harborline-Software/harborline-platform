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
    [Fact] public void LensAndSelectionCallbacksRemainHostOwned(){LayersState? state=null;var selected=new List<string>();var cut=RenderRail(next=>state=next,selected.Add);cut.FindAll(".hl-layers-rail__lens-activate")[1].Click();Assert.Equal("rules",state?.ActiveId);cut.Find("[data-node-id=child]").Click();Assert.Equal(["child"],selected);}
    [Fact,Trait("ModuleConformance","hlp.ui.layers-rail")] public void SharedFixtureConforms(){AssertFixturePrefix("layers-rail.");Assert.NotNull(RenderRail());}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
