using Bunit;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

// Escape belongs to the innermost layer that uses it. The lens group clears an active lens through
// @onkeydown, which cannot mark the key handled, so layers-rail.js observes the group and marks Escape
// only while a lens is active (the jsdom test conformance/hlp.ui.layers-rail/layers-rail-escape.test.mjs
// runs the module with the real overlays, and the batch's browser probe checks it in three engines).
public sealed class LayersRailEscapeLayerTests : BunitContext
{
    private readonly BunitJSModuleInterop module;
    private readonly BunitJSModuleInterop registration;
    public LayersRailEscapeLayerTests()
    {
        module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/layers-rail.js");
        module.Mode = JSRuntimeMode.Loose;
        registration = module.SetupModule(invocation => invocation.Identifier == "observeLensKeys");
        registration.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task TheLensGroupIsObservedOnceAndReleasedOnDispose()
    {
        var cut = Render<LayersRailHost>();
        var observe = Assert.Single(module.Invocations["observeLensKeys"]);
        Assert.IsType<ElementReference>(observe.Arguments[0]);
        cut.Find(".hl-layers-rail__lens-activate").Click();
        Assert.Single(module.Invocations["observeLensKeys"]);
        await DisposeComponentsAsync();
        Assert.Single(registration.Invocations["dispose"]);
    }

    [Fact]
    public void EscapeInTheLensGroupStillClearsTheActiveLens()
    {
        var cut = Render<LayersRailHost>();
        Assert.NotEmpty(cut.FindAll("[role=status]"));
        cut.Find(".hl-layers-rail__lenses").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[role=status]"));
        Assert.All(cut.FindAll(".hl-layers-rail__lens-activate"), lens => Assert.Equal("false", lens.GetAttribute("aria-pressed")));
    }
}
