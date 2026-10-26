using System.Globalization;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;
using Clock = Harborline.UIAdapters.Blazor.Tests.GuardedControlAnnouncementTests;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The arm window is fixed at five seconds from the arming activation, so a host that re-renders the
/// locale provider every second, passing a fresh catalog, a fresh number formatter and an inline
/// OnEvent callback, neither moves the deadline nor repeats the recovery. The React lane asserts the
/// same (GuardedControl.arm-window.test.tsx).
/// </summary>
public sealed class GuardedControlHostRenderTests : BunitContext
{
    public GuardedControlHostRenderTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public async Task ReCoversAtFiveSecondsWhileTheHostReRendersEverySecond()
    {
        var events = new List<GuardedControlEvent>();
        var clock = new Clock.ManualClock();
        var host = Render<HarborlineLocaleProvider>(p => FreshHost(p, events));
        var guard = host.FindComponent<HarborlineGuardedControl>();
        Clock.UseClock(guard.Instance, clock);
        guard.Find("button").Click();
        for (var second = 1; second <= 4; second++)
        {
            await Clock.TickAsync(guard, clock);
            host.Render(p => FreshHost(p, events));
        }
        guard.WaitForAssertion(() => Assert.Equal("1 second", guard.Find("[role=timer]").TextContent));
        Assert.Equal("armed", guard.Find("button").GetAttribute("data-guard-state"));
        await Clock.TickAsync(guard, clock);
        host.Render(p => FreshHost(p, events));
        guard.WaitForAssertion(() => Assert.Equal("covered", guard.Find("button").GetAttribute("data-guard-state")));
        Assert.Equal("⁨Publish⁩ is protected again.", guard.Find("[role=status]").TextContent);
        Assert.Equal(GuardedControlRecoveryReason.Timeout, Assert.Single(events.OfType<GuardedControlRecovered>()).Reason);
    }

    // Every render passes new identities: a new catalog dictionary, a new formatter delegate and a new
    // OnEvent callback.
    private static void FreshHost(ComponentParameterCollectionBuilder<HarborlineLocaleProvider> p, List<GuardedControlEvent> events) => p
        .Add(x => x.Locale, "en-US")
        .Add(x => x.Catalog, new Dictionary<string, string> { ["chrome.guard.recovered.whole"] = "{action} is protected again." })
        .Add(x => x.NumberFormatter, request => request.Value.ToString(CultureInfo.InvariantCulture))
        .AddChildContent<HarborlineGuardedControl>(g => g
            .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, "Publish").Add(x => x.OnCommit, () => { })
            .Add(x => x.OnEvent, e => events.Add(e)));
}
