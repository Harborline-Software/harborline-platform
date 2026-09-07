using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Connection identity for hlp.ui.sheet. The content hands sheet.js the portal and the content
/// element and connects them on <c>Context?.Open == true</c>, but the markup has a second branch
/// under <c>@if (Context.Modal)</c>: a modal flip while the sheet is open moves no boolean the
/// connect predicate reads. Every (open, modal) transition is driven here, in both directions.
/// </summary>
public sealed class SheetConnectionIdentityTests
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/sheet.js";

    private readonly record struct State(bool Open, bool Modal)
    {
        public override string ToString() => $"open={Open},modal={Modal}";
    }

    private static readonly State[] States =
    [
        new(true, true), new(true, false), new(false, true), new(false, false),
    ];

    private static RenderFragment Content(int triggerKey = 0) => builder =>
    {
        builder.OpenComponent<HarborlineSheetTrigger>(0);
        builder.SetKey(triggerKey);
        builder.AddAttribute(1, nameof(HarborlineSheetTrigger.ChildContent), (RenderFragment)(b => b.AddContent(0, "Open")));
        builder.CloseComponent();
        builder.OpenComponent<HarborlineSheetContent>(2);
        builder.AddAttribute(3, nameof(HarborlineSheetContent.CloseLabel), "Close");
        builder.AddAttribute(4, nameof(HarborlineSheetContent.ChildContent), (RenderFragment)(b => b.AddContent(0, "body")));
        builder.CloseComponent();
    };

    private static void Parameters(ComponentParameterCollectionBuilder<HarborlineSheet> parameters, State state) => parameters
        .Add(c => c.Open, state.Open)
        .Add(c => c.Modal, state.Modal)
        .Add(c => c.ChildContent, Content());

    private static void AssertBoth(BunitJSInterop module, IRenderedComponent<HarborlineSheet> cut, State state, string where)
    {
        var content = cut.FindComponent<HarborlineSheetContent>().Instance;
        ConnectionIdentity.Assert(module, 0, state.Open ? ConnectionIdentity.ElementId(content, "portal") : null, $"portal @ {where}");
        ConnectionIdentity.Assert(module, 1, state.Open ? ConnectionIdentity.ElementId(content, "content") : null, $"content @ {where}");
    }

    [Fact]
    public void EveryRenderBranchTransitionReconnectsExactlyTheElementsThatWereReplaced()
    {
        foreach (var from in States)
        {
            foreach (var to in States)
            {
                if (from.Equals(to)) continue;
                using var context = new BunitContext();
                context.JSInterop.Mode = JSRuntimeMode.Loose;
                var module = context.JSInterop.SetupModule(Module);

                var cut = context.Render<HarborlineSheet>(p => Parameters(p, from));
                AssertBoth(module, cut, from, $"{from} (initial)");

                cut.Render(p => Parameters(p, to));
                AssertBoth(module, cut, to, $"{from} -> {to}");

                cut.Render(p => Parameters(p, from));
                AssertBoth(module, cut, from, $"{from} -> {to} -> {from}");
            }
        }
    }

    /// <summary>
    /// The modal flip on its own: the overlay branch appears beside the content element while
    /// <c>Open</c> never moves. Whatever Blazor does to the element, the live connection must hold
    /// the element rendered now and must have been told the modality rendered now.
    /// </summary>
    [Fact]
    public void AModalFlipWhileOpenLeavesTheConnectionOnTheElementRenderedNow()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineSheet>(p => Parameters(p, new State(true, true)));

        cut.Render(p => Parameters(p, new State(true, false)));

        AssertBoth(module, cut, new State(true, false), "modal flip");
        Assert.False((bool)module.Invocations["connect"].Last().Arguments[3]!,
            "the connection was told modal=true while a non-modal sheet is rendered");
    }

    /// <summary>A re-render that replaces nothing must not reconnect.</summary>
    [Fact]
    public void AReRenderWithNoBranchChangeKeepsTheOneConnectionItAlreadyHas()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineSheet>(p => Parameters(p, new State(true, true)));

        cut.Render(p => Parameters(p, new State(true, true)));

        AssertBoth(module, cut, new State(true, true), "idle re-render");
        Assert.Single(module.Invocations["connect"]);
    }

    /// <summary>
    /// The trigger element is an argument too, and sheet.js captures it once (it restores focus to
    /// it on dispose and exports no update()). A keyed trigger swap while the sheet is open replaces
    /// that element, so the connection must be re-established on the element rendered now.
    /// Companion row to the modality flip above; together they cover every mutable argument
    /// sheet.js is handed (portal and content are the element arguments the identity property
    /// already drives, and the callback is created once).
    /// </summary>
    [Fact]
    public void ATriggerSwapWhileOpenReachesTheConnection()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineSheet>(p => p
            .Add(c => c.Open, true).Add(c => c.Modal, true).Add(c => c.ChildContent, Content()));

        cut.Render(p => p
            .Add(c => c.Open, true).Add(c => c.Modal, true).Add(c => c.ChildContent, Content(triggerKey: 1)));
        // The trigger publishes its ElementReference into the context from its OWN OnAfterRender,
        // which runs after the content's, so the new element is visible to the connect site from
        // the next render onwards. That publication order is the trigger/context seam's, unchanged
        // by this slice; what is asserted here is that the connect site then acts on it.
        cut.Render(p => p
            .Add(c => c.Open, true).Add(c => c.Modal, true).Add(c => c.ChildContent, Content(triggerKey: 1)));

        var trigger = ConnectionIdentity.ElementId(cut.FindComponent<HarborlineSheetTrigger>().Instance, "trigger");
        Assert.Equal(trigger, ((ElementReference)module.Invocations["connect"].Last().Arguments[2]!).Id);
        AssertBoth(module, cut, new State(true, true), "trigger swap");
    }
}
