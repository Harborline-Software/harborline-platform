using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// The property every JavaScript-connected element in this component obeys: a connection is keyed
/// to the ELEMENT it was handed, never to the boolean that decides whether the element is rendered.
/// For each connected element, over every render-branch transition the component has:
///   * every connect is handed a distinct element (no element is connected twice);
///   * while the element is rendered, the newest connection holds the element rendered NOW;
///   * every superseded connection was disposed, and none is left live once the element is gone.
/// The render predicate is only a proxy for element identity, and the rail/modal branch swap is
/// where that proxy lies: the boolean stays true while the whole subtree is replaced.
/// </summary>
public sealed class DetailPanelConnectionIdentityTests : BunitContext
{
    private sealed class RailMedia : IMediaQueryObserver
    {
        public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> onChanged, CancellationToken cancellationToken = default)
            => new(new Subscription(query));

        private sealed class Subscription(string query) : IMediaQuerySubscription
        {
            public string Query => query;
            public bool Matches => true;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private const string TabsModule = "./_content/Harborline.UIAdapters.Blazor/detail-panel-tabs.js";
    private const string DialogModule = "./_content/Harborline.UIAdapters.Blazor/dialog.js";

    public DetailPanelConnectionIdentityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IMediaQueryObserver>(new RailMedia());
    }

    private static DetailPanelSubject Root => new("PMP-0412", "Pump", "/p/PMP-0412",
        [new DetailPanelFacet("overview", "Overview", 1), new DetailPanelFacet("attachments", "Attachments", 3)],
        [new DetailPanelRelation("r1", "Related work order", "WO-8841")]);

    private static DetailPanelSubject Level2 => new("WO-8841", "Work order", "/p/WO-8841",
        [new DetailPanelFacet("summary", "Summary", 2), new DetailPanelFacet("history", "History", 5)], []);

    /// <summary>One render state: every axis that moves a connected element in or out.</summary>
    private readonly record struct State(bool Open, bool Rail, bool Pushed)
    {
        public override string ToString() => $"open={Open},rail={Rail},pushed={Pushed}";
    }

    private static readonly State[] States =
    [
        new(true, true, false), new(true, true, true),
        new(true, false, false), new(true, false, true),
        new(false, true, false), new(false, false, false),
    ];

    private static void Parameters(ComponentParameterCollectionBuilder<HarborlineDetailPanel> parameters, State state) => parameters
        .Add(c => c.Label, "Inspector")
        .Add(c => c.Open, state.Open)
        .Add(c => c.RailCapable, state.Rail)
        .Add(c => c.Subject, Root)
        .Add(c => c.Pushed, state.Pushed ? Level2 : null)
        .Add(c => c.ChildContent, (RenderFragment)(builder => builder.AddContent(0, "body")));

    private static string ElementId(HarborlineDetailPanel component, string field) =>
        ((ElementReference)typeof(HarborlineDetailPanel)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component)!).Id;

    /// <summary>The invariant, asserted for one connected element after one render.</summary>
    private static void AssertConnectionTracksElement(
        BunitJSInterop module, HarborlineDetailPanel component, string field, bool rendered, string where)
    {
        var connects = module.Invocations["connect"]
            .Select(invocation => ((ElementReference)invocation.Arguments[0]!).Id).ToList();
        var disposes = module.Invocations["dispose"].Count();

        Assert.True(connects.Count == connects.Distinct().Count(), $"{where}: an element was connected twice");
        if (rendered)
        {
            Assert.True(connects.Count > 0, $"{where}: the rendered element was never connected");
            Assert.True(ElementId(component, field) == connects[^1],
                $"{where}: the live connection holds a replaced element, not the one rendered now");
            Assert.True(connects.Count - 1 == disposes, $"{where}: a superseded connection was not disposed");
        }
        else
        {
            Assert.True(connects.Count == disposes, $"{where}: a connection outlived its element");
        }
    }

    private static void AssertBoth(BunitJSInterop tabs, BunitJSInterop dialog, HarborlineDetailPanel component, State state, string where)
    {
        AssertConnectionTracksElement(tabs, component, "TabList", state.Open, $"tabs @ {where}");
        AssertConnectionTracksElement(dialog, component, "Overlay", state.Open && !state.Rail, $"dialog @ {where}");
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
                context.Services.AddSingleton<IMediaQueryObserver>(new RailMedia());
                var tabs = context.JSInterop.SetupModule(TabsModule);
                var dialog = context.JSInterop.SetupModule(DialogModule);

                var cut = context.Render<HarborlineDetailPanel>(p => Parameters(p, from));
                AssertBoth(tabs, dialog, cut.Instance, from, $"{from} (initial)");

                cut.Render(p => Parameters(p, to));
                AssertBoth(tabs, dialog, cut.Instance, to, $"{from} -> {to}");

                // Returning to the start branch is a NEW element and must be a new connection.
                cut.Render(p => Parameters(p, from));
                AssertBoth(tabs, dialog, cut.Instance, from, $"{from} -> {to} -> {from}");
            }
        }
    }

    [Fact]
    public void AFacetChangeKeepsTheOneConnectionItAlreadyHas()
    {
        var tabs = JSInterop.SetupModule(TabsModule);
        using var cut = Render<HarborlineDetailPanel>(p => Parameters(p, new State(true, true, false)));

        cut.Find("[role='tab'][data-facet-id='attachments']").Click();

        AssertConnectionTracksElement(tabs, cut.Instance, "TabList", true, "facet change");
        Assert.Single(tabs.Invocations["connect"]);
    }

    /// <summary>
    /// The reviewer's killing case on its own: the viewport crosses the rail query, the panel swaps
    /// branch, and the tab bar the module holds becomes a detached node.
    /// </summary>
    [Fact]
    public void ARailToModalFlipReconnectsTheTabBarAndDisposesThePreviousConnection()
    {
        var tabs = JSInterop.SetupModule(TabsModule);
        using var cut = Render<HarborlineDetailPanel>(p => Parameters(p, new State(true, true, false)));
        var before = ElementId(cut.Instance, "TabList");

        cut.Render(p => Parameters(p, new State(true, false, false)));

        var after = ElementId(cut.Instance, "TabList");
        Assert.NotEqual(before, after);
        Assert.Equal(
            new[] { before, after },
            tabs.Invocations["connect"].Select(invocation => ((ElementReference)invocation.Arguments[0]!).Id));
        Assert.Single(tabs.Invocations["dispose"]);
    }
}
