using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppLayoutPerformanceTests : BunitContext
{
    public AppLayoutPerformanceTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IMediaQueryObserver>(new DrawerMedia());}
    private static RenderFragment Nav(int count)=>builder=>{for(var i=0;i<count;i++){builder.OpenElement(i*2,"a");builder.AddAttribute(i*2+1,"href",$"/{i}");builder.AddContent(i*2+2,$"Item {i}");builder.CloseElement();}};
    private static RenderFragment Body(int value)=>builder=>builder.AddContent(0,$"Body {value}");
    [Fact] public void LargeNavigationAndRepeatedUpdatesRemainStructurallyBounded(){var cut=Render<HarborlineAppLayout>(p=>p.Add(x=>x.SideNav,Nav(256)).Add(x=>x.ChildContent,Body(0)).Add(x=>x.DefaultMobileNavOpen,true));for(var i=1;i<=96;i++)cut.Render(p=>p.Add(x=>x.SideNav,Nav(256)).Add(x=>x.ChildContent,Body(i)).Add(x=>x.DefaultMobileNavOpen,true));Assert.Single(cut.FindAll("nav"));Assert.Equal(256,cut.FindAll("nav a").Count);Assert.Single(cut.FindAll("main"));Assert.Contains("Body 96",cut.Find("main").TextContent);Assert.DoesNotContain("Body 95",cut.Find("main").TextContent);}
    private sealed class DrawerMedia:IMediaQueryObserver{public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> onChanged,CancellationToken cancellationToken=default)=>new(new Subscription(query));private sealed class Subscription(string query):IMediaQuerySubscription{public string Query=>query;public bool Matches=>false;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
