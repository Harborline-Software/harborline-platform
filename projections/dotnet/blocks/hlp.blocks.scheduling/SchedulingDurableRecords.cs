namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>A scheduling-definition document and its optimistic revision.</summary>
public sealed record SchedulingDefinitionDraft(string TenantId, string DefinitionId, long Revision, string DocumentJson);

/// <summary>The audit artifact that is physically co-committed with a draft revision.</summary>
public sealed record SchedulingDefinitionAudit(string TenantId, string DefinitionId, long Revision, string ActorId, DateTimeOffset OccurredAt);

/// <summary>The calendar subset required by the Wave C durable rows.</summary>
public enum SchedulingCalendarEntityKind : byte { OwnedCalendar = 1, CalendarEvent = 2, ResourceAvailability = 3 }

/// <summary>
/// A lossless calendar aggregate envelope. PayloadJson is the landed calendar entity's serialized
/// representation; identity remains tenant scoped and kind disambiguates equal ids.
/// </summary>
public sealed record SchedulingCalendarEntity(string TenantId, SchedulingCalendarEntityKind Kind, string EntityId, string PayloadJson);

/// <summary>The result of an optimistic draft save.</summary>
public sealed record SchedulingDraftSaveResult(bool Saved, long CurrentRevision)
{
    public static SchedulingDraftSaveResult Conflict(long currentRevision) => new(false, currentRevision);
    public static SchedulingDraftSaveResult Committed(long revision) => new(true, revision);
}
