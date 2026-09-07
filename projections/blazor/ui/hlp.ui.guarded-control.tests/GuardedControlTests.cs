using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class GuardedControlTests : BunitContext
{
    public GuardedControlTests()=>JSInterop.Mode=JSRuntimeMode.Loose;
    private IRenderedComponent<HarborlineGuardedControl> RenderGuard(Action? commit=null,Action<GuardedControlEvent>? observe=null)=>Render<HarborlineGuardedControl>(p=>p.Add(x=>x.Action,"packages.export").Add(x=>x.ClassificationId,"new-record-type").Add(x=>x.Label,"Sign and save").Add(x=>x.OnCommit,()=>commit?.Invoke()).Add(x=>x.OnEvent,e=>observe?.Invoke(e)));
    [Fact] public void RequiresTwoDistinctActivations(){var commits=0;var cut=RenderGuard(()=>commits++);cut.Find("button").Click();Assert.Equal(GuardedControlState.Armed,cut.Instance.State);Assert.Equal(0,commits);cut.Find("button").Click();Assert.Equal(1,commits);Assert.Equal(GuardedControlState.Covered,cut.Instance.State);}
    [Fact] public async Task EscapeRecoversOnce(){var events=new List<GuardedControlEvent>();var cut=RenderGuard(observe:events.Add);cut.Find("button").Click();await cut.Find("button").KeyDownAsync(new KeyboardEventArgs{Key="Escape"});Assert.Equal(GuardedControlState.Covered,cut.Instance.State);Assert.Single(events.OfType<GuardedControlRecovered>());}
    [Fact] public void DisabledReasonIsAssociated(){var cut=Render<HarborlineGuardedControl>(p=>p.Add(x=>x.Action,"x").Add(x=>x.ClassificationId,"c").Add(x=>x.Label,"Delete").Add(x=>x.Disabled,true).Add(x=>x.DescribedBy,"denial").Add(x=>x.OnCommit,()=>{}));var button=cut.Find("button");Assert.True(button.HasAttribute("disabled"));Assert.Equal("denial",button.GetAttribute("aria-describedby"));}
    [Fact,Trait("ModuleConformance","hlp.ui.guarded-control")] public void SharedFixtureConforms(){AssertFixturePrefix("guarded-control.");Assert.NotNull(RenderGuard());}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
