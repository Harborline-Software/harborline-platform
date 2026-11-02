using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Browser;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ScrollAffordanceTests
{
    [Fact] public void ToleranceAndLogicalMasksMatch() { var idle=ScrollAffordancePolicy.Resolve(new(0,301,300)); Assert.False(idle.CanScroll); var rtl=ScrollAffordancePolicy.Resolve(new(-200,700,300,true)); Assert.False(rtl.AtStart); Assert.False(rtl.AtEnd); Assert.Contains("to left",rtl.MaskImage); }
    [Fact] public void ItemAndGenericAnnouncementsMatch() { var items=ScrollAffordancePolicy.Resolve(new(100,700,200),new(ItemCount:7)); Assert.Equal("scrollAffordance.showingRange",items.AnnouncementKey); Assert.Equal(2,items.AnnouncementArguments["start"]); Assert.Equal(3,items.AnnouncementArguments["end"]); Assert.Equal("scrollAffordance.endReached",ScrollAffordancePolicy.Resolve(new(400,700,300)).AnnouncementKey); }
    // T-1106 parity: React's percent is Math.round, which rounds .5 up. .NET's Math.Round defaults to
    // banker's rounding (.5 to even), so 12.5 and 62.5 split the lanes unless the midpoint rounds away from zero.
    [Theory]
    [InlineData(50, 700, 300, false, 13)]
    [InlineData(250, 700, 300, false, 63)]
    [InlineData(-50, 700, 300, true, 13)]
    [InlineData(200, 700, 300, false, 50)]
    public void PercentScrolledRoundsHalfUpLikeReact(double rawPosition, double scrollSize, double clientSize, bool rtl, int expected) { var state=ScrollAffordancePolicy.Resolve(new(rawPosition,scrollSize,clientSize,rtl)); Assert.Equal("scrollAffordance.percentScrolled",state.AnnouncementKey); Assert.Equal(expected,state.AnnouncementArguments["percent"]); }
    [Fact] public void KeyboardTargetsClampAndRespectRtl() { var m=new ScrollAffordanceMeasurement(200,500,100); Assert.Equal(280,ScrollAffordancePolicy.ResolveKey("ArrowRight",m).LogicalTarget); Assert.Equal(400,ScrollAffordancePolicy.ResolveKey("End",m).LogicalTarget); Assert.False(ScrollAffordancePolicy.ResolveKey("PageDown",m).Handled); var rtl=ScrollAffordancePolicy.ResolveKey("ArrowLeft",new(-200,500,100,true)); Assert.Equal(280,rtl.LogicalTarget); Assert.Equal(-280,rtl.RawTarget); }
    [Theory, Trait("ModuleConformance","hlp.ui.use-scroll-affordance")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.use-scroll-affordance", MemberType = typeof(SharedFixtureBatch))]
    public Task SharedFixtureConforms(string caseId, string? rawFixture)
    {
        return SharedFixtureBatch.RunAsync(caseId, rawFixture, async () => {
            var raw=SharedFixtureBatch.Current; if(string.IsNullOrWhiteSpace(raw))return; using var fixture=System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("scroll-affordance.",fixture.RootElement.GetProperty("id").GetString()); await using var observer=new ScrollAffordanceObserver(new FakeRuntime(new FakeModule())); await using var registration=await observer.ObserveAsync(default,new(),_=>ValueTask.CompletedTask); Assert.NotNull(registration);
        });
    }
    // T-1135 (interface revision 2). Oracle: WAI-ARIA 1.2 aria-live and the APG live-region practice, as ruled
    // on the ticket; the expected keys are literals. The observer's announcement stays empty on load and on
    // resize and follows a scroll (the sizes hold while the position moves).
    [Fact]
    public async Task ObserverAnnouncesOnlyAfterAScroll()
    {
        var module = new FakeModule();
        var states = new List<ScrollAffordanceState>();
        await using var observer = new ScrollAffordanceObserver(new FakeRuntime(module));
        await using var registration = await observer.ObserveAsync(default, new(), state => { states.Add(state); return ValueTask.CompletedTask; });
        async Task<ScrollAffordanceState> Measure(double raw, double scrollSize, double clientSize)
        {
            await module.MeasureAsync(new(raw, scrollSize, clientSize));
            return states[^1];
        }
        var load = await Measure(0, 700, 300);
        Assert.True(load.CanScroll);
        Assert.Null(load.AnnouncementKey);
        Assert.Null((await Measure(0, 700, 300)).AnnouncementKey); // the initial scroll event, same position
        Assert.Null((await Measure(0, 700, 350)).AnnouncementKey); // a resize
        var scrolled = await Measure(175, 700, 350);
        Assert.Equal("scrollAffordance.percentScrolled", scrolled.AnnouncementKey);
        Assert.Equal(50, scrolled.AnnouncementArguments["percent"]);
        Assert.Equal("scrollAffordance.endReached", (await Measure(350, 700, 350)).AnnouncementKey); // keyboard End's scrollTo
        Assert.Equal("scrollAffordance.endReached", (await Measure(400, 700, 300)).AnnouncementKey); // resize keeps the message
        Assert.Null((await Measure(0, 700, 700)).AnnouncementKey); // no overflow, no message
    }

    private sealed class FakeRuntime(IJSObjectReference module):IJSRuntime { public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>new((T)module); public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)=>new((T)module); }
    private sealed class FakeModule:IJSObjectReference
    {
        private object? reference;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>InvokeAsync<T>(identifier,default,args);
        public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args){ if(identifier=="observe") reference=args?[1]; object value=identifier=="observe"?3L:default(T)!;return new((T)value);}
        // Plays the browser's debounced measurement back through the registration's [JSInvokable] OnMeasured.
        public Task MeasureAsync(ScrollAffordanceMeasurement measurement)
        {
            var target = reference!.GetType().GetProperty("Value")!.GetValue(reference)!;
            return (Task)target.GetType().GetMethod("OnMeasured")!.Invoke(target, [measurement])!;
        }
    }
}
