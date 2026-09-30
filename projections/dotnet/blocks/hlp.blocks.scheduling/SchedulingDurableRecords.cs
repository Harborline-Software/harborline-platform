namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>The calendar subset required by the Wave C durable rows.</summary>
public enum SchedulingCalendarEntityKind : byte
{
    /// <summary>An owned calendar aggregate.</summary>
    OwnedCalendar = 1,
    /// <summary>A calendar event aggregate.</summary>
    CalendarEvent = 2,
    /// <summary>A resource-availability aggregate.</summary>
    ResourceAvailability = 3,
}

/// <summary>
/// A lossless calendar aggregate envelope. PayloadJson is the landed calendar entity's serialized
/// representation; identity remains tenant scoped and kind disambiguates equal ids.
/// </summary>
/// <param name="TenantId">Tenant owning the entity.</param>
/// <param name="Kind">Entity discriminator.</param>
/// <param name="EntityId">Tenant-scoped entity identifier.</param>
/// <param name="PayloadJson">Lossless serialized entity payload.</param>
public sealed record SchedulingCalendarEntity(string TenantId, SchedulingCalendarEntityKind Kind, string EntityId, string PayloadJson);
