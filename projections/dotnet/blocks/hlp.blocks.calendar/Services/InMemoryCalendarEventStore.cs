using System.Collections.Concurrent;
using System.Text.Json;
using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// In-memory <see cref="ICalendarEventStore"/> for Slice S0. Persists each event as a serialized
/// <see cref="CalendarEventSnapshot"/> keyed by composite <c>(TenantId, Id)</c> — so the
/// EXDATE set + RECURRENCE-ID overrides genuinely round-trip through (de)serialization, not just
/// a held object reference. A durable EF-backed store reuses the same snapshot shape later.
/// </summary>
/// <remarks>
/// Storing the JSON (rather than the live entity) is deliberate: it exercises the real persistence
/// path in tests — a saved series re-loads with its occurrence-level edits intact — and matches the
/// fleet financial-master in-memory pattern (composite <c>(TenantId, Id)</c> keying).
/// </remarks>
public sealed class InMemoryCalendarEventStore : ICalendarEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    // Composite key (tenant, id) → serialized snapshot. ConcurrentDictionary for thread-safety
    // parity with the other in-memory fleet stores.
    private readonly ConcurrentDictionary<(string Tenant, Guid Id), string> _store = new();

    // (tenant, resource) → capacity epoch. Every write or removal that occupies a resource moves its
    // epoch; SaveIfCapacityUnchangedAsync compares and writes under _gate, so the compare-and-set is
    // one step. The lock's ceiling is this process, which is also where all of this store's data
    // lives — the durable implementation does the same thing with a conditional UPDATE.
    private readonly Dictionary<(string Tenant, string Resource), long> _epochs = new();
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        lock (_gate) Write(calendarEvent);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
            return Task.FromResult(_epochs.GetValueOrDefault((tenantId.Value, EpochKey(resource))));
    }

    /// <inheritdoc />
    public Task<bool> SaveIfCapacityUnchangedAsync(
        CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        ArgumentNullException.ThrowIfNull(resource);
        lock (_gate)
        {
            var key = (calendarEvent.TenantId.Value, EpochKey(resource));
            if (_epochs.GetValueOrDefault(key) != expectedEpoch)
                return Task.FromResult(false);
            Write(calendarEvent);
            return Task.FromResult(true);
        }
    }

    /// <summary>Serialize into the store and move the epoch of every resource the event occupies. Call under <c>_gate</c>.</summary>
    private void Write(CalendarEvent calendarEvent)
    {
        var json = JsonSerializer.Serialize(CalendarEventSnapshot.FromEntity(calendarEvent), JsonOptions);
        _store[(calendarEvent.TenantId.Value, calendarEvent.Id.Value)] = json;
        BumpEpochs(calendarEvent);
    }

    /// <summary>
    /// Move the epoch of every resource this event occupies — the headline resource and every
    /// participant, which is exactly the set the free/busy occupancy gather treats as "on the event".
    /// Call under <c>_gate</c>.
    /// </summary>
    private void BumpEpochs(CalendarEvent calendarEvent)
    {
        var tenant = calendarEvent.TenantId.Value;
        foreach (var resource in Occupied(calendarEvent))
        {
            var key = (tenant, EpochKey(resource));
            _epochs[key] = _epochs.GetValueOrDefault(key) + 1;
        }
    }

    private static IEnumerable<ParticipantRef> Occupied(CalendarEvent calendarEvent)
    {
        if (calendarEvent.ResourceRef is { } headline) yield return headline;
        foreach (var participation in calendarEvent.Participations)
            if (participation.Participant != calendarEvent.ResourceRef) yield return participation.Participant;
    }

    private static string EpochKey(ParticipantRef resource) => $"{resource.Kind}:{resource.Value}";

    /// <inheritdoc />
    public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
    {
        if (_store.TryGetValue((tenantId.Value, id.Value), out var json))
        {
            var snapshot = JsonSerializer.Deserialize<CalendarEventSnapshot>(json, JsonOptions)
                ?? throw new InvalidOperationException($"Corrupt snapshot for calendar event {id}.");
            return Task.FromResult<CalendarEvent?>(snapshot.ToEntity());
        }
        return Task.FromResult<CalendarEvent?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
    {
        var tenant = tenantId.Value;
        var events = _store
            .Where(kvp => kvp.Key.Tenant == tenant)
            .Select(kvp => JsonSerializer.Deserialize<CalendarEventSnapshot>(kvp.Value, JsonOptions)!.ToEntity())
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Id.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<CalendarEvent>>(events);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_store.TryRemove((tenantId.Value, id.Value), out var json))
                return Task.FromResult(false);
            // A release frees capacity, so it moves the epoch too: a claim that read capacity before
            // the release must re-read rather than commit against the stale count.
            BumpEpochs(JsonSerializer.Deserialize<CalendarEventSnapshot>(json, JsonOptions)!.ToEntity());
            return Task.FromResult(true);
        }
    }
}
