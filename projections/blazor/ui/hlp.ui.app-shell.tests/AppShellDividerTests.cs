using System.Text.Json;
using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Contracts.Authorization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppShellDividerTests : BunitContext
{
    // A case names its own panel set, so depth2 opens three panels: that nests a stacked pane inside the
    // horizontal tree and replays the clamp and no-eviction rules against the divider two levels down.
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public void SharedDividerScripts(int index)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../conformance/hlp.ui.app-shell/dividers-v1.json"))));
        var fixture=json.RootElement;var row=fixture.GetProperty("cases")[index];
        var declared=JsonSerializer.Deserialize<PackPanelDeclaration[]>(fixture.GetProperty("panels").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var ids=row.GetProperty("panels").EnumerateArray().Select(id=>id.GetString()!).ToArray();
        var panels=ids.Select(id=>declared.Single(panel=>panel.Id==id)).ToArray();
        Services.AddSingleton<IMediaQueryObserver>(new Media());JSInterop.Mode=JSRuntimeMode.Loose;
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction",_=>true).SetResult(1);
        module.Setup<double>("measure",_=>true).SetResult(row.GetProperty("extent").GetDouble());
        var cut=Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"dividers").Add(x=>x.Navigation,new PackNavigationDeclaration([new("ops","Ops")],PanelSet:panels)).Add(x=>x.RoleVocabulary,RoleVocabulary.FromApi([])).Add(x=>x.HeldRoles,new HeldRoleSet([])).Add(x=>x.ChildContent,b=>b.AddContent(0,"Body")).Add(x=>x.DefaultOpenPanelIds,panels.Select(p=>p.Id).ToArray()).Add(x=>x.Spread,row.GetProperty("spread").GetBoolean()).Add(x=>x.PanelContent,panel=>(RenderFragment)(b=>b.AddContent(0,panel.Id))));
        AngleSharp.Dom.IElement Divider()=>cut.FindAll(".hl-app-shell__dock-divider[tabindex='0']")[0];
        void Check(double expected)
        {
            Assert.Equal(row.GetProperty("orientation").GetString(),Divider().GetAttribute("aria-orientation"));
            Assert.Equal(expected,double.Parse(Divider().GetAttribute("aria-valuenow")!,CultureInfo.InvariantCulture),6);
            Assert.Equal(row.GetProperty("min").GetDouble(),double.Parse(Divider().GetAttribute("aria-valuemin")!,CultureInfo.InvariantCulture));
            Assert.Equal(row.GetProperty("max").GetDouble(),double.Parse(Divider().GetAttribute("aria-valuemax")!,CultureInfo.InvariantCulture));
            Assert.Equal(panels.Select(p=>p.Id),cut.FindAll("[data-shell-panel-id]").Select(e=>e.GetAttribute("data-shell-panel-id")));
            Assert.All(cut.FindAll("[data-shell-panel-id]"),e=>Assert.Equal("docked",e.GetAttribute("data-shell-container-kind")));
        }
        Check(row.GetProperty("initial").GetDouble());
        foreach(var step in row.GetProperty("steps").EnumerateArray())
        {
            var action=step.GetProperty("action").GetString();
            if(action=="reset")cut.Find("button[aria-label='Reset panels']").Click();
            else if(action=="key")Divider().KeyDown(new KeyboardEventArgs{Key=step.GetProperty("key").GetString()!,ShiftKey=step.TryGetProperty("shift",out var shift)&&shift.GetBoolean()});
            else {var delta=step.GetProperty("delta").GetDouble();Divider().PointerDown(new PointerEventArgs{Button=0,PointerId=1});Divider().PointerMove(new PointerEventArgs{ClientX=delta,ClientY=delta,PointerId=1});if(action=="cancelDrag")Divider().PointerCancel(new PointerEventArgs{PointerId=1});else Divider().PointerUp(new PointerEventArgs{PointerId=1});}
            Check(step.GetProperty("expected").GetDouble());
            if(action=="drag") {
                var ratio=row.GetProperty("spread").GetBoolean()?double.Parse(cut.Find("[data-split-ratio]").GetAttribute("data-split-ratio")!,CultureInfo.InvariantCulture):double.Parse(cut.Find("[data-shell-panel-id]").GetAttribute("style")!.Split("--hl-app-shell-panel-fraction:")[1],CultureInfo.InvariantCulture);
                Assert.Equal(step.GetProperty("expected").GetDouble(),ratio*Math.Max(row.GetProperty("extent").GetDouble(),row.GetProperty("min").GetDouble()+row.GetProperty("secondMin").GetDouble()),6);
            }
        }
    }
    // Mirror of AppShell.dividers.test.tsx "shell shortcut ... reaches the shell from a focused divider".
    // DockDivider.razor used to carry an unconditional @onkeydown:stopPropagation, which swallowed every
    // shell shortcut while a divider had focus. Both shells read the pass-through rows from the same fixture.
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void ShortcutReachesTheShellFromAFocusedDivider(int index)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../conformance/hlp.ui.app-shell/dividers-v1.json"))));
        var fixture=json.RootElement;Assert.Equal(7,fixture.GetProperty("shellPassThroughKeys").GetArrayLength());var row=fixture.GetProperty("shellPassThroughKeys")[index];
        var panels=JsonSerializer.Deserialize<PackPanelDeclaration[]>(fixture.GetProperty("panels").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        Services.AddSingleton<IMediaQueryObserver>(new Media());JSInterop.Mode=JSRuntimeMode.Loose;
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction",_=>true).SetResult(1);module.Setup<double>("measure",_=>true).SetResult(800d);
        var searched=0;
        var cut=Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"dividers").Add(x=>x.Navigation,new PackNavigationDeclaration([new("ops","Ops")],PanelSet:panels)).Add(x=>x.RoleVocabulary,RoleVocabulary.FromApi([])).Add(x=>x.HeldRoles,new HeldRoleSet([])).Add(x=>x.ChildContent,b=>b.AddContent(0,"Body")).Add(x=>x.DefaultOpenPanelIds,panels.Select(p=>p.Id).ToArray()).Add(x=>x.PanelContent,panel=>(RenderFragment)(b=>b.AddContent(0,panel.Id)))
            .Add(x=>x.ToggleNavigationShortcut,(string?)null).Add(x=>x.CreateShortcut,(string?)null).Add(x=>x.InspectorShortcut,(string?)null)
            .Add(x=>x.OpenSearchShortcut,row.GetProperty("token").GetString()).Add(x=>x.SearchCommand,EventCallback.Factory.Create(this,()=>{searched++;})));
        var arguments=new KeyboardEventArgs{Key=row.GetProperty("key").GetString()!,CtrlKey=row.TryGetProperty("ctrl",out var ctrl)&&ctrl.GetBoolean(),ShiftKey=row.TryGetProperty("shift",out var shift)&&shift.GetBoolean()};
        cut.Find("[data-shell-region='content']").KeyDown(arguments);
        Assert.Equal(1,searched);
        cut.Find(".hl-app-shell__dock-divider[tabindex='0']").KeyDown(arguments);
        Assert.Equal(2,searched);
    }

    // The slice 3 residual: a Mod chord on a focused divider diverged. Blazor fired the shell shortcut AND
    // cancelled the divider's edit; React swallowed the chord entirely. Both now let it pass and neither acts
    // on it. Mirror: AppShell.dividers.test.tsx "a Mod chord on a focused divider neither resizes nor ...".
    [Fact]
    public void AModChordOnAFocusedDividerNeitherResizesNorCommitsNorCancelsTheEdit()
    {
        using var json=JsonDocument.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../../conformance/hlp.ui.app-shell/dividers-v1.json"))));
        var fixture=json.RootElement;var chord=fixture.GetProperty("modChordIgnored");
        var row=fixture.GetProperty("cases").EnumerateArray().Single(entry=>entry.GetProperty("id").GetString()==chord.GetProperty("case").GetString());
        var declared=JsonSerializer.Deserialize<PackPanelDeclaration[]>(fixture.GetProperty("panels").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var panels=row.GetProperty("panels").EnumerateArray().Select(id=>declared.Single(panel=>panel.Id==id.GetString())).ToArray();
        Services.AddSingleton<IMediaQueryObserver>(new Media());JSInterop.Mode=JSRuntimeMode.Loose;
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction",_=>true).SetResult(1);module.Setup<double>("measure",_=>true).SetResult(row.GetProperty("extent").GetDouble());
        var cut=Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"dividers").Add(x=>x.Navigation,new PackNavigationDeclaration([new("ops","Ops")],PanelSet:panels)).Add(x=>x.RoleVocabulary,RoleVocabulary.FromApi([])).Add(x=>x.HeldRoles,new HeldRoleSet([])).Add(x=>x.ChildContent,b=>b.AddContent(0,"Body")).Add(x=>x.DefaultOpenPanelIds,panels.Select(p=>p.Id).ToArray()).Add(x=>x.Spread,row.GetProperty("spread").GetBoolean()).Add(x=>x.PanelContent,panel=>(RenderFragment)(b=>b.AddContent(0,panel.Id))));
        AngleSharp.Dom.IElement Divider()=>cut.FindAll(".hl-app-shell__dock-divider[tabindex='0']")[0];
        double Value()=>double.Parse(Divider().GetAttribute("aria-valuenow")!,CultureInfo.InvariantCulture);
        Divider().KeyDown(new KeyboardEventArgs{Key=chord.GetProperty("setup").GetProperty("key").GetString()!});
        Assert.Equal(chord.GetProperty("setup").GetProperty("expected").GetDouble(),Value(),6);
        foreach(var entry in chord.GetProperty("chords").EnumerateArray())
        {
            Divider().KeyDown(new KeyboardEventArgs{Key=entry.GetProperty("key").GetString()!,CtrlKey=entry.GetProperty("ctrl").GetBoolean()});
            Assert.Equal(chord.GetProperty("expectedAfterChords").GetDouble(),Value(),6);
        }
        Divider().KeyDown(new KeyboardEventArgs{Key=chord.GetProperty("release").GetProperty("key").GetString()!});
        Assert.Equal(chord.GetProperty("release").GetProperty("expected").GetDouble(),Value(),6);
    }

    // Parity with React, which calls event.currentTarget.setPointerCapture on ALL THREE shell separators
    // (AppShell.tsx rail-resize, end-panel-resize and dock-resize). Blazor has no pointer-capture API of its
    // own, so each one routes through dock-divider.js capturePointer; without it a drag that leaves the small
    // handle box stops firing pointermove at the element and the resize silently stops mid-gesture.
    [Fact]
    public void EveryShellSeparatorCapturesThePointerOnPointerDown()
    {
        Services.AddSingleton<IMediaQueryObserver>(new Media());JSInterop.Mode=JSRuntimeMode.Loose;
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction",_=>true).SetResult(1);
        module.Setup<double>("measure",_=>true).SetResult(900d);
        module.Setup<double>("measureInlineSize",_=>true).SetResult(1600d);
        var panels=new[]{new PackPanelDeclaration("documents","panels.documents.toggle","mod+shift+d",420,220,false,"Documents")};
        var cut=Render<HarborlineAppShell>(p=>p.Add(x=>x.ShellId,"capture").Add(x=>x.Navigation,new PackNavigationDeclaration([new("ops","Ops")],PanelSet:panels))
            .Add(x=>x.RoleVocabulary,RoleVocabulary.FromApi([])).Add(x=>x.HeldRoles,new HeldRoleSet([]))
            .Add(x=>x.ChildContent,b=>b.AddContent(0,"Body")).Add(x=>x.ShellInlineSize,1600d).Add(x=>x.RailCapable,true).Add(x=>x.LargeBarCapable,true)
            .Add(x=>x.DefaultOpenPanelIds,new[]{"documents"}).Add(x=>x.PanelContent,panel=>(RenderFragment)(b=>b.AddContent(0,panel.Id)))
            .Add(x=>x.EndPanel,(RenderFragment)(b=>b.AddContent(0,"Pilot"))).Add(x=>x.EndPanelLabel,"Pilot").Add(x=>x.EndPanelOpen,true));
        // One pointer id per separator, so the assertion names WHICH element captured, not just how many did.
        var separators=new (string Selector,long PointerId)[]{(".hl-app-shell__rail-resize",7),(".hl-app-shell__end-panel-resize",8),(".hl-app-shell__dock-resize",9)};
        foreach(var (selector,pointerId) in separators)
        {
            Assert.Single(cut.FindAll(selector));
            cut.Find(selector).PointerDown(new PointerEventArgs{Button=0,PointerId=pointerId,ClientX=10});
        }
        var captures=module.Invocations["capturePointer"].ToArray();
        Assert.Equal(separators.Select(separator=>separator.PointerId),captures.Select(invocation=>Convert.ToInt64(invocation.Arguments[1])));
        Assert.Equal(3,captures.Select(invocation=>invocation.Arguments[0]).Distinct().Count());
    }

    private sealed class Media:IMediaQueryObserver
    { public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> callback,CancellationToken cancellationToken=default)=>ValueTask.FromResult<IMediaQuerySubscription>(new Subscription(query)); }
    private sealed class Subscription(string query):IMediaQuerySubscription
    { public string Query=>query;public bool Matches=>true;public ValueTask DisposeAsync()=>ValueTask.CompletedTask; }
}
