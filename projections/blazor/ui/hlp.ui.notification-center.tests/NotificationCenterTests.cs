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
    [Theory,Trait("ModuleConformance","hlp.ui.notification-center")] [MemberData(nameof(SharedFixtureBatch.Cases), "hlp.ui.notification-center", MemberType = typeof(SharedFixtureBatch))]
    public void SharedFixtureConforms(string caseId, string? rawFixture)
    {
        SharedFixtureBatch.Run(caseId, rawFixture, () => {
            AssertFixturePrefix("notification-center.");Assert.NotNull(Render<HarborlineNotificationCenter>(p=>p.Add(x=>x.Items,Items)));
        });
    }
    // b20: WCAG 2.2 SC 1.4.11 Non-text Contrast. With no host token reaching them, the unread marker
    // (role="img"), the trigger dot and the focus ring end at CanvasText-based fallbacks (9:1 or more on
    // Canvas in light and dark), never at Highlight (about 1.5:1) or Mark (1.07:1). Forced-colors
    // blocks keep their system colours; the React lane computes the contrast from the same bytes.
    [Fact]
    public void IndicatorsAndFocusRingFallBackToCanvasTextOutsideForcedColors()
    {
        var css = System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(Repo("projections/blazor/ui/hlp.ui.notification-center/wwwroot/notification-center.css")),
            @"@media\(forced-colors:active\)\{(?:[^{}]*\{[^{}]*\})*[^{}]*\}", "");
        const string fallback = "color-mix(in srgb,CanvasText 72%,Canvas))";
        Assert.Contains("border-radius:50%;background:var(--hl-notification-unread)}", css);
        foreach (var token in new[] { "--hl-notification-unread:var(--hl-primary,", "--hl-notification-focus:var(--hl-focus," })
        {
            var definitions = System.Text.RegularExpressions.Regex.Matches(css, System.Text.RegularExpressions.Regex.Escape(token) + @"[^;}]*");
            Assert.Equal(2, definitions.Count);
            Assert.All(definitions, definition => Assert.EndsWith(fallback, definition.Value));
        }
        // The dot's own rule (not the badge-and-dot positioning rule that precedes it).
        var dot = System.Text.RegularExpressions.Regex.Match(css, @"(?<=\})\.hl-notification-center__dot\{[^}]*\}").Value;
        Assert.Contains("background:var(--hl-danger,var(--hl-destructive," + fallback + ")", dot);
        Assert.DoesNotContain("Mark", dot);
        // b20 review: the badge is an indicator carrying text. Its unthemed pair is CanvasText fill with
        // Canvas text (21:1), where it was Mark with #fff (about 1.07:1).
        var badge = System.Text.RegularExpressions.Regex.Match(css, @"(?<=\})\.hl-notification-center__badge\{[^}]*\}").Value;
        Assert.Contains("background:var(--hl-danger,var(--hl-destructive,CanvasText))", badge);
        Assert.Contains("color:var(--hl-destructive-foreground,Canvas)", badge);
        Assert.DoesNotContain("Mark", badge);
    }
    private static string Repo(string relative) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../", relative));
    private static void AssertFixturePrefix(string prefix){var raw=SharedFixtureBatch.Current;if(string.IsNullOrWhiteSpace(raw))return;using var fixture=System.Text.Json.JsonDocument.Parse(raw);Assert.StartsWith(prefix,fixture.RootElement.GetProperty("id").GetString());}
    private sealed class FakeOutside:IOutsidePointerObserver{public ValueTask<IOutsidePointerRegistration> ObserveAsync(Microsoft.AspNetCore.Components.ElementReference e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>throw new NotSupportedException();public ValueTask<IOutsidePointerRegistration> ObserveAsync(IReadOnlyList<Microsoft.AspNetCore.Components.ElementReference> e,Func<OutsidePointerEvent,ValueTask> c,OutsidePointerOptions? o=null,CancellationToken t=default)=>new(new Registration());private sealed class Registration:IOutsidePointerRegistration{public ValueTask DisposeAsync()=>ValueTask.CompletedTask;public ValueTask SetEnabledAsync(bool enabled)=>ValueTask.CompletedTask;public void SetCallback(Func<OutsidePointerEvent,ValueTask> c){}}}
}
