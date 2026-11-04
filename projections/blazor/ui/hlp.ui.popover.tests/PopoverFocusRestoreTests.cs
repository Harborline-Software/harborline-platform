using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// G5 (R-0125, owner ruling 2026-10-27): an outside pointer dismissal never moves focus; other closes
/// restore the trigger only when focus is inside the closing content or was lost with it. The decision
/// is made in popover.js: the pointer press is recorded when it happens, and dispose() checks the
/// active element when the closing render arrives. That JavaScript runs on the real module in
/// conformance/hlp.ui.popover/popover-focus-restore.test.mjs; bUnit cannot execute it. Here, the
/// component's side: a dismissal requested from JavaScript closes the content and disposes that
/// connection exactly once, so dispose() is where every close's focus rule is applied.
/// </summary>
public sealed class PopoverFocusRestoreTests : BunitContext
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/popover.js";

    [Fact]
    public async Task ADismissalFromJavaScriptClosesAndDisposesTheConnectionOnce()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule(Module);
        var emitted = new List<bool>();
        RenderFragment body = builder =>
        {
            builder.OpenComponent<HarborlinePopoverTrigger>(0); builder.AddAttribute(1, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Open"))); builder.CloseComponent();
            builder.OpenComponent<HarborlinePopoverContent>(2); builder.AddAttribute(3, "AccessibleLabel", "Details"); builder.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddContent(0, "Body"))); builder.CloseComponent();
        };
        var cut = Render<HarborlinePopover>(p => p.Add(x => x.DefaultOpen, true).Add(x => x.ChildContent, body).Add(x => x.OpenChanged, value => emitted.Add(value)));
        Assert.Single(module.Invocations["connect"]);

        await cut.InvokeAsync(() => cut.FindComponent<HarborlinePopoverContent>().Instance.DismissFromJavaScriptAsync());

        Assert.Empty(cut.FindAll(".hl-popover__content"));
        Assert.Equal(new[] { false }, emitted);
        Assert.Single(module.Invocations["dispose"]);
    }
}
