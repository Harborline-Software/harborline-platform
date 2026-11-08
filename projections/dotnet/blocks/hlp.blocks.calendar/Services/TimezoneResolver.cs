namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// Resolves a wall-clock local date+time in an IANA timezone to a UTC <see cref="DateTimeOffset"/>. The time-of-day and
/// zone layer that sits on top of the date-granular RRULE expansion: each generated date and its time of day become a
/// real instant here.
/// </summary>
/// <remarks>
/// The conversion is the platform's one wall-clock resolver,
/// <see cref="Harborline.Foundation.Scheduling.WallClockResolver"/> (ADR-0123 decision 4), so the calendar block, the
/// availability runtime and Scheduling cannot resolve a local time differently. It follows RFC 5545 §3.3.5 and Errata
/// 4271: an ambiguous fall-back time is its <b>first</b> occurrence (01:30 on 1 November 2026 in New York is 05:30Z), and
/// a spring-forward gap time is read at the offset before the gap (02:30 on 8 March 2026 there is 07:30Z). The standard
/// offset this helper used to pick for an ambiguous time was R-0146 call 5's case (e) and is gone (T-1175).
/// </remarks>
public static class TimezoneResolver
{
    /// <summary>
    /// Resolve <paramref name="ianaTimezone"/> to a <see cref="TimeZoneInfo"/>. Throws
    /// <see cref="TimeZoneNotFoundException"/> for an unknown id (callers should validate the IANA
    /// id at event-create time; the entity already requires a non-empty tz).
    /// </summary>
    public static TimeZoneInfo Resolve(string ianaTimezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ianaTimezone);
        return TimeZoneInfo.FindSystemTimeZoneById(ianaTimezone);
    }

    /// <summary>
    /// Convert a wall-clock <paramref name="localDate"/> + <paramref name="localTime"/> in <paramref name="tz"/> to a UTC
    /// <see cref="DateTimeOffset"/> through <see cref="Harborline.Foundation.Scheduling.WallClockResolver"/>.
    /// </summary>
    public static DateTimeOffset ToUtcInstant(DateOnly localDate, TimeOnly localTime, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        return Harborline.Foundation.Scheduling.WallClockResolver.ToUtcInstant(localDate, localTime, IanaId(tz));
    }

    // TimeZoneInfo found by an IANA id keeps that id; one found by a Windows id is mapped to its IANA equivalent.
    private static string IanaId(TimeZoneInfo tz) =>
        tz.HasIanaId || !TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out var iana) ? tz.Id : iana;
}
