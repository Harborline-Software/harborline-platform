using System.Text.Json;
using Harborline.Blocks.Aggregates;
using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

public sealed class ContractTests
{
    private static readonly AggregateHostBounds Host = new(1000, 1000, 1000);
    private readonly AggregateDefinitionValidator validator = new();

    [Fact] public void CanonicalDefinitionRoundTrips() { var definition = TestFixture.Definition(); var json = JsonSerializer.Serialize(definition, AggregateJson.CreateOptions()); var decoded = JsonSerializer.Deserialize<AggregateDefinition>(json, AggregateJson.CreateOptions()); Assert.Equal(json, JsonSerializer.Serialize(decoded, AggregateJson.CreateOptions())); }
    [Fact] public void CanonicalResultRoundTrips() { var result = new AggregateResult("id", 1, "s", [new(AggregateGroupKind.GrandTotal, 0, [], [new("m", AggregateValueType.Decimal, AggregateCellState.Value, "0.3")])]); var json = JsonSerializer.Serialize(result, AggregateJson.CreateOptions()); var decoded = JsonSerializer.Deserialize<AggregateResult>(json, AggregateJson.CreateOptions()); Assert.Equal(json, JsonSerializer.Serialize(decoded, AggregateJson.CreateOptions())); }
    [Fact] public void UnknownOperatorIsRejectedByClosedJsonEnum() => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AggregateMeasure>("""{"key":"m","operator":"median","field":"value","inputType":"decimal","resultType":"decimal"}""", AggregateJson.CreateOptions()));
    [Fact] public void OmittedOperatorIsRejected() => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AggregateMeasure>("""{"key":"m","field":"value","inputType":"decimal","resultType":"decimal"}""", AggregateJson.CreateOptions()));
    [Fact] public void DuplicateResultKeyIsRejected() { var fields = new Dictionary<string, AggregateValueType> { ["value"] = AggregateValueType.Decimal }; var dimension = new AggregateDimension("same", "value", AggregateValueType.Decimal, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc); Assert.Throws<AggregateException>(() => validator.Validate(TestFixture.Definition(fields, [dimension], [new("same", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal)]), Host)); }
    [Fact] public void MissingDimensionFieldIsRejected() { var dimension = new AggregateDimension("x", "absent", AggregateValueType.String, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc); Assert.Throws<AggregateException>(() => validator.Validate(TestFixture.Definition(grouping: [dimension]), Host)); }
    [Fact] public void FilterTypeMismatchIsRejected() { var filter = new AggregateComparisonFilter("value", AggregateComparisonOperator.Eq, TestFixture.V(AggregateValueType.Number, 1d)); Assert.Throws<AggregateException>(() => validator.Validate(TestFixture.Definition(filter: filter), Host)); }
    [Fact] public void MeasureTypeMismatchIsRejected() { var measure = new AggregateMeasure("m", AggregateOperator.Sum, "value", AggregateValueType.Number, AggregateValueType.Number); Assert.Throws<AggregateException>(() => validator.Validate(TestFixture.Definition(measures: [measure]), Host)); }
    [Fact] public void DuplicateSubtotalDepthIsRejected() => Assert.Throws<AggregateException>(() => validator.Validate(TwoDimensions() with { Totals = new AggregateTotals([1, 1], false) }, Host));
    [Fact] public void BoundsAboveHostPolicyAreRejected() => Assert.Throws<AggregateException>(() => validator.Validate(TestFixture.Definition(bounds: new AggregateBounds(1001, 1, 1)), Host));

    private static AggregateDefinition TwoDimensions()
    {
        var fields = new Dictionary<string, AggregateValueType> { ["a"] = AggregateValueType.String, ["b"] = AggregateValueType.String, ["value"] = AggregateValueType.Decimal };
        return TestFixture.Definition(fields, [new("a", "a", AggregateValueType.String, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc), new("b", "b", AggregateValueType.String, AggregateDimensionNullPolicy.Bucket, AggregateSortDirection.Asc)]);
    }
}
