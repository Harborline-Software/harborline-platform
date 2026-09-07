using System.Globalization;

namespace Harborline.UIAdapters.Blazor.Components.Scheduling;

public enum SchedulerRecurrenceFrequency { Daily, Weekly, Monthly, Yearly }
public sealed record SchedulerRecurrenceDay(DayOfWeek Day, int? Ordinal = null);
public sealed record SchedulerRecurrenceRule(
    SchedulerRecurrenceFrequency Frequency,
    int Interval = 1,
    int? Count = null,
    DateTimeOffset? Until = null,
    IReadOnlyList<SchedulerRecurrenceDay>? ByDay = null,
    IReadOnlyList<int>? ByMonthDay = null,
    IReadOnlyList<int>? ByMonth = null);

public static class SchedulerRecurrence
{
    public const int OccurrenceCap = 1000;
    private static readonly IReadOnlyDictionary<string, DayOfWeek> Days = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
    { ["SU"] = DayOfWeek.Sunday, ["MO"] = DayOfWeek.Monday, ["TU"] = DayOfWeek.Tuesday, ["WE"] = DayOfWeek.Wednesday, ["TH"] = DayOfWeek.Thursday, ["FR"] = DayOfWeek.Friday, ["SA"] = DayOfWeek.Saturday };

    public static SchedulerRecurrenceRule? ParseRRule(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var values = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries)).Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].ToUpperInvariant(), pair => pair[1], StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("FREQ", out var frequencyText) || !Enum.TryParse<SchedulerRecurrenceFrequency>(frequencyText, true, out var frequency)) return null;
        var interval = values.TryGetValue("INTERVAL", out var intervalText) && int.TryParse(intervalText, out var parsedInterval) ? parsedInterval : 1;
        if (interval <= 0) return null;
        int? count = values.TryGetValue("COUNT", out var countText) && int.TryParse(countText, out var parsedCount) && parsedCount > 0 ? parsedCount : null;
        DateTimeOffset? until = values.TryGetValue("UNTIL", out var untilText) ? ParseUntil(untilText) : null;
        var byDay = values.TryGetValue("BYDAY", out var byDayText) ? ParseDays(byDayText) : null;
        var byMonthDay = values.TryGetValue("BYMONTHDAY", out var byMonthDayText)
            ? byMonthDayText.Split(',').Select(value => int.TryParse(value, out var day) ? day : 0).Where(day => day is >= 1 and <= 28).Distinct().Order().ToArray() : null;
        var byMonth = values.TryGetValue("BYMONTH", out var byMonthText)
            ? byMonthText.Split(',').Select(value => int.TryParse(value, out var month) ? month : 0).Where(month => month is >= 1 and <= 12).Distinct().Order().ToArray() : null;
        return new(frequency, interval, count, until, byDay, byMonthDay, byMonth);
    }

    public static IReadOnlyList<SchedulerEvent> Expand(SchedulerEvent master, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        if (rangeEnd <= rangeStart) throw new ArgumentOutOfRangeException(nameof(rangeEnd), "invalid-range");
        var rule = ParseRRule(master.RecurrenceRule);
        if (rule is null) return [];
        var duration = master.End - master.Start;
        var occurrences = new List<SchedulerEvent>();
        var generated = 0;
        foreach (var start in Candidates(master.Start, rule, rangeEnd))
        {
            if (start < master.Start) continue;
            if (rule.Until is DateTimeOffset until && start > until) break;
            generated++;
            if (rule.Count is int count && generated > count) break;
            if (generated > OccurrenceCap) break;
            var end = start + duration;
            if (end <= rangeStart || start >= rangeEnd) continue;
            if (IsExcluded(start, master.RecurrenceExceptions)) continue;
            occurrences.Add(master with { Start = start, End = end, RecurrenceId = master.Id, RecurrenceRule = null, RecurrenceExceptions = null, OriginalStart = null });
        }
        return occurrences;
    }

    public static IReadOnlyList<SchedulerEvent> BuildRenderedEvents(IEnumerable<SchedulerEvent> input, DateTimeOffset rangeStart, DateTimeOffset rangeEnd)
    {
        var events = input.ToArray();
        var masters = events.Where(item => !string.IsNullOrWhiteSpace(item.RecurrenceRule) && item.RecurrenceId is null).ToArray();
        var exceptions = events.Where(item => item.RecurrenceId is not null).ToArray();
        var singles = events.Where(item => string.IsNullOrWhiteSpace(item.RecurrenceRule) && item.RecurrenceId is null);
        var map = masters.SelectMany(master => Expand(master, rangeStart, rangeEnd)).ToDictionary(Key, item => item, StringComparer.Ordinal);
        foreach (var exception in exceptions.Where(item => item.OriginalStart.HasValue)) map[$"{exception.RecurrenceId}:{exception.OriginalStart!.Value.UtcDateTime:O}"] = exception;
        return singles.Concat(map.Values).OrderBy(item => item.Start).ThenBy(item => item.Title, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<DateTimeOffset> Candidates(DateTimeOffset start, SchedulerRecurrenceRule rule, DateTimeOffset rangeEnd)
    {
        return rule.Frequency switch
        {
            SchedulerRecurrenceFrequency.Daily => Daily(start, rule, rangeEnd),
            SchedulerRecurrenceFrequency.Weekly => Weekly(start, rule, rangeEnd),
            SchedulerRecurrenceFrequency.Monthly => Monthly(start, rule, rangeEnd),
            _ => Yearly(start, rule, rangeEnd),
        };
    }

    private static IEnumerable<DateTimeOffset> Daily(DateTimeOffset start, SchedulerRecurrenceRule rule, DateTimeOffset end)
    {
        for (var index = 0; index <= OccurrenceCap && start.AddDays((long)index * rule.Interval) < end; index++) yield return start.AddDays((long)index * rule.Interval);
    }
    private static IEnumerable<DateTimeOffset> Weekly(DateTimeOffset start, SchedulerRecurrenceRule rule, DateTimeOffset end)
    {
        var allowed = rule.ByDay is { Count: > 0 } ? rule.ByDay.Select(value => value.Day).ToHashSet() : new HashSet<DayOfWeek> { start.DayOfWeek };
        var firstWeek = StartOfWeek(start);
        for (var day = new DateTimeOffset(start.Date, start.Offset); day < end; day = day.AddDays(1))
        {
            var week = (int)((StartOfWeek(day) - firstWeek).TotalDays / 7);
            if (week % rule.Interval == 0 && allowed.Contains(day.DayOfWeek)) yield return AtTime(day, start);
        }
    }
    private static IEnumerable<DateTimeOffset> Monthly(DateTimeOffset start, SchedulerRecurrenceRule rule, DateTimeOffset end)
    {
        for (var index = 0; index <= OccurrenceCap; index++)
        {
            var month = new DateTimeOffset(start.Year, start.Month, 1, start.Hour, start.Minute, start.Second, start.Offset).AddMonths(index * rule.Interval);
            if (month >= end) yield break;
            foreach (var candidate in MonthCandidates(month, start, rule).OrderBy(value => value)) if (candidate >= start) yield return candidate;
        }
    }
    private static IEnumerable<DateTimeOffset> Yearly(DateTimeOffset start, SchedulerRecurrenceRule rule, DateTimeOffset end)
    {
        for (var index = 0; index <= OccurrenceCap; index++)
        {
            var year = start.Year + index * rule.Interval;
            if (year > end.Year + 1) yield break;
            var months = rule.ByMonth is { Count: > 0 } ? rule.ByMonth : [start.Month];
            foreach (var monthNumber in months)
            {
                var month = new DateTimeOffset(year, monthNumber, 1, start.Hour, start.Minute, start.Second, start.Offset);
                foreach (var candidate in MonthCandidates(month, start, rule).OrderBy(value => value)) if (candidate >= start && candidate < end) yield return candidate;
            }
        }
    }

    private static IEnumerable<DateTimeOffset> MonthCandidates(DateTimeOffset month, DateTimeOffset start, SchedulerRecurrenceRule rule)
    {
        if (rule.ByMonthDay is { Count: > 0 }) return rule.ByMonthDay.Select(day => month.AddDays(day - 1));
        if (rule.ByDay is { Count: > 0 }) return rule.ByDay.SelectMany(value => value.Ordinal is int ordinal ? NthWeekday(month, value.Day, ordinal) is DateTimeOffset date ? [date] : [] : AllWeekdays(month, value.Day));
        var day = Math.Min(start.Day, DateTime.DaysInMonth(month.Year, month.Month));
        return [month.AddDays(day - 1)];
    }
    private static IEnumerable<DateTimeOffset> AllWeekdays(DateTimeOffset month, DayOfWeek day)
    {
        for (var date = month; date.Month == month.Month; date = date.AddDays(1)) if (date.DayOfWeek == day) yield return date;
    }
    private static DateTimeOffset? NthWeekday(DateTimeOffset month, DayOfWeek day, int ordinal)
    {
        var dates = AllWeekdays(month, day).ToArray();
        var index = ordinal > 0 ? ordinal - 1 : dates.Length + ordinal;
        return index >= 0 && index < dates.Length ? dates[index] : null;
    }
    private static DateTimeOffset AtTime(DateTimeOffset day, DateTimeOffset time) => new(day.Year, day.Month, day.Day, time.Hour, time.Minute, time.Second, time.Offset);
    private static DateTimeOffset StartOfWeek(DateTimeOffset date) => new DateTimeOffset(date.Date.AddDays(-(int)date.DayOfWeek), date.Offset);
    private static bool IsExcluded(DateTimeOffset start, IReadOnlyList<DateTimeOffset>? exceptions) => exceptions?.Any(value => value == start || (value.TimeOfDay == TimeSpan.Zero && value.Date == start.Date)) == true;
    private static string Key(SchedulerEvent item) => $"{item.RecurrenceId}:{item.Start.UtcDateTime:O}";
    private static DateTimeOffset? ParseUntil(string text)
    {
        if (DateTimeOffset.TryParseExact(text, ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            return text.Length == 8 ? date.AddDays(1).AddTicks(-1) : date;
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date) ? date : null;
    }
    private static IReadOnlyList<SchedulerRecurrenceDay> ParseDays(string text)
    {
        var result = new List<SchedulerRecurrenceDay>();
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var code = token.Length >= 2 ? token[^2..] : token;
            if (!Days.TryGetValue(code, out var day)) continue;
            int? ordinal = token.Length > 2 && int.TryParse(token[..^2], out var parsed) && parsed is >= -5 and <= 5 and not 0 ? parsed : null;
            result.Add(new(day, ordinal));
        }
        return result;
    }
}
