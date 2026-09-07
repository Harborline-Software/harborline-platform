using System.Reflection;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.Feedback;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Connection identity for hlp.ui.popover. The content element lives inside
/// <c>@if (Context?.Open == true)</c>, so a close destroys it — yet the connect site returns early
/// on <c>connection is not null</c> and nothing disposes on close, only on component disposal. A
/// close→reopen therefore renders a new element and keeps the old connection forever: identity
/// ignored, plus a leaked document listener. Both directions of the open axis are driven here.
/// </summary>
public sealed class PopoverConnectionIdentityTests
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/popover.js";

    /// <summary>
    /// Sequence numbers are MONOTONIC with the anchor's slots reserved ahead of the content's
    /// (trigger 0/1, anchor 2/3, content 4-9). They must be: the first shape of this fragment
    /// numbered the anchor 5/6 -- after the content's 2/3/4 -- so inserting the anchor made the
    /// sequence go backwards, Blazor gave up matching and DESTROYED AND RECREATED the content
    /// component. A brand-new HarborlinePopoverContent has connection == null and connects with
    /// whatever it is handed, so the anchor row below passed under the capture-once bug: it was
    /// measuring component construction, not the connect site. Reserved slots keep the SAME
    /// instance across the swap, which is the only shape in which the row discriminates.
    /// </summary>
    private static RenderFragment Content(
        PopoverSide side = PopoverSide.Bottom,
        PopoverAlign align = PopoverAlign.Center,
        double sideOffset = 6,
        bool anchor = false,
        int triggerKey = 0) => builder =>
    {
        builder.OpenComponent<HarborlinePopoverTrigger>(0);
        builder.SetKey(triggerKey);
        builder.AddAttribute(1, nameof(HarborlinePopoverTrigger.ChildContent), (RenderFragment)(b => b.AddContent(0, "Open")));
        builder.CloseComponent();
        if (anchor)
        {
            builder.OpenComponent<HarborlinePopoverAnchor>(2);
            builder.AddAttribute(3, nameof(HarborlinePopoverAnchor.ChildContent), (RenderFragment)(b => b.AddContent(0, "anchor")));
            builder.CloseComponent();
        }
        builder.OpenComponent<HarborlinePopoverContent>(4);
        builder.AddAttribute(5, nameof(HarborlinePopoverContent.AccessibleLabel), "Details");
        builder.AddAttribute(6, nameof(HarborlinePopoverContent.Side), side);
        builder.AddAttribute(7, nameof(HarborlinePopoverContent.Align), align);
        builder.AddAttribute(8, nameof(HarborlinePopoverContent.SideOffset), sideOffset);
        builder.AddAttribute(9, nameof(HarborlinePopoverContent.ChildContent), (RenderFragment)(b => b.AddContent(0, "body")));
        builder.CloseComponent();
    };

    private static void Parameters(ComponentParameterCollectionBuilder<HarborlinePopover> parameters, bool open) => parameters
        .Add(c => c.Open, open)
        .Add(c => c.ChildContent, Content());

    /// <summary>One argument of the newest connect call, by name, read off the options object.</summary>
    private static object? Option(BunitJSInterop module, string name)
    {
        var options = module.Invocations["connect"].Last().Arguments[3]!;
        return options.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(options);
    }

    private static void AssertContent(BunitJSInterop module, IRenderedComponent<HarborlinePopover> cut, bool open, string where)
    {
        var content = cut.FindComponent<HarborlinePopoverContent>().Instance;
        // connect(trigger, anchor, content, options, callback) — the content element is argument 2.
        ConnectionIdentity.Assert(module, 2, open ? ConnectionIdentity.ElementId(content, "content") : null, $"content @ {where}");
    }

    [Fact]
    public void EveryRenderBranchTransitionReconnectsExactlyTheElementsThatWereReplaced()
    {
        foreach (var from in new[] { true, false })
        {
            var to = !from;
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            var module = context.JSInterop.SetupModule(Module);

            var cut = context.Render<HarborlinePopover>(p => Parameters(p, from));
            AssertContent(module, cut, from, $"open={from} (initial)");

            cut.Render(p => Parameters(p, to));
            AssertContent(module, cut, to, $"open={from} -> {to}");

            cut.Render(p => Parameters(p, from));
            AssertContent(module, cut, from, $"open={from} -> {to} -> {from}");
        }
    }

    /// <summary>
    /// The killing case: close then reopen. The second content element is a different node, so it
    /// needs its own connection, and the first must have been disposed exactly once.
    /// </summary>
    [Fact]
    public void AReopenConnectsTheNewContentAndDisposesThePreviousConnection()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => Parameters(p, true));
        var before = ConnectionIdentity.ElementId(cut.FindComponent<HarborlinePopoverContent>().Instance, "content");

        cut.Render(p => Parameters(p, false));
        cut.Render(p => Parameters(p, true));

        var after = ConnectionIdentity.ElementId(cut.FindComponent<HarborlinePopoverContent>().Instance, "content");
        Assert.NotEqual(before, after);
        Assert.Equal(
            new[] { before, after },
            module.Invocations["connect"].Select(invocation => ((ElementReference)invocation.Arguments[2]!).Id));
        Assert.Single(module.Invocations["dispose"]);
    }

    /// <summary>A re-render that replaces nothing must not reconnect.</summary>
    [Fact]
    public void AReRenderWhileOpenKeepsTheOneConnectionItAlreadyHas()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => Parameters(p, true));

        cut.Render(p => Parameters(p, true));

        AssertContent(module, cut, true, "idle re-render");
        Assert.Single(module.Invocations["connect"]);
    }

    /// <summary>
    /// Every MUTABLE argument popover.js is handed at connect time, one row each: the module
    /// captures the options object once and exports no update(), so a change while the popover is
    /// open must re-establish the connection exactly once and the newest connection must carry the
    /// new value. RED at 8434d86 for all three rows (connect captured once, never refreshed).
    /// </summary>
    [Theory]
    [InlineData("side", PopoverSide.Top, PopoverAlign.Center, 6d, "top")]
    [InlineData("align", PopoverSide.Bottom, PopoverAlign.End, 6d, "end")]
    [InlineData("sideOffset", PopoverSide.Bottom, PopoverAlign.Center, 24d, 24d)]
    public void AConnectArgumentThatChangesWhileOpenReachesTheConnection(
        string argument, PopoverSide side, PopoverAlign align, double sideOffset, object expected)
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content()));
        Assert.Single(module.Invocations["connect"]);

        cut.Render(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content(side, align, sideOffset)));

        Assert.Equal(expected, Option(module, argument));
        AssertContent(module, cut, true, $"{argument} changed while open");
        Assert.Equal(2, module.Invocations["connect"].Count());
        Assert.Single(module.Invocations["dispose"]);
    }

    /// <summary>
    /// The positioning reference is an argument too: an anchor appearing while the popover is open
    /// changes which element popover.js measures, and place() dereferences the one it was handed.
    /// The discriminating shape (see <see cref="Content"/>): the content component instance is
    /// pinned across the swap, so a connect site that returns early on <c>connection is not null</c>
    /// cannot pass by being handed a fresh component. Two renders after the anchor appears -- the
    /// anchor publishes its ElementReference from its OWN OnAfterRender, which runs after the
    /// content's, so the connect site sees it from the next render onwards (the trigger/context
    /// publication order, unchanged by this slice; what is asserted is that the site then acts).
    /// RED with the capture-once guard restored: connects stays at 1 and argument 1 is the trigger.
    /// </summary>
    [Fact]
    public void AnAnchorAppearingWhileOpenBecomesTheConnectionsReferenceElement()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content()));
        var instance = cut.FindComponent<HarborlinePopoverContent>().Instance;
        Assert.Single(module.Invocations["connect"]);

        cut.Render(p => p.Add(c => c.Open, true).Add(c => c.ChildContent, Content(anchor: true)));
        cut.Render(p => p.Add(c => c.Open, true).Add(c => c.ChildContent, Content(anchor: true)));

        Assert.Same(instance, cut.FindComponent<HarborlinePopoverContent>().Instance);
        var anchorId = ConnectionIdentity.ElementId(cut.FindComponent<HarborlinePopoverAnchor>().Instance, "anchor");
        Assert.Equal(anchorId, ((ElementReference)module.Invocations["connect"].Last().Arguments[1]!).Id);
        Assert.Equal(2, module.Invocations["connect"].Count());
        Assert.Single(module.Invocations["dispose"]);
        AssertContent(module, cut, true, "anchor appeared while open");
    }

    /// <summary>
    /// The trigger element is the popover's other element argument, and the axis with the visible
    /// consequence: popover.js captures <c>trigger</c> once and its outside-pointerdown handler
    /// exempts it (<c>!trigger?.contains(event.target)</c>), so a connection holding a REPLACED
    /// trigger dismisses the popover when the user clicks the live trigger. A keyed trigger swap
    /// while open must therefore re-establish the connection on the element rendered now.
    /// The exemption itself is popover.js behaviour and cannot be observed through the bUnit seam
    /// (JSInterop records the call; no module executes), so what is asserted here is the input the
    /// exemption is computed from -- the trigger reference the connection was handed -- plus the
    /// old trigger's element no longer being it. The exemption's own behaviour given that input is
    /// popover.js's, unchanged by this slice.
    /// RED with the fix-1 trigger clause neutralised: the connection keeps the old element.
    /// </summary>
    [Fact]
    public void ATriggerSwapWhileOpenReachesTheConnection()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content()));
        var oldTrigger = ((ElementReference)module.Invocations["connect"].Last().Arguments[0]!).Id;

        cut.Render(p => p.Add(c => c.Open, true).Add(c => c.ChildContent, Content(triggerKey: 1)));
        cut.Render(p => p.Add(c => c.Open, true).Add(c => c.ChildContent, Content(triggerKey: 1)));

        var newTrigger = ConnectionIdentity.ElementId(cut.FindComponent<HarborlinePopoverTrigger>().Instance, "trigger");
        Assert.NotEqual(oldTrigger, newTrigger);
        Assert.Equal(newTrigger, ((ElementReference)module.Invocations["connect"].Last().Arguments[0]!).Id);
        Assert.Equal(2, module.Invocations["connect"].Count());
        Assert.Single(module.Invocations["dispose"]);
        AssertContent(module, cut, true, "trigger swap while open");
    }

    /// <summary>
    /// The other half of the property: re-rendering with the SAME arguments must not reconnect, so
    /// the fix above cannot degenerate into a reconnect on every render.
    /// </summary>
    [Fact]
    public void ReRenderingWithUnchangedArgumentsKeepsTheOneConnection()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlinePopover>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content(PopoverSide.Left, PopoverAlign.Start, 12)));

        cut.Render(p => p
            .Add(c => c.Open, true)
            .Add(c => c.ChildContent, Content(PopoverSide.Left, PopoverAlign.Start, 12)));

        Assert.Single(module.Invocations["connect"]);
        Assert.Empty(module.Invocations["dispose"]);
    }
}
