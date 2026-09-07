using System.Globalization;
using System.Numerics;

namespace Harborline.Blocks.Aggregates;

/// <summary>An arbitrary-precision canonical base-10 value.</summary>
public readonly record struct CanonicalDecimal : IComparable<CanonicalDecimal>
{
    /// <summary>Creates a normalized value.</summary><param name="unscaled">Signed unscaled integer.</param><param name="scale">Non-negative decimal scale.</param>
    public CanonicalDecimal(BigInteger unscaled, int scale)
    {
        if (scale < 0) throw new ArgumentOutOfRangeException(nameof(scale));
        while (scale > 0 && unscaled % 10 == 0) { unscaled /= 10; scale--; }
        Unscaled = unscaled;
        Scale = unscaled.IsZero ? 0 : scale;
    }

    /// <summary>Signed unscaled integer.</summary>
    public BigInteger Unscaled { get; }
    /// <summary>Number of fractional base-10 digits.</summary>
    public int Scale { get; }

    /// <summary>Parses the strict base-10 wire form.</summary><param name="text">Text without exponent notation.</param><returns>Normalized value.</returns>
    public static CanonicalDecimal Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('e', StringComparison.OrdinalIgnoreCase)) throw new FormatException("Decimal must be plain base-10 text.");
        var negative = text[0] == '-';
        var unsigned = negative || text[0] == '+' ? text[1..] : text;
        var pieces = unsigned.Split('.');
        if (pieces.Length > 2 || pieces.Any(piece => piece.Length == 0 || piece.Any(ch => ch is < '0' or > '9'))) throw new FormatException("Invalid decimal text.");
        var scale = pieces.Length == 2 ? pieces[1].Length : 0;
        var digits = string.Concat(pieces);
        var unscaled = BigInteger.Parse(digits, CultureInfo.InvariantCulture) * (negative ? -1 : 1);
        return new CanonicalDecimal(unscaled, scale);
    }

    /// <summary>Adds two exact values.</summary>
    public static CanonicalDecimal operator +(CanonicalDecimal left, CanonicalDecimal right)
    {
        var scale = Math.Max(left.Scale, right.Scale);
        return new CanonicalDecimal(left.Unscaled * Pow10(scale - left.Scale) + right.Unscaled * Pow10(scale - right.Scale), scale);
    }

    /// <summary>Divides exactly by a positive integer, failing when the decimal expansion does not terminate.</summary><param name="divisor">Positive divisor.</param><returns>Exact quotient.</returns>
    public CanonicalDecimal DivideExactly(long divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        var denominator = new BigInteger(divisor);
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(Unscaled), denominator);
        denominator /= gcd;
        while (denominator % 2 == 0) denominator /= 2;
        while (denominator % 5 == 0) denominator /= 5;
        if (denominator != BigInteger.One) throw new AggregateException("aggregates.measure.non_terminating_decimal", "Decimal average does not terminate exactly.");
        var numerator = Unscaled;
        var scale = Scale;
        var original = new BigInteger(divisor);
        while (numerator % original != 0) { numerator *= 10; scale++; }
        return new CanonicalDecimal(numerator / original, scale);
    }

    /// <inheritdoc />
    public int CompareTo(CanonicalDecimal other)
    {
        var scale = Math.Max(Scale, other.Scale);
        return (Unscaled * Pow10(scale - Scale)).CompareTo(other.Unscaled * Pow10(scale - other.Scale));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var negative = Unscaled.Sign < 0;
        var digits = BigInteger.Abs(Unscaled).ToString(CultureInfo.InvariantCulture).PadLeft(Scale + 1, '0');
        var text = Scale == 0 ? digits : $"{digits[..^Scale]}.{digits[^Scale..]}";
        return negative ? $"-{text}" : text;
    }

    private static BigInteger Pow10(int exponent) => BigInteger.Pow(10, exponent);
}
