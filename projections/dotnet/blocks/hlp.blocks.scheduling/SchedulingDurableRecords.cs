namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>A scheduling-definition document and its optimistic revision.</summary>
/// <param name="TenantId">Tenant owning the definition.</param>
/// <param name="DefinitionId">Stable definition identifier.</param>
/// <param name="Revision">Optimistic-concurrency revision.</param>
/// <param name="DocumentJson">Serialized definition document.</param>
public sealed record SchedulingDefinitionDraft(string TenantId, string DefinitionId, long Revision, string DocumentJson);

/// <summary>The audit artifact that is physically co-committed with a draft revision.</summary>
/// <param name="TenantId">Tenant owning the audited definition.</param>
/// <param name="DefinitionId">Definition identifier.</param>
/// <param name="Revision">Revision committed by the actor.</param>
/// <param name="ActorId">Server actor that committed the revision.</param>
/// <param name="OccurredAt">Commit timestamp.</param>
public sealed record SchedulingDefinitionAudit(string TenantId, string DefinitionId, long Revision, string ActorId, DateTimeOffset OccurredAt);

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

/// <summary>The result of an optimistic draft save.</summary>
/// <param name="Saved">Whether the requested revision was committed.</param>
/// <param name="CurrentRevision">Committed revision, or the observed revision on conflict.</param>
public sealed record SchedulingDraftSaveResult(bool Saved, long CurrentRevision)
{
    /// <summary>Creates the result returned when the expected revision was stale.</summary>
    /// <param name="currentRevision">Revision currently stored.</param>
    public static SchedulingDraftSaveResult Conflict(long currentRevision) => new(false, currentRevision);
    /// <summary>Creates the result returned after committing a new revision.</summary>
    /// <param name="revision">Newly committed revision.</param>
    public static SchedulingDraftSaveResult Committed(long revision) => new(true, revision);
}
