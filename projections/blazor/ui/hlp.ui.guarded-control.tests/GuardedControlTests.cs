using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
    // The armed instruction already says "within", so its duration is a plain localized unit; a
    // relative "in 5 seconds" made it read "within in 5 seconds".
    [Fact] public void ArmedInstructionStatesTheWindowAsAPlainDuration(){var cut=RenderGuard();cut.Find("button").Click();Assert.Equal("Armed for \u2068Sign and save\u2069. Activate again within 5 seconds to commit.",cut.Find("[role=status]").TextContent);Assert.Equal("5 seconds",cut.Find("[role=timer]").TextContent);}
    [Theory,Trait("ModuleConformance","hlp.ui.guarded-control")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.guarded-control", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("guarded-control.");Assert.NotNull(RenderGuard());
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;
            using var fixture=System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!,fixture.RootElement.GetProperty("input"),fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production component against the
    // fixture's own input and expected values. The ids in Unasserted need a real browser (native
    // keyboard activation, layout, motion) or name no observable bUnit outcome; a new or renamed
    // case id fails until it is added to one list or the other.
    private static readonly string[] Unasserted=["guarded-control.keyboard-two-step","guarded-control.localization","guarded-control.rtl-motion","guarded-control.authorization-boundary","guarded-control.projection-equivalence"];
    private sealed class Harness
    {
        public required IRenderedComponent<HarborlineGuardedControl> Cut;
        public required BunitJSModuleInterop Module;
        public List<GuardedControlEvent> Events=[];
        public int Commits;
        public TaskCompletionSource? Pending;
        public AngleSharp.Dom.IElement Button=>Cut.Find("button");
        public IEnumerable<GuardedControlRecovered> Recoveries=>Events.OfType<GuardedControlRecovered>();
        public IEnumerable<GuardedControlCommitSettled> Settlements=>Events.OfType<GuardedControlCommitSettled>();
        public string? LastFocus=>Module.Invocations["focus"].Count==0?null:((ElementReference)Module.Invocations["focus"][^1].Arguments[0]!).Id;
        public int FocusCount=>Module.Invocations["focus"].Count;
        // bUnit does not reliably render element-reference ids into markup, so focus targets are
        // compared by the focus calls themselves: the first call after arming targets the armed
        // action, and a later call to a different element while covered targets the covered action.
        public string? ArmedFocus=>Module.Invocations["focus"].Count==0?null:((ElementReference)Module.Invocations["focus"][0].Arguments[0]!).Id;
        public bool FocusedCovered=>Cut.Instance.State==GuardedControlState.Covered&&FocusCount>1&&LastFocus!=ArmedFocus;
        public void Host(string reason)=>Cut.InvokeAsync(()=>Cut.Instance.RecoverFromHostAsync(reason)).GetAwaiter().GetResult();
    }
    private Harness Guard(bool pending=false,Func<Task>? commit=null,string label="Sign and save",string action="packages.export",string classification="new-record-type",Action<ComponentParameterCollectionBuilder<HarborlineGuardedControl>>? more=null)
    {
        var module=JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/guarded-control.js");module.Mode=JSRuntimeMode.Loose;
        Harness? h=null;
        var cut=Render<HarborlineGuardedControl>(p=>{p.Add(x=>x.Action,action).Add(x=>x.ClassificationId,classification).Add(x=>x.Label,label)
            .Add(x=>x.OnCommit,async()=>{h!.Commits++;if(commit is not null)await commit();else if(h.Pending is not null)await h.Pending.Task;})
            .Add(x=>x.OnEvent,e=>h!.Events.Add(e));more?.Invoke(p);});
        h=new Harness{Cut=cut,Module=module,Pending=pending?new TaskCompletionSource():null};
        return h;
    }
    private static string[] States(Harness h)=>h.Events.OfType<GuardedControlTransition>().Select(x=>x.Previous).Take(1).Concat(h.Events.OfType<GuardedControlTransition>().Select(x=>x.Next)).Select(x=>x.ToString().ToLowerInvariant()).ToArray();
    private void AssertCase(string id,System.Text.Json.JsonElement input,System.Text.Json.JsonElement expected)
    {
        static string[] Strings(System.Text.Json.JsonElement element)=>element.EnumerateArray().Select(value=>value.GetString()!).ToArray();
        static string Name(GuardedControlState state)=>state.ToString().ToLowerInvariant();
        switch(id)
        {
            case "guarded-control.initial-covered":
            {
                var h=Guard(label:input.GetProperty("label").GetString()!);
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal(expected.GetProperty("armedActionPresent").GetBoolean(),h.Cut.FindAll("[data-guard-state=armed]").Count>0);
                break;
            }
            case "guarded-control.first-activation-arms":
            {
                var h=Guard();h.Button.Click();
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal($"{expected.GetProperty("deadlineMilliseconds").GetInt32()/1000} seconds",h.Cut.Find("[role=timer]").TextContent);
                break;
            }
            case "guarded-control.pointer-two-step":
            {
                var h=Guard();foreach(var _ in Strings(input.GetProperty("activations")))h.Button.Click();
                Assert.Equal(Strings(expected.GetProperty("states")),States(h));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                break;
            }
            case "guarded-control.commit-once":
            {
                var h=Guard(pending:true);h.Button.Click();
                for(var i=0;i<input.GetProperty("simultaneousActivations").GetInt32();i++)h.Cut.InvokeAsync(()=>h.Cut.Find("button").Click());
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal(GuardedControlState.Committing,h.Cut.Instance.State);
                h.Cut.InvokeAsync(()=>h.Pending!.SetResult());
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                break;
            }
            case "guarded-control.committing-inert":
            {
                var h=Guard(pending:true);h.Button.Click();h.Button.Click();
                foreach(var e in Strings(input.GetProperty("events")))
                {
                    if(e=="pointer")h.Cut.InvokeAsync(()=>h.Cut.Find("button").Click());
                    else if(e=="Enter")h.Button.KeyDown(new KeyboardEventArgs{Key="Enter"});
                    else if(e=="window-blur")h.Host("window-blur");
                    // "timeout": the attempt's deadline was cancelled when the latch was acquired; covered by the timeout case.
                }
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(1+expected.GetProperty("additionalCommitCount").GetInt32(),h.Commits);
                Assert.Equal(expected.GetProperty("recoveryCount").GetInt32(),h.Recoveries.Count());
                break;
            }
            case "guarded-control.commit-completed":
            {
                var h=Guard();h.Button.Click();h.Button.Click();
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("settlementCount").GetInt32(),h.Settlements.Count());
                Assert.Equal(GuardedControlCommitOutcome.Completed,h.Settlements.Single().Outcome);
                Assert.Equal("covered-action",expected.GetProperty("focus").GetString());
                Assert.True(h.FocusedCovered);
                break;
            }
            case "guarded-control.commit-rejected":
            {
                var h=Guard(commit:()=>throw new InvalidOperationException("SECRET-RAW-ERROR"));h.Button.Click();
                GuardedControlState? stateAtRouting=null;
                var error=Assert.ThrowsAny<Exception>(()=>{try{h.Button.Click();}finally{stateAtRouting=h.Cut.Instance.State;}});
                Assert.Contains("SECRET-RAW-ERROR",error.ToString());
                Assert.Equal(expected.GetProperty("stateBeforeExceptionRouting").GetString(),Name(stateAtRouting!.Value));
                Assert.Equal(expected.GetProperty("settlementCount").GetInt32(),h.Settlements.Count());
                Assert.Equal(GuardedControlCommitOutcome.Rejected,h.Settlements.Single().Outcome);
                Assert.Equal(expected.GetProperty("rawErrorRendered").GetBoolean(),h.Cut.Markup.Contains("SECRET-RAW-ERROR",StringComparison.Ordinal));
                Assert.Equal("covered-action",expected.GetProperty("focus").GetString());
                Assert.True(h.FocusedCovered);
                break;
            }
            case "guarded-control.timeout-recovery":
            {
                var h=Guard();h.Button.Click();
                // The recovery is reported after the component's last render (contract ordering), and
                // WaitForAssertion only re-checks on renders, so poll for the event itself.
                Assert.True(SpinWait.SpinUntil(()=>h.Recoveries.Any(),TimeSpan.FromMilliseconds(input.GetProperty("elapsedMilliseconds").GetInt32()+3000)));
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal(expected.GetProperty("recoveryCount").GetInt32(),h.Recoveries.Count());
                Assert.Equal(GuardedControlRecoveryReason.Timeout,h.Recoveries.Single().Reason);
                break;
            }
            case "guarded-control.escape-recovery":
            {
                var h=Guard();h.Button.Click();h.Button.KeyDown(new KeyboardEventArgs{Key=input.GetProperty("key").GetString()!});
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal(GuardedControlRecoveryReason.Escape,h.Recoveries.Single().Reason);
                Assert.Equal("covered-action",expected.GetProperty("focus").GetString());
                Assert.True(h.FocusedCovered);
                break;
            }
            case "guarded-control.host-recovery":
            {
                foreach(var e in Strings(input.GetProperty("events")))
                {
                    var h=Guard();h.Button.Click();var focusBefore=h.FocusCount;
                    var reason=e switch{"window-blur-after-grace"=>"window-blur",_=>e};
                    if(reason=="navigation")h.Cut.InvokeAsync(()=>Services.GetRequiredService<NavigationManager>().NavigateTo("/elsewhere"));else h.Host(reason);
                    Assert.Equal(expected.GetProperty("eachRecovers").GetBoolean(),h.Cut.Instance.State==GuardedControlState.Covered&&h.Recoveries.Count()==1);
                    Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                    if(reason!="navigation")
                    {
                        Assert.Equal(expected.GetProperty("focusDeferredWhenUnsafe").GetBoolean(),h.FocusCount==focusBefore);
                        h.Cut.InvokeAsync(()=>h.Cut.Instance.RestoreDeferredFocusAsync()).GetAwaiter().GetResult();
                        Assert.True(h.FocusedCovered);
                    }
                }
                break;
            }
            case "guarded-control.recovery-race":
            {
                var h=Guard();h.Button.Click();
                h.Cut.InvokeAsync(async()=>{await Task.WhenAll(h.Cut.Instance.RecoverFromHostAsync("document-hidden"),h.Cut.Instance.RecoverFromHostAsync("window-blur"));}).GetAwaiter().GetResult();
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("recoveryCount").GetInt32(),h.Recoveries.Count());
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                break;
            }
            case "guarded-control.disabled":
            {
                var ids=string.Join(' ',Strings(input.GetProperty("describedBy")));
                var h=Guard(more:p=>p.Add(x=>x.Disabled,input.GetProperty("disabled").GetBoolean()).Add(x=>x.DescribedBy,ids));
                h.Button.Click();
                Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                Assert.Equal(expected.GetProperty("nativeDisabled").GetBoolean(),h.Button.HasAttribute("disabled"));
                Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                Assert.Equal(expected.GetProperty("descriptionAssociated").GetBoolean(),h.Button.GetAttribute("aria-describedby")==ids);
                break;
            }
            case "guarded-control.became-disabled":
            {
                var h=Guard();h.Button.Click();
                h.Cut.Render(p=>p.Add(x=>x.Disabled,input.GetProperty("disabled").GetBoolean()));
                h.Cut.WaitForAssertion(()=>Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State)));
                Assert.Equal(expected.GetProperty("recoveryCount").GetInt32(),h.Recoveries.Count());
                Assert.Equal(GuardedControlRecoveryReason.BecameDisabled,h.Recoveries.Single().Reason);
                break;
            }
            case "guarded-control.focus":
            {
                var h=Guard();h.Button.Click();
                Assert.Equal(expected.GetProperty("focusMovesToArmed").GetBoolean(),h.FocusCount==1&&h.Cut.Instance.State==GuardedControlState.Armed);
                h.Button.KeyDown(new KeyboardEventArgs{Key="Escape"});
                Assert.Equal(expected.GetProperty("focusReturnsAfterEverySettlement").GetBoolean(),h.FocusedCovered);
                var completed=Guard();completed.Button.Click();completed.Button.Click();
                Assert.Equal(expected.GetProperty("focusReturnsAfterEverySettlement").GetBoolean(),completed.FocusedCovered);
                break;
            }
            case "guarded-control.disposal":
            {
                var h=Guard();h.Button.Click();var focusBefore=h.FocusCount;var eventsBefore=h.Events.Count;
                var instance=h.Cut.Instance;DisposeComponentsAsync().GetAwaiter().GetResult();
                foreach(var e in Strings(input.GetProperty("laterEvents")))instance.RecoverFromHostAsync(e).GetAwaiter().GetResult();
                Assert.Equal(expected.GetProperty("staleEventCount").GetInt32(),h.Events.Count-eventsBefore);
                Assert.Equal(expected.GetProperty("focusAttempted").GetBoolean(),h.FocusCount>focusBefore);
                break;
            }
            case "guarded-control.invalid-input":
            {
                foreach(var variant in Strings(input.GetProperty("variants")))
                {
                    var h=variant switch{"blank-label"=>Guard(label:" "),"blank-action"=>Guard(action:" "),_=>Guard(classification:" ")};
                    h.Button.Click();h.Cut.InvokeAsync(()=>h.Cut.Find("button").Click());
                    Assert.Equal(expected.GetProperty("inert").GetBoolean(),h.Button.HasAttribute("disabled"));
                    Assert.Equal(expected.GetProperty("state").GetString(),Name(h.Cut.Instance.State));
                    Assert.Equal(expected.GetProperty("commitCount").GetInt32(),h.Commits);
                }
                break;
            }
            case "guarded-control.host-attributes":
            {
                var attributes=input.GetProperty("attributes").EnumerateObject().ToDictionary(x=>x.Name,x=>(object)x.Value.GetString()!);
                var h=Guard(more:p=>p.AddUnmatched(attributes.First().Key,attributes.First().Value));
                Assert.Equal(expected.GetProperty("preserved").GetBoolean(),h.Cut.Find(".hl-guarded-control").GetAttribute("data-testid")==(string)attributes["data-testid"]);
                Assert.Equal("covered",h.Button.GetAttribute("data-guard-state"));
                Assert.False(string.IsNullOrWhiteSpace(h.Button.GetAttribute("aria-label")));
                break;
            }
            default:
                Assert.Contains(id,Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
