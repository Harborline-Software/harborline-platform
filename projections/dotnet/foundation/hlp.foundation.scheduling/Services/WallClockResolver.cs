using NodaTime;
using NodaTime.TimeZones;

namespace Harborline.Foundation.Scheduling;

/// <summary>
/// The one wall-clock resolver (ADR-0123 decision 4): a local date and time in an IANA zone to its UTC instant, as RFC 5545
/// §3.3.5 requires. The calendar block, the availability runtime and Scheduling resolve through it, so they cannot drift
/// apart (ADR 0080). Its engine is NodaTime, which Ical.Net already brings (ADR-0122 decision 3).
/// </summary>
/// <remarks>
/// RFC 5545 §3.3.5: an ambiguous local time "refers to the first occurrence of the referenced time", and a time in a
/// spring-forward gap is "interpreted using the UTC offset before the gap"; Errata 4271 applies both to recurrence
/// instances. So 01:30 on 1 November 2026 in New York is 05:30Z (the first, daylight instance), and 02:30 on 8 March 2026
/// there is 07:30Z (read at -05:00, the offset before the gap). NodaTime's lenient resolver makes exactly these two
/// choices.
/// </remarks>
public static class WallClockResolver
{
    /// <summary>Converts a kernel instant to the series' local date and time using the same IANA database as resolution.</summary>
    /// <exception cref="ArgumentException">Thrown when the zone is unknown.</exception>
    public static DateTime ToLocalDateTime(DateTimeOffset instant, string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone)
            ?? throw new ArgumentException($"'{timezone}' is not an IANA time zone id.", nameof(timezone));
        return Instant.FromDateTimeOffset(instant).InZone(zone).LocalDateTime.ToDateTimeUnspecified();
    }

    /// <summary>Resolves <paramref name="date"/> at <paramref name="time"/> in <paramref name="timezone"/> to a UTC instant.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timezone"/> is not an IANA time zone id.</exception>
    public static DateTimeOffset ToUtcInstant(DateOnly date, TimeOnly time, string timezone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timezone);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone)
            ?? throw new ArgumentException($"'{timezone}' is not an IANA time zone id.", nameof(timezone));
        var local = new LocalDate(date.Year, date.Month, date.Day) + LocalTime.FromTicksSinceMidnight(time.Ticks);
        return zone.ResolveLocal(local, Resolvers.LenientResolver).ToDateTimeOffset().ToUniversalTime();
    }

    /// <summary>Whether <paramref name="timezone"/> is an IANA time zone id this resolver knows.</summary>
    public static bool IsKnownZone(string timezone) =>
        !string.IsNullOrWhiteSpace(timezone) && DateTimeZoneProviders.Tzdb.GetZoneOrNull(timezone) is not null;
}
