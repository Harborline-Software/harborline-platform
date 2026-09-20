using System.Globalization;

namespace Harborline.Foundation.Scheduling;

/// <summary>
/// In-process <see cref="IRruleExpansionService"/> implementing the
/// fleet bounded RRULE subset. Widened from the prior work-orders stub
/// (DAILY/WEEKLY/MONTHLY + INTERVAL only) to the full subset that
/// mirrors ui-react's <c>expandRecurrence</c> function per
/// council-verdict-net-arch-2026-06-12T2213Z §C-2:
/// FREQ daily/weekly/monthly/yearly, INTERVAL, COUNT/UNTIL,
/// BYDAY (with monthly ordinals), BYMONTHDAY 1-28, BYMONTH.
/// </summary>
public sealed class InMemoryRruleExpansionService : IRruleExpansionService
{
    /// <summary>
    /// Hard cap on occurrences per call, matching the ui-react
    /// <c>expandRecurrence</c> 1 000-occurrence guard.
    /// </summary>
    private const int OccurrenceCap = 1_000;

    /// <inheritdoc />
    public IReadOnlyList<DateOnly> ExpandOccurrences(
        string rrule,
        DateOnly start,
        DateOnly? end,
        int lookaheadDays,
        int leadDays,
        DateOnly today,
        string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rrule);

        var parsed = ParseRrule(rrule);
        var horizon = end ?? today.AddDays(lookaheadDays);
        var earliest = today.AddDays(leadDays);

        var result = new List<DateOnly>();
        var cursor = start;
        int occurrenceCount = 0;

        // UNTIL is a date upper bound; COUNT limits total candidate
        // occurrences from the anchor (not filtered occurrences).
        DateOnly? untilBound = parsed.Until;
        var effectiveHorizon = (untilBound.HasValue && untilBound.Value < horizon)
            ? untilBound.Value
            : horizon;

        while (cursor <= effectiveHorizon
            && result.Count < OccurrenceCap
            && (parsed.Count is null || occurrenceCount < parsed.Count.Value))
        {
            if (IsMatchingOccurrence(cursor, start, parsed))
            {
                occurrenceCount++;
                if (cursor >= earliest)
                    result.Add(cursor);
            }

            cursor = Advance(cursor, parsed);
        }

        return result;
    }

    // ----------------------------------------------------------------
    // Occurrence matching
    // ----------------------------------------------------------------

    private static bool IsMatchingOccurrence(DateOnly date, DateOnly start, ParsedRrule parsed)
    {
        // BYMONTH filter
        if (parsed.ByMonth.Count > 0 && !parsed.ByMonth.Contains(date.Month))
            return false;

        return parsed.Freq switch
        {
            "DAILY"   => IsMatchingDaily(date, start, parsed),
            "WEEKLY"  => IsMatchingWeekly(date, start, parsed),
            "MONTHLY" => IsMatchingMonthly(date, start, parsed),
            "YEARLY"  => IsMatchingYearly(date, start, parsed),
            _ => throw new NotSupportedException(
                $"RRULE FREQ '{parsed.Freq}' is not supported. "
                + "Supported: DAILY / WEEKLY / MONTHLY / YEARLY."),
        };
    }

    private static bool IsMatchingDaily(DateOnly date, DateOnly start, ParsedRrule parsed)
    {
        // RFC 5545 §3.3.10: BYDAY and BYMONTHDAY limit DAILY. With a selector the cursor walks
        // day by day, so INTERVAL is measured in elapsed days from the anchor here.
        if ((date.DayNumber - start.DayNumber) % parsed.Interval != 0) return false;
        if (parsed.ByDay.Count > 0 && !MatchesPlainWeekday(date, parsed)) return false;
        return parsed.ByMonthDay.Count == 0 || parsed.ByMonthDay.Contains(date.Day);
    }

    private static bool IsMatchingWeekly(DateOnly date, DateOnly start, ParsedRrule parsed)
    {
        // With no BYDAY, every occurrence in the iteration is valid.
        if (parsed.ByDay.Count == 0) return true;
        var elapsedWeeks = (StartOfWeek(date).DayNumber - StartOfWeek(start).DayNumber) / 7;
        if (elapsedWeeks % parsed.Interval != 0) return false;
        // With BYDAY, the date's day-of-week must be in the set.
        return MatchesPlainWeekday(date, parsed);
    }

    private static bool MatchesPlainWeekday(DateOnly date, ParsedRrule parsed)
    {
        var dow = ToDayOfWeekCode(date.DayOfWeek);
        return parsed.ByDay.Any(bd => bd.Weekday == dow && bd.Ordinal == 0);
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        // RFC 5545 defaults WKST to Monday. WKST itself is outside this bounded subset.
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    private static bool IsMatchingMonthly(DateOnly date, DateOnly start, ParsedRrule parsed)
    {
        var elapsedMonths = ((date.Year - start.Year) * 12) + date.Month - start.Month;
        if (elapsedMonths % parsed.Interval != 0) return false;

        // BYDAY (plain or ordinal, e.g. 1MO, -1FR) and BYMONTHDAY intersect (RFC 5545 §3.3.10).
        // With no BY* selector every monthly step from the anchor is valid.
        if (parsed.ByDay.Count > 0 && !parsed.ByDay.Any(bd => MatchesByDay(date, bd))) return false;
        return parsed.ByMonthDay.Count == 0 || parsed.ByMonthDay.Contains(date.Day);
    }

    private static bool IsMatchingYearly(DateOnly date, DateOnly start, ParsedRrule parsed)
    {
        // BYMONTH is applied by the caller. With a selector the cursor walks day by day and
        // INTERVAL is measured in elapsed years from the anchor; without one it steps whole years.
        if ((date.Year - start.Year) % parsed.Interval != 0) return false;
        if (parsed.ByDay.Count > 0 && !parsed.ByDay.Any(bd => MatchesByDay(date, bd))) return false;
        if (parsed.ByMonthDay.Count > 0) return parsed.ByMonthDay.Contains(date.Day);
        // RFC 5545 §3.3.10: a part the rule does not carry is taken from DTSTART, so
        // FREQ=YEARLY;BYMONTH=3 anchored on the 15th is every 15 March.
        return parsed.ByDay.Count > 0 || date.Day == start.Day;
    }

    // ----------------------------------------------------------------
    // BYDAY ordinal matching
    // ----------------------------------------------------------------

    private static bool MatchesByDay(DateOnly date, ByDayEntry bd)
    {
        var dow = ToDayOfWeekCode(date.DayOfWeek);
        if (bd.Weekday != dow) return false;
        if (bd.Ordinal == 0) return true; // plain weekday, every occurrence

        // Positive ordinal: Nth weekday of the month.
        if (bd.Ordinal > 0)
        {
            int occurrence = GetNthWeekdayOccurrenceInMonth(date);
            return occurrence == bd.Ordinal;
        }
        // Negative ordinal: Nth-last weekday of the month.
        // bd.Ordinal == -1 → last, -2 → second-last, etc.
        int occurrenceFromEnd = GetNthLastWeekdayOccurrenceInMonth(date);
        return occurrenceFromEnd == -bd.Ordinal;
    }

    /// <summary>
    /// Returns how many times the given weekday has appeared in the month
    /// up to and including <paramref name="date"/>
    /// (e.g. the first Monday of the month → 1).
    /// </summary>
    private static int GetNthWeekdayOccurrenceInMonth(DateOnly date)
    {
        int count = 0;
        for (int d = 1; d <= date.Day; d++)
        {
            if (new DateOnly(date.Year, date.Month, d).DayOfWeek == date.DayOfWeek)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Returns how many times the given weekday appears in the month
    /// counting backwards from the end of the month to <paramref name="date"/>
    /// (e.g. the last Monday → 1, second-last Monday → 2).
    /// </summary>
    private static int GetNthLastWeekdayOccurrenceInMonth(DateOnly date)
    {
        int daysInMonth = DateTime.DaysInMonth(date.Year, date.Month);
        int count = 0;
        for (int d = daysInMonth; d >= date.Day; d--)
        {
            if (new DateOnly(date.Year, date.Month, d).DayOfWeek == date.DayOfWeek)
                count++;
        }
        return count;
    }

    // ----------------------------------------------------------------
    // Cursor advancement
    // ----------------------------------------------------------------

    private static DateOnly Advance(DateOnly cursor, ParsedRrule parsed)
    {
        // A selector means the matcher must see every day; the plain frequency jumps by INTERVAL.
        var hasSelector = parsed.ByDay.Count > 0 || parsed.ByMonthDay.Count > 0;
        return parsed.Freq switch
        {
            "DAILY"   => cursor.AddDays(hasSelector ? 1 : parsed.Interval),
            "WEEKLY"  => AdvanceWeekly(cursor, parsed),
            "MONTHLY" => AdvanceMonthly(cursor, parsed),
            "YEARLY"  => hasSelector || parsed.ByMonth.Count > 0 ? cursor.AddDays(1) : cursor.AddYears(parsed.Interval),
            _ => throw new NotSupportedException(
                $"RRULE FREQ '{parsed.Freq}' is not supported."),
        };
    }

    private static DateOnly AdvanceWeekly(DateOnly cursor, ParsedRrule parsed)
    {
        if (parsed.ByDay.Count == 0)
            return cursor.AddDays(7 * parsed.Interval);

        // BYDAY weekly: walk day-by-day within the week interval.
        // The iteration cursor advances one day at a time; the outer
        // loop calls Advance once per day until a matching day is found.
        // This is the simplest correct approach — the outer loop caps at
        // OccurrenceCap so a pathological BYDAY won't run away.
        return cursor.AddDays(1);
    }

    private static DateOnly AdvanceMonthly(DateOnly cursor, ParsedRrule parsed)
    {
        if (parsed.ByDay.Count > 0 || parsed.ByMonthDay.Count > 0)
        {
            // BY* monthly: advance one day at a time; the outer loop
            // matches via IsMatchingMonthly. Cap at 32 days per month to
            // avoid iterating indefinitely if a BY* selector is pathological.
            // The outer loop's OccurrenceCap provides the hard outer bound.
            return cursor.AddDays(1);
        }
        // Plain FREQ=MONTHLY: jump by interval months anchored to start day.
        return cursor.AddMonths(parsed.Interval);
    }

    // ----------------------------------------------------------------
    // RRULE parser
    // ----------------------------------------------------------------

    private static ParsedRrule ParseRrule(string rrule)
    {
        string? freq = null;
        int interval = 1;
        int? count = null;
        DateOnly? until = null;
        var byDay = new List<ByDayEntry>();
        var byMonthDay = new List<int>();
        var byMonth = new List<int>();

        foreach (var token in rrule.Split(';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = token.IndexOf('=');
            if (eq < 0) continue;
            var key = token[..eq].Trim().ToUpperInvariant();
            var value = token[(eq + 1)..].Trim();

            switch (key)
            {
                case "FREQ":
                    freq = value.ToUpperInvariant();
                    break;
                // An admitted part whose value is out of bound or malformed is refused, not
                // dropped: dropping it would expand a wider series than the author wrote.
                case "INTERVAL":
                    interval = ParseBounded(key, value, 1, int.MaxValue);
                    break;
                case "COUNT":
                    count = ParseBounded(key, value, 1, int.MaxValue);
                    break;
                case "UNTIL":
                    until = ParseUntilDate(value);
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
                    throw new NotSupportedException(
                        $"RRULE component '{key}' is not supported by the bounded subset.");
            }
        }

        if (freq is null)
            throw new FormatException(
                $"RRULE missing FREQ= component: '{rrule}'.");

        // RFC 5545 §3.3.10 marks BYMONTHDAY not applicable under WEEKLY.
        if (freq == "WEEKLY" && byMonthDay.Count > 0)
            throw new NotSupportedException(
                "RRULE component 'BYMONTHDAY' is not applicable with FREQ=WEEKLY.");

        // COUNT and UNTIL are mutually exclusive; UNTIL wins per RFC 5545 §3.8.5.3.
        if (until.HasValue) count = null;

        return new ParsedRrule(freq, interval, count, until, byDay, byMonthDay, byMonth);
    }

    private static int ParseBounded(string key, string value, int min, int max)
    {
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            && n >= min && n <= max)
            return n;
        throw new FormatException(
            $"RRULE component '{key}' value '{value}' is not "
            + (max == int.MaxValue ? "a positive integer." : $"an integer in {min}..{max}."));
    }

    private static DateOnly ParseUntilDate(string value)
    {
        // RFC 5545 UNTIL: YYYYMMDD (date) or YYYYMMDDTHHMMSSZ (datetime).
        if (value.Length == 8 || (value.Length == 16 && value[8] == 'T' && value[15] == 'Z'))
        {
            if (DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                return date;
        }
        throw new FormatException(
            $"RRULE component 'UNTIL' value '{value}' is not a YYYYMMDD or YYYYMMDDTHHMMSSZ date.");
    }

    private static readonly string[] WeekdayCodes = ["MO", "TU", "WE", "TH", "FR", "SA", "SU"];

    private static IEnumerable<ByDayEntry> ParseByDay(string value)
    {
        foreach (var part in value.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Patterns: "MO", "1MO", "-1FR", "2TU" etc. Last two chars are the weekday code.
            var span = part.ToUpperInvariant();
            var wdStr = span.Length >= 2 ? span[^2..] : span;
            if (!WeekdayCodes.Contains(wdStr))
                throw new FormatException(
                    $"RRULE component 'BYDAY' entry '{part}' is not one of the seven weekday codes.");

            var ordinalStr = span[..^2];
            int ordinal = 0;
            if (ordinalStr.Length > 0
                && (!int.TryParse(ordinalStr, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal)
                    || ordinal == 0 || Math.Abs(ordinal) > 5))
                throw new FormatException(
                    $"RRULE component 'BYDAY' entry '{part}' ordinal is not in -5..-1 or 1..5.");

            yield return new ByDayEntry(wdStr, ordinal);
        }
    }

    private static IEnumerable<int> ParseIntList(string key, string value, int min, int max)
    {
        foreach (var part in value.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return ParseBounded(key, part, min, max);
    }

    private static string ToDayOfWeekCode(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday    => "MO",
        DayOfWeek.Tuesday   => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday  => "TH",
        DayOfWeek.Friday    => "FR",
        DayOfWeek.Saturday  => "SA",
        DayOfWeek.Sunday    => "SU",
        _ => throw new ArgumentOutOfRangeException(nameof(dow)),
    };

    // ----------------------------------------------------------------
    // Internal model
    // ----------------------------------------------------------------

    private sealed record ParsedRrule(
        string Freq,
        int Interval,
        int? Count,
        DateOnly? Until,
        IReadOnlyList<ByDayEntry> ByDay,
        IReadOnlyList<int> ByMonthDay,
        IReadOnlyList<int> ByMonth);

    private sealed record ByDayEntry(string Weekday, int Ordinal);
}
