using Harborline.Blocks.Aggregates;
using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

/// <summary>Each refusal in the validator is pinned by a definition with exactly one fault, so a weakened guard accepts it.</summary>
public sealed class ValidatorRefusalTests
{
    private static readonly AggregateHostBounds Host = new(1000, 1000, 1000);
    private static readonly AggregateValueType D = AggregateValueType.Decimal;

    private static void Refuses(AggregateDefinition definition)
    {
        var error = Assert.Throws<AggregateException>(() => new AggregateDefinitionValidator().Validate(definition, Host));
        Assert.Equal("aggregates.definition.invalid", error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    private static void Accepts(AggregateDefinition definition) => new AggregateDefinitionValidator().Validate(definition, Host);
    private static AggregateDefinition Measure(AggregateMeasure measure, Dictionary<string, AggregateValueType>? fields = null) => TestFixture.Definition(fields, measures: [measure]);
    private static AggregateMeasure M(AggregateOperator op, string? field, AggregateValueType? input, AggregateValueType result, AggregateCountMode? count = null) => new("m", op, field, input, result, Count: count);
    private static AggregateDefinition Filtered(AggregateFilter filter) => TestFixture.Definition(filter: filter);
    private static AggregateComparisonFilter Cmp(string field, AggregateComparisonOperator op, AggregateValue? value = null, IReadOnlyList<AggregateValue>? values = null) => new(field, op, value, values);
    private static AggregateDefinition Dims(int count, AggregateTotals totals)
    {
        var fields = new Dictionary<string, AggregateValueType> { ["a"] = AggregateValueType.String, ["b"] = AggregateValueType.String, ["c"] = AggregateValueType.String, ["value"] = D };
        var all = new[] { "a", "b", "c" }.Select(name => new AggregateDimension(name, name, AggregateValueType.String, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc)).Take(count).ToArray();
        return TestFixture.Definition(fields, all, totals: totals);
    }

    [Fact] public void BaselineDefinitionIsAccepted() => Accepts(TestFixture.Definition());
    [Fact] public void UnsupportedSchemaVersionIsRefused() => Refuses(TestFixture.Definition() with { SchemaVersion = 2 });
    [Fact] public void BlankDefinitionIdIsRefused() => Refuses(TestFixture.Definition() with { DefinitionId = " " });
    [Fact] public void ZeroRevisionIsRefused() => Refuses(TestFixture.Definition() with { Revision = 0 });
    [Fact] public void NegativeRevisionIsRefused() => Refuses(TestFixture.Definition() with { Revision = -1 });
    [Fact] public void BlankSourceRefIsRefused() => Refuses(TestFixture.Definition() with { Source = new AggregateSourceDefinition("", new Dictionary<string, AggregateValueType> { ["value"] = D }) });
    [Fact] public void NoMeasuresIsRefused() => Refuses(TestFixture.Definition(measures: []));
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(-1, 1, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 1, -1)]
    public void NonPositiveBoundsAreRefused(int rows, int groups, int cells) => Refuses(TestFixture.Definition(bounds: new AggregateBounds(rows, groups, cells)));
    [Theory]
    [InlineData(1001, 1, 1)]
    [InlineData(1, 1001, 1)]
    [InlineData(1, 1, 1001)]
    public void EachBoundAboveHostPolicyIsRefused(int rows, int groups, int cells) => Refuses(TestFixture.Definition(bounds: new AggregateBounds(rows, groups, cells)));
    [Fact] public void BoundsEqualToHostPolicyAreAccepted() => Accepts(TestFixture.Definition(bounds: new AggregateBounds(1000, 1000, 1000)));
    [Fact] public void BlankMeasureKeyIsRefused() => Refuses(Measure(new(" ", AggregateOperator.Sum, "value", D, D)));
    [Fact] public void DimensionOfAnotherTypeThanItsFieldIsRefused() => Refuses(TestFixture.Definition(grouping: [new("g", "value", AggregateValueType.String, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc)]));

    [Fact] public void RowCountIsAccepted() => Accepts(Measure(M(AggregateOperator.Count, null, null, AggregateValueType.Integer, AggregateCountMode.Rows)));
    [Fact] public void RowCountWithFieldIsRefused() => Refuses(Measure(M(AggregateOperator.Count, "value", null, AggregateValueType.Integer, AggregateCountMode.Rows)));
    [Fact] public void RowCountWithInputTypeIsRefused() => Refuses(Measure(M(AggregateOperator.Count, null, D, AggregateValueType.Integer, AggregateCountMode.Rows)));
    [Fact] public void RowCountWithNonIntegerResultIsRefused() => Refuses(Measure(M(AggregateOperator.Count, null, null, D, AggregateCountMode.Rows)));
    [Fact] public void MeasureWithoutFieldIsRefused() => Refuses(Measure(M(AggregateOperator.Sum, null, D, D)));
    [Fact] public void MeasureWithoutInputTypeIsRefused() => Refuses(Measure(M(AggregateOperator.Sum, "value", null, D)));
    [Fact] public void MeasureOverUndeclaredFieldIsRefused() => Refuses(Measure(M(AggregateOperator.Sum, "absent", D, D)));
    [Fact] public void ValueCountIsAccepted() => Accepts(Measure(M(AggregateOperator.Count, "value", D, AggregateValueType.Integer, AggregateCountMode.Values)));
    [Fact] public void CountWithoutModeIsRefused() => Refuses(Measure(M(AggregateOperator.Count, "value", D, AggregateValueType.Integer)));
    [Fact] public void ValueCountWithNonIntegerResultIsRefused() => Refuses(Measure(M(AggregateOperator.Count, "value", D, D, AggregateCountMode.Values)));
    [Fact] public void NonCountMeasureWithCountModeIsRefused() => Refuses(Measure(M(AggregateOperator.Sum, "value", D, D, AggregateCountMode.Values)));
    [Fact] public void SumResultOfAnotherTypeIsRefused() => Refuses(Measure(M(AggregateOperator.Sum, "value", D, AggregateValueType.Number)));
    [Fact] public void AverageOfIntegerReturnsNumber() => Accepts(Measure(M(AggregateOperator.Average, "n", AggregateValueType.Integer, AggregateValueType.Number), new() { ["n"] = AggregateValueType.Integer }));
    [Fact] public void AverageOfIntegerReturningIntegerIsRefused() => Refuses(Measure(M(AggregateOperator.Average, "n", AggregateValueType.Integer, AggregateValueType.Integer), new() { ["n"] = AggregateValueType.Integer }));
    [Fact] public void AverageOfNumberReturnsNumber() => Accepts(Measure(M(AggregateOperator.Average, "n", AggregateValueType.Number, AggregateValueType.Number), new() { ["n"] = AggregateValueType.Number }));
    [Fact] public void AverageOfDecimalReturnsDecimal() => Accepts(Measure(M(AggregateOperator.Average, "value", D, D)));
    [Theory]
    [InlineData(AggregateOperator.Sum)]
    [InlineData(AggregateOperator.Average)]
    public void NonNumericSumAndAverageAreRefused(AggregateOperator op) => Refuses(Measure(M(op, "s", AggregateValueType.String, AggregateValueType.String), new() { ["s"] = AggregateValueType.String }));
    [Theory]
    [InlineData(AggregateOperator.Min)]
    [InlineData(AggregateOperator.Max)]
    public void BooleanMinAndMaxAreRefused(AggregateOperator op) => Refuses(Measure(M(op, "b", AggregateValueType.Boolean, AggregateValueType.Boolean), new() { ["b"] = AggregateValueType.Boolean }));
    [Theory]
    [InlineData(AggregateOperator.Min)]
    [InlineData(AggregateOperator.Max)]
    public void StringMinAndMaxAreAccepted(AggregateOperator op) => Accepts(Measure(M(op, "s", AggregateValueType.String, AggregateValueType.String), new() { ["s"] = AggregateValueType.String }));

    [Fact] public void FilterOverUndeclaredFieldIsRefused() => Refuses(Filtered(Cmp("absent", AggregateComparisonOperator.IsNull)));
    [Fact] public void MembershipWithoutValuesIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.In)));
    [Fact] public void MembershipWithSingleOperandIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.In, TestFixture.V(D, "1"), [TestFixture.V(D, "1")])));
    [Fact] public void MembershipWithMistypedValueIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.In, values: [TestFixture.V(D, "1"), TestFixture.V(AggregateValueType.Number, 1d)])));
    [Fact] public void MembershipIsAccepted() => Accepts(Filtered(Cmp("value", AggregateComparisonOperator.In, values: [TestFixture.V(D, "1")])));
    [Theory]
    [InlineData(AggregateComparisonOperator.IsNull)]
    [InlineData(AggregateComparisonOperator.IsNotNull)]
    public void NullTestIsAcceptedWithoutOperand(AggregateComparisonOperator op) => Accepts(Filtered(Cmp("value", op)));
    [Theory]
    [InlineData(AggregateComparisonOperator.IsNull)]
    [InlineData(AggregateComparisonOperator.IsNotNull)]
    public void NullTestWithOperandIsRefused(AggregateComparisonOperator op) => Refuses(Filtered(Cmp("value", op, TestFixture.V(D, "1"))));
    [Fact] public void NullTestWithValuesIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.IsNull, values: [])));
    [Fact] public void ComparisonWithoutOperandIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.Eq)));
    [Fact] public void ComparisonWithValuesIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.Eq, TestFixture.V(D, "1"), [TestFixture.V(D, "1")])));
    [Fact] public void ComparisonWithNullOperandIsRefused() => Refuses(Filtered(Cmp("value", AggregateComparisonOperator.Eq, AggregateValue.Null(D))));
    [Fact] public void ComparisonIsAccepted() => Accepts(Filtered(Cmp("value", AggregateComparisonOperator.Eq, TestFixture.V(D, "1"))));
    [Fact] public void InvalidFilterUnderAllIsRefused() => Refuses(Filtered(new AggregateAllFilter([Cmp("absent", AggregateComparisonOperator.IsNull)])));
    [Fact] public void InvalidFilterUnderAnyIsRefused() => Refuses(Filtered(new AggregateAnyFilter([Cmp("absent", AggregateComparisonOperator.IsNull)])));
    [Fact] public void InvalidFilterUnderNotIsRefused() => Refuses(Filtered(new AggregateNotFilter(Cmp("absent", AggregateComparisonOperator.IsNull))));
    [Fact] public void UnknownFilterNodeIsRefused() => Refuses(Filtered(new UnknownFilter()));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(4)]
    public void SubtotalDepthOutsideProperPrefixIsRefused(int depth) => Refuses(Dims(3, new AggregateTotals([depth], false)));
    [Fact] public void ProperPrefixSubtotalsAreAccepted() => Accepts(Dims(3, new AggregateTotals([1, 2], false)));
    [Fact] public void SubtotalWithoutGroupingIsRefused() => Refuses(Dims(0, new AggregateTotals([0], false)));
    [Fact] public void RepeatedSubtotalDepthAmongOthersIsRefused() => Refuses(Dims(3, new AggregateTotals([1, 2, 2], false)));

    private sealed record UnknownFilter : AggregateFilter;
}
