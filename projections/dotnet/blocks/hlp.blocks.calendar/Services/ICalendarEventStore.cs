using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// The series/occurrence store for <see cref="CalendarEvent"/> — persists the series master plus
/// its EXDATE set and RECURRENCE-ID overrides, tenant-scoped.
/// </summary>
/// <remarks>
/// <para>
/// This is the durable home of the temporal core (Slice S0). It round-trips the full entity —
/// including the EXDATE list and the override store — so a saved series re-loads with its
/// occurrence-level edits intact.
/// </para>
/// <para>
/// All reads are tenant-scoped: a <see cref="TenantId"/> is required and the store never returns
/// another tenant's events (composite <c>(TenantId, Id)</c> keying — the fleet financial-master
/// pattern). S0 ships an in-memory implementation; a durable EF-backed implementation is wired in
/// the local-node-host in a later step.
/// </para>
/// </remarks>
public interface ICalendarEventStore
{
    /// <summary>Insert or replace a calendar event (the series master + its EXDATE/overrides).</summary>
    Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default);

    /// <summary>Load a single event by id within a tenant; <see langword="null"/> if absent or owned by another tenant.</summary>
    Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default);

    /// <summary>List all (non-deleted) events for a tenant.</summary>
    Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default);

    /// <summary>Remove an event by id within a tenant. Idempotent; returns true when an event was removed.</summary>
    Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default);

    /// <summary>
    /// The resource's <b>capacity epoch</b>: an opaque counter that moves whenever this store writes
    /// or removes an event that occupies <paramref name="resource"/>. It carries no meaning beyond
    /// "the occupancy a capacity read was derived from is still the current one".
    /// </summary>
    /// <remarks>
    /// Read it BEFORE the capacity check and pass it to <see cref="SaveIfCapacityUnchangedAsync"/>:
    /// that is the epoch-conditional write DES-0025 <c>booking-eng-24</c> and ADR 0095 ruling 8
    /// require. A prior read is advisory and cannot authorize an unconditional save.
    /// </remarks>
    Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default);

    /// <summary>
    /// Save <paramref name="calendarEvent"/> only while <paramref name="resource"/>'s capacity epoch is
    /// still <paramref name="expectedEpoch"/>. Returns <see langword="false"/> and writes NOTHING when
    /// the epoch moved — the capacity read behind the claim is stale.
    /// </summary>
    /// <remarks>
    /// The comparison and the write are one atomic step <i>in the store</i>. That is the whole point of
    /// this member: an implementation that compares and then writes in two steps, or that leans on a
    /// lock held by one host process, does not satisfy the contract. A durable implementation issues a
    /// conditional update (<c>… WHERE epoch = @expected</c>) inside its own transaction.
    /// </remarks>
    Task<bool> SaveIfCapacityUnchangedAsync(
        CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default);
}
