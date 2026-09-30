using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// Flat, serializable snapshot of a <see cref="ResourceAvailability"/> (Slice S3) — what round-trips
/// through <see cref="IResourceAvailabilityStore"/> (the resource ref + tz + windows + exception
/// dates). Mirrors the <see cref="CalendarEventSnapshot"/> discipline: a flat shape that genuinely
/// (de)serializes so the in-memory store exercises the real persistence path.
/// </summary>
public sealed record ResourceAvailabilitySnapshot
{
    /// <summary>Tenant owning the availability record.</summary>
    public required string TenantId { get; init; }
    /// <summary>Resource whose availability is described.</summary>
    public required ParticipantRefSnapshot ResourceRef { get; init; }
    /// <summary>IANA timezone for the local availability windows.</summary>
    public required string Timezone { get; init; }
    /// <summary>Recurring and one-off availability windows.</summary>
    public required IReadOnlyList<AvailabilityWindowSnapshot> Windows { get; init; }
    /// <summary>Individual dates excluded from availability.</summary>
    public required IReadOnlyList<DateOnly> ExceptionDates { get; init; }

    /// <summary>
    /// The spanning per-resource vacation / closure exceptions (Slice CALENDAR-LAYERS). Not
    /// <c>required</c> so an older S3 snapshot (no spanning exceptions) deserializes to an empty list.
    /// </summary>
    /// <summary>Inclusive exception spans excluded from availability.</summary>
    public IReadOnlyList<ExceptionSpanSnapshot> ExceptionSpans { get; init; } = Array.Empty<ExceptionSpanSnapshot>();

    /// <summary>Captures a domain availability record for persistence.</summary>
    public static ResourceAvailabilitySnapshot FromEntity(ResourceAvailability ra)
    {
        ArgumentNullException.ThrowIfNull(ra);
        return new ResourceAvailabilitySnapshot
        {
            TenantId       = ra.TenantId.Value,
            ResourceRef    = ParticipantRefSnapshot.FromModel(ra.ResourceRef)!,
            Timezone       = ra.Timezone,
            Windows        = ra.Windows.Select(AvailabilityWindowSnapshot.FromModel).ToList(),
            ExceptionDates = ra.ExceptionDates.ToList(),
            ExceptionSpans = ra.ExceptionSpans.Select(ExceptionSpanSnapshot.FromModel).ToList(),
        };
    }

    /// <summary>Rehydrates the domain availability record represented by this snapshot.</summary>
    public ResourceAvailability ToEntity()
        => ResourceAvailability.Rehydrate(
            tenantId:       new TenantId(TenantId),
            resourceRef:    ResourceRef.ToModel(),
            timezone:       Timezone,
            windows:        Windows.Select(w => w.ToModel()),
            exceptionDates: ExceptionDates,
            exceptionSpans: ExceptionSpans.Select(s => s.ToModel()));
}

/// <summary>Flat, serializable form of an <see cref="ExceptionSpan"/> (Slice CALENDAR-LAYERS).</summary>
public sealed record ExceptionSpanSnapshot
{
    /// <summary>First excluded date, inclusive.</summary>
    public required DateOnly Start { get; init; }
    /// <summary>Last excluded date, inclusive.</summary>
    public required DateOnly End { get; init; }
    /// <summary>Optional reason for the exception.</summary>
    public string? Reason { get; init; }

    /// <summary>Converts a domain exception span to its persistence form.</summary>
    public static ExceptionSpanSnapshot FromModel(ExceptionSpan s)
        => new() { Start = s.Start, End = s.End, Reason = s.Reason };

    /// <summary>Recreates the validated domain exception span.</summary>
    public ExceptionSpan ToModel() => ExceptionSpan.Create(Start, End, Reason);
}

/// <summary>Flat, serializable form of an <see cref="AvailabilityWindow"/>.</summary>
public sealed record AvailabilityWindowSnapshot
{
    /// <summary>Date anchoring a local availability window.</summary>
    public required DateOnly AnchorDate { get; init; }
    /// <summary>Local start time, inclusive.</summary>
    public required TimeOnly StartTime { get; init; }
    /// <summary>Local end time, exclusive.</summary>
    public required TimeOnly EndTime { get; init; }
    /// <summary>Optional recurrence rule for the window.</summary>
    public string? Rrule { get; init; }

    /// <summary>Converts a domain availability window to its persistence form.</summary>
    public static AvailabilityWindowSnapshot FromModel(AvailabilityWindow w)
        => new()
        {
            AnchorDate = w.AnchorDate,
            StartTime  = w.StartTime,
            EndTime    = w.EndTime,
            Rrule      = w.Rrule,
        };

    /// <summary>Recreates the validated domain availability window.</summary>
    public AvailabilityWindow ToModel() => AvailabilityWindow.Create(AnchorDate, StartTime, EndTime, Rrule);
}
