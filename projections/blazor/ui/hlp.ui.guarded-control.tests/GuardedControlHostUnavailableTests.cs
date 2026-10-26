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
