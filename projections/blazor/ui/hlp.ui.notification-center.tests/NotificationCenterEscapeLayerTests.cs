using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Navigation;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Escape belongs to the innermost layer that uses it. The dialog closes on Escape through @onkeydown,
// which cannot mark the key handled, so notification-center.js observes Escape on the dialog element
// and marks it before an enclosing Dialog or Sheet sees it (the jsdom test
// conformance/hlp.ui.notification-center/notification-center-escape.test.mjs runs the module with the
// real overlays, and the batch's browser probe checks it in three engines).
public sealed class NotificationCenterEscapeLayerTests : BunitContext
{
    private readonly BunitJSModuleInterop module;
    private readonly BunitJSModuleInterop registration;
    private static readonly NotificationCenterItem[] Items = [new("r1", "Export ready", "now", "result")];

    public NotificationCenterEscapeLayerTests()
    {
        Services.AddSingleton<IOutsidePointerObserver>(new CapturingOutside());
        module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/notification-center.js");
        module.Mode = JSRuntimeMode.Loose;
        registration = module.SetupModule(invocation => invocation.Identifier == "observeEscape");
        registration.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void OpeningObservesEscapeOnTheDialogElementAndClosingReleasesIt()
    {
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, Items));
        Assert.Empty(module.Invocations["observeEscape"]);
        cut.Find(".hl-notification-center__trigger").Click();
        var observe = Assert.Single(module.Invocations["observeEscape"]);
        Assert.IsType<ElementReference>(observe.Arguments[0]);
        Assert.Empty(registration.Invocations["dispose"]);
        cut.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.Single(registration.Invocations["dispose"]);
        cut.Find(".hl-notification-center__trigger").Click();
        Assert.Equal(2, module.Invocations["observeEscape"].Count);
    }

    // Over a Server circuit each JS call is a round trip, and focus lands in the panel with focusFirst. An
    // Escape pressed after that but before observeEscape was registered reached an enclosing Sheet
    // unmarked, so the observer is registered first, as UserMenu observes keys before focusing.
    [Fact]
    public void TheEscapeObserverIsRegisteredBeforeFocusMovesIntoThePanel()
    {
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, Items));
        cut.Find(".hl-notification-center__trigger").Click();
        var order = module.Invocations.Select(invocation => invocation.Identifier).Where(identifier => identifier is "observeEscape" or "focusFirst").ToList();
        Assert.Equal(["observeEscape", "focusFirst"], order);
    }

    [Fact]
    public async Task DisposingTheOpenComponentReleasesTheObserver()
    {
        var cut = Render<HarborlineNotificationCenter>(p => p.Add(x => x.Items, Items));
        cut.Find(".hl-notification-center__trigger").Click();
        await cut.InvokeAsync(async () => await ((IAsyncDisposable)cut.Instance).DisposeAsync());
        Assert.Single(registration.Invocations["dispose"]);
    }
}
