using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class ToasterTests : BunitContext
{
    private readonly ToastService service=new();
    public ToasterTests()=>Services.AddSingleton<IToastService>(service);
    [Fact] public void QueueShowsNewestAndUsesRoles(){var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.MaximumVisible,2));service.Show("A");service.Success("B");service.Error("C");cut.WaitForAssertion(()=>Assert.Equal(2,cut.FindAll(".hl-toaster__toast").Count));Assert.DoesNotContain("A",cut.Markup);Assert.Equal("alert",cut.Find("[data-variant=error]").GetAttribute("role"));Assert.False(cut.Find("[role=alert]").HasAttribute("aria-live"));Assert.Equal(2,cut.FindAll(".hl-toaster__marker svg").Count);Assert.DoesNotContain("✓",cut.Find(".hl-toaster__marker").TextContent);Assert.NotNull(cut.Find(".hl-toaster__close svg"));Assert.DoesNotContain("×",cut.Find(".hl-toaster__close").TextContent);}
    [Fact] public void ScopedServicesDoNotShareEntries(){var other=new ToastService();service.Show("Mine");Assert.Single(service.Entries);Assert.Empty(other.Entries);other.Dispose();}
    [Fact] public async Task TrackAsyncRetainsIdentity(){Render<HarborlineToaster>();var result=await service.TrackAsync(Task.FromResult(3),"Loading",value=>$"Done {value}",error=>error.Message);Assert.Equal(3,result);Assert.Single(service.Entries);Assert.Equal(ToastVariant.Success,service.Entries[0].Variant);}
    // Contract: TrackAsync returns or propagates the original operation result. A service disposed
    // while the operation runs (a closed circuit) must not be repopulated or restart timers.
    [Fact] public async Task TrackAsyncOutlivingDisposalKeepsTheOriginalOutcomeAndNoState()
    {
        var disposed=new ToastService();var changes=0;var source=new TaskCompletionSource<int>();
        var tracked=disposed.TrackAsync(source.Task,"Saving",value=>"Saved",error=>"Failed");
        Assert.Single(disposed.Entries);
        disposed.Dispose();disposed.Changed+=()=>changes++;
        source.SetResult(7);
        Assert.Equal(7,await tracked);
        Assert.Empty(disposed.Entries);
        Assert.Equal(0,changes);

        var failing=new ToastService();var failure=new TaskCompletionSource<int>();var original=new InvalidOperationException("network down");
        var rejected=failing.TrackAsync(failure.Task,"Saving",value=>"Saved",error=>"Failed");
        failing.Dispose();
        failure.SetException(original);
        Assert.Same(original,await Assert.ThrowsAsync<InvalidOperationException>(()=>rejected));
        Assert.Empty(failing.Entries);
    }
    // Contract: action activation invokes exactly once and does not dismiss (also a fixture case,
    // which runs only under tooling/run-ui-module-shared.mjs).
    [Fact] public void ActionInvokesOnceHoweverOftenPressed(){var invocations=0;var cut=Render<HarborlineToaster>();service.Show("Saved",new ToastOptions(DurationMilliseconds:60_000,Action:new ToastAction("Undo",()=>{invocations++;return Task.CompletedTask;})));cut.WaitForElement(".hl-toaster__action").Click();cut.Find(".hl-toaster__action").Click();Assert.Equal(1,invocations);Assert.Single(service.Entries);}
    [Theory,Trait("ModuleConformance","hlp.ui.toaster")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.toaster", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("toaster.");Assert.NotNull(Render<HarborlineToaster>());
            var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;
            using var fixture=System.Text.Json.JsonDocument.Parse(raw);
            AssertCase(fixture.RootElement.GetProperty("id").GetString()!,fixture.RootElement.GetProperty("input"),fixture.RootElement.GetProperty("expected"));
        });
    }
    // Targeted neutral-case semantics: each case runs the production service and host against the
    // fixture's own input and expected values. The ids in Unasserted concern motion and layout a
    // browser must observe, or cross-projection comparison; an unknown case id fails.
    private static readonly string[] Unasserted=["toaster.rtl","toaster.projection-equivalence"];
    private static readonly Dictionary<string,ToastVariant> Variants=new(){["default"]=ToastVariant.Default,["success"]=ToastVariant.Success,["error"]=ToastVariant.Error,["warning"]=ToastVariant.Warning,["information"]=ToastVariant.Information,["loading"]=ToastVariant.Loading};
    private static ToastId Show(IToastService toasts,string variant,string message,ToastOptions? options=null)=>variant switch{"success"=>toasts.Success(message,options),"error"=>toasts.Error(message,options),"warning"=>toasts.Warning(message,options),"information"=>toasts.Information(message,options),"loading"=>toasts.Loading(message,options),_=>toasts.Show(message,options)};
    private void AssertCase(string id,System.Text.Json.JsonElement input,System.Text.Json.JsonElement expected)
    {
        static string[] Strings(System.Text.Json.JsonElement element)=>element.EnumerateArray().Select(value=>value.GetString()!).ToArray();
        service.Clear();
        switch(id)
        {
            case "toaster.empty":
            {
                var cut=Render<HarborlineToaster>();
                Assert.Equal(expected.GetProperty("fixedViewport").GetBoolean(),cut.FindAll("section.hl-toaster").Count==1);
                Assert.Equal(expected.GetProperty("visibleEntries").GetInt32(),cut.FindAll(".hl-toaster__toast").Count);
                Assert.Equal(expected.GetProperty("liveItems").GetInt32(),cut.FindAll("[role=status],[role=alert]").Count);
                break;
            }
            case "toaster.variants":
            {
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.MaximumVisible,6));
                foreach(var variant in Strings(input.GetProperty("variants")))Show(service,variant,$"Message {variant}");
                var toasts=cut.WaitForElements(".hl-toaster__toast",TimeSpan.FromSeconds(2));
                Assert.Equal(expected.GetProperty("allMessagesAndVariantsPreserved").GetBoolean(),
                    Strings(input.GetProperty("variants")).SequenceEqual(toasts.Select(toast=>toast.GetAttribute("data-variant")!))&&Strings(input.GetProperty("variants")).Select(v=>$"Message {v}").SequenceEqual(toasts.Select(toast=>toast.QuerySelector(".hl-toaster__copy > div")!.TextContent)));
                break;
            }
            case "toaster.roles":
            {
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.MaximumVisible,6));
                foreach(var variant in Strings(input.GetProperty("variants")))Show(service,variant,$"Message {variant}");
                var toasts=cut.WaitForElements(".hl-toaster__toast",TimeSpan.FromSeconds(2));
                foreach(var toast in toasts)Assert.Equal(toast.GetAttribute("data-variant")=="error"?expected.GetProperty("errorRole").GetString():expected.GetProperty("otherRole").GetString(),toast.GetAttribute("role"));
                Assert.Equal(expected.GetProperty("explicitAriaLive").GetBoolean(),cut.FindAll(".hl-toaster__toast[aria-live]").Count>0);
                break;
            }
            case "toaster.queue-limit":
            case "toaster.dismiss-one":
            {
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.MaximumVisible,input.GetProperty("maximumVisible").GetInt32()));
                var ids=Strings(input.GetProperty("messages")).ToDictionary(message=>message,message=>service.Show(message,new ToastOptions(DurationMilliseconds:60_000)));
                if(id=="toaster.dismiss-one")service.Dismiss(ids[input.GetProperty("dismiss").GetString()!]);
                cut.WaitForAssertion(()=>Assert.Equal(Strings(expected.GetProperty("visible")),cut.FindAll(".hl-toaster__copy > div:first-child").Select(node=>node.TextContent).ToArray()));
                if(id=="toaster.queue-limit")Assert.Equal(Strings(expected.GetProperty("queued")),service.Entries.Select(entry=>entry.Message).Except(Strings(expected.GetProperty("visible"))).ToArray());
                else Assert.Equal(Strings(expected.GetProperty("closed")),Strings(input.GetProperty("messages")).Except(service.Entries.Select(entry=>entry.Message)).ToArray());
                break;
            }
            case "toaster.dismiss-all":
            {
                var local=new ToastService();var changes=0;local.Changed+=()=>changes++;
                foreach(var message in Strings(input.GetProperty("messages")))local.Show(message,new ToastOptions(DurationMilliseconds:200));
                local.Dismiss();var afterDismiss=changes;
                Assert.Equal(expected.GetProperty("openEntries").GetInt32(),local.Entries.Count);
                Thread.Sleep(400);
                // Released timers fire no later dismissal.
                Assert.Equal(expected.GetProperty("timersReleased").GetBoolean(),changes==afterDismiss);
                local.Dispose();
                break;
            }
            case "toaster.duration-precedence":
            {
                var perCall=input.GetProperty("perCall").GetInt32();var defaults=input.GetProperty("defaults").GetInt32();var host=input.GetProperty("host").GetInt32();
                var withDefaults=new ToastService();withDefaults.ConfigureHost(new ToastHostConfiguration(DurationMilliseconds:host,Defaults:new ToastOptions(DurationMilliseconds:defaults)));
                var hostOnly=new ToastService();hostOnly.ConfigureHost(new ToastHostConfiguration(DurationMilliseconds:host));
                withDefaults.Show("Per call",new ToastOptions(DurationMilliseconds:perCall));withDefaults.Show("Defaults");hostOnly.Show("Host");
                Assert.Equal(expected.GetProperty("resolved").EnumerateArray().Select(value=>(int?)value.GetInt32()).ToArray(),
                    withDefaults.Entries.Concat(hostOnly.Entries).Select(entry=>entry.DurationMilliseconds).ToArray());
                withDefaults.Dispose();hostOnly.Dispose();
                break;
            }
            case "toaster.error-persistence":
            {
                var duration=input.GetProperty("duration").GetInt32();
                var keep=new ToastService();keep.ConfigureHost(new ToastHostConfiguration(KeepErrorsPersistent:input.GetProperty("keepErrorsPersistent").GetBoolean()));
                keep.Error("Failed",new ToastOptions(DurationMilliseconds:duration));
                Assert.Equal(expected.GetProperty("persistent").GetBoolean(),keep.Entries.Single().Persistent&&keep.Entries.Single().DurationMilliseconds is null);
                var optOut=new ToastService();optOut.ConfigureHost(new ToastHostConfiguration(KeepErrorsPersistent:false));
                optOut.Error("Failed",new ToastOptions(DurationMilliseconds:duration));
                Assert.Equal(expected.GetProperty("optOutAllowsFinite").GetBoolean(),optOut.Entries.Single().DurationMilliseconds==duration);
                keep.Dispose();optOut.Dispose();
                break;
            }
            case "toaster.loading-persistence":
            {
                var local=new ToastService();var loading=local.Loading("Saving",new ToastOptions(DurationMilliseconds:input.GetProperty("duration").GetInt32()));
                // No timer is started for a persistent entry, so its absent duration is the observable.
                Assert.Equal(expected.GetProperty("persistentUntilTransitionOrDismiss").GetBoolean(),local.Entries.Count==1&&local.Entries.Single().Persistent&&local.Entries.Single().DurationMilliseconds is null);
                local.Dismiss(loading);Assert.Empty(local.Entries);
                local.Dispose();
                break;
            }
            case "toaster.close-policy":
            {
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.ShowCloseButton,input.GetProperty("showCloseButton").GetBoolean()).Add(x=>x.MaximumVisible,6));
                foreach(var variant in Strings(input.GetProperty("variants")))Show(service,variant,$"Message {variant}",new ToastOptions(DurationMilliseconds:60_000));
                cut.WaitForElements(".hl-toaster__toast",TimeSpan.FromSeconds(2));
                bool HasClose(string variant)=>cut.Find($".hl-toaster__toast[data-variant={variant}]").QuerySelector(".hl-toaster__close") is not null;
                Assert.Equal(expected.GetProperty("defaultClose").GetBoolean(),HasClose("default"));
                Assert.Equal(expected.GetProperty("errorClose").GetBoolean(),HasClose("error"));
                break;
            }
            case "toaster.description-action":
            {
                var invocations=0;
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.Defaults,new ToastOptions(Description:input.GetProperty("defaultDescription").GetString())));
                service.Show("Saved",new ToastOptions(DurationMilliseconds:60_000,Description:input.GetProperty("callDescription").GetString(),Action:new ToastAction(input.GetProperty("action").GetString()!,()=>{invocations++;return Task.CompletedTask;})));
                var action=cut.WaitForElement(".hl-toaster__action",TimeSpan.FromSeconds(2));
                Assert.Equal(expected.GetProperty("description").GetString(),cut.Find(".hl-toaster__description").TextContent);
                // Activation invokes exactly once, however often the action is pressed.
                action.Click();cut.Find(".hl-toaster__action").Click();
                Assert.Equal(expected.GetProperty("actionInvocations").GetInt32(),invocations);
                Assert.Equal(expected.GetProperty("implicitlyDismissed").GetBoolean(),service.Entries.Count==0);
                break;
            }
            case "toaster.promise-success":
            case "toaster.promise-error":
            {
                var local=new ToastService();var source=new TaskCompletionSource<int>();
                var tracked=local.TrackAsync(source.Task,"Saving",value=>"Saved",error=>"Failed");
                var initial=local.Entries.Single();
                Assert.Equal(Variants[input.GetProperty("initial").GetString()!],initial.Variant);
                if(input.GetProperty("result").GetString()=="success"){source.SetResult(1);tracked.GetAwaiter().GetResult();}
                else{source.SetException(new InvalidOperationException("down"));Assert.ThrowsAny<InvalidOperationException>(()=>tracked.GetAwaiter().GetResult());}
                var final=local.Entries.Single();
                Assert.Equal(expected.GetProperty("sameIdentity").GetBoolean(),final.Id==initial.Id);
                Assert.Equal(Variants[expected.GetProperty("finalVariant").GetString()!],final.Variant);
                if(expected.TryGetProperty("finiteDuration",out var finite))Assert.Equal(finite.GetBoolean(),final.DurationMilliseconds is not null&&!final.Persistent);
                if(expected.TryGetProperty("persistent",out var persistent))Assert.Equal(persistent.GetBoolean(),final.Persistent);
                local.Dispose();
                break;
            }
            case "toaster.positions":
            {
                var tokens=new List<string>();
                foreach(var position in Strings(input.GetProperty("positions")))
                {
                    var value=Enum.Parse<ToastPosition>(string.Concat(position.Split('-').Select(part=>char.ToUpperInvariant(part[0])+part[1..])));
                    tokens.Add(Render<HarborlineToaster>(p=>p.Add(x=>x.Position,value)).Find("section.hl-toaster").GetAttribute("data-position")!);
                }
                Assert.Equal(Strings(input.GetProperty("positions")),tokens.ToArray());
                Assert.Equal(expected.GetProperty("allDistinct").GetBoolean(),tokens.Distinct().Count()==tokens.Count);
                Assert.Equal(expected.GetProperty("default").GetString(),Render<HarborlineToaster>().Find("section.hl-toaster").GetAttribute("data-position"));
                break;
            }
            case "toaster.localization":
            {
                var cut=Render<HarborlineToaster>(p=>p.Add(x=>x.DismissLabel,input.GetProperty("dismissLabel").GetString()!));
                service.Error("Failed");
                Assert.Equal(expected.GetProperty("closeAccessibleName").GetString(),cut.WaitForElement(".hl-toaster__close",TimeSpan.FromSeconds(2)).GetAttribute("aria-label"));
                break;
            }
            case "toaster.invalid-input":
            {
                var errors=new List<string>();
                void Capture(Action action){try{action();errors.Add("none");}catch(InvalidOperationException error){errors.Add(error.Message);}}
                var local=new ToastService();
                foreach(var message in Strings(input.GetProperty("messages")))Capture(()=>local.Show(message));
                foreach(var limit in input.GetProperty("limits").EnumerateArray())Capture(()=>local.ConfigureHost(new ToastHostConfiguration(MaximumVisible:limit.GetInt32())));
                foreach(var duration in input.GetProperty("durations").EnumerateArray().Where(value=>value.ValueKind==System.Text.Json.JsonValueKind.Number))Capture(()=>local.Show("x",new ToastOptions(DurationMilliseconds:duration.GetInt32())));
                // "NaN" has no Int32 representation in this lane, so only the numeric durations run here.
                // One code per input, in input order: messages, limits, then durations.
                var codes=Strings(expected.GetProperty("errors"));
                var expectedSequence=Strings(input.GetProperty("messages")).Select(_=>codes[0]).Concat(input.GetProperty("limits").EnumerateArray().Select(_=>codes[1])).Concat(input.GetProperty("durations").EnumerateArray().Where(value=>value.ValueKind==System.Text.Json.JsonValueKind.Number).Select(_=>codes[2])).ToArray();
                Assert.Equal(expectedSequence,errors.ToArray());
                local.Dispose();
                break;
            }
            default:
                Assert.Contains(id,Unasserted);
                break;
        }
    }
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
}
