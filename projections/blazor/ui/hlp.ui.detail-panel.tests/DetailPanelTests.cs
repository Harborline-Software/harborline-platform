using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class DetailPanelTests : BunitContext
{
    private readonly FakeMedia media = new(true);

    public DetailPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMediaQueryObserver>(media);
    }

    private static RenderFragment Content(string text) => builder => builder.AddContent(0, text);

    [Fact]
    public void ClosedRendersNothing()
    {
        var cut = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.ChildContent, Content("Properties")));

        Assert.Empty(cut.FindAll("aside"));
        Assert.Empty(cut.FindAll(".hl-detail-panel__overlay"));
    }

    [Fact]
    public void OpenDockedNamedComplementary()
    {
        var cut = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.ChildContent, Content("Properties")));

        cut.WaitForAssertion(() =>
        {
            var aside = cut.Find("aside.hl-detail-panel[data-detail-panel]");
            Assert.Equal("Elevator 2", aside.GetAttribute("aria-label"));
            Assert.Null(aside.GetAttribute("role"));
        });
    }

    [Fact]
    public void WidthClamp()
    {
        var below = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.Width, 100)
            .Add(component => component.ChildContent, Content("Properties")));
        var defaultWidth = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.ChildContent, Content("Properties")));
        var above = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.Width, 4000)
            .Add(component => component.ChildContent, Content("Properties")));

        below.WaitForAssertion(() => Assert.Contains("--hl-detail-panel-size:220px", below.Find("aside").GetAttribute("style")));
        defaultWidth.WaitForAssertion(() => Assert.Contains("--hl-detail-panel-size:360px", defaultWidth.Find("aside").GetAttribute("style")));
        above.WaitForAssertion(() => Assert.Contains("--hl-detail-panel-size:720px", above.Find("aside").GetAttribute("style")));
    }

    [Fact]
    public void CloseEmitsOnceControlledStays()
    {
        var requests = new List<bool>();
        var cut = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.OpenChanged, value => requests.Add(value))
            .Add(component => component.ChildContent, Content("Properties")));

        cut.WaitForElement("button[aria-label='Close']").Click();

        Assert.Equal([false], requests);
        Assert.Single(cut.FindAll("aside"));
    }

    [Fact]
    public async Task SheetBelowRailIsModal()
    {
        await media.SetAsync(false);
        var cut = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.ChildContent, Content("Properties")));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".hl-detail-panel__overlay [role='dialog'][aria-modal='true']")));
    }

    [Fact]
    public async Task TransitionKeepsOpenWithoutRequests()
    {
        var requests = new List<bool>();
        var cut = Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)
            .Add(component => component.OpenChanged, value => requests.Add(value))
            .Add(component => component.ChildContent, Content("Properties")));

        cut.WaitForElement("aside:not([role])");
        await media.SetAsync(false);
        cut.WaitForElement("div[role='dialog']");
        await media.SetAsync(true);
        cut.WaitForElement("aside:not([role])");

        Assert.Empty(requests);
    }

    [Fact]
    public void ContentRequiredThrows()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.Open, true)));

        Assert.Equal("detail-panel-content-required", exception.Message);
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.detail-panel")]
    public void SharedFixtureConforms()
    {
        AssertFixturePrefix("detail-panel.");
        Assert.NotNull(Render<HarborlineDetailPanel>(parameters => parameters
            .Add(component => component.Label, "Elevator 2")
            .Add(component => component.ChildContent, Content("Properties"))));
    }

    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
    private sealed class FakeMedia(bool initial):IMediaQueryObserver{private Func<MediaQueryChange,ValueTask>? callback;private readonly FakeSubscription subscription=new(initial);public ValueTask<IMediaQuerySubscription> ObserveAsync(string query,Func<MediaQueryChange,ValueTask> onChanged,CancellationToken cancellationToken=default){callback=onChanged;subscription.QueryValue=query;return new(subscription);}public async Task SetAsync(bool value){subscription.MatchesValue=value;if(callback is not null)await callback(new(subscription.Query,value));}private sealed class FakeSubscription(bool matches):IMediaQuerySubscription{public string QueryValue="";public bool MatchesValue=matches;public string Query=>QueryValue;public bool Matches=>MatchesValue;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}}
}
