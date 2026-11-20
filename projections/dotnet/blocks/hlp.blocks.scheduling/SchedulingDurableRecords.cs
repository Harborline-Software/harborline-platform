using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Blocks.Scheduling.Planning;
using Harborline.Foundation.ExecutionRuntime;

namespace Harborline.Blocks.Scheduling.Durable;

/// <summary>Write-once Plan state, addressed by tenant and execution identity.</summary>
/// <param name="RunId">The plan-run identity.</param>
/// <param name="ScheduleId">Exact schedule version planned.</param>
/// <param name="CausedBy">Initiating run, if any.</param>
/// <param name="GateAt">Instant the gate was asked.</param>
/// <param name="FactSetPins">Supplied fact versions and completeness.</param>
/// <param name="Proposal">The planner's entire answer.</param>
public sealed record SchedulingPlan(RunId RunId, ScheduleDefinitionPin ScheduleId, RunId? CausedBy,
    DateTimeOffset GateAt, IReadOnlyList<PlanningFactSetPin> FactSetPins, SchedulingProposal Proposal);

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
