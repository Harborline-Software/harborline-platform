using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Harborline.Contracts.Fields;

namespace Harborline.Foundation.FieldRuntime;

// A parsed JSON-number lexeme. Its original decimal places remain distinct from
// mathematical comparison, so equality cannot erase a fraction_digits violation.
internal sealed class FieldNumber
{
    private readonly string _digits;
    private readonly BigInteger _power;
    private readonly bool _negative;
    private readonly bool _zero;

    // The most significant digits a written exponent may have: its magnitude is below 10^18, so it fits a long
    // exactly. A longer one is never read, so no arbitrary-precision parse runs on an authored exponent.
    private const int MaxExponentDigits = 18;

    private FieldNumber(ReadOnlySpan<char> number, bool negative, BigInteger exponent)
    {
        var exponentOffset = number.IndexOfAny('e', 'E');
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

    /// <summary>
    /// Reads a JSON-number lexeme in time and memory linear in its length. False, with no number, when the written
    /// exponent has more than <see cref="MaxExponentDigits"/> significant digits once leading zeros are skipped.
    /// </summary>
    internal static bool TryParse(string raw, [NotNullWhen(true)] out FieldNumber? parsed)
    {
        var number = raw.AsSpan();
        var negative = number[0] == '-';
        if (negative) number = number[1..];
        var exponentOffset = number.IndexOfAny('e', 'E');
        var exponent = 0L;
        if (exponentOffset >= 0 && !TryReadExponent(number[(exponentOffset + 1)..], out exponent))
        {
            parsed = null;
            return false;
        }
        parsed = new(number, negative, exponent);
        return true;
    }

    /// <summary>The refusal for a JSON number whose written exponent is outside the readable range.</summary>
    internal static FieldRefusal ExponentOutOfRange(string code, string jsonPointer)
        => new(code, jsonPointer, "The number's exponent is outside the supported range.");

    // The written exponent: an optional sign, then ASCII digits (the lexeme is already a JSON number). Leading zeros
    // are skipped at the cost of their own digits; more than MaxExponentDigits significant digits refuses before any
    // arithmetic, so the accumulator cannot overflow.
    private static bool TryReadExponent(ReadOnlySpan<char> written, out long exponent)
    {
        var negative = written[0] == '-';
        if (written[0] is '-' or '+') written = written[1..];
        var significant = written.TrimStart('0');
        exponent = 0L;
        if (significant.Length > MaxExponentDigits) return false;
        foreach (var digit in significant) exponent = exponent * 10 + (digit - '0');
        if (negative) exponent = -exponent;
        return true;
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
