using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Authorization;

namespace Harborline.Blocks.Calendar.Services;

/// <summary>
/// The default <see cref="IBookingService"/> (Slice S3, Direction A). The booking gate reads
/// availability through the one shared composition, <see cref="IAvailabilityRuntime"/> (T-626,
/// ADR 0080): supply, shared-exception days, occupancy and the capacity kind are all derived there,
/// so the gate and the free/busy view cannot disagree. This class keeps what Booking owns: the
/// refusal vocabulary, the padding default and the persisted <see cref="Occupancy.Bookable"/> event.
/// </summary>
/// <remarks>
/// The <see cref="IResourceAvailabilityStore"/> is read here only for the resource's timezone, so the
/// stored event is expressed in local wall-clock and re-expands to the same UTC instant. It is not a
/// second composition: the supply itself is derived by the runtime.
/// </remarks>
/// <remarks>
/// The requester is the kernel's authenticated context (<see cref="IPartyContext"/>), never a
/// caller-supplied id (T-568, L535): every booking is attributed to the Party it resolves, and a call
/// with no resolvable identity is refused before any read.
/// </remarks>
public sealed class BookingService : IBookingService
{
    private readonly IAvailabilityRuntime _runtime;
    private readonly IResourceAvailabilityStore _availabilityStore;
    private readonly ICalendarEventStore _eventStore;
    private readonly IPaddingPolicy _paddingPolicy;
    private readonly IPartyContext _requester;

    public BookingService(
        IAvailabilityRuntime runtime,
        IResourceAvailabilityStore availabilityStore,
        ICalendarEventStore eventStore,
        IPaddingPolicy paddingPolicy,
        IPartyContext requester)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(availabilityStore);
        ArgumentNullException.ThrowIfNull(eventStore);
        ArgumentNullException.ThrowIfNull(paddingPolicy);
        ArgumentNullException.ThrowIfNull(requester);
        _runtime = runtime;
        _availabilityStore = availabilityStore;
        _eventStore = eventStore;
        _paddingPolicy = paddingPolicy;
        _requester = requester;
    }

    /// <inheritdoc />
    public async Task<BookingOutcome> Book(
        TenantId tenantId,
        ParticipantRef resourceRef,
        string title,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        ParticipantRef? attendee = null,
        ContextRef? scheduledAgainst = null,
        EventPadding? padding = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resourceRef);
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title must be non-empty.", nameof(title));
        if (endUtc <= startUtc)
            return BookingOutcome.Rejected(BookingOutcome.SlotInverted);

        // The requester is the authenticated principal's server-derived Party, never a caller-supplied
        // id (L535: a booking is requested by somebody). No identity, no booking — resolved before any
        // read so a refused caller learns nothing about the resource's availability.
        Guid bookedBy;
        try
        {
            bookedBy = await _requester.GetCurrentPartyIdAsync(ct).ConfigureAwait(false);
        }
        catch (PrincipalPartyResolutionException)
        {
            return BookingOutcome.Rejected(BookingOutcome.NoRequester);
        }
        if (bookedBy == Guid.Empty)
            return BookingOutcome.Rejected(BookingOutcome.NoRequester);

        // Resolve the padding: a per-event override (passed in) wins over the configured policy default
        // (defaulted-at-booking). EventPadding.None ⇒ the candidate occupies exactly its visible slot.
        var effectivePadding = (padding is { } p)
            ? EventPadding.Of(p.Pre, p.Post)
            : _paddingPolicy.ResolveDefault(tenantId, resourceRef);

        // The availability record carries the resource's timezone (and no record means nothing to book into).
        var availability = await _availabilityStore.GetAsync(tenantId, resourceRef, ct).ConfigureAwait(false);
        if (availability is null)
            return BookingOutcome.Rejected(BookingOutcome.NoAvailability);

        // The gate: the VISIBLE slot inside the supply and the padded OCCUPIED footprint free of existing
        // occupancy, both derived by the one composition. The shipped booking path books one exclusive
        // resource; the padding is the buffer that is part of the hold.
        var read = await _runtime
            .Read(tenantId, new AvailabilityRequest(startUtc, endUtc, [ResourceCapacity.Exclusive(resourceRef, effectivePadding)]), ct)
            .ConfigureAwait(false);
        if (read.Refusal is not null)
            return BookingOutcome.Rejected(BookingOutcome.SlotInverted);
        switch (read.Resources[0].Unavailability)
        {
            case Unavailability.OutsideSupply:
                return BookingOutcome.Rejected(BookingOutcome.NoAvailability);
            case Unavailability.CapacityExhausted:
                return BookingOutcome.Rejected(BookingOutcome.SlotConflict);
        }

        // Admitted. Build a single-occurrence Bookable event from the VISIBLE UTC slot, expressed in the
        // resource's local wall-clock + tz (so the stored event reads naturally and re-expands to the
        // same UTC instant), carrying the resolved padding envelope.
        var ev = BuildBookableEvent(
            tenantId, resourceRef, title, startUtc, endUtc, bookedBy, attendee, scheduledAgainst,
            availability.Timezone, effectivePadding);

        await _eventStore.SaveAsync(ev, ct).ConfigureAwait(false);
        return BookingOutcome.Booked(ev);
    }

    /// <summary>
    /// Build the single-occurrence <see cref="Occupancy.Bookable"/> event from the UTC slot, expressed
    /// in the resource's local wall-clock + timezone. The event re-expands (via
    /// <c>ExpandInstants</c>) to the same UTC instant, so the booking is internally consistent with
    /// free/busy.
    /// </summary>
    private static CalendarEvent BuildBookableEvent(
        TenantId tenantId,
        ParticipantRef resourceRef,
        string title,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        Guid bookedBy,
        ParticipantRef? attendee,
        ContextRef? scheduledAgainst,
        string timezone,
        EventPadding padding)
    {
        var tz = TimezoneResolver.Resolve(timezone);
        var localStart = TimeZoneInfo.ConvertTime(startUtc, tz);
        var localEnd = TimeZoneInfo.ConvertTime(endUtc, tz);

        var startDate = DateOnly.FromDateTime(localStart.DateTime);
        var endDate = DateOnly.FromDateTime(localEnd.DateTime);
        var startTime = TimeOnly.FromDateTime(localStart.DateTime);
        var endTime = TimeOnly.FromDateTime(localEnd.DateTime);

        var ev = CalendarEvent.Create(
            tenantId:  tenantId,
            title:     title,
            start:     startDate,
            end:       endDate,
            createdBy: bookedBy,
            timezone:  timezone,
            startTime: startTime,
            endTime:   endTime,
            occupancy: Occupancy.Bookable,
            padding:   padding);

        ev.SetResource(resourceRef, bookedBy, ParticipationStatus.Confirmed);
        if (attendee is not null)
            ev.AddParticipation(CalendarParticipation.Attendee(attendee), bookedBy);
        if (scheduledAgainst is not null)
            ev.SetScheduledAgainst(scheduledAgainst, bookedBy);

        return ev;
    }
}
