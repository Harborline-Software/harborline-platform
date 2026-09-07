using Bunit;
using Harborline.UIAdapters.Blazor.Components.Buttons;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Connection identity for hlp.ui.data-export-button. <c>Formats.Count == 1</c> is itself a render
/// branch (the direct-export button, which has no root element at all) and the connect site returns
/// early above the connect/dispose pair when the count drops to one, so a live connection survives
/// <c>RootElement</c> being destroyed. Every (format count, open) transition is driven here, in both
/// directions.
/// </summary>
public sealed class DataExportButtonConnectionIdentityTests
{
    private const string Module = "./_content/Harborline.UIAdapters.Blazor/data-export-button.js";

    /// <summary>Open is unobservable with a single format — there is no menu on that branch.</summary>
    private readonly record struct State(int Formats, bool Open)
    {
        public override string ToString() => $"formats={Formats},open={Open}";
    }

    private static readonly State[] States =
    [
        new(2, false), new(2, true), new(1, false),
    ];

    private static void Parameters(ComponentParameterCollectionBuilder<HarborlineDataExportButton> parameters, State state) => parameters
        .Add(c => c.Formats, state.Formats == 1 ? [ExportFormat.Csv] : [ExportFormat.Csv, ExportFormat.Xlsx])
        .Add(c => c.OnExport, EventCallback.Factory.Create<ExportFormat>(new object(), _ => { }));

    /// <summary>Render the state, then drive the menu open or closed through the real trigger.</summary>
    private static void Drive(IRenderedComponent<HarborlineDataExportButton> cut, State state)
    {
        cut.Render(p => Parameters(p, state));
        if (state.Formats == 1) return;
        if (state.Open != cut.FindAll("[role=menu]").Count > 0) cut.Find("button").Click();
        Assert.Equal(state.Open, cut.FindAll("[role=menu]").Count > 0);
    }

    private static void AssertRoot(BunitJSInterop module, IRenderedComponent<HarborlineDataExportButton> cut, State state, string where)
    {
        var connected = state.Formats > 1 && state.Open;
        ConnectionIdentity.Assert(module, 0, connected ? ConnectionIdentity.ElementId(cut.Instance, "RootElement") : null, $"root @ {where}");
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

                var cut = context.Render<HarborlineDataExportButton>(p => Parameters(p, new State(from.Formats, false)));
                Drive(cut, from);
                AssertRoot(module, cut, from, $"{from} (initial)");

                Drive(cut, to);
                AssertRoot(module, cut, to, $"{from} -> {to}");

                Drive(cut, from);
                AssertRoot(module, cut, from, $"{from} -> {to} -> {from}");
            }
        }
    }

    /// <summary>
    /// The killing case: the menu is open, then the host drops to a single format. The whole
    /// two-branch root is replaced by the direct-export button, so the connection must be gone.
    /// </summary>
    [Fact]
    public void DroppingToASingleFormatDisposesTheConnectionThatHeldTheDestroyedRoot()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineDataExportButton>(p => Parameters(p, new State(2, false)));
        Drive(cut, new State(2, true));
        Assert.Single(module.Invocations["connect"]);

        cut.Render(p => Parameters(p, new State(1, false)));

        Assert.Single(cut.FindAll(".hl-data-export--direct"));
        Assert.Single(module.Invocations["dispose"]);
    }

    /// <summary>A re-render that replaces nothing must not reconnect.</summary>
    [Fact]
    public void AReRenderWhileOpenKeepsTheOneConnectionItAlreadyHas()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineDataExportButton>(p => Parameters(p, new State(2, false)));
        Drive(cut, new State(2, true));

        cut.Render(p => Parameters(p, new State(2, true)));

        AssertRoot(module, cut, new State(2, true), "idle re-render");
        Assert.Single(module.Invocations["connect"]);
    }

    /// <summary>
    /// The mutable argument row for this module, stated by enumeration: <c>connect(root, trigger,
    /// callback)</c> is handed no value argument at all, so there is nothing that can go stale the
    /// way the popover's options bag did. Both element arguments live in the same render branch and
    /// are covered by the pair sweep above; the callback is created once. This pins the arity, so
    /// the day someone adds a value argument the row stops being vacuous and has to be driven.
    /// GREEN at baseline.
    /// </summary>
    [Fact]
    public void ConnectIsHandedNoValueArgumentThatCouldGoStale()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var module = context.JSInterop.SetupModule(Module);
        var cut = context.Render<HarborlineDataExportButton>(p => Parameters(p, new State(2, false)));
        Drive(cut, new State(2, true));

        var arguments = module.Invocations["connect"].Last().Arguments;
        Assert.Equal(3, arguments.Count);
        Assert.IsType<ElementReference>(arguments[0]);
        Assert.IsType<ElementReference>(arguments[1]);
        Assert.IsType<DotNetObjectReference<HarborlineDataExportButton>>(arguments[2]);
    }
}
