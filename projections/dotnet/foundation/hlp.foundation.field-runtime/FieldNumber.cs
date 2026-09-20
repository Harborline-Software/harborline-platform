using System.Globalization;
using System.Numerics;

namespace Harborline.Foundation.FieldRuntime;

// A parsed JSON-number lexeme. Its original decimal places remain distinct from
// mathematical comparison, so equality cannot erase a fraction_digits violation.
internal sealed class FieldNumber
{
    private readonly string _digits;
    private readonly BigInteger _power;
    private readonly bool _negative;
    private readonly bool _zero;

    internal FieldNumber(string raw)
    {
        var number = raw.AsSpan();
        var negative = number[0] == '-';
        if (negative) number = number[1..];
        var exponentOffset = number.IndexOfAny('e', 'E');
        var exponent = exponentOffset < 0 ? BigInteger.Zero : BigInteger.Parse(
            number[(exponentOffset + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var significand = exponentOffset < 0 ? number : number[..exponentOffset];
        var point = significand.IndexOf('.');
        var authoredFraction = point < 0 ? 0 : significand.Length - point - 1;
        _power = exponent - authoredFraction;
        FractionDigits = BigInteger.Max(BigInteger.Zero, -_power);
        var coefficient = significand.ToString().Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        _zero = coefficient.Length == 0;
        _negative = negative && !_zero;
        _digits = _zero ? "0" : coefficient;
        TotalDigits = _zero
            ? BigInteger.Max(BigInteger.One, FractionDigits)
            : _digits.Length + BigInteger.Max(BigInteger.Zero, _power);
        var trailingZeroes = 0;
        for (var index = _digits.Length - 1; index >= 0 && _digits[index] == '0'; index--) trailingZeroes++;
        IsInteger = _zero || _power >= 0 || -_power <= trailingZeroes;
    }

    internal BigInteger TotalDigits { get; }
    internal BigInteger FractionDigits { get; }
    internal bool IsInteger { get; }

    internal int CompareTo(FieldNumber other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (_negative != other._negative) return _negative ? -1 : 1;
        var order = CompareMagnitude(other);
        return _negative ? -order : order;
    }

    private int CompareMagnitude(FieldNumber other)
    {
        if (_zero || other._zero) return _zero ? (other._zero ? 0 : -1) : 1;
        var order = (_digits.Length + _power).CompareTo(other._digits.Length + other._power);
        if (order != 0) return order;
        // Equal decimal magnitude: compare coefficient digits with virtual right
        // padding. Even a billion-place exponent never expands into a string.
        for (var index = 0; index < Math.Max(_digits.Length, other._digits.Length); index++)
        {
            var left = index < _digits.Length ? _digits[index] : '0';
            var right = index < other._digits.Length ? other._digits[index] : '0';
            if (left != right) return left.CompareTo(right);
        }
        return 0;
    }
}
