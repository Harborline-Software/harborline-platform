using System.Globalization;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>
/// Faithful ECMAScript <c>Number(string)</c> (ECMA-262 §7.1.4.1.1) — the exact semantics the TS
/// authoring bridge gets from <c>Number(raw)</c> in <c>coerceValue</c>/<c>bound</c>. Differs from
/// the engine's internal <c>JsNumber.TryParse</c> in ONE deliberate way: a trimmed-empty string IS
/// the number <c>0</c> here (the TS bridge calls <c>Number()</c> BEFORE any emptiness guard, and JS
/// <c>Number('')</c> is <c>0</c>), whereas the engine helper models the post-trim call sites that
/// treat empty as not-a-number. Returns <see cref="double.NaN"/> for every input JS rejects;
/// <c>Infinity</c> parses as infinite (callers apply their own <c>isFinite</c> guard, as TS does).
/// </summary>
internal static class JsNumberMirror
{
    public static double ToNumber(string input)
    {
        int start = 0, end = input.Length;
        while (start < end && IsJsSpace(input[start])) start++;
        while (end > start && IsJsSpace(input[end - 1])) end--;
        if (start == end) return 0; // JS: Number('') === Number('  ') === 0

        string s = input[start..end];

        // Non-decimal radix literals carry NO sign in ECMAScript Number(string).
        if (s.Length > 2 && s[0] == '0')
        {
            int radix = s[1] switch { 'x' or 'X' => 16, 'o' or 'O' => 8, 'b' or 'B' => 2, _ => 0 };
            if (radix != 0) return TryRadix(s[2..], radix, out double rv) ? rv : double.NaN;
        }

        int i = 0;
        double sign = 1;
        if (s[0] == '+') i = 1;
        else if (s[0] == '-') { sign = -1; i = 1; }
        string rest = s[i..];
        if (rest.Length == 0) return double.NaN;
        if (rest == "Infinity") return sign * double.PositiveInfinity;

        // Decimal literal only — NO thousands separators, parentheses, currency, or whitespace.
        const NumberStyles styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (double.TryParse(rest, styles, CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d))
        {
            return sign * d;
        }
        return double.NaN;
    }

    /// <summary>JS <c>String(number)</c> for a finite double: integral values print without a
    /// fraction; ±∞ prints as the TS linter's <c>∞</c>/<c>-∞</c> display form.</summary>
    public static string ToDisplayString(double n)
    {
        if (double.IsPositiveInfinity(n)) return "∞";
        if (double.IsNegativeInfinity(n)) return "-∞";
        return n == System.Math.Floor(n) && System.Math.Abs(n) < 9.007e15
            ? ((long)n).ToString(CultureInfo.InvariantCulture)
            : n.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool TryRadix(string digits, int radix, out double value)
    {
        value = 0;
        if (digits.Length == 0) return false;
        double acc = 0;
        foreach (char c in digits)
        {
            int dv = DigitValue(c);
            if (dv < 0 || dv >= radix) return false;
            acc = acc * radix + dv;
        }
        value = acc;
        return true;
    }

    private static int DigitValue(char c)
        => c is >= '0' and <= '9' ? c - '0'
         : c is >= 'a' and <= 'f' ? c - 'a' + 10
         : c is >= 'A' and <= 'F' ? c - 'A' + 10
         : -1;

    private static bool IsJsSpace(char c) => char.IsWhiteSpace(c) || c == '\uFEFF';
}
