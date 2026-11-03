using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Harborline.UIAdapters.Blazor.Localization;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// When the interactive host adapter cannot load, the control stays covered and inert and announces
/// the host-unavailable message, which resolves the host catalog first
/// (chrome.guard.host-unavailable.whole), like every other guard message.
/// </summary>
public sealed class GuardedControlHostUnavailableTests : BunitContext
{
    // The JS host adapter's import fails (strict JS interop with nothing planned), so the control stays
    // covered and inert and announces the host-unavailable message.
    [Fact]
    public void HostUnavailableAnnouncementResolvesTheHostCatalog()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var guard = Render<HarborlineLocaleProvider>(p => p
                .Add(x => x.Locale, "fr-FR")
                .Add(x => x.Catalog, new Dictionary<string, string> { ["chrome.guard.host-unavailable.whole"] = "{action} indisponible." })
                .AddChildContent<HarborlineGuardedControl>(g => g
                    .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
                    .Add(x => x.Label, "Publish").Add(x => x.OnCommit, () => { })))
            .FindComponent<HarborlineGuardedControl>();
        guard.WaitForAssertion(() => Assert.Equal("⁨Publish⁩ indisponible.", guard.Find("[role=status]").TextContent));
        Assert.True(guard.Find("button").HasAttribute("disabled"));
    }

    // The failure is sticky: a parent re-render passes valid parameters again, and the control must stay
    // covered and inert, with no arming and no focus call on a module that never loaded.
    [Fact]
    public void StaysUnavailableAfterTheHostReRenders()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var host = Render<HarborlineLocaleProvider>(Host);
        var guard = host.FindComponent<HarborlineGuardedControl>();
        guard.WaitForAssertion(() => Assert.True(guard.Find("button").HasAttribute("disabled")));
        var announcement = guard.Find("[role=status]").TextContent;
        Assert.Equal("\u2068Publish\u2069 indisponible.", announcement);

        host.Render(Host);

        Assert.True(guard.Find("button").HasAttribute("disabled"));
        Assert.Equal(announcement, guard.Find("[role=status]").TextContent);
        guard.Find("button").Click();
        Assert.Equal(GuardedControlState.Covered, guard.Instance.State);
        Assert.Equal("covered", guard.Find("button").GetAttribute("data-guard-state"));
        Assert.Equal(announcement, guard.Find("[role=status]").TextContent);
    }

    private static void Host(ComponentParameterCollectionBuilder<HarborlineLocaleProvider> p) => p
        .Add(x => x.Locale, "fr-FR")
        .Add(x => x.Catalog, new Dictionary<string, string> { ["chrome.guard.host-unavailable.whole"] = "{action} indisponible." })
        .AddChildContent<HarborlineGuardedControl>(g => g
            .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, "Publish").Add(x => x.OnCommit, () => { }));

    [Fact]
    public void HostUnavailableAnnouncementIsEnglishWithoutAProvider()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        var guard = Render<HarborlineGuardedControl>(g => g
            .Add(x => x.Action, "packages.export").Add(x => x.ClassificationId, "new-record-type")
            .Add(x => x.Label, "Publish").Add(x => x.OnCommit, () => { }));
        guard.WaitForAssertion(() => Assert.Equal("This guarded action is unavailable.", guard.Find("[role=status]").TextContent));
        Assert.True(guard.Find("button").HasAttribute("disabled"));
    }

}
