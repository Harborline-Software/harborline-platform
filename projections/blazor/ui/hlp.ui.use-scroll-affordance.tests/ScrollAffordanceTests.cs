using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Harborline.UIAdapters.Blazor.Browser;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ScrollAffordanceTests
{
    [Fact] public void ToleranceAndLogicalMasksMatch() { var idle=ScrollAffordancePolicy.Resolve(new(0,301,300)); Assert.False(idle.CanScroll); var rtl=ScrollAffordancePolicy.Resolve(new(-200,700,300,true)); Assert.False(rtl.AtStart); Assert.False(rtl.AtEnd); Assert.Contains("to left",rtl.MaskImage); }
    [Fact] public void ItemAndGenericAnnouncementsMatch() { var items=ScrollAffordancePolicy.Resolve(new(100,700,200),new(ItemCount:7)); Assert.Equal("scrollAffordance.showingRange",items.AnnouncementKey); Assert.Equal(2,items.AnnouncementArguments["start"]); Assert.Equal(3,items.AnnouncementArguments["end"]); Assert.Equal("scrollAffordance.endReached",ScrollAffordancePolicy.Resolve(new(400,700,300)).AnnouncementKey); }
    [Fact] public void KeyboardTargetsClampAndRespectRtl() { var m=new ScrollAffordanceMeasurement(200,500,100); Assert.Equal(280,ScrollAffordancePolicy.ResolveKey("ArrowRight",m).LogicalTarget); Assert.Equal(400,ScrollAffordancePolicy.ResolveKey("End",m).LogicalTarget); Assert.False(ScrollAffordancePolicy.ResolveKey("PageDown",m).Handled); var rtl=ScrollAffordancePolicy.ResolveKey("ArrowLeft",new(-200,500,100,true)); Assert.Equal(280,rtl.LogicalTarget); Assert.Equal(-280,rtl.RawTarget); }
    [Fact, Trait("ModuleConformance","hlp.ui.use-scroll-affordance")] public async Task SharedFixtureConforms() { var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE"); if(string.IsNullOrWhiteSpace(raw))return; using var fixture=System.Text.Json.JsonDocument.Parse(raw); Assert.StartsWith("scroll-affordance.",fixture.RootElement.GetProperty("id").GetString()); await using var observer=new ScrollAffordanceObserver(new FakeRuntime(new FakeModule())); await using var registration=await observer.ObserveAsync(default,new(),_=>ValueTask.CompletedTask); Assert.NotNull(registration); }
    private sealed class FakeRuntime(IJSObjectReference module):IJSRuntime { public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>new((T)module); public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)=>new((T)module); }
    private sealed class FakeModule:IJSObjectReference { public ValueTask DisposeAsync()=>ValueTask.CompletedTask; public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>InvokeAsync<T>(identifier,default,args); public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args){object value=identifier=="observe"?3L:default(T)!;return new((T)value);} }
}
