using System.Text.Json;
using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Contracts.Authorization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellTests : BunitContext
{
    private const string MediumQuery="(min-width: 600px)";
    private const string ExpandedQuery="(min-width: 840px)";
    private const string LargeQuery="(min-width: 1200px)";
    private const string ExtraLargeQuery="(min-width: 1600px)";
    private readonly FakeMedia media=new();
    private static readonly RoleVocabulary Vocabulary=RoleVocabulary.FromApi([]);
    private static readonly HeldRoleSet NoRoles=new([]);
    public AppShellTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IMediaQueryObserver>(media);}
    private static RenderFragment Content(string value)=>b=>b.AddContent(0,value);
    private static PackNavigationAction Action(string id,string label,string binding="create",IReadOnlyList<string>? roles=null)=>new(id,label,"plus",binding,"mod+n",roles??[]);
    private static PackNavigationDeclaration Nav(IReadOnlyList<PackNavigationAction>? actions=null,IReadOnlyList<PackPanelDeclaration>? panels=null,bool mode=false)=>new(
        [new("operations","Operations",CountQueryRef:"counts.operations",Groups:[new("ops","Operations",["overview","inspections","trip"])],CreateActions:actions),new("front-desk","Front desk",Groups:[new("front","Front desk",["arrivals"])])],
        mode?new([new("operate","Operate",["operations"])]):null,panels);
    private static ShellNavigationState State(string? guidance=null)=>new(
        Items:new Dictionary<string,ShellNavItem>{{"overview",new("overview","Overview")},{"inspections",new("inspections","Inspections",Count:"218",Threads:[new("audit","Richmond retry audit",Actions:[new("delete","Delete",Destructive:true)])])},{"trip",new("trip","Trip plan",Threads:[new("rename","Old audit",Editing:true)])},{"arrivals",new("arrivals","Arrivals")}},
        Counts:new Dictionary<string,string>{{"counts.operations","3"}},CapabilityGuidanceByBinding:guidance is null?null:new Dictionary<string,string>{{"assets.create",guidance}});
    private IRenderedComponent<HarborlineAppShell> Shell(Action<ComponentParameterCollectionBuilder<HarborlineAppShell>>? add=null,PackNavigationDeclaration? nav=null,ShellNavigationState? state=null)=>Render<HarborlineAppShell>(p=>{p.Add(x=>x.ShellId,"ops").Add(x=>x.Navigation,nav??Nav()).Add(x=>x.NavigationState,state??State()).Add(x=>x.RoleVocabulary,Vocabulary).Add(x=>x.HeldRoles,NoRoles).Add(x=>x.ChildContent,Content("Body"));add?.Invoke(p);});

    [Fact] public void RendersLandmarksAndIdentity(){var cut=Shell();Assert.Single(cut.FindAll("main#main"));Assert.Single(cut.FindAll("nav"));Assert.Single(cut.FindAll("[data-shell-id=ops]"));Assert.Equal("true",cut.Find("[data-shell-bar-slot=rail-toggle] button").GetAttribute("aria-expanded"));}
    [Fact] public void TenantMarkAndPanelScrollRegionExposeEquivalentSemantics(){var cut=Shell(p=>p.Add(x=>x.EndPanel,Content("Panel details")).Add(x=>x.EndPanelLabel,"Pilot").Add(x=>x.EndPanelOpen,true));var mark=cut.Find("[data-tenant-mark]");Assert.Equal("img",mark.GetAttribute("role"));Assert.Equal("Tenant",mark.GetAttribute("aria-label"));var region=cut.Find(".hl-app-shell__end-panel-body");Assert.Equal("region",region.GetAttribute("role"));Assert.Equal("Pilot",region.GetAttribute("aria-label"));Assert.Equal("0",region.GetAttribute("tabindex"));Assert.NotNull(region.GetAttribute("data-shell-scroll-region"));}
    [Fact] public void RejectsMissingInputsAndDuplicateIdentity(){Assert.Equal("app-shell-id-required",Assert.Throws<InvalidOperationException>(()=>Render<HarborlineAppShell>(p=>p.Add(x=>x.Navigation,Nav()).Add(x=>x.ChildContent,Content("Body")))).Message);Assert.Equal("app-shell-body-required",Assert.Throws<InvalidOperationException>(()=>Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"ops").Add(x=>x.Navigation,Nav()))).Message);var duplicate=new PackNavigationDeclaration([new("ops","Ops",Groups:[new("g","G",["x","x"])])]);Assert.Equal("duplicate-nav-identity",Assert.Throws<InvalidOperationException>(()=>Shell(nav:duplicate)).Message);}
    [Fact] public void PinnedItemMovesNotCopiesAndPinIsIsolated(){var pins=new List<(string,bool)>();var navigated=0;var cut=Shell(p=>p.Add(x=>x.PinnedItemIds,new[]{"inspections"}).Add(x=>x.ActiveItemId,"inspections").Add(x=>x.PinToggled,v=>pins.Add(v)).Add(x=>x.Navigated,_=>navigated++));Assert.Contains("Inspections",cut.Find("[data-shell-zone=pinned]").TextContent);Assert.DoesNotContain("Inspections",cut.Find("[data-shell-zone=groups]").TextContent);cut.Find(".hl-app-shell__pin-toggle").Click();Assert.Single(pins);Assert.Equal(0,navigated);}
    [Fact] public void ThreadInteractionRemainsShellRuntimeState(){var activated=new List<ShellThreadEvent>();var renamed=new List<ShellThreadRenameEvent>();var cut=Shell(p=>p.Add(x=>x.ThreadActivated,v=>activated.Add(v)).Add(x=>x.ThreadRenamed,v=>renamed.Add(v)));cut.Find(".hl-app-shell__thread-btn").Click();Assert.Equal("inspections",activated[0].OwnerId);cut.Find(".hl-app-shell__kebab").Click();Assert.Equal("Delete",cut.Find("[role=menuitem]").TextContent);var input=cut.Find(".hl-app-shell__thread-rename");input.Input("Trip audit");input.KeyDown("Enter");Assert.Equal("Trip audit",renamed[0].Value);}
    [Fact] public void PackBindingInvokesHostAndDeniedCreateBecomesGuidance(){var calls=new List<string>();var allowed=Shell(p=>p.Add(x=>x.BindingInvoked,v=>calls.Add(v)),Nav([Action("new","New asset","assets.create")]));allowed.Find(".hl-app-shell__create").Click();Assert.Equal(["assets.create"],calls);const string guidance="Request the Maintainer role to create assets.";var denied=Shell(nav:Nav([Action("new","Create asset","assets.create",["tax.roles/maintainer"])]),state:State(guidance));Assert.Empty(denied.FindAll(".hl-app-shell__create"));Assert.Contains(guidance,denied.Find("[data-capability-guidance]").TextContent);Assert.Empty(denied.FindAll("[disabled]"));}
    [Fact] public void KernelOwnsBarRailCountsAndAddresses(){var state=State() with{RecentByWorkspace=new Dictionary<string,IReadOnlyList<ShellNavItem>>{{"operations",[new("recent","Recent")]}},SuggestedByWorkspace=new Dictionary<string,ShellNavItem>{{"operations",new("suggested","Suggested")}}};var notification=new PackPanelDeclaration("notifications","panels.notifications.toggle","mod+shift+b",360,180,false);var cut=Shell(p=>p.Add(x=>x.NotificationCount,7).Add(x=>x.FooterIdentity,new ShellFooterIdentity("Chris","Inspector")),Nav([Action("new","New")],[notification],mode:true),state);Assert.Equal(new[]{"mark","window-menu","rail-toggle","find","breadcrumb","cluster"},cut.FindAll("[data-shell-bar-slot]").Select(x=>x.GetAttribute("data-shell-bar-slot")));Assert.Equal(ShellChromeContract.RailZoneOrder,cut.FindAll("[data-shell-zone]").Select(x=>x.GetAttribute("data-shell-zone")));Assert.Equal("7",cut.Find("[data-notification-count]").TextContent);Assert.Empty(cut.FindAll("[data-notification-dot]"));Assert.Equal("/workspaces/operations",cut.Find("[data-shell-zone=workspaces] a").GetAttribute("href"));}
    [Fact] public void RailSeparatorSupportsArrowResizeAndValueSemantics(){var widths=new List<int>();var cut=Shell(p=>p.Add(x=>x.RailWidthChanged,v=>widths.Add(v)));var separator=cut.Find(".hl-app-shell__rail-resize");Assert.Equal("120",separator.GetAttribute("aria-valuemin"));Assert.Equal("216",separator.GetAttribute("aria-valuenow"));separator.KeyDown("ArrowRight");Assert.Equal([224],widths);cut.WaitForAssertion(()=>Assert.Equal("224",separator.GetAttribute("aria-valuenow")));}
    [Fact] public async Task DeclaredPanelToggleIsAbsentAt1199AndPresentAt1200(){var panel=new PackPanelDeclaration("documents","panels.documents.toggle","mod+shift+d",420,220,false,"Documents");media.Set(LargeQuery,false);var cut=Shell(nav:Nav(panels:[panel]));Assert.Empty(cut.FindAll("[data-shell-bar-slot=cluster] > [data-action-id=documents]"));Assert.Single(cut.FindAll(".hl-app-shell__actions-overflow"));await media.SetAsync(LargeQuery,true);cut.WaitForAssertion(()=>Assert.Single(cut.FindAll("[data-shell-bar-slot=cluster] > [data-action-id=documents]")));Assert.Empty(cut.FindAll(".hl-app-shell__actions-overflow"));}
    [Fact] public void Api58DeclarationFixtureDeserializesAndMaps(){var declaration=JsonSerializer.Deserialize<PackNavigationDeclaration>(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/api-58-pack-navigation.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;var view=PackNavigationMapper.Map(declaration,key=>key,new(),Vocabulary,NoRoles);Assert.Equal(["operations","definitions"],view.Workspaces.Select(x=>x.Id));Assert.Equal(["operate","configure"],view.Modes.Select(x=>x.Id));Assert.Equal(["documents","notes"],view.Panels.Select(x=>x.Id));Assert.Equal("documents.assets",view.Workspaces[0].DocumentSpine[0].Binding);var cut=Shell(nav:declaration);Assert.Empty(cut.FindAll("button[aria-label=Notifications]"));Assert.Empty(cut.FindAll("button[aria-label=Pilot]"));}
    [Fact] public void BellAndPilotFollowTheDeclaredPanelSet(){var live=new[]{new PackPanelDeclaration("notifications","panels.notifications.toggle","mod+shift+b",360,180,false),new PackPanelDeclaration("pilot","panels.pilot.toggle","mod+shift+p",400,300,false)};var cut=Shell(nav:Nav(panels:live));Assert.Single(cut.FindAll("button[aria-label=Notifications]"));Assert.Single(cut.FindAll("button[aria-label=Pilot]"));}
    [Fact] public async Task SharedAdaptationFixtureReplaysSheetDockTransitionsWithoutReplacingBodies()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/adaptation-v1.json")));
        using var dock=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        Assert.Equal(fixture.RootElement.GetProperty("breakpointLadder").EnumerateArray().Select(x=>x.GetProperty("expected").GetString()),fixture.RootElement.GetProperty("breakpointLadder").EnumerateArray().Select(x=>ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint(x.GetProperty("width").GetInt32()))));
        var panels=dock.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var initial=fixture.RootElement.GetProperty("initialOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        foreach(var transition in fixture.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var open=transition.TryGetProperty("openPanelIds",out var declared)?declared.EnumerateArray().Select(x=>x.GetString()!).ToArray():initial;
            var first=transition.GetProperty("steps")[0];SetViewport(first.GetProperty("width").GetInt32());
            using var cut=Shell(p=>p.Add(x=>x.DefaultOpenPanelIds,open).Add(x=>x.PanelContent,ProbeBody),Nav(panels:panels));
            var bodies=cut.FindComponents<StatefulPanelProbe>().ToDictionary(probe=>probe.Instance.PanelId,probe=>probe.Instance);
            foreach(var probe in bodies.Values)probe.Value=37;
            foreach(var step in transition.GetProperty("steps").EnumerateArray())
            {
                await SetViewportAsync(step.GetProperty("width").GetInt32());
                var width=step.GetProperty("width").GetInt32();
                Assert.Equal(step.GetProperty("expectedBreakpoint").GetString(),cut.Find("[data-shell-id]").GetAttribute("data-shell-breakpoint"));
                Assert.Equal(open,cut.FindAll("[data-shell-panel-id]").Select(panel=>panel.GetAttribute("data-shell-panel-id")));
                foreach(var expected in step.GetProperty("expectedContainerKinds").EnumerateObject())
                {
                    var panel=cut.Find($"[data-shell-panel-id='{expected.Name}']");Assert.Equal(expected.Value.GetString(),panel.GetAttribute("data-shell-container-kind"));
                    if(expected.Value.GetString()!="docked")Assert.NotNull(panel.QuerySelector("[data-sheet-close]"));
                    var current=cut.FindComponents<StatefulPanelProbe>().Single(probe=>probe.Instance.PanelId==expected.Name).Instance;
                    Assert.Same(bodies[expected.Name],current);Assert.Equal(37,current.Value);Assert.False(current.Disposed);
                }
            }
        }
    }
    [Fact] public async Task SharedClassPlacementRemainsConstantAcrossInteriorsAndBoundaries()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/adaptation-v1.json")));
        using var dock=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=dock.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        foreach(var row in fixture.RootElement.GetProperty("classPlacementCases").EnumerateArray())
        {
            var widths=row.GetProperty("widths").EnumerateArray().Select(x=>x.GetInt32()).ToArray();SetViewport(widths[0]);
            var open=row.GetProperty("openPanelIds").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            using var cut=Shell(p=>p.Add(x=>x.DefaultOpenPanelIds,open).Add(x=>x.PanelContent,PanelBody),Nav(panels:panels));
            foreach(var width in widths)
            {
                await SetViewportAsync(width);
                Assert.Equal(row.GetProperty("expectedBreakpoint").GetString(),cut.Find("[data-shell-id]").GetAttribute("data-shell-breakpoint"));
                Assert.Equal(row.GetProperty("expectedContainerKinds").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()),cut.FindAll("[data-shell-panel-id]").ToDictionary(x=>x.GetAttribute("data-shell-panel-id")!,x=>x.GetAttribute("data-shell-container-kind")));
                // Mirrors the gallery adaptation row at the same mount shape (ticket 266): the close control the
                // fixture sizes at sheetCloseTarget is carried by exactly the non-docked panels of this class. The
                // gallery measures the 44px box; bUnit has no layout, so this suite owns the declaration.
                Assert.Equal(row.GetProperty("expectedContainerKinds").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()!="docked"),cut.FindAll("[data-shell-panel-id]").ToDictionary(x=>x.GetAttribute("data-shell-panel-id")!,x=>x.QuerySelector("[data-sheet-close]")!=null));
            }
        }
    }
    public sealed class StatefulPanelProbe : ComponentBase, IDisposable
    {
        [Parameter] public string PanelId { get; set; } = "";
        public int Value { get; set; }
        public bool Disposed { get; private set; }
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        { builder.OpenElement(0,"span");builder.AddContent(1,$"{PanelId}:{Value}");builder.CloseElement(); }
        public void Dispose()=>Disposed=true;
    }
    private static RenderFragment ProbeBody(PackPanelDeclaration panel)=>builder=>
    { builder.OpenComponent<StatefulPanelProbe>(0);builder.AddAttribute(1,nameof(StatefulPanelProbe.PanelId),panel.Id);builder.CloseComponent(); };
    [Fact] public void SharedAdaptationFixtureKeepsContentFloorBySheetingNewestFirst()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/adaptation-v1.json")));
        using var dock=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=dock.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        foreach(var floorCase in fixture.RootElement.GetProperty("contentFloorCases").EnumerateArray())
        {
            var width=floorCase.GetProperty("width").GetInt32();var spread=floorCase.GetProperty("spread").GetBoolean();
            var open=floorCase.GetProperty("openPanelIds").EnumerateArray().Select(id=>panels.Single(panel=>panel.Id==id.GetString())).ToArray();
            var layout=DockLayout.Create(open,spread,ShellChromeContract.Breakpoint(width),width);
            Assert.Equal(floorCase.GetProperty("expectedContainerKinds").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetString()!),layout.Containers.ToDictionary(x=>x.Panel.Id,x=>x.Kind));
        }
    }
    [Fact] public void SharedAdaptationFixtureRendersExplicitSpreadAvailabilityAndReason()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/adaptation-v1.json")));
        using var dock=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=dock.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var initial=fixture.RootElement.GetProperty("initialOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        foreach(var spreadCase in fixture.RootElement.GetProperty("spreadAvailabilityCases").EnumerateArray())
        {
            var unavailable=spreadCase.GetProperty("spreadUnavailable").GetBoolean();var reason=spreadCase.GetProperty("reason").ValueKind==JsonValueKind.Null?null:spreadCase.GetProperty("reason").GetString();
            using var cut=Shell(p=>p.Add(x=>x.DefaultOpenPanelIds,initial).Add(x=>x.PanelContent,PanelBody).Add(x=>x.SpreadUnavailable,unavailable).Add(x=>x.SpreadUnavailableReason,reason),Nav(panels:panels));
            Assert.Equal(spreadCase.GetProperty("expectedDisabled").GetBoolean(),cut.Find("button[aria-label='Spread panels']").HasAttribute("disabled"));
            Assert.Equal(reason,cut.FindAll("[data-spread-unavailable-reason]").SingleOrDefault()?.TextContent);
        }
    }
    [Fact] public void SharedDockFixtureOpensEveryPanelWithoutEvictionAndRequiresExplicitSpread()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=fixture.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var order=fixture.RootElement.GetProperty("openingOrder").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var expected=fixture.RootElement.GetProperty("expected");
        var layout=order.Aggregate(DockLayout.Create([],expected.GetProperty("spreadInitially").GetBoolean()),(state,id)=>state.Open(panels.Single(panel=>panel.Id==id)));
        Assert.Equal(order,layout.OpenPanels.Select(panel=>panel.Id));
        Assert.Equal(expected.GetProperty("defaultPanes").EnumerateArray().Select(pane=>pane.EnumerateArray().Select(x=>x.GetString()!).ToArray()),layout.Panes.Select(pane=>pane.Panels.Select(panel=>panel.Id).ToArray()));
        Assert.Equal(expected.GetProperty("maximumDerivedStackDepth").GetInt32(),layout.Panes.Max(pane=>pane.Panels.Count));
        Assert.Equal(expected.GetProperty("evenSplit").EnumerateArray().Select(x=>x.GetDouble()),layout.Panes[0].Fractions);
        Assert.False(layout.Spread);
        var spread=layout.WithSpread(true);
        Assert.True(spread.Spread);Assert.Equal(expected.GetProperty("spreadPaneCount").GetInt32(),spread.Panes.Count);
        Assert.Equal(expected.GetProperty("minimumPaneWidth").GetInt32(),spread.MinimumPaneWidth);
        Assert.Equal(expected.GetProperty("pilotMinimumHeight").GetInt32(),spread.OpenPanels.Single(panel=>panel.Id=="pilot").MinimumHeight);
    }
    [Fact] public void SharedDockFixtureRendersOneUnnestedBodyScrollerPerPanel()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=fixture.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var order=fixture.RootElement.GetProperty("openingOrder").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var expected=fixture.RootElement.GetProperty("expected").GetProperty("bodyScrollRegionsPerPanel").GetInt32();
        var cut=Shell(p=>p.Add(x=>x.DefaultOpenPanelIds,order).Add(x=>x.PanelContent,panel=>Content($"{panel.Id} body")).Add(x=>x.Spread,false),Nav(panels:panels));
        var rendered=cut.FindAll("[data-shell-panel-id]");Assert.Equal(order,rendered.Select(panel=>panel.GetAttribute("data-shell-panel-id")));
        foreach(var panel in rendered){Assert.Equal(expected,panel.QuerySelectorAll("[data-shell-panel-body-scroll]").Length);Assert.Null(panel.QuerySelector("[data-shell-panel-body-scroll] [data-shell-scroll-region]"));}
    }
    [Fact] public void SharedDockFixtureReplaysEveryTransitionThroughPureModel()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=fixture.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var layout=DockLayout.Create([],fixture.RootElement.GetProperty("expected").GetProperty("spreadInitially").GetBoolean());
        foreach(var transition in fixture.RootElement.GetProperty("transitions").EnumerateArray())
        {
            var id=transition.GetProperty("panelId").GetString()!;
            layout=transition.GetProperty("action").GetString()=="open"?layout.Open(panels.Single(panel=>panel.Id==id)):layout.Close(id);
            Assert.Equal(transition.GetProperty("expectedOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!),layout.OpenPanels.Select(panel=>panel.Id));
            AssertDockTree(transition.GetProperty("expectedTree"),layout.Root);
        }
    }
    [Fact] public void SharedDockFixtureReplaysEveryTransitionThroughRenderedShellAndHostEvents()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=fixture.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var events=new List<string>();
        var cut=Shell(p=>p.Add(x=>x.LargeBarCapable,true).Add(x=>x.PanelContent,panel=>Content(panel.Id))
            .Add(x=>x.OpenPanelIdsChanged,ids=>events.Add($"openPanelIdsChanged:{string.Join(',',ids)}"))
            .Add(x=>x.BindingInvoked,binding=>events.Add($"bindingInvoked:{binding}"))
            .Add(x=>x.NotificationsCommand,()=>events.Add("notificationsCommand"))
            .Add(x=>x.PilotCommand,()=>events.Add("pilotCommand")),Nav(panels:panels));
        foreach(var transition in fixture.RootElement.GetProperty("transitions").EnumerateArray())
        {
            events.Clear();
            var id=transition.GetProperty("panelId").GetString()!;
            if(transition.GetProperty("action").GetString()=="close")cut.Find($"button[aria-label='Close {id}']").Click();
            else if(id=="notifications")cut.Find("button[aria-label=Notifications]").Click();
            else if(id=="pilot")cut.Find("button[aria-label=Pilot]").Click();
            else cut.Find($"[data-action-id={id}] button").Click();
            Assert.Equal(transition.GetProperty("expectedHostEvents").EnumerateArray().Select(x=>x.GetString()!),events);
            Assert.Equal(transition.GetProperty("expectedOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!),cut.FindAll("[data-shell-panel-id]").Select(panel=>panel.GetAttribute("data-shell-panel-id")));
            var dock=cut.FindAll(".hl-app-shell__dock");
            AssertRenderedDockTree(transition.GetProperty("expectedTree"),dock.Count==0?null:dock[0].FirstElementChild);
        }
    }
    [Fact] public void SharedDockFixtureKeepsOrdinaryPanelAffordancesEquivalent()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/dock-splitter-v1.json")));
        var panels=fixture.RootElement.GetProperty("declaredPanels").Deserialize<PackPanelDeclaration[]>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var invariant=fixture.RootElement.GetProperty("affordanceEquivalence");
        var initial=invariant.GetProperty("initialOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var cases=invariant.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(panels.Where(panel=>panel.Id is not "notifications" and not "pilot").Select(panel=>panel.Id),cases.Select(panelCase=>panelCase.GetProperty("panelId").GetString()));
        foreach(var panelCase in cases)
        foreach(var largeBarCapable in new[]{true,false})
        {
            var events=new List<string>();
            using var cut=Shell(p=>p.Add(x=>x.LargeBarCapable,largeBarCapable).Add(x=>x.DefaultOpenPanelIds,initial).Add(x=>x.PanelContent,panel=>Content(panel.Id))
                .Add(x=>x.OpenPanelIdsChanged,ids=>events.Add($"openPanelIdsChanged:{string.Join(',',ids)}"))
                .Add(x=>x.BindingInvoked,binding=>events.Add($"bindingInvoked:{binding}")),Nav(panels:panels));
            var id=panelCase.GetProperty("panelId").GetString()!;
            foreach(var stepName in new[]{"open","duplicateOpen"})
            {
                events.Clear();
                if(!largeBarCapable)cut.Find("button[aria-label=Panels]").Click();
                cut.Find($"[data-action-id={id}] button").Click();
                var expected=panelCase.GetProperty(stepName);
                Assert.Equal(expected.GetProperty("expectedHostEvents").EnumerateArray().Select(x=>x.GetString()!),events);
                Assert.Equal(expected.GetProperty("expectedOpenPanelIds").EnumerateArray().Select(x=>x.GetString()!),cut.FindAll("[data-shell-panel-id]").Select(panel=>panel.GetAttribute("data-shell-panel-id")));
                AssertRenderedDockTree(expected.GetProperty("expectedTree"),cut.Find(".hl-app-shell__dock").FirstElementChild);
            }
        }
    }
    [Fact] public void SharedChromeLawFixtureIsAppliedByBlazor()
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/chrome-law-mutations.json")));
        var root=fixture.RootElement;
        var attemptedOrder=root.GetProperty("packAttemptsZoneOrder").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var roles=root.GetProperty("deniedCreate").GetProperty("permittedRoles").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var guidance=root.GetProperty("deniedCreate").GetProperty("guidance").GetString()!;
        var notification=root.GetProperty("notification");
        var unread=notification.GetProperty("unread").GetInt32();
        var panel=notification.GetProperty("panel").Deserialize<PackPanelDeclaration>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var requiredFloor=root.GetProperty("content").GetProperty("requiredFloor").GetInt32();
        var attemptedFloor=root.GetProperty("content").GetProperty("attemptedFloor").GetInt32();
        var state=State(guidance) with{RecentByWorkspace=new Dictionary<string,IReadOnlyList<ShellNavItem>>{{"operations",[new("recent","Recent")]}},SuggestedByWorkspace=new Dictionary<string,ShellNavItem>{{"operations",new("suggested","Suggested")}}};
        var cut=Shell(p=>p.Add(x=>x.NotificationCount,unread).Add(x=>x.FooterIdentity,new ShellFooterIdentity("Chris","Inspector")).AddUnmatched("data-pack-zone-order",string.Join(',',attemptedOrder)),Nav([Action("create","Create asset","assets.create",roles)],[panel],mode:true),state);
        Assert.Equal(string.Join(',',attemptedOrder),cut.Find("[data-shell-id=ops]").GetAttribute("data-pack-zone-order"));
        Assert.NotEqual(attemptedOrder,cut.FindAll("[data-shell-zone]").Select(x=>x.GetAttribute("data-shell-zone")));
        Assert.Equal(ShellChromeContract.RailZoneOrder,cut.FindAll("[data-shell-zone]").Select(x=>x.GetAttribute("data-shell-zone")));
        Assert.Empty(cut.FindAll(".hl-app-shell__create"));Assert.Contains(guidance,cut.Markup);Assert.Empty(cut.FindAll("[disabled]"));
        Assert.Equal("count",notification.GetProperty("render").GetString());Assert.Equal(unread.ToString(),cut.Find("[data-notification-count]").TextContent);Assert.Equal("dot",notification.GetProperty("forbidden").GetString());Assert.Empty(cut.FindAll("[data-notification-dot]"));
        Assert.Equal(requiredFloor.ToString(),cut.Find("[data-shell-id=ops]").GetAttribute("data-shell-content-floor"));
        var css=File.ReadAllText(Repo("projections/blazor/ui/hlp.ui.app-shell/wwwroot/app-shell.css"));
        Assert.Matches($@"\.hl-app-shell__page\{{[^}}]*min-inline-size:{requiredFloor}px",css);
        Assert.DoesNotMatch($@"\.hl-app-shell__page\{{[^}}]*min-inline-size:{attemptedFloor}px",css);
        Assert.Equal(ShellChromeContract.ContentFloor,requiredFloor);Assert.True(ShellChromeContract.ContentFloor>attemptedFloor);
    }
    [Fact,Trait("ModuleConformance","hlp.ui.app-shell")]
    public void SharedFixtureDrivesActualRender()
    {
        var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if(string.IsNullOrWhiteSpace(raw)){Assert.NotNull(Shell());return;}
        using var fixture=JsonDocument.Parse(raw);
        var input=fixture.RootElement.GetProperty("input");
        var id=fixture.RootElement.GetProperty("id").GetString();
        if(id=="app-shell.id-required"){Assert.Equal("app-shell-id-required",Assert.Throws<InvalidOperationException>(()=>Render<HarborlineAppShell>(p=>p.Add(x=>x.Navigation,Nav()).Add(x=>x.ChildContent,Content("Body")))).Message);return;}
        if(id=="app-shell.body-required"){Assert.Equal("app-shell-body-required",Assert.Throws<InvalidOperationException>(()=>Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"ops").Add(x=>x.Navigation,Nav()))).Message);return;}
        if(id=="app-shell.chrome-law-mutations"){SharedChromeLawFixtureIsAppliedByBlazor();return;}
        if(id=="app-shell.panel-set-live-absent")
        {
            var declaration=JsonSerializer.Deserialize<PackNavigationDeclaration>(File.ReadAllText(Repo("conformance/hlp.ui.app-shell/api-58-pack-navigation.json")),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
            var absent=Shell(nav:declaration);
            Assert.Empty(absent.FindAll("button[aria-label=Notifications]"));Assert.Empty(absent.FindAll("button[aria-label=Pilot]"));return;
        }
        if(id=="app-shell.panel-set-live-present")
        {
            var panels=input.GetProperty("declaredPanelSet").EnumerateArray().Select(x=>x.GetString()!).Select(panelId=>new PackPanelDeclaration(panelId,$"panels.{panelId}.toggle",$"mod+shift+{panelId[0]}",400,panelId=="pilot"?300:180,false)).ToArray();
            var gate=input.GetProperty("capabilityGate");var guidance=gate.GetProperty("guidance").GetString()!;
            var present=Shell(nav:Nav([Action("create","Create asset",gate.GetProperty("binding").GetString()!,["tax.roles/maintainer"])],panels),state:State(guidance));
            Assert.Single(present.FindAll("button[aria-label=Notifications]"));Assert.Single(present.FindAll("button[aria-label=Pilot]"));Assert.Empty(present.FindAll(".hl-app-shell__create"));Assert.Contains(guidance,present.Find("[data-capability-guidance]").TextContent);return;
        }
        var shellId=input.TryGetProperty("shellId",out var shell)&&shell.ValueKind==JsonValueKind.String?shell.GetString()!:"ops";
        var cut=Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,shellId).Add(x=>x.Navigation,Nav()).Add(x=>x.NavigationState,State()).Add(x=>x.RoleVocabulary,Vocabulary).Add(x=>x.HeldRoles,NoRoles).Add(x=>x.ChildContent,Content("content")));
        Assert.Single(cut.FindAll($"[data-shell-id='{shellId}']"));Assert.Single(cut.FindAll("main#main"));
    }
    private static void AssertDockTree(JsonElement expected,DockNode? actual)
    {
        if(expected.ValueKind==JsonValueKind.Null){Assert.Null(actual);return;}
        if(expected.GetProperty("kind").GetString()=="pane")
        {
            var pane=Assert.IsType<DockPane>(actual);
            Assert.Equal(expected.GetProperty("panels").EnumerateArray().Select(x=>x.GetString()!),pane.Panels.Select(panel=>panel.Id));
            Assert.Equal(expected.GetProperty("fractions").EnumerateArray().Select(x=>x.GetDouble()),pane.Fractions);
            return;
        }
        var split=Assert.IsType<DockSplit>(actual);
        Assert.Equal(expected.GetProperty("orientation").GetString(),split.Orientation);Assert.Equal(expected.GetProperty("ratio").GetDouble(),split.Ratio);
        AssertDockTree(expected.GetProperty("first"),split.First);AssertDockTree(expected.GetProperty("second"),split.Second);
    }
    private static void AssertRenderedDockTree(JsonElement expected,IElement? actual)
    {
        if(expected.ValueKind==JsonValueKind.Null){Assert.Null(actual);return;}
        Assert.NotNull(actual);
        if(expected.GetProperty("kind").GetString()=="pane")
        {
            Assert.Contains("hl-app-shell__dock-pane",actual.ClassList);
            var panels=actual.Children.Where(child=>child.HasAttribute("data-shell-panel-id")).ToArray();
            Assert.Equal(expected.GetProperty("panels").EnumerateArray().Select(x=>x.GetString()!),panels.Select(panel=>panel.GetAttribute("data-shell-panel-id")));
            Assert.Equal(expected.GetProperty("fractions").EnumerateArray().Select(x=>x.GetDouble()),panels.Select(PanelFraction));
            return;
        }
        Assert.Contains("hl-app-shell__dock-split",actual.ClassList);Assert.Equal(expected.GetProperty("orientation").GetString(),actual.GetAttribute("data-orientation"));Assert.Equal(expected.GetProperty("ratio").GetDouble(),double.Parse(actual.GetAttribute("data-split-ratio")!,CultureInfo.InvariantCulture));
        var children=actual.Children.Where(child=>!child.ClassList.Contains("hl-app-shell__dock-divider")).ToArray();
        AssertRenderedDockTree(expected.GetProperty("first"),children[0]);AssertRenderedDockTree(expected.GetProperty("second"),children[1]);
    }
    private static double PanelFraction(IElement panel)
    {
        const string prefix="--hl-app-shell-panel-fraction:";
        var declaration=panel.GetAttribute("style")!.Split(';').Single(value=>value.StartsWith(prefix,StringComparison.Ordinal));
        return double.Parse(declaration[prefix.Length..],CultureInfo.InvariantCulture);
    }
    private static RenderFragment PanelBody(PackPanelDeclaration panel)=>builder=>{builder.OpenElement(0,"div");builder.AddAttribute(1,"data-test-panel-body",panel.Id);builder.AddContent(2,panel.Id);builder.CloseElement();};
    private void SetViewport(int width){media.Set(MediumQuery,width>=600);media.Set(ExpandedQuery,width>=840);media.Set(LargeQuery,width>=1200);media.Set(ExtraLargeQuery,width>=1600);for(var i=1;i<=6;i++)media.Set($"(min-width: {420+i*300}px)",width>=420+i*300);}
    private async Task SetViewportAsync(int width){await media.SetAsync(MediumQuery,width>=600);await media.SetAsync(ExpandedQuery,width>=840);await media.SetAsync(LargeQuery,width>=1200);await media.SetAsync(ExtraLargeQuery,width>=1600);for(var i=1;i<=6;i++)await media.SetAsync($"(min-width: {420+i*300}px)",width>=420+i*300);}
    private static string Repo(string relative)=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../",relative));
    private sealed class FakeMedia:IMediaQueryObserver{private readonly Dictionary<string,bool> values=[];private readonly Dictionary<string,List<Func<MediaQueryChange,ValueTask>>> callbacks=[];public void Set(string query,bool value)=>values[query]=value;public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> callback,CancellationToken cancellationToken=default){if(!callbacks.TryGetValue(query,out var list))callbacks[query]=list=[];list.Add(callback);return new(new Subscription(query,values.GetValueOrDefault(query,true)));}public async Task SetAsync(string query,bool value){values[query]=value;if(callbacks.TryGetValue(query,out var list))foreach(var callback in list.ToArray())await callback(new(query,value));}private sealed record Subscription(string Query,bool Matches):IMediaQuerySubscription{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
