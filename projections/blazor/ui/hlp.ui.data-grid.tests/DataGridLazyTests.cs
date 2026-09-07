using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed partial class DataGridTests
{
    [Fact]
    public void LazyFixtureInterleavesRequestsAndNestedResponses()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        var lazy = fixture.RootElement;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{lazy.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        SliceRow Parse(JsonElement value) => new(value.GetProperty("id").GetString()!, value.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value.Clone()));
        var allRows = SliceRows(source).Concat(new[] { Parse(lazy.GetProperty("branch")), Parse(lazy.GetProperty("otherBranch")) }).ToDictionary(row => row.Id);
        var events = new List<string>();
        using var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Columns, SliceColumns(source)).Add(c => c.GetRowId, row => row.Id).Add(c => c.OnChildrenRequest, request => events.Add(request.RowId)));
        foreach (var step in lazy.GetProperty("interleaving").EnumerateArray())
        {
            if (step.TryGetProperty("host", out var host))
            {
                var rows = host.GetProperty("rows").EnumerateArray().Select(id => allRows[id.GetString()!]).ToArray();
                var states = host.GetProperty("lazyChildren").EnumerateObject().ToDictionary(p => p.Name, p => new DataGridChildren<SliceRow>(p.Value.GetProperty("count").GetInt32(), p.Value.GetProperty("state").GetString()!, p.Value.GetProperty("children").EnumerateArray().Select(id => allRows[id.GetString()!]).ToArray()));
                cut.Render(p => p.Add(c => c.Rows, rows).Add(c => c.LazyChildren, states));
            }
            else cut.Find($"[data-group-id='row:{step.GetProperty("rowId").GetString()}'] [aria-expanded]").KeyDown(new KeyboardEventArgs { Key = step.GetProperty("key").GetString()! });
            Assert.Equal(step.GetProperty("visibleIds").EnumerateArray().Select(id => id.GetString()), cut.FindAll("[data-row-id]").Select(row => row.GetAttribute("data-row-id")));
            Assert.Equal(step.GetProperty("events").EnumerateArray().Select(id => id.GetString()), events);
            if (step.TryGetProperty("alert", out _)) Assert.Equal(lazy.GetProperty("failedLabel").GetString(), cut.Find("[role='alert']").TextContent);
        }
        Assert.Equal(source.GetProperty("branchCells").EnumerateArray().Skip(1).Select(value => value.GetString()), cut.FindAll("[data-group-id='row:rig'] [data-aggregate-scope]").Select(cell => cell.TextContent));
        var window = lazy.GetProperty("window");
        var branchId = lazy.GetProperty("branch").GetProperty("id").GetString()!;
        var children = Enumerable.Range(0, window.GetProperty("childCount").GetInt32()).Select(index => SliceRows(source)[0] with { Id = $"{window.GetProperty("idPrefix").GetString()}{index}" }).ToArray();
        cut.Render(p => p.Add(c => c.Rows, new[] { allRows[branchId] }).Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [branchId] = new(lazy.GetProperty("count").GetInt32(), "loaded", children) }));
        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "End", CtrlKey = true });
        Assert.Equal(window.GetProperty("rowCount").GetInt32().ToString(), cut.Find("[role='treegrid']").GetAttribute("aria-rowcount"));
        Assert.True(int.Parse(cut.Find("[data-rendered-row-count]").GetAttribute("data-rendered-row-count")!) <= window.GetProperty("maxRendered").GetInt32());
        Assert.Equal($"row:{window.GetProperty("lastId").GetString()}", runtime.Module.FocusCalls.Last()[0]);
        foreach (var invalid in lazy.GetProperty("validationCases").EnumerateArray())
        {
            var states = new Dictionary<string, DataGridChildren<SliceRow>> { [lazy.GetProperty("branch").GetProperty("id").GetString()!] = new(invalid.GetProperty("count").GetInt32(), invalid.GetProperty("state").GetString()!, []) };
            Assert.Equal(invalid.GetProperty("expected").GetString(), Assert.Throws<InvalidOperationException>(() => Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, new[] { allRows[lazy.GetProperty("branch").GetProperty("id").GetString()!] }).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, SliceColumns(source)).Add(c => c.LazyChildren, states).Add(c => c.OnChildrenRequest, _ => { }))).Message);
        }
        foreach (var id in lazy.GetProperty("ordinaryRowIds").EnumerateArray())
        {
            using var ordinary = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, new[] { allRows.Values.First() with { Id = id.GetString()! } }).Add(c => c.GetRowId, row => row.Id).Add(c => c.Columns, SliceColumns(source)));
            Assert.Equal(id.GetString(), ordinary.Find("[data-row-id]").GetAttribute("data-row-id"));
        }
        Assert.Equal("children-request-required", Assert.Throws<InvalidOperationException>(() => Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { ["rig"] = new(1, "unloaded", []) }))).Message);
    }

    [Fact]
    public async Task LazyFixtureReplaysEveryStateReplacementAndFocusEntryPoint()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        var lazy = fixture.RootElement;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{lazy.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var columns = SliceColumns(source);
        var branch = lazy.GetProperty("branch");
        var id = branch.GetProperty("id").GetString()!;
        var row = new SliceRow(id, branch.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value.Clone()));
        foreach (var entryPoint in lazy.GetProperty("focusEntryPoints").EnumerateArray())
        foreach (var activation in lazy.GetProperty("activation").EnumerateArray())
        {
            var events = new List<string>();
            DataGridChildren<SliceRow> Snapshot(JsonElement transition) => new(lazy.GetProperty("count").GetInt32(), transition.GetProperty("state").GetString()!,
                transition.GetProperty("children").EnumerateArray().Select(child => SliceRows(source).Single(r => r.Id == child.GetString())).ToArray());
            var state = Snapshot(lazy.GetProperty("transitions")[0]);
            var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, new[] { row }).Add(c => c.GetRowId, r => r.Id).Add(c => c.Columns, columns)
                .Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [id] = state })
                .Add(c => c.OnChildrenRequest, request => events.Add(JsonSerializer.Serialize(request))));
            void Update() => cut.Render(p => p.Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [id] = state }));
            void Activate() { if (activation.GetString() == "click") cut.Find("[aria-expanded]").Click(); else cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = activation.GetString()! }); }
            void ValidateTransition()
            {
                Assert.Equal(state.State, cut.Find("[data-group-id]").GetAttribute("data-children-state"));
                Assert.Equal(cut.Find("[aria-expanded]").GetAttribute("aria-expanded") == "true" ? state.Children.Select(row => row.Id) : Array.Empty<string>(),
                    cut.FindAll("[data-row-id]").Select(row => row.GetAttribute("data-row-id")));
                if (state.State == "loading") Assert.Contains(lazy.GetProperty("loadingLabel").GetString()!, cut.Find("[data-group-id]").TextContent);
                if (state.State == "failed") Assert.Contains(lazy.GetProperty("failedLabel").GetString()!, cut.Find("[data-group-id]").TextContent);
            }
            ValidateTransition();
            Assert.Equal("false", cut.Find("[aria-expanded]").GetAttribute("aria-expanded"));
            Assert.Contains($"({state.Count})", cut.Find("[aria-expanded]").TextContent);
            state = state with { Count = lazy.GetProperty("countReplacement").GetInt32() }; Update();
            Assert.Contains($"({state.Count})", cut.Find("[aria-expanded]").TextContent);
            Assert.Empty(events);
            Activate();
            Assert.Equal(new[] { lazy.GetProperty("eventJson").GetString() }, events);
            Assert.Contains(lazy.GetProperty("loadingLabel").GetString()!, cut.Find("[data-group-id]").TextContent);
            cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
            Assert.Single(events);
            foreach (var transition in lazy.GetProperty("transitions").EnumerateArray().Skip(1))
            {
                state = Snapshot(transition) with { Count = state.Count }; Update();
                ValidateTransition();
                if (state.State != "loaded") continue;
                Assert.Equal(lazy.GetProperty("expectedLoadedIds").EnumerateArray().Select(x => x.GetString()), cut.FindAll("[data-row-id]").Select(x => x.GetAttribute("data-row-id")));
                foreach (var capacity in source.GetProperty("visibleColumnsByCapacity").EnumerateArray())
                {

                    var ids = capacity.GetProperty("ids").EnumerateArray().Select(x => x.GetString()!).ToArray();
                    if (entryPoint.GetString() == "resize") await cut.InvokeAsync(() => cut.Instance.OnResizedAsync(capacity.GetProperty("capacity").GetInt32() * 160));
                    else cut.Render(p => p.Add(c => c.Columns, columns.Where(c => ids.Contains(c.Id)).ToArray()));
                    Assert.Equal(ids.Select(x => Header(source, x)), cut.FindAll("[role='columnheader']").Select(x => x.TextContent));
                    Assert.Equal(ids.Skip(1).Select(x => source.GetProperty("branchCells")[Array.FindIndex(columns, c => c.Id == x)].GetString()), cut.FindAll("[data-aggregate-scope]").Select(x => x.TextContent));
                    Assert.Contains(lazy.GetProperty("aggregateLabel").GetString()!, cut.Find(".hl-data-grid").TextContent);
                }
                cut.Render(p => p.Add(c => c.Columns, columns));
                await cut.InvokeAsync(() => cut.Instance.OnResizedAsync(columns.Length * 160));
                var focus = source.GetProperty("focusRecovery");
                cut.Find($"[data-row-id='{state.Children[0].Id}'] [data-column-id='{focus.GetProperty("focusedColumnId").GetString()}']").Focus();
                runtime.Module.FocusCalls.Clear();
                if (entryPoint.GetString() == "resize") await cut.InvokeAsync(() => cut.Instance.OnResizedAsync(focus.GetProperty("afterCapacity").GetInt32() * 160, true));
                else
                {
                    var ids = source.GetProperty("visibleColumnsByCapacity").EnumerateArray().Single(x => x.GetProperty("capacity").GetInt32() == focus.GetProperty("afterCapacity").GetInt32()).GetProperty("ids").EnumerateArray().Select(x => x.GetString()).ToArray();
                    cut.Render(p => p.Add(c => c.Columns, columns.Where(c => ids.Contains(c.Id)).ToArray()));
                }
                Assert.Equal(new object?[] { $"row:{state.Children[0].Id}", focus.GetProperty("expectedColumnId").GetString() }, runtime.Module.FocusCalls.Last());
                Assert.Equal("0", cut.Find($"[data-row-id='{state.Children[0].Id}'] [data-column-id='{focus.GetProperty("expectedColumnId").GetString()}']").GetAttribute("tabindex"));
            }
            cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" }); Activate();
            Assert.Equal(2, events.Count);
            cut.Render(p => p.Add(c => c.Rows, Array.Empty<SliceRow>()).Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>>()));
            Assert.Empty(cut.FindAll("[data-group-id]"));
            cut.Render(p => p.Add(c => c.Rows, new[] { row }).Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [id] = state }));
            Assert.Equal("false", cut.Find("[aria-expanded]").GetAttribute("aria-expanded"));
            cut.Dispose();
        }
    }
}
