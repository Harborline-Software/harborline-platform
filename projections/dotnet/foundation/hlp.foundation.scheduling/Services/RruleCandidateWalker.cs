using Ical.Net;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;

namespace Harborline.Foundation.Scheduling;

// Ical.Net's unmatched-period limit resets after each match. Bound the total walk instead, including empty periods.
internal static class RruleCandidateWalker
{
    public static IEnumerable<CalDateTime> Walk(string rrule, ParsedRrule parsed, CalDateTime anchor,
        DateOnly horizon, DateOnly earliest, int candidateLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(candidateLimit, 1);
        var rule = new RecurrenceRule((parsed with { Count = null }).ToRuleWithoutUntil());
        // Freeze DTSTART defaults before moving the engine's seed to the next period. Selectors remain Ical.Net's.
        if (rule.Frequency == FrequencyType.Weekly && rule.ByDay.Count == 0)
            rule.ByDay.Add(new WeekDay(anchor.DayOfWeek));
        if (rule.Frequency >= FrequencyType.Monthly && rule.ByDay.Count == 0 && rule.ByMonthDay.Count == 0)
            rule.ByMonthDay.Add(anchor.Day);
        if (rule.Frequency == FrequencyType.Yearly && rule.ByDay.Count == 0 && rule.ByMonth.Count == 0)
            rule.ByMonth.Add(anchor.Month);
        var seed = anchor;
        var examined = 0;
        var matched = 0;
        DateOnly? last = null;
        void Charge()
        {
            if (++examined > candidateLimit)
                throw new RruleExpansionBudgetExceededException(rrule, anchor.Date, earliest, horizon,
                    candidateLimit, examined, last, parsed.Until is not null ? RecurrenceBound.Until
                        : parsed.Count is not null ? RecurrenceBound.Count : RecurrenceBound.None);
        }
        while (seed.Date <= horizon)
        {
            Charge();
            var first = seed.Date;
            var remaining = rule.Frequency switch
            {
                FrequencyType.Weekly => 6 - (((int)first.DayOfWeek + 6) % 7),
                FrequencyType.Monthly => DateTime.DaysInMonth(first.Year, first.Month) - first.Day,
                FrequencyType.Yearly => new DateOnly(first.Year, 12, 31).DayNumber - first.DayNumber,
                _ => 0,
            };
            var end = DateOnly.FromDayNumber(Math.Min(horizon.DayNumber, first.DayNumber + remaining));
            // The engine adds a day internally to UNTIL; avoid its overflow at DateOnly.MaxValue.
            var engineEnd = end == DateOnly.MaxValue ? end.AddDays(-1) : end;
            rule.Until = anchor.HasTime ? new CalDateTime(engineEnd, TimeOnly.MaxValue, null) : new CalDateTime(engineEnd);
            using var dates = new RecurrencePatternEvaluator(rule).Evaluate(seed, null, null).GetEnumerator();
            while (true)
            {
                bool more;
                try { more = dates.MoveNext(); }
                catch (EvaluationOutOfRangeException) { break; }
                if (!more) break;
                var candidate = dates.Current.StartTime;
                if (candidate.Date > end) break;
                Charge();
                last = candidate.Date;
                yield return candidate;
                if (++matched == parsed.Count) yield break;
            }
            if (end == horizon) yield break;
            try
            {
                var next = rule.Frequency switch
                {
                    FrequencyType.Weekly => first.AddDays(-(((int)first.DayOfWeek + 6) % 7)).AddDays(checked(parsed.Interval * 7)),
                    FrequencyType.Monthly => new DateOnly(first.Year, first.Month, 1).AddMonths(parsed.Interval),
                    FrequencyType.Yearly => new DateOnly(first.Year, 1, 1).AddYears(parsed.Interval),
                    _ => first.AddDays(parsed.Interval),
                };
                seed = anchor.HasTime ? new CalDateTime(next, TimeOnly.FromDateTime(anchor.Value), null) : new CalDateTime(next);
            }
            catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException) { yield break; }
        }
    }
}
