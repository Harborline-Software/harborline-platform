using System.Globalization;

namespace Harborline.Foundation.Scheduling;

/// <summary>
/// The bounded RRULE subset (DES-0057 <c>recurrence-expansion-ck-1</c>) as a validator in front of the expansion
/// engine (ADR-0122 decision 1). The subset is the contract; Ical.Net is the engine behind it, and it accepts some rules
/// the subset and RFC 5545 refuse, so every rule passes through here first.
/// </summary>
/// <remarks>
/// Part names and enumerated values are case-insensitive (RFC 5234 §2.3; RFC 5545 §3.1), so <c>freq=daily;count=2</c>
/// is valid. RFC 5545 §3.3.10 refusals the engine does not make are made here: a part given twice, COUNT with UNTIL,
/// and a numeric BYDAY outside MONTHLY or YEARLY.
/// </remarks>
internal static class RruleSubset
{
    private static readonly string[] Frequencies = ["DAILY", "WEEKLY", "MONTHLY", "YEARLY"];
    private static readonly string[] WeekdayCodes = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    /// <summary>Parses and checks a rule, refusing anything outside the subset.</summary>
    /// <exception cref="FormatException">A malformed rule: no FREQ, a repeated part, COUNT with UNTIL, or a bad value.</exception>
    /// <exception cref="NotSupportedException">A part, frequency or combination outside the subset.</exception>
    public static ParsedRrule Parse(string rrule)
    {
        string? freq = null;
        int interval = 1;
        int? count = null;
        RruleUntil? until = null;
        var byDay = new List<ByDayEntry>();
        var byMonthDay = new List<int>();
        var byMonth = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var token in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = token.IndexOf('=', StringComparison.Ordinal);
            var key = (eq < 0 ? token : token[..eq]).Trim().ToUpperInvariant();
            var value = eq < 0 ? "" : token[(eq + 1)..].Trim();

            // A token with no value is an admitted part written wrong, or a part outside the subset.
            if (eq < 0)
            {
                if (IsAdmittedPart(key))
                    throw new FormatException($"RRULE component '{key}' has no value.");
                throw new NotSupportedException($"RRULE component '{key}' is not supported by the bounded subset.");
            }

            // RFC 5545 §3.3.10: "Individual rule parts MUST only be specified once."
            if (!seen.Add(key))
                throw new FormatException($"RRULE component '{key}' is given more than once.");

            switch (key)
            {
                case "FREQ":
                    freq = value.ToUpperInvariant();
                    if (!Frequencies.Contains(freq))
                        throw new NotSupportedException(
                            $"RRULE FREQ '{value}' is not supported by the bounded subset (DAILY, WEEKLY, MONTHLY or YEARLY).");
                    break;
                // An admitted part whose value is out of bound or malformed is refused, not dropped: dropping it would
                // expand a wider series than the author wrote.
                case "INTERVAL":
                    interval = ParseBounded(key, value, 1, int.MaxValue);
                    break;
                case "COUNT":
                    count = ParseBounded(key, value, 1, int.MaxValue);
                    break;
                case "UNTIL":
                    until = ParseUntil(value);
                    break;
                case "BYDAY":
                    byDay.AddRange(ParseByDay(value));
                    break;
                case "BYMONTHDAY":
                    byMonthDay.AddRange(ParseIntList(key, value, min: 1, max: 28));
                    break;
                case "BYMONTH":
                    byMonth.AddRange(ParseIntList(key, value, min: 1, max: 12));
                    break;
                default:
                    throw new NotSupportedException($"RRULE component '{key}' is not supported by the bounded subset.");
            }
        }

        if (freq is null)
            throw new FormatException($"RRULE missing FREQ= component: '{rrule}'.");

        // RFC 5545 §3.3.10: "The UNTIL or COUNT rule parts are OPTIONAL, but they MUST NOT occur in the same 'recur'."
        if (count is not null && until is not null)
            throw new FormatException("RRULE components 'COUNT' and 'UNTIL' must not occur in the same rule.");

        // RFC 5545 §3.3.10 marks BYMONTHDAY not applicable under WEEKLY.
        if (freq == "WEEKLY" && byMonthDay.Count > 0)
            throw new NotSupportedException("RRULE component 'BYMONTHDAY' is not applicable with FREQ=WEEKLY.");

        // RFC 5545 §3.3.10: a numeric BYDAY has meaning only under MONTHLY or YEARLY.
        if (freq is "DAILY" or "WEEKLY" && byDay.Any(entry => entry.Ordinal != 0))
            throw new NotSupportedException($"RRULE component 'BYDAY' with an ordinal is not applicable with FREQ={freq}.");

        return new ParsedRrule(freq, interval, count, until, byDay, byMonthDay, byMonth);
    }

    private static bool IsAdmittedPart(string key) =>
        key is "FREQ" or "INTERVAL" or "COUNT" or "UNTIL" or "BYDAY" or "BYMONTHDAY" or "BYMONTH";

    private static int ParseBounded(string key, string value, int min, int max)
    {
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max)
            return n;
        throw new FormatException(
            $"RRULE component '{key}' value '{value}' is not "
            + (max == int.MaxValue ? "a positive integer." : $"an integer in {min}..{max}."));
    }

    private static RruleUntil ParseUntil(string value)
    {
        // RFC 5545 §3.3.10: UNTIL is a DATE for a date-valued series and a DATE-TIME in UTC for a timed one.
        if (value.Length == 8
            && DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return new RruleUntil(date, null);
        if (value.Length == 16 && value[8] == 'T' && value[15] == 'Z'
            && DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var instant))
            return new RruleUntil(DateOnly.FromDateTime(instant), new DateTimeOffset(instant, TimeSpan.Zero));
        throw new FormatException($"RRULE component 'UNTIL' value '{value}' is not a YYYYMMDD or YYYYMMDDTHHMMSSZ date.");
    }

    private static IEnumerable<ByDayEntry> ParseByDay(string value)
    {
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Patterns: "MO", "1MO", "-1FR", "2TU". The last two characters are the weekday code.
            var span = part.ToUpperInvariant();
            var weekday = span.Length >= 2 ? span[^2..] : span;
            if (!WeekdayCodes.Contains(weekday))
                throw new FormatException($"RRULE component 'BYDAY' entry '{part}' is not one of the seven weekday codes.");

            var ordinalText = span[..^2];
            int ordinal = 0;
            if (ordinalText.Length > 0
                && (!int.TryParse(ordinalText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal)
                    || ordinal == 0 || Math.Abs(ordinal) > 5))
                throw new FormatException($"RRULE component 'BYDAY' entry '{part}' ordinal is not in -5..-1 or 1..5.");

            yield return new ByDayEntry(weekday, ordinal);
        }
    }

    private static IEnumerable<int> ParseIntList(string key, string value, int min, int max)
    {
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return ParseBounded(key, part, min, max);
    }
}

/// <summary>The rule's UNTIL: always a date, and the UTC instant when the rule gave a DATE-TIME.</summary>
internal sealed record RruleUntil(DateOnly Date, DateTimeOffset? Instant);

/// <summary>A BYDAY entry: a weekday code and its ordinal, zero when none was given.</summary>
internal sealed record ByDayEntry(string Weekday, int Ordinal);

/// <summary>A rule the subset admitted. <see cref="ToRuleWithoutUntil"/> writes it back in the canonical upper-case form.</summary>
internal sealed record ParsedRrule(
    string Freq,
    int Interval,
    int? Count,
    RruleUntil? Until,
    IReadOnlyList<ByDayEntry> ByDay,
    IReadOnlyList<int> ByMonthDay,
    IReadOnlyList<int> ByMonth)
{
    /// <summary>The canonical rule without UNTIL, which each caller bounds itself.</summary>
    public string ToRuleWithoutUntil()
    {
        var parts = new List<string> { $"FREQ={Freq}" };
        if (Interval != 1) parts.Add(FormattableString.Invariant($"INTERVAL={Interval}"));
        if (Count is { } count) parts.Add(FormattableString.Invariant($"COUNT={count}"));
        if (ByDay.Count > 0)
            parts.Add("BYDAY=" + string.Join(",", ByDay.Select(entry =>
                entry.Ordinal == 0 ? entry.Weekday : FormattableString.Invariant($"{entry.Ordinal}{entry.Weekday}"))));
        if (ByMonthDay.Count > 0) parts.Add("BYMONTHDAY=" + string.Join(",", ByMonthDay.Select(day => day.ToString(CultureInfo.InvariantCulture))));
        if (ByMonth.Count > 0) parts.Add("BYMONTH=" + string.Join(",", ByMonth.Select(month => month.ToString(CultureInfo.InvariantCulture))));
        return string.Join(";", parts);
    }
}
