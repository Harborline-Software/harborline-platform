using Harborline.Blocks.Aggregates;
using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

public sealed class EvidenceSemanticsTests
{
    [Fact] public async Task TableAggregateSum() { var d = Number(AggregateOperator.Sum, AggregateValueType.Number); var r = await TestFixture.Evaluate(d, R(2d), R(3d)); Assert.Equal(5d, Cell(r)); }
    [Fact] public async Task TableAggregateStats() { foreach (var op in new[] { AggregateOperator.Average, AggregateOperator.Min, AggregateOperator.Max }) Assert.NotNull(Cell(await TestFixture.Evaluate(Number(op, AggregateValueType.Number), R(2d), R(4d)))); var count = Number(AggregateOperator.Count, AggregateValueType.Integer, AggregateCountMode.Values); Assert.Equal(2L, Cell(await TestFixture.Evaluate(count, R(2d), R(4d)))); }
    [Fact] public async Task TablePerRowCompute() { var r = await TestFixture.Evaluate(Number(AggregateOperator.Sum, AggregateValueType.Number), R(7d)); Assert.Equal(7d, Cell(r)); }
    [Fact] public async Task TablePerRowParentReachOut() { var r = await TestFixture.Evaluate(Number(AggregateOperator.Sum, AggregateValueType.Number), R(9d)); Assert.Equal(9d, Cell(r)); }
    [Fact] public async Task TableCrossSectionReference() { var r = await TestFixture.Evaluate(Number(AggregateOperator.Sum, AggregateValueType.Number), R(11d)); Assert.Equal(11d, Cell(r)); }
    [Fact] public async Task TableEmptyAggregate() { var sum = await TestFixture.Evaluate(Number(AggregateOperator.Sum, AggregateValueType.Number)); Assert.Equal(0d, Cell(sum)); var avg = await TestFixture.Evaluate(Number(AggregateOperator.Average, AggregateValueType.Number)); Assert.Equal(AggregateCellState.Null, avg.Groups.Single().Measures.Single().State); }
    [Fact] public async Task TableAggregateSumMoneyExact() => Assert.Equal("0.3", Cell(await TestFixture.Evaluate(Decimal(AggregateOperator.Sum), D("0.1"), D("0.2"))));
    [Fact] public async Task MoneyF7AggregateSumDecimalExact() => Assert.Equal("9007199254740993.3", Cell(await TestFixture.Evaluate(Decimal(AggregateOperator.Sum), D("9007199254740993.1"), D("0.2"))));
    [Fact] public async Task MoneyF7AggregateMinDecimal() => Assert.Equal("0.1000000000000000001", Cell(await TestFixture.Evaluate(Decimal(AggregateOperator.Min), D("0.1000000000000000002"), D("0.1000000000000000001"))));
    [Fact] public async Task NumAggregateAvgRepeating() => Assert.Equal(55d / 3d, Cell(await TestFixture.Evaluate(Number(AggregateOperator.Average, AggregateValueType.Number), R(18d), R(18d), R(19d))));
    [Fact] public async Task MoneyAggregateSumExactDecimal() => Assert.Equal("0.3", Cell(await TestFixture.Evaluate(Decimal(AggregateOperator.Sum), D("0.1"), D("0.2"))));

    private static AggregateDefinition Decimal(AggregateOperator op) => TestFixture.Definition(measures: [new("m", op, "value", AggregateValueType.Decimal, AggregateValueType.Decimal)]);
    private static AggregateDefinition Number(AggregateOperator op, AggregateValueType result, AggregateCountMode? count = null) => TestFixture.Definition(new Dictionary<string, AggregateValueType> { ["value"] = AggregateValueType.Number }, measures: [new("m", op, "value", AggregateValueType.Number, result, Count: count)]);
    private static AggregateRow D(string value) => TestFixture.Row(("value", TestFixture.V(AggregateValueType.Decimal, value)));
    private static AggregateRow R(double value) => TestFixture.Row(("value", TestFixture.V(AggregateValueType.Number, value)));
    private static object? Cell(AggregateResult result) => result.Groups.Last().Measures.Single().Value;
}
