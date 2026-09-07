using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Bunit;
using Harborline.UIAdapters.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Xunit;

namespace Harborline.UIAdapters.Blazor.Tests;

public sealed partial class DataGridTests : BunitContext
{
    private readonly GridJsRuntime runtime = new();

    public DataGridTests() => Services.AddSingleton<IJSRuntime>(runtime);

    private sealed record Row(string Id, string Property, string Status);
    private sealed record SliceRow(string Id, IReadOnlyDictionary<string, JsonElement> Values);
    private sealed record IdentityRow(string Id, object? Value);
    private sealed record RepresentativeRow(string Id, string Group, string Value);

    private static readonly DataGridColumn<Row>[] Columns =
    [
        new("property", "Property", row => row.Property, RemovalPriority: 20),
        new("status", "Status", row => row.Status, RemovalPriority: 10),
    ];

    [Fact]
    public void GroupingPreservesFirstSeenOrderAndLeafZebra()
    {
        var rows = new[] { new Row("1", "Palm", "Open"), new Row("2", "Bay", "Closed"), new Row("3", "Palm", "Closed") };
        var cut = Render<HarborlineDataGrid<Row>>(parameters => parameters
            .Add(component => component.Rows, rows)
            .Add(component => component.GetRowId, row => row.Id)
            .Add(component => component.Columns, Columns)
            .Add(component => component.Grouping, new[] { "property" })
            .Add(component => component.Zebra, true));

        Assert.Equal("treegrid", cut.Find(".hl-data-grid").GetAttribute("role"));
        Assert.All(cut.FindAll("[aria-expanded]"), cell => Assert.Equal("true", cell.GetAttribute("aria-expanded")));
        Assert.Equal(2, cut.FindAll(".hl-data-grid__disclosure svg").Count);
        Assert.All(cut.FindAll(".hl-data-grid__disclosure"), icon =>
        {
            Assert.Equal("false", icon.GetAttribute("data-collapsed"));
            Assert.DoesNotContain("▸", icon.TextContent);
            Assert.DoesNotContain("▾", icon.TextContent);
        });
        Assert.Equal(
            new[] { "root/group:0:8:property:11:string:Palm", "root/group:0:8:property:10:string:Bay" },
            cut.FindAll("[data-hl-group]").Select(element => element.GetAttribute("data-hl-group")));
        Assert.Equal(3, cut.FindAll("[data-hl-row-id]").Count);
        Assert.Single(cut.FindAll("[data-zebra-stripe='alternate']"));
    }

    [Fact]
    public void ReplacementRemovesStaleRows()
    {
        var cut = Render<HarborlineDataGrid<Row>>(parameters => parameters
            .Add(component => component.Rows, new[] { new Row("1", "Palm", "Open") })
            .Add(component => component.GetRowId, row => row.Id)
            .Add(component => component.Columns, Columns));

        cut.Render(parameters => parameters
            .Add(component => component.Rows, new[] { new Row("2", "Bay", "Open") })
            .Add(component => component.GetRowId, row => row.Id)
            .Add(component => component.Columns, Columns));

        Assert.Empty(cut.FindAll("[data-hl-row-id='1']"));
        Assert.Single(cut.FindAll("[data-hl-row-id='2']"));
    }

    [Fact, Trait("ModuleConformance", "hlp.ui.data-grid")]
    public async Task SharedFixtureConforms()
    {
        var raw = Environment.GetEnvironmentVariable("HARBORLINE_CONFORMANCE_FIXTURE");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var fixture = JsonDocument.Parse(raw);
        var fixtureId = fixture.RootElement.GetProperty("id").GetString();
        Assert.StartsWith("data-grid.", fixtureId);
        if (fixtureId == "data-grid.lazy-children") { await LazyFixtureReplaysEveryStateReplacementAndFocusEntryPoint(); LazyFixtureInterleavesRequestsAndNestedResponses(); StaticAncestorKeepsFirstLoadedRepresentative(); EveryStaticAncestorAggregateFollowsIndependentSourceTraversal(); return; }
        if (fixtureId == "data-grid.removal-priority-required")
        {
            var parameter = typeof(DataGridColumn<Row>).GetConstructors().Single().GetParameters()
                .Single(candidate => candidate.Name == nameof(DataGridColumn<Row>.RemovalPriority));
            Assert.Equal(typeof(int), parameter.ParameterType);
            Assert.False(parameter.IsOptional);
            Assert.False(parameter.HasDefaultValue);
            Assert.Equal("column-removal-priority-required", fixture.RootElement.GetProperty("expected").GetProperty("error").GetString());
        }
        Assert.Equal("grid", Render<HarborlineDataGrid<Row>>(parameters => parameters
            .Add(component => component.Rows, Array.Empty<Row>())
            .Add(component => component.GetRowId, row => row.Id)
            .Add(component => component.Columns, Columns)).Find(".hl-data-grid").GetAttribute("role"));
    }

    [Fact]
    public async Task Slice1FixtureDrivesEveryCapacityAndMixedAggregate()
    {
        using var fixture = SliceFixture();
        var root = fixture.RootElement;
        var columns = SliceColumns(root);
        var cut = RenderSlice(root, columns, grouped: true);
        var callback = runtime.Module.Callback<HarborlineDataGrid<SliceRow>>();

        foreach (var expectation in root.GetProperty("visibleColumnsByCapacity").EnumerateArray())
        {
            await cut.InvokeAsync(() => callback.OnResizedAsync(expectation.GetProperty("capacity").GetInt32() * 160));
            var expectedHeaders = expectation.GetProperty("ids").EnumerateArray().Select(id => Header(root, id.GetString()!));
            Assert.Equal(expectedHeaders, cut.FindAll("[role=columnheader]").Select(element => element.TextContent));
        }

        await cut.InvokeAsync(() => callback.OnResizedAsync(root.GetProperty("visibleColumnsByCapacity")[0].GetProperty("capacity").GetInt32() * 160));
        Assert.Equal(
            root.GetProperty("branchCells").EnumerateArray().Select(value => value.GetString()),
            cut.FindAll("[data-hl-group] .hl-data-grid__group-cell-content").Select(element => element.TextContent.Trim()));
    }

    [Fact]
    public async Task ResizeConnectionUsesOuterGridAndRetainsFocusedColumnIdentity()
    {
        using var fixture = SliceFixture();
        var root = fixture.RootElement;
        var focusRecovery = root.GetProperty("focusRecovery");
        Assert.Equal("nearest-survivor-by-column-order", focusRecovery.GetProperty("rule").GetString());
        var columns = SliceColumns(root);
        var cut = RenderSlice(root, columns, grouped: false);
        var arguments = Assert.IsType<object[]>(runtime.Module.ConnectArguments);
        var container = Assert.IsType<ElementReference>(arguments[0]);
        var viewport = Assert.IsType<ElementReference>(arguments[1]);
        Assert.False(string.IsNullOrWhiteSpace(container.Id));
        Assert.NotEqual(container.Id, viewport.Id);

        cut.Find("[data-hl-row-id='a1'] [data-column-id='due']").Focus();
        var capacityThree = root.GetProperty("visibleColumnsByCapacity")[1].GetProperty("capacity").GetInt32();
        await cut.InvokeAsync(() => runtime.Module.Callback<HarborlineDataGrid<SliceRow>>().OnResizedAsync(capacityThree * 160, focusWithin: true));
        Assert.Equal("0", cut.Find("[data-hl-row-id='a1'] [data-column-id='due']").GetAttribute("tabindex"));
        Assert.Contains(runtime.Module.FocusCalls, call => call.SequenceEqual(new object?[] { "row:a1", "due" }));

        var focusedColumnId = focusRecovery.GetProperty("focusedColumnId").GetString()!;
        var expectedColumnId = focusRecovery.GetProperty("expectedColumnId").GetString()!;
        cut.Find($"[data-hl-row-id='a1'] [data-column-id='{focusedColumnId}']").Focus();
        await cut.InvokeAsync(() => runtime.Module.Callback<HarborlineDataGrid<SliceRow>>().OnResizedAsync(focusRecovery.GetProperty("afterCapacity").GetInt32() * 160, focusWithin: true));
        Assert.Equal("0", cut.Find($"[data-hl-row-id='a1'] [data-column-id='{expectedColumnId}']").GetAttribute("tabindex"));
        Assert.Contains(runtime.Module.FocusCalls, call => call.SequenceEqual(new object?[] { "row:a1", expectedColumnId }));
    }

    [Fact]
    public async Task EveryFocusRecoveryEntryPointUsesPriorOrderForEverySurvivorSet()
    {
        using var fixture = SliceFixture();
        var root = fixture.RootElement;
        Assert.Equal("nearest-survivor-by-column-order", root.GetProperty("focusRecovery").GetProperty("rule").GetString());
        var allColumns = SliceColumns(root);
        var focusOrder = new[] { 3, 0, 1, 2 };
        var survivorMasks = new[] { 0b0101 }.Concat(Enumerable.Range(1, 15).Where(mask => mask != 0b0101));

        foreach (var focusedIndex in focusOrder)
        foreach (var survivorMask in survivorMasks)
        foreach (var entryPoint in new[] { "replacement", "resize" })
        {
            var survivors = allColumns.Where((_, index) => (survivorMask & (1 << index)) != 0).ToArray();
            var focusedId = allColumns[focusedIndex].Id;
            var expectedId = survivors.Any(column => column.Id == focusedId)
                ? focusedId
                : survivors[Math.Min(focusedIndex, survivors.Length - 1)].Id;
            var resizeColumns = allColumns.Select((column, index) => column with
            {
                RemovalPriority = (survivorMask & (1 << index)) != 0 ? 1000 - index : -index,
            }).ToArray();
            using var cut = RenderSlice(root, entryPoint == "resize" ? resizeColumns : allColumns, grouped: false);
            cut.Find($"[data-hl-row-id='a1'] [data-column-id='{focusedId}']").Focus();
            runtime.Module.FocusCalls.Clear();

            if (entryPoint == "replacement")
            {
                cut.Render(parameters => parameters
                    .Add(component => component.Rows, SliceRows(root))
                    .Add(component => component.GetRowId, row => row.Id)
                    .Add(component => component.Columns, survivors));
            }
            else
            {
                await cut.InvokeAsync(() => runtime.Module.Callback<HarborlineDataGrid<SliceRow>>()
                    .OnResizedAsync(survivors.Length * 160, focusWithin: true));
            }

            Assert.Equal("0", cut.Find($"[data-hl-row-id='a1'] [data-column-id='{expectedId}']").GetAttribute("tabindex"));
            if (entryPoint == "replacement" && expectedId == focusedId) Assert.Empty(runtime.Module.FocusCalls);
            else Assert.Equal(new object?[] { "row:a1", expectedId }, runtime.Module.FocusCalls.Last());
        }
    }

    [Fact]
    public void DateDistinctnessCasesCompareInstants()
    {
        using var fixture = SliceFixture();
        var root = fixture.RootElement;
        var groupingColumn = root.GetProperty("columns")[0];
        var dateColumn = root.GetProperty("columns").EnumerateArray().Single(column => column.GetProperty("valueKind").GetString() == "date");
        var columns = SliceColumns(root).Where(column => column.Id == groupingColumn.GetProperty("id").GetString() || column.Id == dateColumn.GetProperty("id").GetString()).ToArray();

        foreach (var dateCase in root.GetProperty("dateDistinctnessCases").EnumerateArray())
        {
            var values = dateCase.GetProperty("values").EnumerateArray().Select(value => value.Clone()).ToArray();
            var rows = values.Select((value, index) => new SliceRow($"date-{index}", new Dictionary<string, JsonElement>
            {
                [groupingColumn.GetProperty("id").GetString()!] = JsonSerializer.SerializeToElement(dateCase.GetProperty("id").GetString()),
                [dateColumn.GetProperty("id").GetString()!] = value,
            })).ToArray();
            var cut = Render<HarborlineDataGrid<SliceRow>>(parameters => parameters
                .Add(component => component.Rows, rows)
                .Add(component => component.GetRowId, row => row.Id)
                .Add(component => component.Columns, columns)
                .Add(component => component.Grouping, new[] { groupingColumn.GetProperty("id").GetString()! }));
            var text = cut.Find($"[data-hl-group] [data-column-id='{dateColumn.GetProperty("id").GetString()}']").TextContent.Trim();
            Assert.Equal(dateCase.GetProperty("expected").GetString(), text.StartsWith("mixed —", StringComparison.Ordinal) ? text : "single-valued");
        }
    }

    [Fact]
    public void ProjectionIdentityCasesMatchFixtureGroupKeys()
    {
        using var fixture = SliceFixture();

        foreach (var identityCase in fixture.RootElement.GetProperty("groupingIdentityCases").EnumerateArray())
        {
            var valueKind = Enum.Parse<DataGridValueKind>(identityCase.GetProperty("valueKind").GetString()!, ignoreCase: true);
            var runtimeType = identityCase.GetProperty("runtimeType").GetString()!;
            var rows = identityCase.GetProperty("rows").EnumerateArray()
                .Select(row => new IdentityRow(row.GetProperty("id").GetString()!, IdentityValue(row.GetProperty("value"), runtimeType)))
                .ToArray();
            var column = new DataGridColumn<IdentityRow>("value", "Value", row => row.Value, 1, valueKind);
            var cut = Render<HarborlineDataGrid<IdentityRow>>(parameters => parameters
                .Add(component => component.Rows, rows)
                .Add(component => component.GetRowId, row => row.Id)
                .Add(component => component.Columns, new[] { column })
                .Add(component => component.Grouping, new[] { column.Id }));
            var actual = cut.FindAll("[data-group-id]").Select(element => element.GetAttribute("data-group-id")).ToArray();
            var expected = identityCase.GetProperty("expectedGroupKeys").EnumerateArray().Select(value => value.GetString()).ToArray();

            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void CanonicalIdentityTableCoversEveryFixtureScalar()
    {
        using var fixture = SliceFixture();

        foreach (var identityCase in fixture.RootElement.GetProperty("canonicalIdentityCases").EnumerateArray())
        {
            var valueKind = Enum.Parse<DataGridValueKind>(identityCase.GetProperty("valueKind").GetString()!, ignoreCase: true);
            var runtimeType = identityCase.GetProperty("runtimeType").GetString()!;
            var value = IdentityValue(identityCase.GetProperty("value"), runtimeType);
            var column = new DataGridColumn<IdentityRow>("value", "Value", row => row.Value, 1, valueKind);
            var cut = Render<HarborlineDataGrid<IdentityRow>>(parameters => parameters
                .Add(component => component.Rows, new[] { new IdentityRow(identityCase.GetProperty("id").GetString()!, value) })
                .Add(component => component.GetRowId, row => row.Id)
                .Add(component => component.Columns, new[] { column })
                .Add(component => component.Grouping, new[] { column.Id }));
            var identity = identityCase.GetProperty("expectedIdentity").GetString()!;
            var expected = $"root/group:0:5:value:{identity.Length}:{identity}";

            Assert.Equal(expected, cut.Find("[data-group-id]").GetAttribute("data-group-id"));
        }
    }

    [Fact]
    public void EveryClrNumericFamilyMatchesTheNodeNumberOracle()
    {
        foreach (var family in NumericFamilies())
        {
            var oracle = NodeNumberSpellings(family.Values);
            Assert.Equal(family.Values.Count, oracle.Count);

            for (var index = 0; index < family.Values.Count; index++)
            {
                var value = family.Values[index];
                var identity = $"number:{oracle[index]}";
                var column = new DataGridColumn<IdentityRow>("value", "Value", row => row.Value, 1, DataGridValueKind.Number);
                using var cut = Render<HarborlineDataGrid<IdentityRow>>(parameters => parameters
                    .Add(component => component.Rows, new[] { new IdentityRow($"{family.Name}-{index}", value) })
                    .Add(component => component.GetRowId, row => row.Id)
                    .Add(component => component.Columns, new[] { column })
                    .Add(component => component.Grouping, new[] { column.Id }));

                Assert.Equal($"root/group:0:5:value:{identity.Length}:{identity}", cut.Find("[data-group-id]").GetAttribute("data-group-id"));
            }
        }
    }

    [Fact]
    public void AggregateRepresentativeIsFirstBySourceOrder()
    {
        using var fixture = SliceFixture();
        var representative = fixture.RootElement.GetProperty("aggregateRepresentative");
        Assert.Equal("first-by-source-order", representative.GetProperty("rule").GetString());
        var rows = representative.GetProperty("values").EnumerateArray()
            .Select((value, index) => new RepresentativeRow($"representative-{index}", "same", value.GetString()!))
            .ToArray();
        var columns = new[]
        {
            new DataGridColumn<RepresentativeRow>("group", "Group", row => row.Group, 2, DataGridValueKind.Text),
            new DataGridColumn<RepresentativeRow>("value", "Value", row => row.Value, 1, DataGridValueKind.Date),
        };
        var cut = Render<HarborlineDataGrid<RepresentativeRow>>(parameters => parameters
            .Add(component => component.Rows, rows)
            .Add(component => component.GetRowId, row => row.Id)
            .Add(component => component.Columns, columns)
            .Add(component => component.Grouping, new[] { "group" }));

        Assert.Equal(representative.GetProperty("expectedLabel").GetString(), cut.Find("[data-group-id] [data-column-id='value']").TextContent.Trim());
    }

    private IRenderedComponent<HarborlineDataGrid<SliceRow>> RenderSlice(JsonElement root, IReadOnlyList<DataGridColumn<SliceRow>> columns, bool grouped) =>
        Render<HarborlineDataGrid<SliceRow>>(parameters =>
        {
            parameters
                .Add(component => component.Rows, SliceRows(root))
                .Add(component => component.GetRowId, row => row.Id)
                .Add(component => component.Columns, columns);
            if (grouped) parameters.Add(component => component.Grouping, root.GetProperty("grouping").EnumerateArray().Select(value => value.GetString()!).ToArray());
        });

    private static JsonDocument SliceFixture() => JsonDocument.Parse(File.ReadAllText(Repo("conformance/hlp.ui.data-grid/slice-1-grid.json")));

    private static SliceRow[] SliceRows(JsonElement root) => root.GetProperty("rows").EnumerateArray()
        .Select(row => new SliceRow(
            row.GetProperty("id").GetString()!,
            row.EnumerateObject().Where(property => property.Name != "id").ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)))
        .ToArray();

    private static DataGridColumn<SliceRow>[] SliceColumns(JsonElement root) => root.GetProperty("columns").EnumerateArray()
        .Select(column =>
        {
            var id = column.GetProperty("id").GetString()!;
            var kind = Enum.Parse<DataGridValueKind>(column.GetProperty("valueKind").GetString()!, ignoreCase: true);
            return new DataGridColumn<SliceRow>(id, column.GetProperty("header").GetString()!, row => SliceValue(row, id, kind), column.GetProperty("removalPriority").GetInt32(), kind);
        })
        .ToArray();

    private static object? SliceValue(SliceRow row, string id, DataGridValueKind kind)
    {
        if (!row.Values.TryGetValue(id, out var value)) return null;
        return kind switch
        {
            DataGridValueKind.Number => value.GetDecimal(),
            DataGridValueKind.Date => value.GetDateTimeOffset(),
            DataGridValueKind.Boolean => value.GetBoolean(),
            _ => value.GetString(),
        };
    }

    private static object? IdentityValue(JsonElement value, string runtimeType) => runtimeType switch
    {
        "number" => value.GetDouble(),
        "numberSpecial" => double.NaN,
        "bigInteger" => BigInteger.Parse(value.GetString()!, CultureInfo.InvariantCulture),
        "dateTimeOffset" => value.GetDateTimeOffset(),
        "dateTime" => value.GetDateTime(),
        "null" => null,
        _ => value.GetString(),
    };

    private static IReadOnlyList<NumericFamily> NumericFamilies()
    {
        var twoTo53 = 9_007_199_254_740_992L;
        var tenTo21 = BigInteger.Pow(10, 21);
        return
        [
            Family("byte", byte.MinValue, byte.MaxValue, (byte)0),
            Family("sbyte", sbyte.MinValue, sbyte.MaxValue, (sbyte)0),
            Family("short", short.MinValue, short.MaxValue, (short)0),
            Family("ushort", ushort.MinValue, ushort.MaxValue, (ushort)0),
            Family("int", int.MinValue, int.MaxValue, 0),
            Family("uint", uint.MinValue, uint.MaxValue, 0U),
            Family("long", long.MinValue, long.MaxValue, 0L, twoTo53 - 1, twoTo53, twoTo53 + 1),
            Family("ulong", ulong.MinValue, ulong.MaxValue, 0UL, (ulong)(twoTo53 - 1), (ulong)twoTo53, (ulong)(twoTo53 + 1)),
            Family("nint", nint.MinValue, nint.MaxValue, (nint)0, (nint)(twoTo53 - 1), (nint)twoTo53, (nint)(twoTo53 + 1)),
            Family("nuint", nuint.MinValue, nuint.MaxValue, (nuint)0, (nuint)(twoTo53 - 1), (nuint)twoTo53, (nuint)(twoTo53 + 1)),
            Family("Int128", Int128.MinValue, Int128.MaxValue, (Int128)0, (Int128)(twoTo53 - 1), (Int128)twoTo53, (Int128)(twoTo53 + 1), (Int128)(tenTo21 - 1), (Int128)tenTo21, (Int128)(tenTo21 + 1)),
            Family("UInt128", UInt128.MinValue, UInt128.MaxValue, (UInt128)0, (UInt128)(twoTo53 - 1), (UInt128)twoTo53, (UInt128)(twoTo53 + 1), (UInt128)(tenTo21 - 1), (UInt128)tenTo21, (UInt128)(tenTo21 + 1)),
            Family("Half", Half.MinValue, Half.MaxValue, (Half)0, BitConverter.UInt16BitsToHalf(0x8000), (Half)(twoTo53 - 1d), (Half)twoTo53, (Half)(twoTo53 + 1d), Half.BitDecrement((Half)1e-7), (Half)1e-7, Half.BitIncrement((Half)1e-7)),
            Family("float", float.MinValue, float.MaxValue, 0f, -0f, (float)(twoTo53 - 1d), (float)twoTo53, (float)(twoTo53 + 1d), float.BitDecrement(1e-7f), 1e-7f, float.BitIncrement(1e-7f)),
            Family("double", double.MinValue, double.MaxValue, 0d, -0d, twoTo53 - 1d, twoTo53, twoTo53 + 1d, 1e21 - 1d, 1e21, 1e21 + 1d, double.BitDecrement(1e-7), 1e-7, double.BitIncrement(1e-7)),
            Family("decimal", decimal.MinValue, decimal.MaxValue, decimal.Zero, new decimal(0, 0, 0, true, 0), twoTo53 - 1m, twoTo53, twoTo53 + 1m, 999999999999999999999m, 1000000000000000000000m, 1000000000000000000001m, 0.0000000999999999999999999999m, 0.0000001m, 0.0000001000000000000000000001m),
            Family("BigInteger", -BigInteger.Pow(10, 100), BigInteger.Pow(10, 100), BigInteger.Zero, twoTo53 - 1, twoTo53, twoTo53 + 1, tenTo21 - 1, tenTo21, tenTo21 + 1, BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture)),
        ];
    }

    private static NumericFamily Family<T>(string name, params T[] values) where T : struct
    {
        var boxed = values.SelectMany(value => new[] { (object)value, BoxNullable(value) }).ToArray();
        return new NumericFamily(name, boxed);
    }

    private static object BoxNullable<T>(T value) where T : struct
    {
        T? nullable = value;
        return nullable;
    }

    private static IReadOnlyList<string> NodeNumberSpellings(IReadOnlyList<object> values)
    {
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("process.stdout.write(JSON.stringify(process.argv.slice(1).map(value => String(Number(value)))))");
        start.ArgumentList.Add("--");
        foreach (var value in values) start.ArgumentList.Add(NodeInput(value));
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return JsonSerializer.Deserialize<string[]>(output)!;
    }

    private static string NodeInput(object value) => value switch
    {
        Half number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
        float number => ((double)number).ToString("R", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        decimal number when number == 0 && decimal.GetBits(number)[3] < 0 => "-0",
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        BigInteger number => number.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)!,
    };

    private sealed record NumericFamily(string Name, IReadOnlyList<object> Values);

    private static string Header(JsonElement root, string id) => root.GetProperty("columns").EnumerateArray()
        .Single(column => column.GetProperty("id").GetString() == id).GetProperty("header").GetString()!;

    private static string Repo(string relative) => Path.Combine(
        Environment.GetEnvironmentVariable("HARBORLINE_REPO_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../")),
        relative);

    private sealed class GridJsRuntime : IJSRuntime
    {
        public GridJsModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.Equal("import", identifier);
            return ValueTask.FromResult((TValue)(object)Module);
        }
    }

    private sealed class GridJsModule : IJSObjectReference
    {
        public object?[]? ConnectArguments { get; private set; }
        public List<object?[]> FocusCalls { get; } = [];

        public T Callback<T>() where T : class => Assert.IsType<DotNetObjectReference<T>>(ConnectArguments![2]).Value;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "connect")
            {
                ConnectArguments = args;
                return ValueTask.FromResult((TValue)(object)this);
            }
            if (identifier == "focus") FocusCalls.Add(args ?? []);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
