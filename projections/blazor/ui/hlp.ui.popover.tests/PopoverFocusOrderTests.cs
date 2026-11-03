using System.Reflection;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// T-1128: popover.js keeps the content right after its trigger in sequential focus order (Shift+Tab out
/// of the content goes to the trigger, Tab out to what follows it). Under Blazor Server the trigger's
/// ElementReference can be missing when the content connects (the trigger captures it in its own
/// OnAfterRender) or stale after a re-render, so connect() is also handed the trigger's DOM id and
/// resolves the trigger when a key arrives. The key handling itself is JavaScript, run on the real
/// module in conformance/hlp.ui.popover/popover-focus-order.test.mjs; bUnit checks what it is handed.
/// </summary>
public sealed class PopoverFocusOrderTests : BunitContext
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/popover.js";

    [Fact]
    public void ConnectIsHandedTheTriggerIdSoTheFocusOrderCanResolveTheTriggerLate()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(Module);
        RenderFragment body = builder =>
        {
            builder.OpenComponent<HarborlinePopoverTrigger>(0); builder.AddAttribute(1, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
            builder.OpenComponent<HarborlinePopoverContent>(2); builder.AddAttribute(3, "AccessibleLabel", "Details"); builder.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body"))); builder.CloseComponent();
        };
        var cut = Render<HarborlinePopover>(p => p.Add(x => x.DefaultOpen, true).Add(x => x.ChildContent, body));

        var options = Assert.Single(module.Invocations["connect"]).Arguments[3]!;
        var triggerId = options.GetType().GetProperty("triggerId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(options) as string;
        var trigger = cut.Find("button.hl-popover-trigger");
        Assert.False(string.IsNullOrEmpty(trigger.Id));
        Assert.Equal(trigger.Id, triggerId);
    }
    /// <summary>
    /// b31 review note 2: the trigger splats caller attributes after its own id, so a caller's id wins
    /// there (and must keep winning). popover.js finds the trigger by data-hl-popover-trigger instead,
    /// rendered after the splat so no caller attribute replaces it.
    /// </summary>
    [Fact]
    public void ACallerIdIsKeptAndTheTriggerStillCarriesTheMarkerConnectLooksFor()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(Module);
        RenderFragment body = builder =>
        {
            builder.OpenComponent<HarborlinePopoverTrigger>(0);
            builder.AddAttribute(1, "id", "caller-trigger");
            builder.AddAttribute(2, "data-hl-popover-trigger", "caller-marker");
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open")));
            builder.CloseComponent();
            builder.OpenComponent<HarborlinePopoverContent>(4); builder.AddAttribute(5, "AccessibleLabel", "Details"); builder.AddAttribute(6, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body"))); builder.CloseComponent();
        };
        var cut = Render<HarborlinePopover>(p => p.Add(x => x.DefaultOpen, true).Add(x => x.ChildContent, body));

        var options = Assert.Single(module.Invocations["connect"]).Arguments[3]!;
        var triggerId = options.GetType().GetProperty("triggerId", BindingFlags.Public | BindingFlags.Instance)?.GetValue(options) as string;
        var trigger = cut.Find("button.hl-popover-trigger");
        Assert.Equal("caller-trigger", trigger.Id);
        Assert.False(string.IsNullOrEmpty(triggerId));
        Assert.Equal(triggerId, trigger.GetAttribute("data-hl-popover-trigger"));
    }
}
