using Bunit;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Microsoft.AspNetCore.Components;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Connection identity for hlp.ui.window. The window hands window.js three elements — the portal,
/// the window section and the title bar — and connects them on <c>connection is null</c> alone, with
/// no render predicate at all, while the markup swaps branches on <c>Modal</c> (the overlay) and on
/// <c>EffectiveState != Minimized</c> (the body and the resize handles). Every one of those
/// transitions is driven here, in both directions.
/// </summary>
public sealed class WindowConnectionIdentityTests
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/window.js";

    private readonly record struct State(bool Modal, HarborlineWindowState WindowState)
    {
        public override string ToString() => $"modal={Modal},state={WindowState}";
    }

    private static readonly State[] States =
    [
        new(false, HarborlineWindowState.Default), new(false, HarborlineWindowState.Minimized), new(false, HarborlineWindowState.Maximized),
        new(true, HarborlineWindowState.Default), new(true, HarborlineWindowState.Minimized), new(true, HarborlineWindowState.Maximized),
    ];

    private static void Parameters(ComponentParameterCollectionBuilder<HarborlineWindow> parameters, State state) => parameters
        .Add(c => c.AccessibleLabel, "Inspector")
        .Add(c => c.CloseLabel, "Close").Add(c => c.MinimizeLabel, "Minimize").Add(c => c.MaximizeLabel, "Maximize")
        .Add(c => c.RestoreLabel, "Restore").Add(c => c.ResizeWidthLabel, "Width").Add(c => c.ResizeHeightLabel, "Height")
        .Add(c => c.ResizeBothLabel, "Both")
        .Add(c => c.Modal, state.Modal)
        .Add(c => c.State, state.WindowState)
        .Add(c => c.ChildContent, (RenderFragment)(builder => builder.AddContent(0, "body")));

    /// <summary>All three connected elements are unconditional, so all three are always rendered.</summary>
    private static void AssertAll(BunitJSInterop module, HarborlineWindow component, string where)
    {
        ConnectionIdentity.Assert(module, 0, ConnectionIdentity.ElementId(component, "portal"), $"portal @ {where}");
        ConnectionIdentity.Assert(module, 1, ConnectionIdentity.ElementId(component, "window"), $"window @ {where}");
        ConnectionIdentity.Assert(module, 2, ConnectionIdentity.ElementId(component, "titleBar"), $"titleBar @ {where}");
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

                var cut = context.Render<HarborlineWindow>(p => Parameters(p, from));
                AssertAll(module, cut.Instance, $"{from} (initial)");

                cut.Render(p => Parameters(p, to));
                AssertAll(module, cut.Instance, $"{from} -> {to}");

                cut.Render(p => Parameters(p, from));
                AssertAll(module, cut.Instance, $"{from} -> {to} -> {from}");
            }
        }
    }

    [Fact]
    public void AModalFlipKeepsTheConnectionOnTheElementsRenderedNow()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineWindow>(p => Parameters(p, new State(false, HarborlineWindowState.Default)));
        var before = ConnectionIdentity.ElementId(cut.Instance, "window");

        cut.Render(p => Parameters(p, new State(true, HarborlineWindowState.Default)));

        AssertAll(module, cut.Instance, "modal flip");
        var after = ConnectionIdentity.ElementId(cut.Instance, "window");
        // Whether the flip replaces the section is Blazor's business; the connection must hold
        // whichever element is rendered now, and must have disposed the other exactly once.
        Assert.Equal(after == before ? 1 : 2, module.Invocations["connect"].Count());
        Assert.Equal(after == before ? 0 : 1, module.Invocations["dispose"].Count());
    }

    /// <summary>A re-render that replaces nothing must not reconnect: the fix must not degenerate.</summary>
    [Fact]
    public void AResizeKeepsTheOneConnectionItAlreadyHas()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineWindow>(p => Parameters(p, new State(false, HarborlineWindowState.Default)));

        cut.Find("[data-resize-edge='e']").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowRight" });

        AssertAll(module, cut.Instance, "resize");
        Assert.Single(module.Invocations["connect"]);
    }

    /// <summary>
    /// The mutable argument row for this module. window.js is handed one value argument — the
    /// options bag (draggable, resizable, modal, autoFocus, geometry) — and the component already
    /// pushes every change through the module's own <c>update()</c> export on every render, so a
    /// change while connected reaches JS without re-establishing the connection. GREEN at baseline;
    /// this pins it. The three element arguments are unconditional and driven by the pair sweep
    /// above, and the callback is created once.
    /// </summary>
    [Fact]
    public void AnOptionChangedWhileConnectedIsPushedThroughUpdateWithoutReconnecting()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineWindow>(p => Parameters(p, new State(true, HarborlineWindowState.Default)));

        cut.Render(p => Parameters(p, new State(false, HarborlineWindowState.Default)));

        var options = module.Invocations["update"].Last().Arguments[0]!;
        Assert.Equal(false, options.GetType().GetProperty("modal")!.GetValue(options));
        Assert.Single(module.Invocations["connect"]);
        Assert.Empty(module.Invocations["dispose"]);
        AssertAll(module, cut.Instance, "modal changed while connected");
    }
}
