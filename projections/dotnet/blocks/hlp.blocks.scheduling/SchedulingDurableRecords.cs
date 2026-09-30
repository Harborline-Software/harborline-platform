namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>The calendar subset required by the Wave C durable rows.</summary>
public enum SchedulingCalendarEntityKind : byte { OwnedCalendar = 1, CalendarEvent = 2, ResourceAvailability = 3 }

/// <summary>
/// A lossless calendar aggregate envelope. PayloadJson is the landed calendar entity's serialized
/// representation; identity remains tenant scoped and kind disambiguates equal ids.
/// </summary>
public sealed record SchedulingCalendarEntity(string TenantId, SchedulingCalendarEntityKind Kind, string EntityId, string PayloadJson);
