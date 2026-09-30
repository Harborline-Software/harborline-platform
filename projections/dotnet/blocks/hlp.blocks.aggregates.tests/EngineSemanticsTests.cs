using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

/// <summary>Filter, fold, grouping, ordering and wire-value behaviour of the evaluator.</summary>
public sealed class EngineSemanticsTests
{
    private static readonly AggregateValueType Str = AggregateValueType.String, Int = AggregateValueType.Integer, Num = AggregateValueType.Number, Dec = AggregateValueType.Decimal;
    private static readonly AggregateComparisonOperator Eq = AggregateComparisonOperator.Eq;

    private static AggregateValue I(long value) => TestFixture.V(Int, value);
    private static AggregateValue NullInt => AggregateValue.Null(Int);
    private static AggregateComparisonFilter C(AggregateComparisonOperator op, AggregateValue? value = null, IReadOnlyList<AggregateValue>? values = null) => new("n", op, value, values);

    // Rows n = 1, 2, 3, null; counts the rows a filter keeps.
    private static async Task<long> Kept(AggregateFilter filter, params AggregateValue[] rows)
    {
        var definition = TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["n"] = Int }, measures: [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)], filter: filter);
        var result = await TestFixture.Evaluate(definition, (rows.Length == 0 ? [I(1), I(2), I(3), NullInt] : rows).Select(value => TestFixture.Row(("n", value))).ToArray());
        return (long)result.Groups.Last().Measures.Single().Value!;
    }

    [Fact] public async Task EqKeepsEqualRows() => Assert.Equal(1, await Kept(C(Eq, I(2))));
    [Fact] public async Task NeqKeepsDifferentNonNullRows() => Assert.Equal(2, await Kept(C(AggregateComparisonOperator.Neq, I(2))));
    [Fact] public async Task LtKeepsSmallerRows() => Assert.Equal(1, await Kept(C(AggregateComparisonOperator.Lt, I(2))));
    [Fact] public async Task LteKeepsSmallerOrEqualRows() => Assert.Equal(2, await Kept(C(AggregateComparisonOperator.Lte, I(2))));
    [Fact] public async Task GtKeepsLargerRows() => Assert.Equal(1, await Kept(C(AggregateComparisonOperator.Gt, I(2))));
    [Fact] public async Task GteKeepsLargerOrEqualRows() => Assert.Equal(2, await Kept(C(AggregateComparisonOperator.Gte, I(2))));
    [Fact] public async Task InKeepsMembers() => Assert.Equal(2, await Kept(C(AggregateComparisonOperator.In, values: [I(1), I(3)])));
    [Fact] public async Task InWithNoMembersKeepsNothing() => Assert.Equal(0, await Kept(C(AggregateComparisonOperator.In, values: [])));
    [Fact] public async Task IsNullKeepsNullRows() => Assert.Equal(1, await Kept(C(AggregateComparisonOperator.IsNull)));
    [Fact] public async Task IsNotNullKeepsNonNullRows() => Assert.Equal(3, await Kept(C(AggregateComparisonOperator.IsNotNull)));
    [Fact] public async Task NotInvertsItsChild() => Assert.Equal(3, await Kept(new AggregateNotFilter(C(Eq, I(2)))));
    [Fact] public async Task AnyKeepsRowsMatchingAnyChild() => Assert.Equal(2, await Kept(new AggregateAnyFilter([C(Eq, I(1)), C(Eq, I(3))])));
    [Fact] public async Task AllKeepsRowsMatchingEveryChild() => Assert.Equal(1, await Kept(new AggregateAllFilter([C(AggregateComparisonOperator.Gt, I(1)), C(AggregateComparisonOperator.Lt, I(3))])));
    [Fact] public async Task AllWithAContradictionKeepsNothing() => Assert.Equal(0, await Kept(new AggregateAllFilter([C(Eq, I(1)), C(Eq, I(2))])));
    [Fact] public async Task EmptyAllKeepsEveryRow() => Assert.Equal(4, await Kept(new AggregateAllFilter([])));
    [Fact] public async Task EmptyAnyKeepsNothing() => Assert.Equal(0, await Kept(new AggregateAnyFilter([])));

    [Theory]
    [InlineData(AggregateComparisonOperator.Eq)]
    [InlineData(AggregateComparisonOperator.Neq)]
    [InlineData(AggregateComparisonOperator.Lt)]
    [InlineData(AggregateComparisonOperator.Gte)]
    public async Task ComparisonsNeverKeepAnUnavailableValue(AggregateComparisonOperator op) => Assert.Equal(0, await Kept(C(op, I(2)), AggregateValue.Unavailable(Int)));

    [Fact]
    public async Task InDoesNotKeepUnavailableOrNullValues() => Assert.Equal(0, await Kept(C(AggregateComparisonOperator.In, values: [I(1)]), AggregateValue.Unavailable(Int), NullInt));

    [Fact]
    public async Task FilterOperandsAreCanonicalizedLikeRowValues()
    {
        var definition = TestFixture.Definition(filter: new AggregateComparisonFilter("value", Eq, TestFixture.V(Dec, "2.50")), measures: [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)]);
        var result = await TestFixture.Evaluate(definition, Row(Dec, "2.5"), Row(Dec, "2.6"));
        Assert.Equal(1L, result.Groups.Last().Measures.Single().Value);
        static AggregateRow Row(AggregateValueType type, object value) => TestFixture.Row(("value", TestFixture.V(type, value)));
    }

    public static TheoryData<AggregateValueType, object, object, object> Ordered => new()
    {
        { AggregateValueType.String, "B", "a", "b" },
        { AggregateValueType.Boolean, false, true, true },
        { AggregateValueType.Integer, 9L, 10L, 100L },
        { AggregateValueType.Number, 1.5d, 2.5d, 10d },
        { AggregateValueType.Decimal, "1", "2.50", "10" },
        { AggregateValueType.Date, "2024-01-05", "2024-02-05", "2024-10-05" },
        { AggregateValueType.DateTime, "2024-01-05T08:00:00Z", "2024-01-05T09:00:00+00:00", "2024-01-06T00:00:00Z" },
    };

    [Theory, MemberData(nameof(Ordered))]
    public async Task EveryTypeComparesByItsOwnOrder(AggregateValueType type, object low, object mid, object high)
    {
        if (type == AggregateValueType.Boolean) { low = false; mid = true; high = true; }
        async Task<long> Count(AggregateComparisonOperator op, object operand)
        {
            var definition = TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["f"] = type }, measures: [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)], filter: new AggregateComparisonFilter("f", op, TestFixture.V(type, operand)));
            var result = await TestFixture.Evaluate(definition, new[] { low, mid, high }.Select(value => TestFixture.Row(("f", TestFixture.V(type, value)))).ToArray());
            return (long)result.Groups.Last().Measures.Single().Value!;
        }
        if (type == AggregateValueType.Boolean)
        {
            Assert.Equal(1, await Count(AggregateComparisonOperator.Lt, true));
            Assert.Equal(2, await Count(AggregateComparisonOperator.Eq, true));
            return;
        }
        Assert.Equal(1, await Count(AggregateComparisonOperator.Lt, mid));
        Assert.Equal(1, await Count(AggregateComparisonOperator.Gt, mid));
        Assert.Equal(1, await Count(Eq, mid));
        Assert.Equal(2, await Count(AggregateComparisonOperator.Gte, mid));
    }

    public static TheoryData<AggregateValueType, object, object> Wire => new()
    {
        { AggregateValueType.Integer, 5, 5L },
        { AggregateValueType.Integer, 5L, 5L },
        { AggregateValueType.Number, 1.5f, 1.5d },
        { AggregateValueType.Decimal, "1.50", "1.5" },
        { AggregateValueType.Decimal, "-0.05", "-0.05" },
        { AggregateValueType.Decimal, new CanonicalDecimal(150, 2), "1.5" },
        { AggregateValueType.Date, "2024-01-05", "2024-01-05" },
        { AggregateValueType.Date, new DateOnly(2024, 1, 5), "2024-01-05" },
        { AggregateValueType.DateTime, new DateTimeOffset(2024, 1, 5, 8, 0, 0, TimeSpan.Zero), "2024-01-05T08:00:00Z" },
        { AggregateValueType.DateTime, new DateTimeOffset(2024, 1, 5, 8, 0, 0, TimeSpan.Zero).AddTicks(5_000_000), "2024-01-05T08:00:00.5Z" },
        { AggregateValueType.DateTime, "2024-01-05T10:00:00+02:00", "2024-01-05T08:00:00Z" },
        { AggregateValueType.DateTime, "2024-01-05T08:00:00", "2024-01-05T08:00:00Z" },
        { AggregateValueType.String, "x", "x" },
        { AggregateValueType.Boolean, true, true },
    };

    [Theory, MemberData(nameof(Wire))]
    public async Task GroupKeysCarryTheCanonicalWireValue(AggregateValueType type, object raw, object expected)
    {
        var definition = TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["f"] = type }, [new("f", "f", type, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc)], [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)], totals: new AggregateTotals([], false));
        var key = (await TestFixture.Evaluate(definition, TestFixture.Row(("f", TestFixture.V(type, raw))))).Groups.Single().Keys.Single();
        Assert.Equal(type, key.Type);
        Assert.Equal(expected, key.Value);
    }

    private static AggregateDefinition Fold(AggregateOperator op, AggregateValueType input, AggregateValueType? result = null, AggregateNullPolicy nulls = AggregateNullPolicy.Ignore, AggregateCountMode? count = null) => TestFixture.Definition(
        new Dictionary<string, AggregateValueType> { ["value"] = input },
        measures: [new("m", op, "value", input, result ?? (op == AggregateOperator.Count ? Int : input), nulls, Count: count)]);
    private static AggregateRow R(AggregateValue value) => TestFixture.Row(("value", value));
    private static async Task<AggregateCell> Cell(AggregateDefinition definition, params AggregateValue[] values) => (await TestFixture.Evaluate(definition, values.Select(R).ToArray())).Groups.Last().Measures.Single();

    [Fact] public async Task IntegerSumAdds() => Assert.Equal(6L, (await Cell(Fold(AggregateOperator.Sum, Int), I(1), I(2), I(3))).Value);
    [Fact] public async Task IntegerAverageIsANumber() { var cell = await Cell(Fold(AggregateOperator.Average, Int, Num), I(1), I(2)); Assert.Equal(1.5d, cell.Value); Assert.Equal(Num, cell.Type); }
    [Fact] public async Task DecimalAverageIsExact() => Assert.Equal("0.5", (await Cell(Fold(AggregateOperator.Average, Dec), TestFixture.V(Dec, "1"), TestFixture.V(Dec, "0"))).Value);
    [Fact] public async Task NumberSumAdds() => Assert.Equal(0.75d, (await Cell(Fold(AggregateOperator.Sum, Num), TestFixture.V(Num, 0.5d), TestFixture.V(Num, 0.25d))).Value);
    [Fact] public async Task MinPicksTheSmallest() => Assert.Equal(1L, (await Cell(Fold(AggregateOperator.Min, Int), I(3), I(1), I(2))).Value);
    [Fact] public async Task MaxPicksTheLargest() => Assert.Equal(3L, (await Cell(Fold(AggregateOperator.Max, Int), I(1), I(3), I(2))).Value);
    [Fact] public async Task StringMinAndMaxAreOrdinal() { var values = new[] { TestFixture.V(Str, "b"), TestFixture.V(Str, "a"), TestFixture.V(Str, "B") }; Assert.Equal("B", (await Cell(Fold(AggregateOperator.Min, Str), values)).Value); Assert.Equal("b", (await Cell(Fold(AggregateOperator.Max, Str), values)).Value); }
    [Fact] public async Task DecimalMinAndMaxCarryWireText() { var values = new[] { TestFixture.V(Dec, "2.50"), TestFixture.V(Dec, "10") }; Assert.Equal("2.5", (await Cell(Fold(AggregateOperator.Min, Dec), values)).Value); Assert.Equal("10", (await Cell(Fold(AggregateOperator.Max, Dec), values)).Value); }
    [Fact] public async Task NullInputsAreIgnoredByDefault() => Assert.Equal(4L, (await Cell(Fold(AggregateOperator.Sum, Int), I(1), NullInt, I(3))).Value);
    [Fact] public async Task PropagateWithoutNullsStillFolds() { var cell = await Cell(Fold(AggregateOperator.Sum, Int, nulls: AggregateNullPolicy.Propagate), I(1), I(3)); Assert.Equal(AggregateCellState.Value, cell.State); Assert.Equal(4L, cell.Value); }
    [Fact] public async Task PropagateWithANullYieldsANullCellOfTheResultType() { var cell = await Cell(Fold(AggregateOperator.Sum, Int, nulls: AggregateNullPolicy.Propagate), I(1), NullInt); Assert.Equal(AggregateCellState.Null, cell.State); Assert.Null(cell.Value); Assert.Equal(Int, cell.Type); }
    [Fact] public async Task UnavailablePropagatesWithoutAValue() { var definition = Fold(AggregateOperator.Average, Int, Num) with { Measures = [new("m", AggregateOperator.Average, "value", Int, Num, Unavailable: AggregateUnavailablePolicy.Propagate)] }; var cell = await Cell(definition, I(1), AggregateValue.Unavailable(Int)); Assert.Equal(AggregateCellState.Unavailable, cell.State); Assert.Null(cell.Value); Assert.Equal(Num, cell.Type); }

    [Fact] public async Task RowCountCountsEveryRowIncludingNulls() => Assert.Equal(3L, (await TestFixture.Evaluate(TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["value"] = Int }, measures: [new("c", AggregateOperator.Count, null, null, Int, Count: AggregateCountMode.Rows)]), R(I(1)), R(NullInt), R(I(3)))).Groups.Last().Measures.Single().Value);
    [Fact] public async Task ValueCountSkipsNulls() { var cell = await Cell(Fold(AggregateOperator.Count, Int, count: AggregateCountMode.Values), I(1), NullInt, I(3)); Assert.Equal(2L, cell.Value); Assert.Equal(Int, cell.Type); }
    [Fact] public async Task ValueCountOfNothingIsZero() => Assert.Equal(0L, (await Cell(Fold(AggregateOperator.Count, Int, count: AggregateCountMode.Values), NullInt)).Value);

    [Fact] public async Task IntegerSumOfNoValuesIsZero() => Assert.Equal(0L, (await Cell(Fold(AggregateOperator.Sum, Int), NullInt)).Value);
    [Fact] public async Task NumberSumOfNoValuesIsZero() => Assert.Equal(0d, (await Cell(Fold(AggregateOperator.Sum, Num), AggregateValue.Null(Num))).Value);
    [Theory]
    [InlineData(AggregateOperator.Min)]
    [InlineData(AggregateOperator.Max)]
    [InlineData(AggregateOperator.Average)]
    public async Task OtherReductionsOfNoValuesAreNull(AggregateOperator op) { var cell = await Cell(Fold(op, Int, op == AggregateOperator.Average ? Num : Int), NullInt); Assert.Equal(AggregateCellState.Null, cell.State); Assert.Null(cell.Value); }

    private static AggregateDefinition Grouped(AggregateSortDirection first = AggregateSortDirection.Asc, AggregateValueType type = AggregateValueType.String, AggregateSortDirection second = AggregateSortDirection.Asc, AggregateTotals? totals = null) => TestFixture.Definition(
        new Dictionary<string, AggregateValueType> { ["a"] = type, ["b"] = Str, ["value"] = Dec },
        [new("a", "a", type, AggregateDimensionNullPolicy.Bucket, first), new("b", "b", Str, AggregateDimensionNullPolicy.Bucket, second)],
        totals: totals ?? new AggregateTotals([], false));
    private static AggregateRow G(AggregateValue a, string b, string value = "1") => TestFixture.Row(("a", a), ("b", TestFixture.V(Str, b)), ("value", TestFixture.V(Dec, value)));
    private static AggregateValue S(string value) => TestFixture.V(Str, value);
    private static string Shape(AggregateResult result) => string.Join(" ", result.Groups.Select(group => $"{group.Kind}{group.Level}[{string.Join(",", group.Keys.Select(key => key.Value ?? "null"))}]={group.Measures.Single().Value}"));

    [Fact]
    public async Task EachGroupSumsItsOwnRows()
    {
        var result = await TestFixture.Evaluate(Grouped(), G(S("A"), "x", "1"), G(S("A"), "x", "2"), G(S("B"), "x", "10"));
        Assert.Equal("Detail2[A,x]=3 Detail2[B,x]=10", Shape(result));
    }

    [Fact]
    public async Task SubtotalsFollowTheirDetailRowsAndTheGrandTotalIsLast()
    {
        var result = await TestFixture.Evaluate(Grouped(totals: new AggregateTotals([1], true)), G(S("B"), "x", "4"), G(S("A"), "y", "2"), G(S("A"), "x", "1"));
        Assert.Equal("Detail2[A,x]=1 Detail2[A,y]=2 Subtotal1[A]=3 Detail2[B,x]=4 Subtotal1[B]=4 GrandTotal0[]=7", Shape(result));
    }

    [Fact]
    public async Task DescendingDimensionReversesItsOrderAndLaterDimensionsBreakTies()
    {
        var result = await TestFixture.Evaluate(Grouped(AggregateSortDirection.Desc, second: AggregateSortDirection.Desc), G(S("A"), "x"), G(S("B"), "x"), G(S("B"), "y"));
        Assert.Equal("Detail2[B,y]=1 Detail2[B,x]=1 Detail2[A,x]=1", Shape(result));
    }

    [Fact]
    public async Task IntegerKeysOrderNumericallyNotAsText()
    {
        var rows = new[] { 10L, 9L, 100L }.Select(n => G(I(n), "x")).ToArray();
        Assert.Equal("Detail2[9,x]=1 Detail2[10,x]=1 Detail2[100,x]=1", Shape(await TestFixture.Evaluate(Grouped(type: Int), rows)));
        Assert.Equal("Detail2[100,x]=1 Detail2[10,x]=1 Detail2[9,x]=1", Shape(await TestFixture.Evaluate(Grouped(AggregateSortDirection.Desc, Int), rows)));
    }

    [Fact]
    public async Task NullKeysFormTheirOwnGroupFirstWhenAscendingAndLastWhenDescending()
    {
        foreach (var rows in new[] { new[] { G(S("a"), "x"), G(AggregateValue.Null(Str), "x") }, new[] { G(AggregateValue.Null(Str), "x"), G(S("a"), "x") } })
        {
            Assert.Equal(new object?[] { null, "a" }, (await TestFixture.Evaluate(Grouped(), rows)).Groups.Select(group => group.Keys[0].Value));
            Assert.Equal(new object?[] { "a", null }, (await TestFixture.Evaluate(Grouped(AggregateSortDirection.Desc), rows)).Groups.Select(group => group.Keys[0].Value));
        }
    }

    [Fact]
    public async Task KeysThatDifferOnlyAcrossTheSeparatorStayDistinctGroups()
    {
        var result = await TestFixture.Evaluate(Grouped(), G(S("aString:b"), "c"), G(S("a"), "bString:c"));
        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public async Task NullKeyAndAPlainKeyAreNotMerged()
    {
        var result = await TestFixture.Evaluate(Grouped(), G(AggregateValue.Null(Str), "x"), G(S("a"), "x"));
        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public async Task ExcludedNullRowsAreNotCounted()
    {
        var definition = Grouped() with { Grouping = [new("a", "a", Str, AggregateDimensionNullPolicy.Exclude, AggregateSortDirection.Asc)] };
        Assert.Equal("Detail1[A]=1", Shape(await TestFixture.Evaluate(definition, G(S("A"), "x"), G(AggregateValue.Null(Str), "x"))));
    }

    [Fact] public async Task DecimalSumOfNoValuesIsTheWireStringZero() { var cell = await Cell(Fold(AggregateOperator.Sum, Dec), AggregateValue.Null(Dec)); Assert.Equal("0", cell.Value); Assert.IsType<string>(cell.Value); }

    [Fact]
    public async Task IntegerSumOverflowIsACodedRefusal()
    {
        var error = await Assert.ThrowsAsync<AggregateException>(() => Cell(Fold(AggregateOperator.Sum, Int), I(long.MaxValue), I(1)));
        Assert.Equal("aggregates.source.contract_mismatch", error.Code);
    }

    [Fact]
    public async Task IntegerAverageOverflowIsACodedRefusal()
    {
        var error = await Assert.ThrowsAsync<AggregateException>(() => Cell(Fold(AggregateOperator.Average, Int, Num), I(long.MaxValue), I(1)));
        Assert.Equal("aggregates.source.contract_mismatch", error.Code);
    }

    [Fact]
    public async Task NullKeyAndTheTextNullAreDistinctGroups()
    {
        var result = await TestFixture.Evaluate(Grouped(), G(AggregateValue.Null(Str), "x"), G(S("null"), "x"));
        Assert.Equal(2, result.Groups.Count);
    }

    [Fact]
    public async Task KeyPairsThatCollideUnderPipeJoiningStayDistinctGroups()
    {
        var result = await TestFixture.Evaluate(Grouped(), G(S("a|String:b"), "c"), G(S("a"), "b|String:c"));
        Assert.Equal(2, result.Groups.Count);
    }
}
