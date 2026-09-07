using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Harborline.Contracts.Authorization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;
public sealed class AppShellPerformanceTests:BunitContext
{
    public AppShellPerformanceTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IMediaQueryObserver>(new FakeMedia(true));}
    private static RenderFragment Content(string value)=>b=>b.AddContent(0,value);
    [Fact] public void AppShellRerendersMaximumFixture(){var items=Enumerable.Range(0,16).SelectMany(g=>Enumerable.Range(0,16).Select(i=>new ShellNavItem($"i{g}-{i}",$"Item {g}-{i}",Threads:i==0?[new($"t{g}","Thread")]:null))).ToDictionary(i=>i.Id);var groups=Enumerable.Range(0,16).Select(g=>new PackNavigationGroup($"g{g}",$"Group {g}",Enumerable.Range(0,16).Select(i=>$"i{g}-{i}").ToArray())).ToArray();var nav=new PackNavigationDeclaration([new("operations","Operations",Groups:groups)]);var state=new ShellNavigationState(Items:items);void Add(ComponentParameterCollectionBuilder<HarborlineAppShell> p,string body){p.Add(x=>x.ShellId,"perf").Add(x=>x.Navigation,nav).Add(x=>x.NavigationState,state).Add(x=>x.RoleVocabulary,RoleVocabulary.FromApi([])).Add(x=>x.HeldRoles,new HeldRoleSet([])).Add(x=>x.GroupCap,16).Add(x=>x.Collapsed,false).Add(x=>x.ChildContent,Content(body));}var cut=Render<HarborlineAppShell>(p=>Add(p,"Body 0"));for(var i=1;i<=96;i++)cut.Render(p=>Add(p,$"Body {i}"));Assert.Single(cut.FindAll("nav"));Assert.Equal(256,cut.FindAll("[data-shell-zone=groups] .hl-app-shell__rail-link").Count);Assert.Contains("Body 96",cut.Find("main").TextContent);Assert.DoesNotContain("Body 95",cut.Markup);}
    private sealed class FakeMedia(bool initial):IMediaQueryObserver{public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> onChanged,CancellationToken cancellationToken=default)=>new(new FakeSubscription(query,initial));private sealed class FakeSubscription(string query,bool matches):IMediaQuerySubscription{public string Query=>query;public bool Matches=>matches;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
