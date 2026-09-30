using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// Flat, serializable snapshot of a <see cref="SharedCalendar"/> (Slice CALENDAR-LAYERS) — what
/// round-trips through <see cref="ISharedCalendarStore"/> (id + tenant + name + exception spans).
/// Mirrors the <see cref="CalendarEventSnapshot"/> / <see cref="ResourceAvailabilitySnapshot"/>
/// discipline: a flat shape that genuinely (de)serializes so the in-memory store exercises the real
/// persistence path; a durable EF-backed store reuses the same shape.
/// </summary>
public sealed record SharedCalendarSnapshot
{
    /// <summary>Persisted shared-calendar identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Tenant owning the shared calendar.</summary>
    public required string TenantId { get; init; }
    /// <summary>Display name.</summary>
    public required string Name { get; init; }
    /// <summary>Inclusive exception spans applied by the shared calendar.</summary>
    public required IReadOnlyList<ExceptionSpanSnapshot> Exceptions { get; init; }

    /// <summary>Captures a domain shared calendar for persistence.</summary>
    public static SharedCalendarSnapshot FromEntity(SharedCalendar c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new SharedCalendarSnapshot
        {
            Id         = c.Id.Value,
            TenantId   = c.TenantId.Value,
            Name       = c.Name,
            Exceptions = c.Exceptions.Select(ExceptionSpanSnapshot.FromModel).ToList(),
        };
    }

    /// <summary>Rehydrates the domain shared calendar represented by this snapshot.</summary>
    public SharedCalendar ToEntity()
        => SharedCalendar.Rehydrate(
            id:         new SharedCalendarId(Id),
            tenantId:   new TenantId(TenantId),
            name:       Name,
            exceptions: Exceptions.Select(e => e.ToModel()));
}
