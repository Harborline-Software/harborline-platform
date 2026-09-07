using System.Text.Json;
using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Harborline.Contracts.Authorization;
using Harborline.UIAdapters.Blazor.Browser;
using Harborline.UIAdapters.Blazor.Components.Layout;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;

/// <summary>
/// Blazor half of conformance/hlp.ui.app-shell/persistence-v1.json. Every row is replayed through the
/// rendered shell; the remembered widths are read back from the one dock-state seam, never from internals.
/// The React mirror is AppShell.persistence.test.tsx and reads the same file.
/// </summary>
public sealed class AppShellPersistenceTests : BunitContext
{
    private static readonly JsonElement Fixture = Load();
    private static JsonElement Load()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../conformance/hlp.ui.app-shell/persistence-v1.json"));
        var root = JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
        // Missing fixture names fail loudly rather than silently skipping a rule.
        foreach (var name in new[] { "panels", "rememberedWidthCases", "roundTrip", "staleOrMalformed", "dockWidthProperty", "dockPlacementProperty", "dockTreeRetentionProperty", "dockRestoreMeasurementProperty", "dockSettlementProperty" })
            if (!root.TryGetProperty(name, out _)) throw new InvalidOperationException($"persistence-v1.json is missing \"{name}\"");
        return root;
    }

    private static PackPanelDeclaration[] Panels(JsonElement? declared = null) => JsonSerializer.Deserialize<PackPanelDeclaration[]>((declared ?? Fixture.GetProperty("panels")).GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private IRenderedComponent<HarborlineAppShell> Mount(int viewportWidth, JsonElement? restore, string[]? open, List<DockStateSnapshot> emitted, JsonElement? panelSet = null, bool? railCapable = null, double? measuredWidth = null, bool inlineSizeParameter = true)
    {
        Services.AddSingleton<IMediaQueryObserver>(new Media(viewportWidth));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var module = JSInterop.SetupModule("./_content/Harborline.UIAdapters.Blazor/dock-divider.js");
        module.Setup<double>("direction", _ => true).SetResult(1);
        module.Setup<double>("measure", _ => true).SetResult(900d);
        return Render<HarborlineAppShell>(p => p.Add(x => x.ShellId, "persistence")
            .Add(x => x.Navigation, new PackNavigationDeclaration([new("ops", "Ops")], PanelSet: Panels(panelSet)))
            .Add(x => x.RoleVocabulary, RoleVocabulary.FromApi([])).Add(x => x.HeldRoles, new HeldRoleSet([]))
            .Add(x => x.ChildContent, b => b.AddContent(0, "Body")).Add(x => x.LargeBarCapable, viewportWidth >= 1200).Add(x => x.RailCapable, railCapable)
            // The measured shell inline size - the same number React reads from its ResizeObserver. A fixture row
            // that names a measuredWidth is a narrow scene inside a wide viewport; otherwise the box is the viewport.
            // A row that drives the [JSInvokable] ShellMeasured seam instead leaves the parameter unset.
            .Add(x => x.ShellInlineSize, inlineSizeParameter ? measuredWidth ?? (double)viewportWidth : null)
            .Add(x => x.DefaultOpenPanelIds, open).Add(x => x.DefaultDockState, restore)
            .Add(x => x.DockStateChanged, EventCallback.Factory.Create<DockStateSnapshot>(this, emitted.Add))
            .Add(x => x.PanelContent, panel => (RenderFragment)(b => b.AddContent(0, panel.Id))));
    }

    private static double? Width(IRenderedComponent<HarborlineAppShell> cut)
    {
        var dock = cut.FindAll(".hl-app-shell__dock").FirstOrDefault();
        var style = dock?.GetAttribute("style");
        if (style is null || !style.Contains("--hl-app-shell-dock-size:")) return null;
        return double.Parse(style.Split("--hl-app-shell-dock-size:")[1].Split("px")[0], CultureInfo.InvariantCulture);
    }
    private static string[] Order(IRenderedComponent<HarborlineAppShell> cut) => cut.FindAll("[data-shell-panel-id]").Select(e => e.GetAttribute("data-shell-panel-id")!).ToArray();
    private static Dictionary<string, double> Remembered(List<DockStateSnapshot> emitted) => emitted.Count == 0 ? [] : new(emitted[^1].Widths);
    private static Dictionary<string, double> Expected(JsonElement row, string name) => row.GetProperty(name).EnumerateObject().ToDictionary(entry => entry.Name, entry => entry.Value.GetDouble());

    // Three real open routes reach one seam: the inline bar action, the overflow menu below `large`, and the
    // live-panel control the bar keeps for pilot/notifications outside the declared action list.
    private static void OpenPanel(IRenderedComponent<HarborlineAppShell> cut, string id)
    {
        if (cut.FindAll($"[data-action-id='{id}'] button").Count == 0 && cut.FindAll("button[aria-label='Panels']").Count > 0) cut.Find("button[aria-label='Panels']").Click();
        var action = cut.FindAll($"[data-action-id='{id}'] button").FirstOrDefault();
        (action ?? cut.Find($"button[aria-label='{char.ToUpperInvariant(id[0])}{id[1..]}']")).Click();
    }

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void RememberedDockWidths(int index)
    {
        Assert.Equal(7, Fixture.GetProperty("rememberedWidthCases").GetArrayLength());
        var row = Fixture.GetProperty("rememberedWidthCases")[index];
        var emitted = new List<DockStateSnapshot>();
        var restore = row.TryGetProperty("restore", out var state) ? state : (JsonElement?)null;
        var open = row.GetProperty("initialOpen").EnumerateArray().Select(id => id.GetString()!).ToArray();
        var cut = Mount(row.GetProperty("viewportWidth").GetInt32(), restore, open, emitted, row.TryGetProperty("panels", out var declared) ? declared : null, row.TryGetProperty("railCapable", out var capable) ? capable.GetBoolean() : null, row.TryGetProperty("measuredWidth", out var measured) ? measured.GetDouble() : null);
        Assert.Equal(row.GetProperty("initialWidth").ValueKind == JsonValueKind.Null ? null : row.GetProperty("initialWidth").GetDouble(), Width(cut));
        Assert.Equal(Expected(row, "initialRemembered"), Remembered(emitted));
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var action = step.GetProperty("action").GetString();
            if (action == "open") OpenPanel(cut, step.GetProperty("panel").GetString()!);
            else if (action == "close") cut.Find($"button[aria-label='Close {step.GetProperty("panel").GetString()}']").Click();
            else if (action == "reset") cut.Find("button[aria-label='Reset panels']").Click();
            else
            {
                // The handle widens the dock as the pointer moves toward the content, so the fixture names only
                // the target width and the drag is derived from the width actually on screen.
                var handle = cut.Find(".hl-app-shell__dock-resize");
                var target = step.GetProperty("width").GetDouble();
                handle.PointerDown(new PointerEventArgs { ClientX = 0, PointerId = 1 });
                handle.PointerMove(new PointerEventArgs { ClientX = Width(cut)!.Value - target, PointerId = 1 });
                handle.PointerUp(new PointerEventArgs { PointerId = 1 });
            }
            var expectedWidth = step.GetProperty("expectedWidth").ValueKind == JsonValueKind.Null ? null : (double?)step.GetProperty("expectedWidth").GetDouble();
            Assert.Equal(expectedWidth, Width(cut));
            Assert.Equal(step.GetProperty("expectedOpen").EnumerateArray().Select(id => id.GetString()!).ToArray(), Order(cut));
            Assert.Equal(Expected(step, "expectedRemembered"), Remembered(emitted));
            if (step.TryGetProperty("expectedContainerKinds", out var kinds))
                Assert.Equal(kinds.EnumerateArray().Select(kind => kind.GetString()!).ToArray(), cut.FindAll("[data-shell-panel-id]").Select(e => e.GetAttribute("data-shell-container-kind")!).ToArray());
            // A sheet has no dock separator at all, so no width can reach it.
            Assert.Equal(expectedWidth is not null, cut.FindAll(".hl-app-shell__dock-resize").Count == 1);
        }
    }

    [Fact]
    public void RoundTripsADockStateIntoAnIdenticalLayout()
    {
        var row = Fixture.GetProperty("roundTrip");
        var emitted = new List<DockStateSnapshot>();
        var cut = Mount(row.GetProperty("viewportWidth").GetInt32(), row.GetProperty("state"), null, emitted);
        Assert.Equal(row.GetProperty("expectedOpen").EnumerateArray().Select(id => id.GetString()!).ToArray(), Order(cut));
        Assert.Equal(row.GetProperty("expectedWidth").GetDouble(), Width(cut));
        Assert.Equal(row.GetProperty("expectedFractions").EnumerateArray().Select(part => part.GetDouble()).ToArray(),
            cut.FindAll("[data-shell-panel-id]").Select(e => double.Parse(e.GetAttribute("style")!.Split("--hl-app-shell-panel-fraction:")[1], CultureInfo.InvariantCulture)).ToArray());
        Assert.Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<DockStateSnapshot>(row.GetProperty("state").GetRawText())), JsonSerializer.Serialize(emitted[^1]));
    }

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)]
    public void StaleOrMalformedDockStateIsRejectedWithoutThrowing(int index)
    {
        var rows = Fixture.GetProperty("staleOrMalformed");
        Assert.Equal(8, rows.GetArrayLength());
        var row = rows[index];
        var cut = Mount(1600, row.GetProperty("state"), null, []);
        Assert.Equal(row.GetProperty("expectedOpen").EnumerateArray().Select(id => id.GetString()!).ToArray(), Order(cut));
        Assert.Equal(row.GetProperty("expectedWidth").ValueKind == JsonValueKind.Null ? null : (double?)row.GetProperty("expectedWidth").GetDouble(), Width(cut));
        if (row.TryGetProperty("expectedSplitRatio", out var ratio))
            Assert.Equal(ratio.GetDouble(), double.Parse(cut.Find("[data-split-ratio]").GetAttribute("data-split-ratio")!, CultureInfo.InvariantCulture));
    }

    // The property, not a point fix. Over the whole W band - including the 840..936 rail band the four
    // hand-picked widths used to skip - the effective dock width is clamp(D, PaneMinimumWidth, W - R - F) AND the
    // shell's pane capacity is derived from that same ceiling, so rail + dock + floor <= W always holds and where
    // the ceiling cannot hold one pane nothing is docked at all. The sheet collapse is read from the MODEL's own
    // containers, never from the fixture literal.
    [Fact]
    public void TheModelClampsEveryRequestedDockWidthToTheContentFloor()
    {
        var property = Fixture.GetProperty("dockWidthProperty");
        var floor = property.GetProperty("contentFloor").GetDouble();
        var cases = property.GetProperty("cases");
        Assert.Equal(270, cases.GetArrayLength());
        Assert.Equal(DockLayout.PaneMinimumWidth, property.GetProperty("minimumPaneWidth").GetInt32());
        Assert.Equal(420d, floor);
        var panels = Panels(property.GetProperty("panels"));
        foreach (var row in cases.EnumerateArray())
        {
            var where = row.GetProperty("id").GetString();
            var shell = row.GetProperty("shellWidth").GetDouble();
            var rail = row.GetProperty("railWidth").GetDouble();
            var requested = row.GetProperty("requestedWidth").GetDouble();
            var count = row.GetProperty("panelCount").GetInt32();
            var expectedWidth = row.GetProperty("expectedWidth").GetDouble();
            Assert.Equal($"{where} {row.GetProperty("expectedCeiling").GetDouble()}", $"{where} {DockLayout.WidthCeiling(shell, rail, floor)}");
            Assert.Equal($"{where} {expectedWidth}", $"{where} {DockLayout.ClampWidth(requested, shell, rail, floor)}");
            // The breakpoint is still the CLASS the width falls in; the capacity is the measured width less the rail.
            var breakpoint = ShellChromeContract.Breakpoint((int)shell switch { >= 1600 => 1600, >= 1200 => 1200, >= 840 => 840, >= 600 => 600, _ => 0 });
            var layout = DockLayout.Create(panels.Take(count).ToArray(), false, breakpoint, shell, rail);
            Assert.Equal($"{where} {string.Join(',', row.GetProperty("expectedContainerKinds").EnumerateArray().Select(kind => kind.GetString()))}",
                $"{where} {string.Join(',', layout.Containers.Select(container => container.Kind))}");
            var collapsed = layout.Containers.All(container => container.Kind != "docked");
            Assert.Equal($"{where} {row.GetProperty("expectedSheetCollapse").GetBoolean()}", $"{where} {collapsed}");
            // Either the dock's right edge is inside the row (rail + dock + floor <= W), or there is no dock at all.
            Assert.True(collapsed || rail + expectedWidth + floor <= shell, where);
        }
    }

    // The OFF-DIAGONAL half of the same invariant: the viewport class and the measured container are an
    // independent pair, so this grid crosses them (the 270-cell property above only samples the diagonal, where
    // they agree). Placement is decided by the class the MEASURED width falls in - the same width the clamp uses -
    // so rail + dock + floor <= the measured width or nothing is docked; the viewport class drives sheet chrome only.
    [Fact]
    public void DecidesPlacementFromTheClassTheMeasuredContainerFallsIn()
    {
        var property = Fixture.GetProperty("dockPlacementProperty");
        var floor = property.GetProperty("contentFloor").GetDouble();
        var cases = property.GetProperty("cases");
        Assert.Equal(73, cases.GetArrayLength());
        Assert.Equal(DockLayout.PaneMinimumWidth, property.GetProperty("minimumPaneWidth").GetInt32());
        Assert.Equal(420d, floor);
        var panels = Panels(property.GetProperty("panels"));
        foreach (var row in cases.EnumerateArray())
        {
            var where = row.GetProperty("id").GetString();
            var viewport = row.GetProperty("viewportWidth").GetInt32();
            var measured = row.GetProperty("measuredWidth").GetDouble();
            var rail = row.GetProperty("railWidth").GetDouble();
            var requested = row.GetProperty("requestedWidth").GetDouble();
            var count = row.GetProperty("panelCount").GetInt32();
            var expectedWidth = row.GetProperty("expectedWidth").GetDouble();
            Assert.Equal($"{where} {row.GetProperty("expectedViewportClass").GetString()}", $"{where} {ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint(viewport))}");
            Assert.Equal($"{where} {row.GetProperty("expectedPlacementClass").GetString()}", $"{where} {ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint((int)measured))}");
            Assert.Equal($"{where} {row.GetProperty("expectedCeiling").GetDouble()}", $"{where} {DockLayout.WidthCeiling(measured, rail, floor)}");
            Assert.Equal($"{where} {expectedWidth}", $"{where} {DockLayout.ClampWidth(requested, measured, rail, floor)}");
            // Built the way the shell builds it: the VIEWPORT class in the breakpoint argument, the MEASURED box as W.
            var layout = DockLayout.Create(panels.Take(count).ToArray(), false, ShellChromeContract.Breakpoint(viewport), measured, rail);
            Assert.Equal($"{where} {string.Join(',', row.GetProperty("expectedContainerKinds").EnumerateArray().Select(kind => kind.GetString()))}",
                $"{where} {string.Join(',', layout.Containers.Select(container => container.Kind))}");
            var collapsed = layout.Containers.All(container => container.Kind != "docked");
            Assert.Equal($"{where} {row.GetProperty("expectedSheetCollapse").GetBoolean()}", $"{where} {collapsed}");
            // Either the dock's right edge is inside the measured box, or there is no dock at all.
            Assert.True(collapsed || rail + expectedWidth + floor <= measured, where);
        }
    }

    private static string[] Kinds(IRenderedComponent<HarborlineAppShell> cut) => cut.FindAll("[data-shell-panel-id]").Select(e => e.GetAttribute("data-shell-container-kind")!).ToArray();

    // The same grid replayed as a RE-RENDER of one mounted shell, through BOTH runtime seams: the [JSInvokable]
    // ShellMeasured callback the ResizeObserver drives, and the ShellInlineSize parameter. The viewport class never
    // moves, so a layout cached on anything less than every input the placement reads goes stale here.
    // One mount per case: a BunitContext takes its service registrations once, so the grid is a Theory.
    public static TheoryData<int, string> ReMeasureRows()
    {
        var data = new TheoryData<int, string>();
        for (var index = 0; index < Fixture.GetProperty("dockPlacementProperty").GetProperty("cases").GetArrayLength(); index++)
            foreach (var seam in new[] { "parameter", "ShellMeasured" }) data.Add(index, seam);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReMeasureRows))]
    public async Task ReMeasurementRePlacesTheDockOnReRender(int index, string seam)
    {
        var property = Fixture.GetProperty("dockPlacementProperty");
        var floor = property.GetProperty("contentFloor").GetDouble();
        var declared = property.GetProperty("panels");
        {
            var row = property.GetProperty("cases")[index];
            var where = row.GetProperty("id").GetString();
            var viewport = row.GetProperty("viewportWidth").GetInt32();
            var measured = row.GetProperty("measuredWidth").GetDouble();
            var remeasured = row.GetProperty("remeasuredWidth").GetDouble();
            var rail = row.GetProperty("railWidth").GetDouble();
            var open = Panels(declared).Take(row.GetProperty("panelCount").GetInt32()).Select(panel => panel.Id).ToArray();
            var mountWidth = row.GetProperty("expectedMountWidth").ValueKind == JsonValueKind.Null ? null : (double?)row.GetProperty("expectedMountWidth").GetDouble();
            var afterWidth = row.GetProperty("expectedRemeasuredWidth").ValueKind == JsonValueKind.Null ? null : (double?)row.GetProperty("expectedRemeasuredWidth").GetDouble();
            var before = row.GetProperty("expectedContainerKinds").EnumerateArray().Select(kind => kind.GetString()!).ToArray();
            var after = row.GetProperty("expectedRemeasuredContainerKinds").EnumerateArray().Select(kind => kind.GetString()!).ToArray();
            {
                var cut = Mount(viewport, null, open, [], declared, rail > 0, measured, inlineSizeParameter: seam == "parameter");
                if (seam != "parameter") await cut.InvokeAsync(() => cut.Instance.ShellMeasured(measured));
                Assert.Equal($"{where} {seam} {string.Join(',', before)}", $"{where} {seam} {string.Join(',', Kinds(cut))}");
                Assert.Equal($"{where} {seam} {mountWidth}", $"{where} {seam} {Width(cut)}");
                if (seam == "parameter") cut.Render(p => p.Add(x => x.ShellInlineSize, remeasured));
                else await cut.InvokeAsync(() => cut.Instance.ShellMeasured(remeasured));
                Assert.Equal($"{where} {seam} {string.Join(',', after)}", $"{where} {seam} {string.Join(',', Kinds(cut))}");
                Assert.Equal($"{where} {seam} {afterWidth}", $"{where} {seam} {Width(cut)}");
                // Either the dock's right edge is inside the RE-measured box, or there is no dock at all.
                Assert.True(afterWidth is null || rail + afterWidth + floor <= remeasured, $"{where} {seam}");
            }
        }
    }

    // The rail is the other placement input the memo used to omit: collapsing it inside ONE measured box frees
    // its inline size, so capacity is recounted on the re-render.
    [Fact]
    public void CollapsingTheRailRePlacesTheDockOnReRender()
    {
        var row = Fixture.GetProperty("dockPlacementProperty").GetProperty("railToggleCase");
        var declared = Fixture.GetProperty("dockPlacementProperty").GetProperty("panels");
        var open = Panels(declared).Take(row.GetProperty("panelCount").GetInt32()).Select(panel => panel.Id).ToArray();
        var cut = Mount(row.GetProperty("viewportWidth").GetInt32(), null, open, [], declared, true, row.GetProperty("measuredWidth").GetDouble());
        Assert.Equal(row.GetProperty("expectedContainerKinds").EnumerateArray().Select(kind => kind.GetString()).ToArray(), Kinds(cut));
        Assert.Equal(row.GetProperty("expectedWidth").ValueKind == JsonValueKind.Null ? null : row.GetProperty("expectedWidth").GetDouble(), Width(cut));
        cut.Render(p => p.Add(x => x.Collapsed, true));
        Assert.Equal(row.GetProperty("expectedCollapsedContainerKinds").EnumerateArray().Select(kind => kind.GetString()).ToArray(), Kinds(cut));
        Assert.Equal(row.GetProperty("expectedCollapsedWidth").GetDouble(), Width(cut));
    }

    private static double[] Fractions(IRenderedComponent<HarborlineAppShell> cut) => cut.FindAll("[data-shell-panel-id]")
        .Select(e => double.Parse(e.GetAttribute("style")!.Split("--hl-app-shell-panel-fraction:")[1], CultureInfo.InvariantCulture)).ToArray();

    // Item 0 property. The AUTHORED tree (what the user dragged, or what the host restored) and the DERIVED
    // placement are different things with different invalidation sets, so one field cannot carry both: placement
    // and the width clamp derive PER RENDER from every input, while the tree is remembered against
    // {open panels, spread, placement class} only. Re-measuring inside one class keeps the arrangement, crossing a
    // class falls back to the derived tree, coming BACK restores it with the remembered widths, and the rail -
    // which changes placement but not what the user arranged - never drops it. Persistence emits the tree only
    // while it is the authored one, so a derived reset is never written back for the host to reload as a request.
    // Driven through the [JSInvokable] ShellMeasured seam the ResizeObserver owns, on ONE mounted shell.
    public static TheoryData<int> TreeRetentionRows()
    {
        var data = new TheoryData<int>();
        for (var index = 0; index < Fixture.GetProperty("dockTreeRetentionProperty").GetProperty("cases").GetArrayLength(); index++) data.Add(index);
        return data;
    }

    [Theory]
    [MemberData(nameof(TreeRetentionRows))]
    public async Task TheAuthoredDockTreeIsRememberedAgainstItsPlacementClassOnly(int index)
    {
        var property = Fixture.GetProperty("dockTreeRetentionProperty");
        var declared = property.GetProperty("panels");
        var row = property.GetProperty("cases")[index];
        var id = row.GetProperty("id").GetString();
        var measured = row.GetProperty("measuredWidth").GetDouble();
        var emitted = new List<DockStateSnapshot>();
        var cut = Mount(row.GetProperty("viewportWidth").GetInt32(), row.GetProperty("state"), null, emitted, declared, row.GetProperty("railCapable").GetBoolean(), measured, inlineSizeParameter: false);
        await cut.InvokeAsync(() => cut.Instance.ShellMeasured(measured));
        void Check(string label, string placement, string[] kinds, double[] fractions, double? width, bool authored)
        {
            var where = $"{id} {label} ";
            Assert.Equal($"{where}{placement}", $"{where}{ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint((int)measured))}");
            Assert.Equal($"{where}{string.Join(',', kinds)}", $"{where}{string.Join(',', Kinds(cut))}");
            Assert.Equal($"{where}{string.Join(',', fractions)}", $"{where}{string.Join(',', Fractions(cut))}");
            Assert.Equal($"{where}{width}", $"{where}{Width(cut)}");
            // The persistence seam is the observation: a derived tree is emitted as null, an authored one as the tree.
            Assert.Equal($"{where}{authored}", $"{where}{emitted[^1].Tree is not null}");
        }
        static string[] Strings(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        static double[] Doubles(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetDouble()).ToArray();
        static double? Nullable(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : value.GetProperty(name).GetDouble();
        Check("mount", row.GetProperty("expectedMountPlacementClass").GetString()!, Strings(row, "expectedMountContainerKinds"), Doubles(row, "expectedMountFractions"), Nullable(row, "expectedMountWidth"), row.GetProperty("expectedMountAuthored").GetBoolean());
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var action = step.GetProperty("action").GetString();
            if (action == "remeasure") { measured = step.GetProperty("width").GetDouble(); await cut.InvokeAsync(() => cut.Instance.ShellMeasured(measured)); }
            else if (action == "collapseRail") cut.Render(p => p.Add(x => x.Collapsed, true));
            else throw new InvalidOperationException($"unknown dockTreeRetentionProperty action \"{action}\"");
            Check($"{action}{(step.TryGetProperty("width", out var next) ? next.GetDouble().ToString(CultureInfo.InvariantCulture) : "")}", step.GetProperty("expectedPlacementClass").GetString()!, Strings(step, "expectedContainerKinds"), Doubles(step, "expectedFractions"), Nullable(step, "expectedWidth"), step.GetProperty("expectedAuthored").GetBoolean());
        }
    }

    private sealed class Media(int width) : IMediaQueryObserver
    { public ValueTask<IMediaQuerySubscription> ObserveAsync(string query, Func<MediaQueryChange, ValueTask> callback, CancellationToken cancellationToken = default) => ValueTask.FromResult<IMediaQuerySubscription>(new Subscription(query, width)); }
    private sealed class Subscription(string query, int width) : IMediaQuerySubscription
    {
        public string Query => query;
        public bool Matches => width >= int.Parse(new string(query.Where(char.IsDigit).ToArray()), CultureInfo.InvariantCulture);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // GEOMETRY UP FRONT (checklist l). Restore and the FIRST measurement: persisted state may only be applied
    // against a MEASURED placement class. Until the box has been measured the restored tree is held PENDING -
    // rendered, so the first paint is the arrangement the user left rather than a derived guess that flashes,
    // but unstamped and NEVER emitted. Stamping it with the pre-measurement viewport class is the defect: a
    // narrow scene inside a wide viewport then reads as a class crossing, the shell derives 0.5/0.5, emits
    // tree:null, and the host persists the loss for good. The grid is viewport x measured x rail with
    // measured != viewport on 8 of its 12 rows - what dockTreeRetentionProperty (measured == viewport) cannot see.
    // Driven through the real [JSInvokable] ShellMeasured seam; the React mirror reads the same rows.
    public static TheoryData<int> RestoreMeasurementRows()
    {
        var data = new TheoryData<int>();
        for (var index = 0; index < Fixture.GetProperty("dockRestoreMeasurementProperty").GetProperty("cases").GetArrayLength(); index++) data.Add(index);
        return data;
    }

    [Theory]
    [MemberData(nameof(RestoreMeasurementRows))]
    public async Task RestoredStateIsAppliedAgainstTheMeasuredClass(int index)
    {
        var property = Fixture.GetProperty("dockRestoreMeasurementProperty");
        var floor = property.GetProperty("contentFloor").GetDouble();
        var declared = property.GetProperty("panels");
        var row = property.GetProperty("cases")[index];
        var id = row.GetProperty("id").GetString();
        var viewport = row.GetProperty("viewportWidth").GetInt32();
        var rail = row.GetProperty("railWidth").GetDouble();
        var emitted = new List<DockStateSnapshot>();
        // Unset ShellInlineSize and no ShellMeasured yet: the shell is mounted and NOT measured.
        var cut = Mount(viewport, row.GetProperty("state"), null, emitted, declared, row.GetProperty("railCapable").GetBoolean(), null, inlineSizeParameter: false);
        void Geometry(string label, double source, string[] kinds, double[] fractions, double? width)
        {
            var where = $"{id} {label} ";
            Assert.Equal($"{where}{string.Join(',', kinds)}", $"{where}{string.Join(',', Kinds(cut))}");
            Assert.Equal($"{where}{string.Join(',', fractions)}", $"{where}{string.Join(',', Fractions(cut))}");
            Assert.Equal($"{where}{width}", $"{where}{Width(cut)}");
            // rail + dock + content floor <= the width the geometry was derived from, or there is no dock at all.
            Assert.True(width is null || rail + width.Value + floor <= source, where);
        }
        static string[] Strings(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        static double[] Doubles(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetDouble()).ToArray();
        static double? Nullable(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.Null ? null : value.GetProperty(name).GetDouble();
        // Before the first measurement: the arrangement is SHOWN, the guess still fits, and nothing at all is
        // written back. The exact guessed width is deliberately NOT asserted - see the property's `why`: it is the
        // one number the two lanes cannot share, which is exactly why the emit waits for the measurement instead.
        Assert.Equal($"{id} mount {string.Join(',', Strings(row, "expectedMountContainerKinds"))}", $"{id} mount {string.Join(',', Kinds(cut))}");
        Assert.Equal($"{id} mount {string.Join(',', Doubles(row, "expectedMountFractions"))}", $"{id} mount {string.Join(',', Fractions(cut))}");
        Assert.True(Width(cut) is null || rail + Width(cut)!.Value + floor <= viewport, $"{id} mount");
        Assert.Equal($"{id} emissions {row.GetProperty("expectedMountEmissions").GetInt32()}", $"{id} emissions {emitted.Count}");
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var width = step.GetProperty("width").GetDouble();
            await cut.InvokeAsync(() => cut.Instance.ShellMeasured(width));
            // The placement class the shell must have used is the one the MEASURED width falls in, never the viewport's.
            Assert.Equal($"{id} {width} {step.GetProperty("expectedPlacementClass").GetString()}", $"{id} {width} {ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint((int)width))}");
            Geometry(width.ToString(CultureInfo.InvariantCulture), width, Strings(step, "expectedContainerKinds"), Doubles(step, "expectedFractions"), Nullable(step, "expectedWidth"));
            // The persistence seam: an authored tree is emitted as the tree, a derived one honestly as null.
            Assert.Equal($"{id} {width} {step.GetProperty("expectedAuthored").GetBoolean()}", $"{id} {width} {emitted[^1].Tree is not null}");
        }
    }

    // Settlement: measurement is settled only by a report the shell ACCEPTS (width > 0), and Reset panels clears
    // the PENDING restored root. Blazor is immune to both by construction - measuredInlineSize takes only widths
    // > 0 and ResetDock clears pendingRestoredRoot - and these rows hold it to that. The React mirror is
    // AppShell.persistence.test.tsx "settles measurement only on an accepted report".
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public async Task SettlementIsDrivenByAcceptedReports(int index)
    {
        var property = Fixture.GetProperty("dockSettlementProperty");
        var row = property.GetProperty("cases")[index];
        var id = row.GetProperty("id").GetString();
        var emitted = new List<DockStateSnapshot>();
        // Unset ShellInlineSize and no ShellMeasured yet: the shell is mounted and NOT measured.
        var cut = Mount(row.GetProperty("viewportWidth").GetInt32(), row.GetProperty("state"), null, emitted, property.GetProperty("panels"), row.GetProperty("railCapable").GetBoolean(), null, inlineSizeParameter: false);
        static string[] Strings(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
        static double[] Doubles(JsonElement value, string name) => value.GetProperty(name).EnumerateArray().Select(item => item.GetDouble()).ToArray();
        Assert.Equal($"{id} mount {string.Join(',', Strings(row, "expectedMountContainerKinds"))}", $"{id} mount {string.Join(',', Kinds(cut))}");
        Assert.Equal($"{id} mount {string.Join(',', Doubles(row, "expectedMountFractions"))}", $"{id} mount {string.Join(',', Fractions(cut))}");
        Assert.Equal($"{id} emissions {row.GetProperty("expectedMountEmissions").GetInt32()}", $"{id} emissions {emitted.Count}");
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            var action = step.GetProperty("action").GetString();
            if (action == "reset") cut.Find("button[aria-label='Reset panels']").Click();
            else await cut.InvokeAsync(() => cut.Instance.ShellMeasured(step.GetProperty("width").GetDouble()));
            var where = $"{id} {action}:{(step.TryGetProperty("width", out var w) ? w.GetDouble().ToString(CultureInfo.InvariantCulture) : string.Empty)} ";
            if (step.TryGetProperty("expectedPlacementClass", out var placement))
                Assert.Equal($"{where}{placement.GetString()}", $"{where}{ShellChromeContract.BreakpointName(ShellChromeContract.Breakpoint((int)step.GetProperty("width").GetDouble()))}");
            Assert.Equal($"{where}{string.Join(',', Strings(step, "expectedContainerKinds"))}", $"{where}{string.Join(',', Kinds(cut))}");
            Assert.Equal($"{where}{string.Join(',', Doubles(step, "expectedFractions"))}", $"{where}{string.Join(',', Fractions(cut))}");
            // A step with no expectedWidth key asserts no width: in the unmeasured window the guess is the one
            // number the two lanes cannot share, which is why nothing is emitted there.
            if (step.TryGetProperty("expectedWidth", out var width))
                Assert.Equal($"{where}{(width.ValueKind == JsonValueKind.Null ? null : (double?)width.GetDouble())}", $"{where}{Width(cut)}");
            Assert.Equal($"{where}emissions {step.GetProperty("expectedEmissions").GetInt32()}", $"{where}emissions {emitted.Count}");
            if (step.TryGetProperty("expectedAuthored", out var authored))
                Assert.Equal($"{where}authored {authored.GetBoolean()}", $"{where}authored {emitted[^1].Tree is not null}");
        }
    }
}
