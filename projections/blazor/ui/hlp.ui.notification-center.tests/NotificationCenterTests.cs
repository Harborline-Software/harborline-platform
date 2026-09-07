using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed class NotificationCenterTests : BunitContext
{
    public NotificationCenterTests(){JSInterop.Mode=JSRuntimeMode.Loose;Services.AddSingleton<IOutsidePointerObserver>(new FakeOutside());}
    private static IReadOnlyList<NotificationCenterItem> Items=>[new("p1","Approve","now","proposal"),new("r1","Done","later","result")];
    [Fact] public void BadgeAndDialogSemanticsMatch(){var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items));Assert.Equal("Notifications, 2 unread",cut.Find("button").GetAttribute("aria-label"));cut.Find("button").Click();Assert.Equal("dialog",cut.Find("[role=dialog]").GetAttribute("role"));Assert.Equal(3,cut.FindAll("[role=tab]").Count);}
    [Fact] public void ProposalDecisionInvokesOnceAndCloses(){var calls=0;var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items).Add(x=>x.OnConfirm,_=>calls++));cut.Find("button").Click();cut.FindAll(".hl-notification-center__actions button")[0].Click();Assert.Equal(1,calls);Assert.Empty(cut.FindAll("[role=dialog]"));}
    [Fact] public void ItemDismissalNeverMutatesCallerList(){var dismissed=new List<string>();var cut=Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items).Add(x=>x.OnDismiss,item=>dismissed.Add(item.Id)));cut.Find("button").Click();var dismiss=cut.Find("[aria-label='Dismiss: Done']");Assert.NotNull(dismiss.QuerySelector("svg"));Assert.DoesNotContain("×",dismiss.TextContent);dismiss.Click();Assert.Equal(["r1"],dismissed);Assert.Equal(2,Items.Count);}
    [Fact,Trait("ModuleConformance","hlp.ui.notification-center")] public void SharedFixtureConforms(){AssertFixturePrefix("notification-center.");Assert.NotNull(Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items)));}
    private static void AssertFixturePrefix(string prefix){var raw=Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
    private sealed class FakeOutside:IOutsidePointerObserver{public ValueTask<IOutsidePointerRegistration> ObserveAsync(Microsoft.AspNetCore.Components.ElementReference e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>throw new NotSupportedException();public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<Microsoft.AspNetCore.Components.ElementReference> e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>new(new Registration());private sealed class Registration:IOutsidePointerRegistration{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;public ValueTask SetEnabledAsync(bool enabled)=>ValueTask.CompletedTask;public void SetCallback(Func<OutsidePointerEvent,ValueTask> c){}}}
}
