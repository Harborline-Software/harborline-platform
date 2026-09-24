using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class AppLayoutTests : BunitContext
{
    private readonly FakeMedia media=new(false);
    public AppLayoutTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IMediaQueryObserver>(media);}
    private static RenderFragment Content(string text)=>builder=>builder.AddContent(0,text);
    [Fact] public void DrawerViewportRendersOneMainAndOneNavigationSubtree(){var cut=Render<HarborlineAppLayout>(p=>p.Add(x=>x.SideNav,Content("Nav")).Add(x=>x.ChildContent,Content("Body")).Add(x=>x.DefaultMobileNavOpen,true));Assert.Single(cut.FindAll("main#main"));Assert.Single(cut.FindAll("nav"));Assert.Single(cut.FindAll("[role=dialog]"));}
    [Fact] public async Task RailTransitionClosesDrawerOnce(){var values=new List<bool>();var cut=Render<HarborlineAppLayout>(p=>p.Add(x=>x.SideNav,Content("Nav")).Add(x=>x.ChildContent,Content("Body")).Add(x=>x.DefaultMobileNavOpen,true).Add(x=>x.MobileNavOpenChanged,v=>values.Add(v)));await media.SetAsync(true);cut.WaitForAssertion(()=>Assert.Single(cut.FindAll(".hl-app-layout__rail")));Assert.Equal([false],values);Assert.Single(cut.FindAll("nav"));}
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ControlledRailTransitionRequestsOneClosePerCrossing(bool composedRailCapability)
    {
        var values = new List<bool>();
        var cut = Render<HarborlineAppLayout>(p =>
        {
            p.Add(x => x.SideNav, Content("Nav")).Add(x => x.ChildContent, Content("Body"))
                .Add(x => x.MobileNavOpen, true).Add(x => x.MobileNavOpenChanged, value => values.Add(value));
            if (composedRailCapability) p.Add(x => x.RailCapable, false).Add(x => x.MobileNavTriggerId, "shell-navigation");
        });
        Assert.Single(cut.FindAll("[role=dialog]"));
        Assert.Empty(values);

        async Task SetRailAsync(bool capable)
        {
            if (composedRailCapability) cut.Render(p => p.Add(x => x.RailCapable, capable));
            else await media.SetAsync(capable);
        }

        await SetRailAsync(true);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".hl-app-layout__rail")));
        Assert.Equal([false], values);
        cut.Render(p => p.Add(x => x.MobileNavOpen, true));
        Assert.Equal([false], values);

        await SetRailAsync(false);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=dialog]")));
        await SetRailAsync(true);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".hl-app-layout__rail")));
        Assert.Equal([false, false], values);
    }
    [Fact] public void HiddenModeMountsNoNavigation(){var cut=Render<HarborlineAppLayout>(p=>p.Add(x=>x.SideNav,Content("Nav")).Add(x=>x.SideNavMode,SideNavMode.Hidden).Add(x=>x.ChildContent,Content("Body")));Assert.Empty(cut.FindAll("nav"));Assert.Empty(cut.FindAll(".hl-app-layout__nav-trigger"));Assert.Single(cut.FindAll("main"));}
    [Fact,Trait("ModuleConformance","hlp.ui.app-layout")] public void SharedFixtureConforms(){AssertFixturePrefix("app-layout.");Assert.NotNull(Render<HarborlineAppLayout>(p=>p.Add(x=>x.ChildContent,Content("Body"))));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
    private sealed class FakeMedia(bool initial):IMediaQueryObserver{private Func<MediaQueryChange,ValueTask>? callback;private readonly FakeSubscription subscription=new(initial);public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> onChanged,CancellationToken cancellationToken=default){callback=onChanged;subscription.QueryValue=query;return new(subscription);}public async Task SetAsync(bool value){subscription.MatchesValue=value;if(callback is not null)await callback(new(subscription.Query,value));}private sealed class FakeSubscription(bool matches):IMediaQuerySubscription{public string QueryValue="";public bool MatchesValue=matches;public string Query=>QueryValue;public bool Matches=>MatchesValue;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
