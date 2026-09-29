using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.Calendar.Booking;

/// <summary>One required Resource of a Bookable, resolved to the Party or Asset that supplies it.</summary>
public sealed record BookableClaimResource(ParticipantRef Resource, BookingResourceDefinition Definition);

/// <summary>Stable refusal codes of a Bookable claim (T-606).</summary>
public static class BookableClaimCodes
{
    /// <summary>The window omits an endpoint or ends at or before its start.</summary>
    public const string WindowInvalid = "booking.claim.window_invalid";
    /// <summary>A Resource the Bookable requires has no resolved supplier, or it requires none.</summary>
    public const string RequirementUnresolved = "booking.claim.requirement_unresolved";
    /// <summary>No authenticated requester could be resolved.</summary>
    public const string NoRequester = "booking.claim.no_requester";
    /// <summary>The window is not inside a required Resource's supply.</summary>
    public const string OutsideSupply = "booking.claim.outside_supply";
    /// <summary>A required Resource has no place left over its buffered footprint.</summary>
    public const string CapacityExhausted = "booking.claim.capacity_exhausted";
    /// <summary>Every attempt lost a capacity epoch to a concurrent write; nothing was written.</summary>
    public const string Contended = "booking.claim.contended";
}

/// <summary>The events a claim committed, one per required Resource, or the refusal and nothing written.</summary>
public sealed record BookableClaimOutcome(IReadOnlyList<CalendarEvent> Events, string? Refusal)
{
    /// <summary>True when every required Resource was claimed.</summary>
    public bool Claimed => Refusal is null;

    internal static BookableClaimOutcome Refused(string code) => new([], code);
}

/// <summary>
/// Claims a Bookable across its required Resources (T-606; DES-0025 <c>booking-eng-1</c>, <c>-5</c>,
/// <c>-6</c>, <c>-8</c>, <c>-21</c>). Capacity is read through the one composition,
/// <see cref="IAvailabilityRuntime"/>: exclusive refuses any overlap, a pool refuses where the overlap
/// depth reaches its size, and the required set is a conjunction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Buffers are the Resource's.</b> Each required Resource's footprint is the window widened by its
/// own <see cref="BookingResourceDefinition.SetupMinutes"/> before and
/// <see cref="BookingResourceDefinition.CleanupMinutes"/> after. The claim writes one
/// <see cref="Occupancy.Bookable"/> event per Resource carrying that Resource's buffers as its padding,
/// so a later read sees each Resource's full buffered footprint. The DES-0025 ruling on where buffers
/// live is still open; the Resource fields are the recommended answer.
/// </para>
/// <para>
/// <b>Atomic across the conjunction.</b> Every Resource's capacity epoch is read before the capacity
/// read, and the events commit through one
/// <see cref="ICalendarEventStore.SaveAllIfCapacityUnchangedAsync"/>: if any epoch moved, nothing is
/// written and the claim re-reads (<c>booking-eng-24</c>, ADR 0095 ruling 8).
/// </para>
/// </remarks>
public sealed class BookableClaimService
{
    /// <summary>
    /// How many times a claim re-reads capacity after losing an epoch before it refuses. Each pass is a
    /// full recheck, so refusing at the cap never claims an occupied place.
    /// </summary>
    private const int MaxCapacityAttempts = 3;

    private readonly IAvailabilityRuntime _runtime;
    private readonly ICalendarEventStore _events;
    private readonly IPartyContext _requester;

    public BookableClaimService(IAvailabilityRuntime runtime, ICalendarEventStore events, IPartyContext requester)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(requester);
        _runtime = runtime;
        _events = events;
        _requester = requester;
    }

    /// <summary>
    /// Claim <paramref name="bookable"/> over [<paramref name="startUtc"/>, <paramref name="endUtc"/>),
    /// resolving each Resource it requires by name from <paramref name="resources"/>. On success one
    /// event per required Resource is committed; on refusal nothing is written.
    /// </summary>
    public async Task<BookableClaimOutcome> Claim(
        TenantId tenantId,
        BookingBookableDefinition bookable,
        IReadOnlyList<BookableClaimResource> resources,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bookable);
        ArgumentNullException.ThrowIfNull(resources);

        var required = new List<BookableClaimResource>(bookable.Requires.Count);
        foreach (var name in bookable.Requires)
        {
            if (resources.FirstOrDefault(r => r.Definition.Name == name) is not { } resolved)
                return BookableClaimOutcome.Refused(BookableClaimCodes.RequirementUnresolved);
            required.Add(resolved);
        }
        if (required.Count == 0)
            return BookableClaimOutcome.Refused(BookableClaimCodes.RequirementUnresolved);

        // The requester is the authenticated Party, resolved before any read (T-568, L535).
        Guid claimedBy;
        try
        {
            claimedBy = await _requester.GetCurrentPartyIdAsync(ct).ConfigureAwait(false);
        }
        catch (PrincipalPartyResolutionException)
        {
            return BookableClaimOutcome.Refused(BookableClaimCodes.NoRequester);
        }
        if (claimedBy == Guid.Empty)
            return BookableClaimOutcome.Refused(BookableClaimCodes.NoRequester);

        var capacities = required.Select(Capacity).ToArray();
        for (var attempt = 1; attempt <= MaxCapacityAttempts; attempt++)
        {
            // Epochs first: the capacity read below is advisory until the conditional write accepts it.
            var epochs = new Dictionary<ParticipantRef, long>(required.Count);
            foreach (var resource in required)
                epochs[resource.Resource] = await _events.GetCapacityEpochAsync(tenantId, resource.Resource, ct).ConfigureAwait(false);

            var read = await _runtime.Read(tenantId, new AvailabilityRequest(startUtc, endUtc, capacities), ct).ConfigureAwait(false);
            if (read.Refusal is not null)
                return BookableClaimOutcome.Refused(BookableClaimCodes.WindowInvalid);
            if (read.Resources.Any(r => r.Unavailability == Unavailability.OutsideSupply))
                return BookableClaimOutcome.Refused(BookableClaimCodes.OutsideSupply);
            if (!read.Available)
                return BookableClaimOutcome.Refused(BookableClaimCodes.CapacityExhausted);

            var claimed = required
                .Select((resource, i) => Occupying(tenantId, bookable.Name, resource.Resource, capacities[i].Buffer, startUtc, endUtc, claimedBy))
                .ToArray();
            if (await _events.SaveAllIfCapacityUnchangedAsync(tenantId, claimed, epochs, ct).ConfigureAwait(false))
                return new BookableClaimOutcome(claimed, null);
            // An epoch moved: nothing was written. Re-read; a claim that lost the last place is refused
            // CapacityExhausted on the next pass.
        }
        return BookableClaimOutcome.Refused(BookableClaimCodes.Contended);
    }

    /// <summary>The runtime input for one required Resource: its capacity kind and its own buffers.</summary>
    private static ResourceCapacity Capacity(BookableClaimResource resource)
    {
        var definition = resource.Definition;
        var buffer = EventPadding.Of(TimeSpan.FromMinutes(definition.SetupMinutes), TimeSpan.FromMinutes(definition.CleanupMinutes));
        return definition.CapacityKind == CapacityKind.Pool
            ? ResourceCapacity.Pool(resource.Resource, definition.PoolSize!.Value, buffer)
            : ResourceCapacity.Exclusive(resource.Resource, buffer);
    }

    /// <summary>A single-occurrence Bookable event on one Resource, in UTC, occupying its buffered footprint.</summary>
    private static CalendarEvent Occupying(
        TenantId tenantId, string title, ParticipantRef resource, EventPadding buffer,
        DateTimeOffset startUtc, DateTimeOffset endUtc, Guid claimedBy)
    {
        var ev = CalendarEvent.Create(
            tenantId:  tenantId,
            title:     title,
            start:     DateOnly.FromDateTime(startUtc.UtcDateTime),
            end:       DateOnly.FromDateTime(endUtc.UtcDateTime),
            createdBy: claimedBy,
            timezone:  "UTC",
            startTime: TimeOnly.FromDateTime(startUtc.UtcDateTime),
            endTime:   TimeOnly.FromDateTime(endUtc.UtcDateTime),
            occupancy: Occupancy.Bookable,
            padding:   buffer);
        ev.SetResource(resource, claimedBy, ParticipationStatus.Confirmed);
        return ev;
    }
}
