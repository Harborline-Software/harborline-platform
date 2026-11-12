using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Scheduling;

namespace Harborline.Blocks.Scheduling;

internal static class OccurrenceTime
{
    // Keep calendar components until a real due point exists; XmlConvert alone approximates months and years.
    private static readonly Regex CalendarPart = new(@"^P(?:(\d+)Y)?(?:(\d+)M)?(?:(\d+)W)?(?:(\d+)D)?", RegexOptions.CultureInvariant);

    internal static DateTimeOffset Add(DateTimeOffset due, string duration, string? zone)
    {
        if (ScheduleDefinitionAdmission.SignedDuration(duration) is null)
            throw new OccurrenceRefusalException("scheduling.occurrence.duration_invalid");
        var sign = duration.StartsWith('-') ? -1 : 1;
        var text = duration.TrimStart('-');
        var parts = CalendarPart.Match(text);
        int Number(int group) => parts.Groups[group].Success
            ? checked(int.Parse(parts.Groups[group].Value, CultureInfo.InvariantCulture) * sign) : 0;
        var local = zone is null ? due.DateTime : WallClockResolver.ToLocalDateTime(due, zone);
        var days = checked(Number(3) * 7 + Number(4));
        if (Number(1) != 0 || Number(2) != 0 || days != 0)
        {
            local = local.AddYears(Number(1)).AddMonths(Number(2)).AddDays(days);
            due = zone is null ? new DateTimeOffset(local, due.Offset)
                : WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local), zone);
        }
        var time = text[parts.Length..];
        return time.Length == 0 ? due : due.AddTicks(checked(XmlConvert.ToTimeSpan("P" + time).Ticks * sign));
    }

    internal static SchedulingOccurrence Create(DueOccurrenceRequest request, DateTimeOffset now, string identity,
        DateTimeOffset due, ScheduleWindow? window, ScheduleTolerance? tolerance, DateTime? recurrence = null)
    {
        var series = request.Series;
        var zone = series.Timezone;
        DateTimeOffset? earliest = null, latest = null;
        if (window is not null)
        {
            if (series.Definition.Basis is null)
            {
                earliest = ScheduleDefinitionAdmission.Instant(window.Earliest);
                latest = ScheduleDefinitionAdmission.Instant(window.Latest);
            }
            else
            {
                earliest = Add(due, window.Earliest, zone);
                latest = Add(due, window.Latest, zone);
            }
            if (earliest is null || latest is null || earliest > due || latest < due || earliest >= latest)
                throw new OccurrenceRefusalException("scheduling.definition.window_invalid");
        }
        var completion = request.Completions.Where(c => c.SeriesId == series.SeriesId && c.OccurrenceId == identity && c.CompletedAt <= now)
            .OrderBy(c => c.CompletedAt).ThenBy(c => c.CompletionId, StringComparer.Ordinal).FirstOrDefault();
        var observed = completion?.CompletedAt ?? now;
        var early = tolerance is null ? due : Add(due, tolerance.Early.StartsWith('-') ? tolerance.Early[1..] : "-" + tolerance.Early, zone);
        var late = tolerance is null ? due : Add(due, tolerance.Late, zone);
        var classification = latest is not null && (completion is null ? now > latest : observed < earliest || observed > latest)
            ? OccurrenceClassification.Void
            : completion is null && now < due ? OccurrenceClassification.Pending
            : observed < early ? OccurrenceClassification.Early
            : observed > late ? OccurrenceClassification.Late : OccurrenceClassification.OnTime;
        return new(identity, series.SeriesId, series.SubjectRef, due, earliest, latest, classification,
            completion?.CompletedAt, completion is null && now >= due && classification != OccurrenceClassification.Void,
            completion is null && now > due ? now - due : TimeSpan.Zero, recurrence, series.Timezone,
            WallClockResolver.ToLocalDateTime(now, zone ?? "UTC"));
    }

    internal static string Key(params string[] parts) => string.Join('/', parts.Select(Uri.EscapeDataString));
    internal static bool InRange(DueOccurrenceRequest request, DateTimeOffset due) => due >= request.From && due <= request.To;
}
