using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components.Web;
using Xunit;
namespace Harborline.UIAdapters.Blazor.Tests;
public sealed partial class DataGridTests
{
    public static IEnumerable<object[]> RequestLifetimes()
    {
        foreach (var initial in new[] { "unloaded", "failed" })
        foreach (var activation in new[] { "click", "Enter", " ", "ArrowRight" })
        foreach (var replacement in new[] { "count-only", "same-state", "collapse/re-expand", "independent-branch" })
            yield return new object[] { initial, activation, replacement };
    }

    // Re-materialise a snapshot as a fresh object graph — new snapshot, new array, new rows with
    // equal values — the shape a host produces on any JSON round-trip or immutable rebuild.
    // Nothing in the result is reference-equal to its input.
    private static DataGridChildren<SliceRow> Rematerialise(DataGridChildren<SliceRow> state) => new(
        state.Count, state.State,
        state.Children.Select(row => new SliceRow(row.Id, row.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal))).ToArray());

    [Theory, MemberData(nameof(RequestLifetimes))]
    // Completion only on a distinguishable snapshot VALUE, never object identity.
    public void RequestLifetimeCompletesOnlyOnDistinguishableSnapshotValueNeverObjectIdentity(string initial, string activation, string replacement)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        var lazy = fixture.RootElement;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{lazy.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        SliceRow Parse(JsonElement value) => new(value.GetProperty("id").GetString()!, value.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value.Clone()));
        var rows = new[] { Parse(lazy.GetProperty("branch")), Parse(lazy.GetProperty("otherBranch")) };
        var events = new List<string>();
        var state = new DataGridChildren<SliceRow>(lazy.GetProperty("count").GetInt32(), initial, []);
        var other = new DataGridChildren<SliceRow>(1, "unloaded", []);
        Dictionary<string, DataGridChildren<SliceRow>> States() => new() { ["rig"] = state, ["deck"] = other };
        using var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, rows).Add(c => c.GetRowId, r => r.Id).Add(c => c.Columns, SliceColumns(source))
            .Add(c => c.LazyChildren, States()).Add(c => c.OnChildrenRequest, request => events.Add(request.RowId)));
        void Activate(string id = "rig") { var cell = cut.Find($"[data-group-id='row:{id}'] [aria-expanded]"); if (activation == "click") cell.Click(); else cell.KeyDown(new KeyboardEventArgs { Key = activation }); }
        void Again(string id = "rig") { cut.Find($"[data-group-id='row:{id}'] [aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" }); Activate(id); }
        void Update() => cut.Render(p => p.Add(c => c.LazyChildren, States()));
        Activate();
        for (var refresh = 0; refresh < 3; refresh++)
        {
            if (replacement == "count-only") state = Rematerialise(state with { Count = state.Count + 1 });
            if (replacement == "same-state") state = Rematerialise(state);
            if (replacement == "independent-branch")
            {
                if (refresh == 0) Activate("deck");
                other = Rematerialise(other with { Count = other.Count + 1 });
                state = Rematerialise(state);
            }
            Update(); Again();
            Assert.Single(events, id => id == "rig");
            if (replacement == "independent-branch") { Again("deck"); Assert.Single(events, id => id == "deck"); }
            Assert.Equal("loading", cut.Find("[data-group-id='row:rig']").GetAttribute("data-children-state"));
        }
        // New retained content distinguishes a failed response from a count refresh.
        state = state with { State = "failed", Children = new[] { SliceRows(source)[0] } }; Update(); Again();
        Assert.Equal(2, events.Count(id => id == "rig"));
        // A re-materialised but EQUAL failed snapshot cannot finish the pending retry.
        state = Rematerialise(state); Update(); Again();
        Assert.Equal(2, events.Count(id => id == "rig"));
        Assert.Equal("loading", cut.Find("[data-group-id='row:rig']").GetAttribute("data-children-state"));
        // A count change carrying re-materialised equal children is still not a response.
        state = Rematerialise(state with { Count = state.Count + 1 }); Update(); Again();
        Assert.Equal(2, events.Count(id => id == "rig"));
        // An equal snapshot arriving during loading neither completes nor cancels the request.
        state = Rematerialise(state with { State = "loading" }); Update();
        state = Rematerialise(state); Update(); Again();
        Assert.Equal(2, events.Count(id => id == "rig"));
        Assert.Equal("loading", cut.Find("[data-group-id='row:rig']").GetAttribute("data-children-state"));
        // failed -> loading -> failed with re-materialised EQUAL children: the move away from the
        // observed loading snapshot is itself distinguishable, so the retry completes exactly once.
        state = Rematerialise(state with { State = "failed" }); Update(); Again();
        Assert.Equal(3, events.Count(id => id == "rig"));
        state = state with { State = "loaded", Children = new[] { SliceRows(source)[1] } }; Update(); Again();
        Assert.Equal(3, events.Count(id => id == "rig"));
        state = state with { State = initial, Children = Array.Empty<SliceRow>() }; Update(); Again();
        Assert.Equal(4, events.Count(id => id == "rig"));
        // Removal completes the request even though the branch comes back with EQUAL children.
        cut.Render(p => p.Add(c => c.Rows, new[] { rows[1] }).Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { ["deck"] = other }));
        state = Rematerialise(state);
        cut.Render(p => p.Add(c => c.Rows, rows).Add(c => c.LazyChildren, States())); Activate();
        Assert.Equal(5, events.Count(id => id == "rig"));
    }

    [Fact]
    public void CompletesRequestComparesValueNeverObjectIdentity()
    {
        SliceRow Row(string id) => new(id, new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        DataGridChildren<SliceRow> Snapshot(string state, string[] ids, int count = 3) => new(count, state, ids.Select(Row).ToArray());
        static bool Completes(DataGridChildren<SliceRow> snapshot, DataGridChildren<SliceRow> observed)
            => DataGridChildrenSnapshot.CompletesRequest(snapshot, observed, row => row.Id);
        var observed = Snapshot("failed", ["a", "b"]);
        Assert.False(Completes(Rematerialise(observed), observed));
        Assert.False(Completes(observed, observed));
        Assert.False(Completes(Rematerialise(observed with { Count = observed.Count + 9 }), observed));
        Assert.False(Completes(Snapshot("loading", ["z"]), observed));
        Assert.True(Completes(Snapshot("loaded", ["a", "b"]), observed));
        Assert.True(Completes(Snapshot("failed", ["a", "c"]), observed));
        Assert.True(Completes(Snapshot("failed", ["b", "a"]), observed));
        Assert.True(Completes(Snapshot("failed", ["a"]), observed));
        Assert.True(Completes(Snapshot("failed", ["a", "b", "c"]), observed));
    }

    [Fact]
    public void CountOnlyRefreshCannotFinishPendingRetry()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        using var original = SliceFixture();
        var lazy = fixture.RootElement;
        var branch = lazy.GetProperty("branch");
        var id = branch.GetProperty("id").GetString()!;
        var row = new SliceRow(id, branch.EnumerateObject().Where(p => p.Name != "id").ToDictionary(p => p.Name, p => p.Value.Clone()));
        var events = new List<string>();
        var state = new DataGridChildren<SliceRow>(lazy.GetProperty("count").GetInt32(), "failed", []);
        using var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, new[] { row }).Add(c => c.GetRowId, r => r.Id).Add(c => c.Columns, SliceColumns(original.RootElement))
            .Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [id] = state }).Add(c => c.OnChildrenRequest, request => events.Add(JsonSerializer.Serialize(request))));
        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        state = state with { Count = lazy.GetProperty("countReplacement").GetInt32() };
        cut.Render(p => p.Add(c => c.LazyChildren, new Dictionary<string, DataGridChildren<SliceRow>> { [id] = state }));
        cut.Find("[aria-expanded]").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        Assert.Equal(new[] { lazy.GetProperty("eventJson").GetString() }, events);
    }
    [Fact]
    public void StaticAncestorKeepsFirstLoadedRepresentative()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        var lazy = fixture.RootElement;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{lazy.GetProperty("staticFixture").GetString()}")));
        var source = original.RootElement;
        var columns = SliceColumns(source).Select(c => c.Id == "due" ? c with { Value = r => r.Values["due"].GetString() } : c).ToArray();
        foreach (var example in lazy.GetProperty("sourceOrderCases").EnumerateArray())
        {
            var rows = SliceRows(example);
            var states = example.GetProperty("lazyChildren").EnumerateObject().ToDictionary(p => p.Name, p => new DataGridChildren<SliceRow>(
                p.Value.GetProperty("count").GetInt32(), p.Value.GetProperty("state").GetString()!, p.Value.GetProperty("children").EnumerateArray().Select(id => rows.Single(row => row.Id == id.GetString())).ToArray()));
            using var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, rows).Add(c => c.GetRowId, r => r.Id).Add(c => c.Columns, columns)
                .Add(c => c.Grouping, example.GetProperty("grouping").EnumerateArray().Select(value => value.GetString()!).ToArray())
                .Add(c => c.LazyChildren, states).Add(c => c.OnChildrenRequest, _ => {}));
            Assert.Equal(example.GetProperty("expectedAncestorDue").EnumerateArray().Select(value => value.GetString()), cut.FindAll("[data-group-id^='root/'] [data-column-id='due']").Select(cell => cell.TextContent));
        }
    }

    [Fact]
    public void EveryStaticAncestorAggregateFollowsIndependentSourceTraversal()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/lazy-children-v1.json")));
        var lazy = fixture.RootElement;
        using var original = JsonDocument.Parse(File.ReadAllText(Repo($"conformance/hlp.ui.data-grid/{lazy.GetProperty("staticFixture").GetString()}")));
        var columns = SliceColumns(original.RootElement).Select(c => c.Id == "due" ? c with { Value = r => r.Values["due"].GetString() } : c).ToArray();
        int[][] permutations = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];
        foreach (var example in lazy.GetProperty("sourceOrderCases").EnumerateArray())
        foreach (var order in permutations)
        for (var mask = 0; mask < 27; mask++)
        {
            var source = SliceRows(example);
            var rows = order.Select(index => source[index]).ToArray();
            var grouping = example.GetProperty("grouping").EnumerateArray().Select(value => value.GetString()!).ToArray();
            var states = new Dictionary<string, DataGridChildren<SliceRow>>();
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                var mode = mask / (int)Math.Pow(3, index) % 3;
                if (mode == 0) continue;
                var nested = row with { Id = $"{row.Id}-nested" };
                states[row.Id] = new(612, mode == 1 ? "unloaded" : "loaded", mode == 1 ? [] : new[] { nested, row with { Id = $"{row.Id}-last" } });
                if (mode == 2) states[nested.Id] = new(100, "loaded", new[] { row with { Id = $"{row.Id}-first" } });
            }
            // Traverse original rows independently of rendered/prepared group order.
            IEnumerable<SliceRow> Leaves(IEnumerable<SliceRow> members) => members.SelectMany(row => states.TryGetValue(row.Id, out var state) ? Leaves(state.Children) : new[] { row });
            var expected = new List<string[]>();
            void Visit(SliceRow[] members, int depth)
            {
                if (depth == grouping.Length) return;
                foreach (var value in members.Select(row => row.Values[grouping[depth]].GetString()).Distinct())
                {
                    var bucket = members.Where(row => row.Values[grouping[depth]].GetString() == value).ToArray();
                    var loaded = Leaves(bucket).ToArray();
                    expected.Add(columns.Skip(1).Select(column =>
                    {
                        var values = loaded.Select(row => row.Values[column.Id]).ToArray();
                        var distinct = values.Select(item => column.ValueKind == DataGridValueKind.Date ? item.GetDateTimeOffset().UtcTicks.ToString() : item.ToString()).Distinct().Count();
                        return distinct > 1 ? $"mixed — {distinct} {column.ValueKind.ToString().ToLowerInvariant()}" : values.FirstOrDefault().ToString();
                    }).ToArray());
                    Visit(bucket, depth + 1);
                }
            }
            Visit(rows, 0);
            using var cut = Render<HarborlineDataGrid<SliceRow>>(p => p.Add(c => c.Rows, rows).Add(c => c.GetRowId, r => r.Id).Add(c => c.Columns, columns)
                .Add(c => c.Grouping, grouping).Add(c => c.LazyChildren, states).Add(c => c.OnChildrenRequest, _ => {}));
            var actual = cut.FindAll("[data-group-id^='root/']").Select(group => group.QuerySelectorAll("[data-column-id]:not([aria-expanded])").Select(cell => cell.TextContent).ToArray()).ToArray();
            Assert.Equal(expected.Count, actual.Length);
            for (var index = 0; index < actual.Length; index++) Assert.Equal(expected[index], actual[index]);
        }
    }

    [Fact]
    public void RequestCompletesWhenHostMutatesHandedOverListInPlace()
    {
        SliceRow Row(string id) => new(id, new Dictionary<string, JsonElement>(StringComparer.Ordinal));
        var live = new List<SliceRow> { Row("a") };
        var observedKey = DataGridChildrenSnapshot.Key(new DataGridChildren<SliceRow>(1, "failed", live), row => row.Id);
        live.Add(Row("b"));
        var aliased = new DataGridChildren<SliceRow>(1, "failed", live);
        Assert.False(DataGridChildrenSnapshot.CompletesRequest(aliased, aliased, row => row.Id));
        Assert.True(DataGridChildrenSnapshot.CompletesRequest(aliased, observedKey, row => row.Id));
    }
}
