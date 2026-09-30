using System.Numerics;
using System.Text.Json;
using Harborline.Blocks.Aggregates;
using Xunit;

namespace Harborline.Blocks.Aggregates.Tests;

public sealed class CanonicalDecimalTests
{
    private static CanonicalDecimal P(string text) => CanonicalDecimal.Parse(text);

    [Fact] public void NegativeScaleIsRefused() => Assert.Throws<ArgumentOutOfRangeException>(() => new CanonicalDecimal(1, -1));
    [Theory]
    [InlineData(1500, 3, "1.5")]
    [InlineData(100, 0, "100")]
    [InlineData(0, 5, "0")]
    [InlineData(1000, 2, "10")]
    [InlineData(-250, 2, "-2.5")]
    public void ValuesAreNormalized(int unscaled, int scale, string text) => Assert.Equal(text, new CanonicalDecimal(unscaled, scale).ToString());
    [Fact] public void NormalizationStripsOnlyTrailingZerosOfTheFraction() { var value = new CanonicalDecimal(1500, 3); Assert.Equal(new BigInteger(15), value.Unscaled); Assert.Equal(1, value.Scale); Assert.Equal(0, new CanonicalDecimal(0, 4).Scale); Assert.Equal(new CanonicalDecimal(15, 1), value); }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("+1.5", "1.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("007", "7")]
    [InlineData("0.050", "0.05")]
    [InlineData("-0.05", "-0.05")]
    [InlineData("-0", "0")]
    [InlineData("12345678901234567890.000000000000000001", "12345678901234567890.000000000000000001")]
    public void ParseReadsPlainBase10(string text, string canonical) => Assert.Equal(canonical, P(text).ToString());
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1e5")]
    [InlineData("1E5")]
    [InlineData("1.2.3")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("1a")]
    [InlineData("-")]
    [InlineData("+")]
    [InlineData("--1")]
    [InlineData("1,5")]
    public void ParseRefusesEverythingElse(string text) => Assert.False(string.IsNullOrWhiteSpace(Assert.Throws<FormatException>(() => P(text)).Message));

    [Theory]
    [InlineData("1.5", "2.25", "3.75")]
    [InlineData("0.25", "1", "1.25")]
    [InlineData("1", "0.25", "1.25")]
    [InlineData("-1.5", "1.5", "0")]
    [InlineData("100", "0.001", "100.001")]
    [InlineData("0.1", "0.2", "0.3")]
    public void AdditionIsExact(string left, string right, string sum) => Assert.Equal(sum, (P(left) + P(right)).ToString());

    [Theory]
    [InlineData("3", 2, "1.5")]
    [InlineData("1", 8, "0.125")]
    [InlineData("6", 3, "2")]
    [InlineData("0", 7, "0")]
    [InlineData("-3", 2, "-1.5")]
    [InlineData("3", 6, "0.5")]
    [InlineData("10", 4, "2.5")]
    [InlineData("1", 25, "0.04")]
    [InlineData("1", 5, "0.2")]
    [InlineData("1", 10, "0.1")]
    [InlineData("7", 7, "1")]
    [InlineData("0.3", 3, "0.1")]
    [InlineData("100", 1, "100")]
    public void DivisionByATerminatingDivisorIsExact(string value, long divisor, string quotient) => Assert.Equal(quotient, P(value).DivideExactly(divisor).ToString());
    [Theory]
    [InlineData("1", 3)]
    [InlineData("1", 6)]
    [InlineData("1", 7)]
    [InlineData("2", 14)]
    [InlineData("1", 15)]
    public void DivisionThatDoesNotTerminateIsRefusedWithItsCode(string value, long divisor) => Assert.Equal("aggregates.measure.non_terminating_decimal", Fail(value, divisor).Code);
    private static AggregateException Fail(string value, long divisor) { var error = Assert.Throws<AggregateException>(() => P(value).DivideExactly(divisor)); Assert.False(string.IsNullOrWhiteSpace(error.Message)); return error; }
    [Theory][InlineData(0)][InlineData(-2)] public void NonPositiveDivisorIsRefused(long divisor) => Assert.Throws<ArgumentOutOfRangeException>(() => P("1").DivideExactly(divisor));

    [Theory]
    [InlineData("0.1", "0.09", 1)]
    [InlineData("0.09", "0.1", -1)]
    [InlineData("1", "1.0", 0)]
    [InlineData("-0.1", "-0.09", -1)]
    [InlineData("10", "9.99", 1)]
    [InlineData("-1", "1", -1)]
    [InlineData("2", "2", 0)]
    public void ComparisonIgnoresScale(string left, string right, int expected) => Assert.Equal(expected, Math.Sign(P(left).CompareTo(P(right))));

    [Theory]
    [InlineData("0.05")]
    [InlineData("-0.05")]
    [InlineData("123.45")]
    [InlineData("5")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("10")]
    [InlineData("0.001")]
    public void ToStringRoundTrips(string text) => Assert.Equal(text, P(text).ToString());
}

public sealed class AggregateJsonTests
{
    [Fact] public void OutputIsCompactCamelCaseWithStringEnums() => Assert.Equal("""{"key":"m","operator":"sum","field":"value","inputType":"decimal","resultType":"decimal","nulls":"ignore","unavailable":"fail","count":null}""", JsonSerializer.Serialize(new AggregateMeasure("m", AggregateOperator.Sum, "value", AggregateValueType.Decimal, AggregateValueType.Decimal), AggregateJson.CreateOptions()));
    [Fact] public void IntegerEnumValuesAreRefused() => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AggregateMeasure>("""{"key":"m","operator":0,"field":"value","inputType":"decimal","resultType":"decimal"}""", AggregateJson.CreateOptions()));
    [Fact] public void QuotedNumbersAreRefused() => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AggregateBounds>("""{"maxInputRows":"5","maxGroups":1,"maxResultCells":1}""", AggregateJson.CreateOptions()));
    [Fact] public void NamesAreReadCaseInsensitively() => Assert.Equal(new AggregateBounds(5, 1, 1), JsonSerializer.Deserialize<AggregateBounds>("""{"MaxInputRows":5,"maxgroups":1,"maxResultCells":1}""", AggregateJson.CreateOptions()));
    [Fact] public void EachCallReturnsItsOwnOptions() => Assert.NotSame(AggregateJson.CreateOptions(), AggregateJson.CreateOptions());
}
