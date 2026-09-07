using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed partial class DataGridTests
{
    private static JsonElement SelectionFixture(out JsonDocument document)
    {
        document = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/selection-v1.json")));
        return document.RootElement;
    }

    // Fixture authority: a name the fixture does not declare fails loudly rather than silently
    // replaying an empty step.
    private static SliceRow FixtureRow(JsonElement source, string id) =>
        SliceRows(source).SingleOrDefault(row => row.Id == id) ?? throw new InvalidOperationException($"missing-fixture-row: {id}");

    private static IReadOnlyList<string?> SelectedIds(IRenderedComponent<HarborlineDataGrid<SliceRow>> cut) =>
        cut.FindAll("[data-row-id][aria-selected='true']").Select(row => row.GetAttribute("data-row-id")).ToArray();

    [Fact]
    public async Task SelectionFixtureReplaysEveryDeclaredTransitionAsync()
    {
        var selection = SelectionFixture(out var fixture);
        using var _ = fixture;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{selection.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var columns = SliceColumns(source);
        var rows = SliceRows(source);
        var activations = new List<string>();
        var state = DataGridListState.Empty;
        string[] grouping = [];

        var cut = Render<HarborlineDataGrid<SliceRow>>(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next)
            .Add(c => c.OnRowActivate, activation => activations.Add(activation.RowId)));
        void Refresh() => cut.Render(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.Grouping, grouping)
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next)
            .Add(c => c.OnRowActivate, activation => activations.Add(activation.RowId)));
        IElement Cell(string rowId) => cut.FindAll($"[data-row-id='{rowId}'] .hl-data-grid__cell").FirstOrDefault()
            ?? throw new InvalidOperationException($"missing-visible-row: {rowId}");

        foreach (var transition in selection.GetProperty("transitions").EnumerateArray())
        {
            var action = transition.GetProperty("action");
            var id = transition.GetProperty("id").GetString();
            switch (action.GetProperty("kind").GetString())
            {
                case "click":
                    Cell(FixtureRow(source, action.GetProperty("rowId").GetString()!).Id).Click();
                    break;
                case "doubleClick":
                    Cell(FixtureRow(source, action.GetProperty("rowId").GetString()!).Id).Click();
                    Refresh();
                    Cell(action.GetProperty("rowId").GetString()!).DoubleClick();
                    break;
                case "key":
                    Cell(FixtureRow(source, action.GetProperty("rowId").GetString()!).Id)
                        .KeyDown(new KeyboardEventArgs { Key = action.GetProperty("key").GetString()! });
                    break;
                case "grouping":
                    grouping = action.GetProperty("grouping").EnumerateArray().Select(value => value.GetString()!).ToArray();
                    break;
                case "capacity":
                    await cut.InvokeAsync(() => runtime.Module.Callback<HarborlineDataGrid<SliceRow>>()
                        .OnResizedAsync(action.GetProperty("capacity").GetInt32() * selection.GetProperty("minimumColumnWidth").GetInt32()));
                    break;
                default:
                    throw new InvalidOperationException($"undeclared-action-kind: {action.GetProperty("kind").GetString()}");
            }
            Refresh();
            var expect = transition.GetProperty("expect");
            Assert.Equal(expect.GetProperty("selectedRowId").ValueKind == JsonValueKind.Null ? null : expect.GetProperty("selectedRowId").GetString(), state.SelectedRowId);
            Assert.Equal(expect.GetProperty("activations").EnumerateArray().Select(value => value.GetString()), activations);
            Assert.Equal(expect.GetProperty("ariaSelected").EnumerateArray().Select(value => value.GetString()), SelectedIds(cut));
            Assert.NotNull(id);
        }

        // The commit callback is optional: a grid with no host action must not throw on Enter.
        using var bare = Render<HarborlineDataGrid<SliceRow>>(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns));
        bare.Find($"[data-row-id='{selection.GetProperty("validationCases")[0].GetProperty("rowId").GetString()}'] .hl-data-grid__cell")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(selection.GetProperty("transitions").EnumerateArray().Last().GetProperty("expect").GetProperty("activations")
            .EnumerateArray().Select(value => value.GetString()), activations);
    }

    [Fact]
    public void SelectionSurvivesLazyCollapseAndAnEqualRematerialisedSnapshot()
    {
        var selection = SelectionFixture(out var fixture);
        using var _ = fixture;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{selection.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var lazy = selection.GetProperty("lazyExpansion");
        var branchId = lazy.GetProperty("branchId").GetString()!;
        var branch = new SliceRow(branchId, new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        var selectRowId = lazy.GetProperty("selectRowId").GetString()!;
        SliceRow[] Children() => lazy.GetProperty("childIds").EnumerateArray().Select(id => FixtureRow(source, id.GetString()!)).ToArray();
        var snapshot = new DataGridChildren<SliceRow>(lazy.GetProperty("count").GetInt32(), "loaded", Children());
        var state = DataGridListState.Empty;

        var cut = Render<HarborlineDataGrid<SliceRow>>(p => p
            .Add(c => c.Rows, new[] { branch }).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, SliceColumns(source))
            .Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [branchId] = snapshot })
            .Add(c => c.OnChildrenRequest, _ => { })
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));
        void Refresh() => cut.Render(p => p
            .Add(c => c.Rows, new[] { branch }).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, SliceColumns(source))
            .Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [branchId] = snapshot })
            .Add(c => c.OnChildrenRequest, _ => { })
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));

        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = lazy.GetProperty("expandKey").GetString()! });
        Refresh();
        cut.Find($"[data-row-id='{selectRowId}'] .hl-data-grid__cell").Click();
        Refresh();
        Assert.Equal(selectRowId, state.SelectedRowId);
        Assert.Equal(new[] { selectRowId }, SelectedIds(cut));

        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = lazy.GetProperty("collapseKey").GetString()! });
        Refresh();
        Assert.Equal(selectRowId, state.SelectedRowId);
        Assert.Empty(SelectedIds(cut));

        // A re-materialised but equal snapshot is a new object with equal values: selection is
        // remembered by row id, so it survives.
        snapshot = new DataGridChildren<SliceRow>(lazy.GetProperty("count").GetInt32(), "loaded", Children().Select(row => row with { }).ToArray());
        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = lazy.GetProperty("expandKey").GetString()! });
        Refresh();
        Assert.Equal(selectRowId, state.SelectedRowId);
        Assert.Equal(new[] { selectRowId }, SelectedIds(cut));
    }

    [Fact]
    public async Task PreservedListStateRestoresScrollSelectionAndColumnsOnRemountAsync()
    {
        var selection = SelectionFixture(out var fixture);
        using var _ = fixture;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{selection.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var preserved = selection.GetProperty("preservedListState");
        var prefix = preserved.GetProperty("idPrefix").GetString()!;
        var template = SliceRows(source)[0];
        var rows = Enumerable.Range(0, preserved.GetProperty("rowCount").GetInt32()).Select(index => template with { Id = $"{prefix}{index}" }).ToArray();
        var columns = SliceColumns(source);
        var state = DataGridListState.Empty;

        var cut = Render<HarborlineDataGrid<SliceRow>>(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));
        void Refresh() => cut.Render(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));
        var component = runtime.Module.Callback<HarborlineDataGrid<SliceRow>>();

        await cut.InvokeAsync(() => component.OnResizedAsync(preserved.GetProperty("capacity").GetInt32() * selection.GetProperty("minimumColumnWidth").GetInt32()));
        Refresh();
        Assert.Equal(
            preserved.GetProperty("expectedColumnWidths").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetDouble()),
            state.ColumnWidths);

        // ResizeObserver fires on any layout change. Measured widths are republished only when
        // their VALUE changes; comparing by identity would publish on every observation.
        var publishes = 0;
        void Count() => cut.Render(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => { publishes++; state = next; }));
        Count();
        for (var round = 0; round < 3; round++)
            await cut.InvokeAsync(() => component.OnResizedAsync(preserved.GetProperty("capacity").GetInt32() * selection.GetProperty("minimumColumnWidth").GetInt32()));
        Assert.Equal(0, publishes);

        await cut.InvokeAsync(() => component.OnScrolledAsync(preserved.GetProperty("scrollTop").GetDouble()));
        Refresh();
        cut.Find($"[data-row-id='{preserved.GetProperty("selectRowId").GetString()}'] .hl-data-grid__cell").Click();
        Refresh();
        var closed = state;
        Assert.Equal(preserved.GetProperty("scrollTop").GetDouble(), closed.ScrollTop);
        Assert.Equal(preserved.GetProperty("selectRowId").GetString(), closed.SelectedRowId);

        // Closing the inspector unmounts the grid; reopening the page remounts it with the state the
        // host kept. Nothing is measured yet, so the first render is the whole assertion (L1701).
        cut.Dispose();
        using var reopened = Render<HarborlineDataGrid<SliceRow>>(p => p
            .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
            .Add(c => c.ListState, closed));
        Assert.Equal(
            preserved.GetProperty("expectedColumnIds").EnumerateArray().Select(id => Header(source, id.GetString()!)),
            reopened.FindAll("[role='columnheader']").Select(header => header.TextContent));
        Assert.Equal(preserved.GetProperty("expectedFirstRenderedId").GetString(), reopened.FindAll("[data-row-id]")[0].GetAttribute("data-row-id"));
        Assert.Equal(new[] { preserved.GetProperty("selectRowId").GetString() }, SelectedIds(reopened));
        Assert.Equal(closed.ScrollTop, runtime.Module.ConnectArguments![3]);
    }
    // Fixture-owned fence: selection changes ONLY on a declared peek gesture or a host-supplied
    // SelectedRowId. Every other route that moves focus — the corner keys, an unhandled key, a
    // wheel scroll, a responsive column removal — leaves SelectedRowId exactly where it was. One
    // case per declared row so a regression names the route it broke.
    public static TheoryData<string> NonPeekGestureIds()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/selection-v1.json")));
        var data = new TheoryData<string>();
        foreach (var gesture in document.RootElement.GetProperty("nonPeekGestures").GetProperty("gestures").EnumerateArray())
            data.Add(gesture.GetProperty("id").GetString()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(NonPeekGestureIds))]
    public async Task NonPeekGestureNeverChangesTheSelectionAsync(string gestureId)
    {
        var selection = SelectionFixture(out var fixture);
        using var _ = fixture;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{selection.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var table = selection.GetProperty("nonPeekGestures");
        var gesture = table.GetProperty("gestures").EnumerateArray().SingleOrDefault(entry => entry.GetProperty("id").GetString() == gestureId);
        if (gesture.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"missing-fixture-gesture: {gestureId}");
        var prefix = table.GetProperty("idPrefix").GetString()!;
        var rowCount = table.GetProperty("rowCount").GetInt32();
        var scrollRow = table.GetProperty("scrollRowIndex").GetInt32();
        var startRowId = table.GetProperty("startFocusRowId").GetString()!;
        var startColumnId = table.GetProperty("startFocusColumnId").GetString()!;
        var template = SliceRows(source)[0];
        var rows = Enumerable.Range(0, rowCount).Select(index => template with { Id = $"{prefix}{index}" }).ToArray();
        var columns = SliceColumns(source);
        var rowHeight = selection.GetProperty("rowHeight").GetInt32();

        string? ExpectedRow(string name) => name switch
        {
            "same" => startRowId,
            "first" => $"{prefix}0",
            "last" => $"{prefix}{rowCount - 1}",
            "windowStart" => $"{prefix}{scrollRow}",
            "none" => null,
            _ => throw new InvalidOperationException($"undeclared-focus-row: {name}"),
        };
        string? ExpectedColumn(string name, IReadOnlyList<string?> ids) => name switch
        {
            "same" => startColumnId,
            "first" => ids[0],
            "last" => ids[^1],
            "none" => null,
            _ => name,
        };

        // The temporal axis of the invariant: no starting selection, a selection on the focused row,
        // and a selection the gesture leaves off screen.
        foreach (var startElement in table.GetProperty("startSelections").EnumerateArray())
        {
            var start = startElement.ValueKind == JsonValueKind.Null ? null : startElement.GetString();
            var state = DataGridListState.Empty with { SelectedRowId = start };
            var cut = Render<HarborlineDataGrid<SliceRow>>(p => p
                .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
                .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));
            void Refresh() => cut.Render(p => p
                .Add(c => c.Rows, rows).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, columns)
                .Add(c => c.ListState, state).Add(c => c.ListStateChanged, next => state = next));
            var component = runtime.Module.Callback<HarborlineDataGrid<SliceRow>>();
            var label = $"{gestureId} / selection {start ?? "null"}";

            IElement Focused() => cut.FindAll($"[data-row-id='{startRowId}'] [data-column-id='{startColumnId}']").FirstOrDefault()
                ?? throw new InvalidOperationException($"missing-fixture-focus: {startRowId}/{startColumnId}");
            Focused().Focus();
            Refresh();

            switch (gesture.GetProperty("kind").GetString())
            {
                case "key":
                    Focused().KeyDown(new KeyboardEventArgs
                    {
                        Key = gesture.GetProperty("key").GetString()!,
                        CtrlKey = gesture.TryGetProperty("ctrlKey", out var ctrl) && ctrl.GetBoolean(),
                    });
                    break;
                case "scroll":
                    await cut.InvokeAsync(() => component.OnScrolledAsync(
                        scrollRow * (double)rowHeight,
                        gesture.GetProperty("focusInside").GetBoolean()));
                    break;
                case "capacity":
                    await cut.InvokeAsync(() => component.OnResizedAsync(
                        gesture.GetProperty("capacity").GetInt32() * selection.GetProperty("minimumColumnWidth").GetInt32(), focusWithin: true));
                    break;
                default:
                    throw new InvalidOperationException($"undeclared-gesture-kind: {gesture.GetProperty("kind").GetString()}");
            }
            Refresh();

            Assert.Equal(start, state.SelectedRowId);
            Assert.DoesNotContain(SelectedIds(cut), id => id != start);
            var stops = cut.FindAll(".hl-data-grid__cell[tabindex='0']");
            Assert.True(stops.Count <= 1, $"multiple-tab-stops: {stops.Count} ({label})");
            var stop = stops.FirstOrDefault();
            Assert.Equal(ExpectedRow(gesture.GetProperty("expectFocusRow").GetString()!), stop?.Closest("[data-row-id]")?.GetAttribute("data-row-id"));
            if (gesture.GetProperty("expectFocusColumn").GetString() != "none")
            {
                var visibleIds = cut.FindAll(".hl-data-grid__row--leaf")[0].QuerySelectorAll("[data-column-id]").Select(cell => cell.GetAttribute("data-column-id")).ToArray();
                Assert.Equal(ExpectedColumn(gesture.GetProperty("expectFocusColumn").GetString()!, visibleIds), stop?.GetAttribute("data-column-id"));
            }
            cut.Dispose();
        }
    }
}
