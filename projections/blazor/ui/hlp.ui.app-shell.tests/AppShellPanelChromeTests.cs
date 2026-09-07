using System.Globalization;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Contracts.Authorization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Blazor half of conformance/hlp.ui.app-shell/chrome-v1.json — the two earned header forms, the body-owned
/// slots with the body as the single scroll region, the slot-derived panel minimum, and pop out as a declared
/// action the host confirms. The React mirror is AppShell.panel-chrome.test.tsx and reads the same file.
/// </summary>
public sealed class AppShellPanelChromeTests : BunitContext
{
    private static readonly JsonElement Fixture = Load();
    private static JsonElement Load()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../conformance/hlp.ui.app-shell/chrome-v1.json"));
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        // Missing fixture names fail loudly rather than silently skipping a rule.
        foreach (var name in new[] { "panels", "slotHeights", "slotOrder", "headerForms", "openItem", "sheetHeader", "singleScroll", "minimums", "overfullPane", "popOut", "chordScope", "passThroughKeys", "fractionalDragRounding" })
            if (!root.TryGetProperty(name, out _)) throw new InvalidOperationException($"chrome-v1.json is missing \"{name}\"");
        return root;
    }

    private static readonly PackPanelDeclaration[] Panels = JsonSerializer.Deserialize<PackPanelDeclaration[]>(Fixture.GetProperty("panels").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static PackPanelDeclaration Declaration(string id) => Panels.Single(panel => panel.Id == id);
    private bool mediaRegistered;
    private static string[] Ids(JsonElement array) => array.EnumerateArray().Select(value => value.GetString()!).ToArray();

    private IRenderedComponent<HarborlineAppShell> Mount(string[] open, int viewportWidth = 1800,
        Func<PackPanelDeclaration, ShellPanelOpenItem?>? openItem = null, EventCallback<ShellPanelPopOutRequest> popOut = default,
        int? endPanelWidth = null, EventCallback<int> endPanelWidthChanged = default)
    {
        if (!mediaRegistered) { Services.AddSingleton<IMediaQueryObserver>(new Media(viewportWidth)); mediaRegistered = true; }
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction", _ => true).SetResult(1);
        module.Setup<double>("measure", _ => true).SetResult(900d);
        return Render<HarborlineAppShell>(p => p.Add(x => x.ShellId, "chrome")
            .Add(x => x.Navigation, new PackNavigationDeclaration([new("ops", "Ops")], PanelSet: Panels))
            .Add(x => x.RoleVocabulary, RoleVocabulary.FromApi([])).Add(x => x.HeldRoles, new HeldRoleSet([]))
            .Add(x => x.ChildContent, b => b.AddContent(0, "Body")).Add(x => x.LargeBarCapable, viewportWidth >= 1200)
            .Add(x => x.RailCapable, true).Add(x => x.ShellInlineSize, (double)viewportWidth)
            .Add(x => x.OpenPanelIds, open).Add(x => x.PanelOpenItem, openItem).Add(x => x.PanelPoppedOut, popOut)
            .Add(x => x.PanelContent, panel => (RenderFragment)(b => b.AddContent(0, panel.Id)))
            .Add(x => x.EndPanel, endPanelWidth is null ? null : (RenderFragment)(b => b.AddContent(0, "End")))
            .Add(x => x.EndPanelOpen, endPanelWidth is not null).Add(x => x.EndPanelWidth, endPanelWidth ?? 400)
            .Add(x => x.EndPanelWidthChanged, endPanelWidthChanged)
            .Add(x => x.PanelToolbar, panel => (RenderFragment)(b => b.AddContent(0, "toolbar"))));
    }

    private static AngleSharp.Dom.IElement Panel(IRenderedComponent<HarborlineAppShell> cut, string id) => cut.Find($"[data-shell-panel-id=\"{id}\"]");
    private static string[] Affordances(AngleSharp.Dom.IElement panel) => panel.QuerySelectorAll("[data-panel-affordances] button")
        .Select(button => button.HasAttribute("data-panel-overflow") ? "overflow" : button.HasAttribute("data-panel-pop-out") ? "pop-out" : button.HasAttribute("data-panel-expand") ? "expand" : "close").ToArray();

    private static AngleSharp.Dom.IElement Header(AngleSharp.Dom.IElement panel) => panel.QuerySelector(".hl-app-shell__end-panel-header")!;
    private static AngleSharp.Dom.IElement Body(AngleSharp.Dom.IElement panel) => panel.QuerySelector("[data-shell-panel-body-scroll]")!;

    [Fact(DisplayName = "every panel takes the header form it earned and the invariant affordance group")]
    public void HeaderFormsAreEarned()
    {
        var rows = Fixture.GetProperty("headerForms").EnumerateArray().ToArray();
        var cut = Mount(rows.Select(row => row.GetProperty("panelId").GetString()!).ToArray());
        foreach (var row in rows)
        {
            var id = row.GetProperty("panelId").GetString()!;
            var declaration = Declaration(id);
            Assert.Equal(row.GetProperty("expectedForm").GetString(), ShellChromeContract.PanelHeaderForm(declaration));
            Assert.Equal(row.GetProperty("earnsOverflow").GetBoolean(), ShellChromeContract.PanelEarnsOverflow(declaration));
            Assert.Equal(row.GetProperty("expectedForm").GetString(), Panel(cut, id).GetAttribute("data-panel-header-form"));
            Assert.Equal(Ids(row.GetProperty("expectedAffordances")), Affordances(Panel(cut, id)));
        }
    }

    [Fact(DisplayName = "the slots render in declared order with the body the only scrolling one")]
    public void SlotsRenderInOrderWithOneScrollRegion()
    {
        var cut = Mount(["documents"]);
        var panel = Panel(cut, "documents");
        var slots = panel.Children.Select(child => child.ClassName!.Contains("header") ? "header" : child.ClassName!.Contains("toolbar") ? "toolbar" : child.ClassName!.Contains("body") ? "body" : "footer").ToArray();
        Assert.Equal(Ids(Fixture.GetProperty("slotOrder")), slots);
        Assert.Equal(Fixture.GetProperty("singleScroll").GetProperty("bodyScrollRegionsPerPanel").GetInt32(), panel.QuerySelectorAll("[data-shell-panel-body-scroll]").Length);
        Assert.Equal(Fixture.GetProperty("singleScroll").GetProperty("nestedScrollRegionsInsideBody").GetInt32(),
            panel.QuerySelectorAll("[data-shell-panel-body-scroll] [data-shell-scroll-region], [data-shell-panel-body-scroll] [data-shell-panel-body-scroll]").Length);
        // Header, toolbar and footer are pinned: none of them is a scroll region, so the body scrolls as one.
        foreach (var (slot, selector) in new[] { ("header", ".hl-app-shell__end-panel-header"), ("toolbar", "[data-shell-panel-toolbar]"), ("footer", "[data-shell-panel-footer]") })
        {
            var element = panel.QuerySelector(selector);
            Assert.True(element is not null, slot);
            Assert.False(element!.HasAttribute("data-shell-scroll-region"), slot);
            Assert.False(element.HasAttribute("data-shell-panel-body-scroll"), slot);
        }
    }

    [Fact(DisplayName = "a panel minimum is the content-sized slots above the body, never the body")]
    public void PanelMinimumComesFromTheSlotsAboveTheBody()
    {
        var slotHeights = Fixture.GetProperty("slotHeights");
        Assert.Equal(ShellChromeContract.PanelHeaderHeight, slotHeights.GetProperty("header").GetInt32());
        Assert.Equal(ShellChromeContract.PanelToolbarHeight, slotHeights.GetProperty("toolbar").GetInt32());
        Assert.Equal(ShellChromeContract.PanelFooterHeight, slotHeights.GetProperty("footer").GetInt32());
        var rows = Fixture.GetProperty("minimums").EnumerateArray().ToArray();
        var cut = Mount(rows.Select(row => row.GetProperty("panelId").GetString()!).ToArray());
        foreach (var row in rows)
        {
            var id = row.GetProperty("panelId").GetString()!;
            var declaration = Declaration(id);
            Assert.Equal(row.GetProperty("slotMinimum").GetInt32(), ShellChromeContract.PanelSlotMinimum(declaration));
            Assert.Equal(row.GetProperty("expectedMinimum").GetInt32(), ShellChromeContract.PanelMinimumHeight(declaration));
            Assert.Contains($"min-block-size:{row.GetProperty("expectedMinimum").GetInt32()}px", Panel(cut, id).GetAttribute("style"));
            // The one flexible slot is never given a floor of its own: that is what starves it.
            Assert.DoesNotContain("min-block-size", Panel(cut, id).QuerySelector("[data-shell-panel-body-scroll]")!.GetAttribute("style") ?? "");
        }
    }


    /// <summary>
    /// ONE minimum per panel. Every reader of a panel height floor - the rendered min-block-size, the pane's
    /// published sum, the intra-pane divider and the splitter-tree height clamp - reads the SAME slot-derived
    /// derivation, never the raw declared number. Every panel kind x every slot combination x every clamp shape.
    /// </summary>
    [Fact(DisplayName = "one derived minimum per panel is what every height-floor reader reads")]
    public void OneDerivedMinimumIsReadEverywhere()
    {
        var combos = (from declared in new[] { 0, 40, 64, 65, 66, 92, 93, 180, 300 }
                      from footer in new PackPanelFooter?[] { null, new("Claim", "panels.f") }
                      select new PackPanelDeclaration($"p{declared}{(footer is null ? "" : "f")}", "b", "s", 360, declared, false, Footer: footer)).ToArray();
        var containers = combos.Select(panel => new DockPanelContainer(panel, "docked")).ToArray();
        static DockPane Pane(params PackPanelDeclaration[] panels) => new(panels, panels.Select(_ => 1d / panels.Length).ToArray());
        foreach (var panel in combos)
        {
            var derived = Math.Max(panel.MinimumHeight, ShellChromeContract.PanelHeaderHeight + ShellChromeContract.PanelToolbarHeight + (panel.Footer is null ? 0 : ShellChromeContract.PanelFooterHeight));
            Assert.Equal(derived, ShellChromeContract.PanelMinimumHeight(panel));
            Assert.Equal(derived, DockLayout.NodeMinimum(Pane(panel), false, containers));
            // A sheet contributes nothing to the clamp, whatever it declares.
            Assert.Equal(0d, DockLayout.NodeMinimum(Pane(panel), false, [new DockPanelContainer(panel, "side-sheet")]));
        }
        foreach (var first in combos)
            foreach (var second in combos)
            {
                var sum = (double)(ShellChromeContract.PanelMinimumHeight(first) + ShellChromeContract.PanelMinimumHeight(second));
                Assert.Equal(sum, DockLayout.NodeMinimum(Pane(first, second), false, containers));
                // A vertical split stacks, so its floor is the sum; a horizontal one sits side by side, so it is the max.
                Assert.Equal(sum, DockLayout.NodeMinimum(new DockSplit("vertical", .5, Pane(first), Pane(second)), false, containers));
                Assert.Equal((double)Math.Max(ShellChromeContract.PanelMinimumHeight(first), ShellChromeContract.PanelMinimumHeight(second)),
                    DockLayout.NodeMinimum(new DockSplit("horizontal", .5, Pane(first), Pane(second)), false, containers));
            }
        // The fixture's discriminating panel: declared 40, slot-derived 93, and the clamp must read 93.
        var row = Fixture.GetProperty("minimums").EnumerateArray().Single(candidate => candidate.GetProperty("panelId").GetString() == "tiny");
        var tiny = Declaration("tiny");
        var expected = row.GetProperty("expectedMinimum").GetInt32();
        Assert.Equal(expected, ShellChromeContract.PanelMinimumHeight(tiny));
        Assert.Equal((double)expected, DockLayout.NodeMinimum(Pane(tiny), false, [new DockPanelContainer(tiny, "docked")]));
        var cut = Mount(["tiny"]);
        Assert.Contains($"min-block-size:{expected}px", Panel(cut, "tiny").GetAttribute("style"));
        Assert.Equal(expected.ToString(CultureInfo.InvariantCulture), cut.Find(".hl-app-shell__dock-pane").GetAttribute("data-pane-minimum-sum"));
    }

    /// <summary>
    /// The affordance group is the SHELL's and it lives in the header, so the pop-out chord is the header's
    /// route in both shells. The browser default for the chord is suppressed in dock-divider.js
    /// (observePanelChord) because Blazor's :preventDefault directive is a render-time flag and cannot vary
    /// per key; conformance/hlp.ui.app-shell/divider-browser.test.mjs asserts that half for this lane.
    /// </summary>
    [Fact(DisplayName = "the pop-out chord is handled from the header affordance group only")]
    public void ChordIsHandledFromTheHeaderOnly()
    {
        var spec = Fixture.GetProperty("chordScope");
        var id = spec.GetProperty("panelId").GetString()!;
        foreach (var row in spec.GetProperty("rows").EnumerateArray())
        {
            var requests = new List<ShellPanelPopOutRequest>();
            var cut = Mount([id], popOut: EventCallback.Factory.Create<ShellPanelPopOutRequest>(this, requests.Add));
            var panel = Panel(cut, id);
            var target = row.GetProperty("from").GetString() == "header" ? Header(panel) : Body(panel);
            target.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = row.GetProperty("key").GetString()!, ShiftKey = row.GetProperty("shiftKey").GetBoolean() });
            Assert.Equal(row.GetProperty("expectedRequests").GetInt32(), requests.Count);
        }
    }

    [Fact(DisplayName = "an overfull pane scrolls instead of starving a body")]
    public void OverfullPaneScrolls()
    {
        var spec = Fixture.GetProperty("overfullPane");
        var open = Ids(spec.GetProperty("openPanelIds"));
        var cut = Mount(open);
        var pane = cut.Find(".hl-app-shell__dock-pane");
        Assert.Equal(spec.GetProperty("expectedPaneScrolls").GetBoolean(), pane.HasAttribute("data-shell-pane-scroll"));
        Assert.Equal(spec.GetProperty("expectedPaneMinimumSum").GetInt32().ToString(), pane.GetAttribute("data-pane-minimum-sum"));
        Assert.Equal(spec.GetProperty("expectedPanelMinimums").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
            open.Select(id => ShellChromeContract.PanelMinimumHeight(Declaration(id))).ToArray());
    }

    [Fact(DisplayName = "the tree toggle and the open item chip appear only while an item is open")]
    public void ToggleAndChipFollowTheOpenItem()
    {
        var spec = Fixture.GetProperty("openItem");
        var id = spec.GetProperty("panelId").GetString()!;
        var closed = 0;
        var item = new ShellPanelOpenItem(spec.GetProperty("itemId").GetString()!, spec.GetProperty("itemLabel").GetString()!, () => closed++);
        var cut = Mount([id], openItem: panel => panel.Id == id ? item : null);
        var chip = Panel(cut, id).QuerySelector("[data-panel-item-chip]");
        Assert.Equal(spec.GetProperty("expectedChipVisible").GetBoolean(), chip is not null);
        Assert.Equal(item.Id, chip!.GetAttribute("data-item-id"));
        Panel(cut, id).QuerySelector("[data-panel-tree-toggle]")!.Click();
        Assert.Equal(spec.GetProperty("expectedTreeHiddenAfterToggle").GetBoolean() ? "hidden" : "shown", Panel(cut, id).GetAttribute("data-panel-tree"));
        Panel(cut, id).QuerySelector("[data-panel-item-close]")!.Click();
        Assert.Equal(1, closed);
        // "Closing the document brings the tree back, even if it was hidden."
        cut.Render(p => p.Add(x => x.PanelOpenItem, _ => null));
        Assert.Equal(spec.GetProperty("expectedTreeShownAfterItemClosed").GetBoolean() ? "shown" : "hidden", Panel(cut, id).GetAttribute("data-panel-tree"));
    }

    [Fact(DisplayName = "a sheet header carries the close affordance only")]
    public void SheetHeaderIsCloseOnly()
    {
        var spec = Fixture.GetProperty("sheetHeader");
        var id = spec.GetProperty("panelId").GetString()!;
        var cut = Mount([id], spec.GetProperty("viewportWidth").GetInt32());
        Assert.NotEqual("docked", Panel(cut, id).GetAttribute("data-shell-container-kind"));
        Assert.Equal(Ids(spec.GetProperty("expectedAffordances")), Affordances(Panel(cut, id)));
    }

    [Fact(DisplayName = "pop out asks the host and the panel stays docked until the host confirms")]
    public void PopOutIsADeclaredRequest()
    {
        var spec = Fixture.GetProperty("popOut");
        var id = spec.GetProperty("panelId").GetString()!;
        var requests = new List<ShellPanelPopOutRequest>();
        var cut = Mount(Ids(spec.GetProperty("openPanelIds")), popOut: EventCallback.Factory.Create<ShellPanelPopOutRequest>(this, requests.Add));
        // The request carries the state the persistence seam would emit, so a width the user just set travels with it.
        for (var press = 0; press < spec.GetProperty("widthArrowLeftPresses").GetInt32(); press++)
            cut.Find(".hl-app-shell__dock-resize").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowLeft" });
        Panel(cut, id).QuerySelector("[data-panel-pop-out]")!.Click();
        Assert.Equal(spec.GetProperty("expectedRequestCountAfterButton").GetInt32(), requests.Count);
        var payload = spec.GetProperty("expectedPayload");
        Assert.Equal(payload.GetProperty("panelId").GetString(), requests[0].PanelId);
        Assert.Equal(Ids(payload.GetProperty("openPanelIds")), requests[0].State.OpenPanelIds);
        Assert.Equal(payload.GetProperty("widths").EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetDouble()), new Dictionary<string, double>(requests[0].State.Widths));
        Header(Panel(cut, id)).KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        Assert.Equal(spec.GetProperty("expectedRequestCountAfterShortcut").GetInt32(), requests.Count);
        // The shell opened no window and undocked nothing: the panel is still where it was.
        Assert.Equal(spec.GetProperty("expectedStillDockedAfterRequest").GetBoolean() ? "docked" : "side-sheet", Panel(cut, id).GetAttribute("data-shell-container-kind"));
        Assert.Equal(Ids(spec.GetProperty("expectedOpenPanelIdsAfterRequest")), cut.FindAll("[data-shell-panel-id]").Select(e => e.GetAttribute("data-shell-panel-id")!).ToArray());
        cut.Render(p => p.Add(x => x.OpenPanelIds, Ids(spec.GetProperty("expectedOpenPanelIdsAfterHostConfirms"))));
        Assert.Equal(Ids(spec.GetProperty("expectedOpenPanelIdsAfterHostConfirms")), cut.FindAll("[data-shell-panel-id]").Select(e => e.GetAttribute("data-shell-panel-id")!).ToArray());
    }

    [Fact(DisplayName = "a panel that did not declare pop-out has no pop-out route at all")]
    public void UndeclaredPanelHasNoPopOutRoute()
    {
        var spec = Fixture.GetProperty("popOut");
        var id = spec.GetProperty("undeclaredPanelId").GetString()!;
        var requests = new List<ShellPanelPopOutRequest>();
        var cut = Mount([id], popOut: EventCallback.Factory.Create<ShellPanelPopOutRequest>(this, requests.Add));
        Assert.Null(Panel(cut, id).QuerySelector("[data-panel-pop-out]"));
        Header(Panel(cut, id)).KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", ShiftKey = true });
        Assert.Empty(requests);
    }

    [Fact(DisplayName = "every fixture-owned pass-through key leaves the panel alone")]
    public void PassThroughKeysAreNotConsumed()
    {
        var spec = Fixture.GetProperty("popOut");
        var id = spec.GetProperty("panelId").GetString()!;
        var requests = new List<ShellPanelPopOutRequest>();
        var cut = Mount([id], popOut: EventCallback.Factory.Create<ShellPanelPopOutRequest>(this, requests.Add));
        foreach (var key in Ids(Fixture.GetProperty("passThroughKeys")))
            Panel(cut, id).KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = key });
        Assert.Empty(requests);
    }

    [Fact(DisplayName = "a fractional drag rounds half-up on the end-panel path, the rule both shells share")]
    public void FractionalDragRoundsHalfUp()
    {
        foreach (var row in Fixture.GetProperty("fractionalDragRounding").GetProperty("rows").EnumerateArray())
        {
            var start = row.GetProperty("start").GetDouble();
            var delta = row.GetProperty("deltaX").GetDouble();
            var expected = row.GetProperty("expected").GetInt32();
            // The rail path shares this one rule; the pure helper is asserted for both, the rendered drag for the
            // end panel, which the shell owns outright (the rail affordance sits behind the layout's own gate).
            Assert.Equal(expected, ShellChromeContract.RoundHalfUp(start + delta));
            // Each row is a fresh drag from the fixture's declared start, so one row cannot inherit another's width.
            var widths = new List<int>();
            var cut = Mount([], endPanelWidth: (int)start, endPanelWidthChanged: EventCallback.Factory.Create<int>(this, widths.Add));
            var separator = cut.Find(".hl-app-shell__end-panel-resize");
            separator.PointerDown(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientX = 0 });
            separator.PointerMove(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientX = -delta });
            separator.PointerUp(new Microsoft.AspNetCore.Components.Web.PointerEventArgs { ClientX = -delta });
            Assert.Equal(expected, widths[^1]);
        }
    }

    private sealed class Media(int width) : IMediaQueryObserver
    { public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> callback, CancellationToken cancellationToken = default) => ValueTask.FromResult<IMediaQuerySubscription>(new Subscription(query, width)); }
    private sealed class Subscription(string query, int width) : IMediaQuerySubscription
    {
        public string Query => query;
        // A query with no min-width in it (the layout observes a couple) is not a width gate: answer true.
        public bool Matches { get { var digits = new string(query.Where(char.IsDigit).ToArray()); return digits.Length == 0 || width >= int.Parse(digits, CultureInfo.InvariantCulture); } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
