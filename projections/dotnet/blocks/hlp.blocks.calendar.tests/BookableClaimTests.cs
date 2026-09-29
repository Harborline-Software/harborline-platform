using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Booking claims a Bookable across pool and mixed Resources (T-606; DES-0025 <c>booking-eng-1</c>,
/// <c>-5</c>, <c>-6</c>, <c>-8</c>, <c>-21</c>). Capacity is read through <see cref="IAvailabilityRuntime"/>;
/// each Resource's buffers are its own <c>SetupMinutes</c> and <c>CleanupMinutes</c>; and the claim
/// commits every required Resource in one epoch-conditional write or writes nothing.
/// </summary>
public sealed class BookableClaimTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 3, 4);

    private static readonly ParticipantRef SeatsRef = ParticipantRef.Asset("asset-seats");
    private static readonly ParticipantRef RoomRef = ParticipantRef.Asset("asset-room");
    private static readonly ParticipantRef NursesRef = ParticipantRef.Asset("asset-nurses");

    // A pool of two with no buffers; an exclusive room with 30 minutes of cleanup; a pool of two
    // nurses with 15 minutes of setup.
    private static readonly BookableClaimResource Seats = new(SeatsRef, Resource("Seats", CapacityKind.Pool, 2, 0, 0));
    private static readonly BookableClaimResource Room = new(RoomRef, Resource("Room", CapacityKind.Exclusive, null, 0, 30));
    private static readonly BookableClaimResource Nurses = new(NursesRef, Resource("Nurses", CapacityKind.Pool, 2, 15, 0));

    private static readonly BookingBookableDefinition SeatBooking = Bookable("Seat booking", "Seats");
    private static readonly BookingBookableDefinition RoomBooking = Bookable("Room booking", "Room");
    private static readonly BookingBookableDefinition NurseVisit = Bookable("Nurse visit", "Nurses");
    private static readonly BookingBookableDefinition Procedure = Bookable("Procedure", "Room", "Nurses");

    private static readonly BookableClaimResource[] All = [Seats, Room, Nurses];

    private static DateTimeOffset Utc(int hour, int minute = 0) => new(2026, 3, 4, hour, minute, 0, TimeSpan.Zero);

    // ----------------------------------------------------------------
    // Pool: a pool of two holds two overlapping claims and refuses the third.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Pool_of_two_refuses_the_third_overlapping_claim()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        var first = await claims.Claim(Acme, SeatBooking, All, Utc(10), Utc(11));
        var second = await claims.Claim(Acme, SeatBooking, All, Utc(10, 30), Utc(11, 30));
        var third = await claims.Claim(Acme, SeatBooking, All, Utc(10, 45), Utc(11, 15));

        Assert.True(first.Claimed);
        Assert.True(second.Claimed);
        Assert.False(third.Claimed);
        Assert.Equal(BookableClaimCodes.CapacityExhausted, third.Refusal);
        Assert.Empty(third.Events);
        Assert.Equal(2, (await store.ListAsync(Acme)).Count);

        // The refusal is the overlap, not the count: a third claim clear of the other two fits.
        Assert.True((await claims.Claim(Acme, SeatBooking, All, Utc(11, 30), Utc(12, 30))).Claimed);
    }

    // ----------------------------------------------------------------
    // Exclusive: one resource refuses a second overlapping claim.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Exclusive_resource_refuses_a_second_overlapping_claim()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        var first = await claims.Claim(Acme, RoomBooking, All, Utc(10), Utc(11));
        var second = await claims.Claim(Acme, RoomBooking, All, Utc(10, 30), Utc(11, 30));

        Assert.True(first.Claimed);
        var claimed = Assert.Single(first.Events);
        Assert.Equal(RoomRef, claimed.ResourceRef);
        Assert.Equal(Occupancy.Bookable, claimed.Occupancy);
        Assert.Equal(Actor, claimed.CreatedBy);
        Assert.False(second.Claimed);
        Assert.Equal(BookableClaimCodes.CapacityExhausted, second.Refusal);
        Assert.Single(await store.ListAsync(Acme));
    }

    [Fact]
    public async Task Exclusive_resource_refuses_a_claim_inside_the_previous_claims_cleanup()
    {
        // The room's 30 minutes of cleanup belong to the first claim: 11:00-11:30 is still occupied,
        // though the visible windows only touch. After the cleanup, the room is free.
        var claims = await SutAsync(new InMemoryCalendarEventStore());
        Assert.True((await claims.Claim(Acme, RoomBooking, All, Utc(10), Utc(11))).Claimed);

        Assert.Equal(BookableClaimCodes.CapacityExhausted,
            (await claims.Claim(Acme, RoomBooking, All, Utc(11), Utc(12))).Refusal);
        Assert.True((await claims.Claim(Acme, RoomBooking, All, Utc(11, 30), Utc(12, 30))).Claimed);
    }

    // ----------------------------------------------------------------
    // Mixed: a two-resource conjunction succeeds only when both full buffered footprints are free.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Mixed_conjunction_is_refused_when_the_exclusive_sides_buffered_footprint_is_taken()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);
        Assert.True((await claims.Claim(Acme, RoomBooking, All, Utc(10), Utc(11))).Claimed);

        // The nurses are free at 11:00; the room is inside its previous claim's cleanup.
        var refused = await claims.Claim(Acme, Procedure, All, Utc(11), Utc(12));

        Assert.Equal(BookableClaimCodes.CapacityExhausted, refused.Refusal);
        Assert.Empty(refused.Events);
        Assert.DoesNotContain(await store.ListAsync(Acme), e => e.ResourceRef == NursesRef);
    }

    [Fact]
    public async Task Mixed_conjunction_is_refused_when_the_pool_sides_setup_buffer_meets_a_full_pool()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);
        Assert.True((await claims.Claim(Acme, NurseVisit, All, Utc(9), Utc(10))).Claimed);
        Assert.True((await claims.Claim(Acme, NurseVisit, All, Utc(9), Utc(10))).Claimed);

        // The room is free at 10:00; the nurses' 15 minutes of setup reach back into the full pool.
        var refused = await claims.Claim(Acme, Procedure, All, Utc(10), Utc(11));

        Assert.Equal(BookableClaimCodes.CapacityExhausted, refused.Refusal);
        Assert.DoesNotContain(await store.ListAsync(Acme), e => e.ResourceRef == RoomRef);

        // Once the setup clears the full pool, both footprints are free.
        Assert.True((await claims.Claim(Acme, Procedure, All, Utc(10, 15), Utc(11, 15))).Claimed);
    }

    [Fact]
    public async Task Mixed_conjunction_claims_every_resource_with_its_own_buffers()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        var outcome = await claims.Claim(Acme, Procedure, All, Utc(10), Utc(11));

        Assert.True(outcome.Claimed);
        Assert.Null(outcome.Refusal);
        Assert.Equal(2, outcome.Events.Count);
        var room = Assert.Single(outcome.Events, e => e.ResourceRef == RoomRef);
        var nurses = Assert.Single(outcome.Events, e => e.ResourceRef == NursesRef);
        Assert.Equal(EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)), room.Padding);
        Assert.Equal(EventPadding.Of(TimeSpan.FromMinutes(15), TimeSpan.Zero), nurses.Padding);
        Assert.Equal(2, (await store.ListAsync(Acme)).Count);

        // The committed events are the occupancy the runtime reads next: the room refuses, and a
        // nurse remains.
        var runtime = await RuntimeAsync(store);
        var read = await runtime.Read(Acme, new AvailabilityRequest(Utc(10), Utc(11),
            [ResourceCapacity.Exclusive(RoomRef), ResourceCapacity.Pool(NursesRef, 2)]));
        Assert.Equal(0, read.Resources[0].Remaining);
        Assert.Equal(1, read.Resources[1].Remaining);
    }

    // ----------------------------------------------------------------
    // Atomic: two racing claims for the last pool place, exactly one wins.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Two_racing_claims_for_the_last_pool_place_yield_exactly_one_winner()
    {
        var store = new RendezvousEventStore(parties: 2);
        var claims = await SutAsync(store);
        await store.Inner.SaveAsync(Occupying("Earlier seat", SeatsRef, Utc(10), Utc(11)));

        // Both claims read occupancy before either writes: each sees one place left.
        var outcomes = await Task.WhenAll(
            claims.Claim(Acme, SeatBooking, All, Utc(10), Utc(11)),
            claims.Claim(Acme, SeatBooking, All, Utc(10), Utc(11)));

        Assert.True(store.AllReadBeforeAnyWrote, "the interleaving under test did not occur");
        Assert.Single(outcomes, o => o.Claimed);
        Assert.Equal(BookableClaimCodes.CapacityExhausted, Assert.Single(outcomes, o => !o.Claimed).Refusal);
        Assert.Equal(2, (await store.ListAsync(Acme)).Count);
    }

    [Fact]
    public async Task A_claim_that_never_wins_the_epochs_is_refused_contended_and_writes_nothing()
    {
        var store = new RacingEventStore();
        var claims = await SutAsync(store);

        var outcome = await claims.Claim(Acme, Procedure, All, Utc(10), Utc(11));

        Assert.Equal(BookableClaimCodes.Contended, outcome.Refusal);
        Assert.Equal(3, store.RefusedSaves);
        Assert.DoesNotContain(await store.ListAsync(Acme), e => e.Title == Procedure.Name);
    }

    [Fact]
    public async Task A_claim_that_loses_one_epoch_rechecks_and_commits()
    {
        var store = new RacingEventStore(races: 1);
        var claims = await SutAsync(store);

        var outcome = await claims.Claim(Acme, Procedure, All, Utc(10), Utc(11));

        Assert.True(outcome.Claimed);
        Assert.Equal(1, store.RefusedSaves);
    }

    [Fact]
    public async Task A_multi_resource_save_with_one_stale_epoch_writes_nothing()
    {
        var store = new InMemoryCalendarEventStore();
        var epochs = new Dictionary<ParticipantRef, long>
        {
            [RoomRef] = await store.GetCapacityEpochAsync(Acme, RoomRef),
            [NursesRef] = await store.GetCapacityEpochAsync(Acme, NursesRef),
        };
        await store.SaveAsync(Occupying("Racer", NursesRef, Utc(15), Utc(16)));

        var room = Occupying("Room side", RoomRef, Utc(10), Utc(11));
        var nurses = Occupying("Nurse side", NursesRef, Utc(10), Utc(11));
        Assert.False(await store.SaveAllIfCapacityUnchangedAsync(Acme, [room, nurses], epochs));
        Assert.Null(await store.GetAsync(Acme, room.Id));
        Assert.Null(await store.GetAsync(Acme, nurses.Id));

        // Against the current epochs the same save commits both, and moves both epochs.
        epochs[NursesRef] = await store.GetCapacityEpochAsync(Acme, NursesRef);
        Assert.True(await store.SaveAllIfCapacityUnchangedAsync(Acme, [room, nurses], epochs));
        Assert.NotNull(await store.GetAsync(Acme, room.Id));
        Assert.NotNull(await store.GetAsync(Acme, nurses.Id));
        Assert.Equal(epochs[RoomRef] + 1, await store.GetCapacityEpochAsync(Acme, RoomRef));
    }

    [Fact]
    public async Task A_multi_resource_save_refuses_an_event_of_another_tenant()
    {
        var store = new InMemoryCalendarEventStore();
        var own = Occupying("Own tenant", NursesRef, Utc(10), Utc(11));
        var elsewhere = Occupying("Other tenant", RoomRef, Utc(10), Utc(11), new TenantId("globex"));

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAllIfCapacityUnchangedAsync(
            Acme, [own, elsewhere], new Dictionary<ParticipantRef, long> { [RoomRef] = 0, [NursesRef] = 0 }));
        Assert.Empty(await store.ListAsync(Acme));
        Assert.Empty(await store.ListAsync(new TenantId("globex")));
    }

    [Fact]
    public async Task Null_arguments_are_refused()
    {
        var store = new InMemoryCalendarEventStore();
        var runtime = await RuntimeAsync(store);
        var requester = new FixedRequester(Actor);
        Assert.Throws<ArgumentNullException>(() => new BookableClaimService(null!, store, requester));
        Assert.Throws<ArgumentNullException>(() => new BookableClaimService(runtime, null!, requester));
        Assert.Throws<ArgumentNullException>(() => new BookableClaimService(runtime, store, null!));

        var claims = new BookableClaimService(runtime, store, requester);
        await Assert.ThrowsAsync<ArgumentNullException>(() => claims.Claim(Acme, null!, All, Utc(10), Utc(11)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => claims.Claim(Acme, Bookable("Nothing required"), null!, Utc(10), Utc(11)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAllIfCapacityUnchangedAsync(
            Acme, null!, new Dictionary<ParticipantRef, long>()));
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveAllIfCapacityUnchangedAsync(Acme, [], null!));
    }

    // ----------------------------------------------------------------
    // The other refusals: each writes nothing.
    // ----------------------------------------------------------------

    [Fact]
    public async Task A_claim_outside_a_required_resources_supply_is_refused()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        var outcome = await claims.Claim(Acme, Procedure, All, Utc(18), Utc(19));

        Assert.Equal(BookableClaimCodes.OutsideSupply, outcome.Refusal);
        Assert.Empty(await store.ListAsync(Acme));
    }

    [Fact]
    public async Task A_claim_outside_one_required_resources_supply_is_refused_though_the_other_has_supply()
    {
        // The annex has no supply at all; the room is free. The conjunction refuses on the annex alone.
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);
        var annex = new BookableClaimResource(ParticipantRef.Asset("asset-annex"), Resource("Annex", CapacityKind.Exclusive, null, 0, 0));

        var outcome = await claims.Claim(Acme, Bookable("Room and annex", "Room", "Annex"), [Room, annex], Utc(10), Utc(11));

        Assert.Equal(BookableClaimCodes.OutsideSupply, outcome.Refusal);
        Assert.Empty(await store.ListAsync(Acme));
    }

    [Fact]
    public async Task An_inverted_window_is_refused()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        Assert.Equal(BookableClaimCodes.WindowInvalid,
            (await claims.Claim(Acme, Procedure, All, Utc(11), Utc(10))).Refusal);
        Assert.Equal(BookableClaimCodes.WindowInvalid,
            (await claims.Claim(Acme, Procedure, All, Utc(10), Utc(10))).Refusal);
        Assert.Empty(await store.ListAsync(Acme));
    }

    [Fact]
    public async Task A_required_resource_without_a_supplier_is_refused()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = await SutAsync(store);

        Assert.Equal(BookableClaimCodes.RequirementUnresolved,
            (await claims.Claim(Acme, Procedure, [Room], Utc(10), Utc(11))).Refusal);
        Assert.Equal(BookableClaimCodes.RequirementUnresolved,
            (await claims.Claim(Acme, Bookable("Nothing required"), All, Utc(10), Utc(11))).Refusal);
        Assert.Empty(await store.ListAsync(Acme));
    }

    [Fact]
    public async Task A_claim_without_an_authenticated_requester_is_refused()
    {
        var store = new InMemoryCalendarEventStore();
        var claims = new BookableClaimService(await RuntimeAsync(store), store, FixedRequester.Unauthenticated);

        Assert.Equal(BookableClaimCodes.NoRequester,
            (await claims.Claim(Acme, Procedure, All, Utc(10), Utc(11))).Refusal);
        Assert.Equal(BookableClaimCodes.NoRequester,
            (await new BookableClaimService(await RuntimeAsync(store), store, new FixedRequester(Guid.Empty))
                .Claim(Acme, Procedure, All, Utc(10), Utc(11))).Refusal);
        Assert.Empty(await store.ListAsync(Acme));
    }

    // ----------------------------------------------------------------
    // Fixtures
    // ----------------------------------------------------------------

    private static BookingResourceDefinition Resource(string name, CapacityKind kind, int? size, int setup, int cleanup)
        => new(name, "type-asset", kind, size, setup, cleanup, [], "base-hours");

    private static BookingBookableDefinition Bookable(string name, params string[] requires)
        => new(name, "type-visit", [60], requires, [], null, false);

    private static async Task<IAvailabilityRuntime> RuntimeAsync(ICalendarEventStore events)
    {
        var rrule = new InMemoryRruleExpansionService();
        var availability = new InMemoryResourceAvailabilityStore();
        foreach (var resource in All)
            await availability.SaveAsync(ResourceAvailability.Create(Acme, resource.Resource, "UTC")
                .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(8, 0), new TimeOnly(18, 0))));
        return new FreeBusyService(
            availability, new AvailabilityExpansionService(rrule), events, new CalendarEventExpansionService(rrule));
    }

    private static async Task<BookableClaimService> SutAsync(ICalendarEventStore events)
        => new(await RuntimeAsync(events), events, new FixedRequester(Actor));

    private static CalendarEvent Occupying(
        string title, ParticipantRef resource, DateTimeOffset startUtc, DateTimeOffset endUtc, TenantId? tenant = null)
    {
        var ev = CalendarEvent.Create(
            tenantId: tenant ?? Acme, title: title, start: Day, end: Day, createdBy: Actor, timezone: "UTC",
            startTime: TimeOnly.FromDateTime(startUtc.UtcDateTime), endTime: TimeOnly.FromDateTime(endUtc.UtcDateTime),
            occupancy: Occupancy.Bookable, padding: EventPadding.None);
        ev.SetResource(resource, Actor, ParticipationStatus.Confirmed);
        return ev;
    }

    /// <summary>
    /// Holds every claim at its occupancy read until <c>parties</c> of them have read, so each derives
    /// capacity from the same pre-write occupancy. Bounded, so a missing party fails instead of hanging.
    /// </summary>
    private sealed class RendezvousEventStore(int parties) : ICalendarEventStore
    {
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
        private readonly TaskCompletionSource _allRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;
        private int _writes;

        public InMemoryCalendarEventStore Inner { get; } = new();
        public bool AllReadBeforeAnyWrote { get; private set; }

        public async Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
        {
            var events = await Inner.ListAsync(tenantId, ct).ConfigureAwait(false);
            if (Interlocked.Increment(ref _reads) == parties)
            {
                AllReadBeforeAnyWrote = Volatile.Read(ref _writes) == 0;
                _allRead.SetResult();
            }
            await _allRead.Task.WaitAsync(Bound, ct).ConfigureAwait(false);
            return events;
        }

        public Task<bool> SaveAllIfCapacityUnchangedAsync(TenantId tenantId, IReadOnlyList<CalendarEvent> calendarEvents,
            IReadOnlyDictionary<ParticipantRef, long> expectedEpochs, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writes);
            return Inner.SaveAllIfCapacityUnchangedAsync(tenantId, calendarEvents, expectedEpochs, ct);
        }

        public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default) => Inner.SaveAsync(calendarEvent, ct);
        public Task<bool> SaveIfCapacityUnchangedAsync(
            CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
            => Inner.SaveIfCapacityUnchangedAsync(calendarEvent, resource, expectedEpoch, ct);
        public Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
            => Inner.GetCapacityEpochAsync(tenantId, resource, ct);
        public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => Inner.GetAsync(tenantId, id, ct);
        public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => Inner.RemoveAsync(tenantId, id, ct);
    }

    /// <summary>
    /// Lands a competing write on the Nurses pool after each of the first <c>races</c> epoch reads of it,
    /// far from the claimed window, so every save conditional on that read loses only on the epoch.
    /// </summary>
    private sealed class RacingEventStore(int races = int.MaxValue) : ICalendarEventStore
    {
        private readonly InMemoryCalendarEventStore _inner = new();
        private int _nurseReads;

        public int RefusedSaves { get; private set; }

        public async Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
        {
            var epoch = await _inner.GetCapacityEpochAsync(tenantId, resource, ct).ConfigureAwait(false);
            if (resource == NursesRef && ++_nurseReads <= races)
                await _inner.SaveAsync(Occupying($"Racer {_nurseReads}", NursesRef, Utc(16), Utc(17)), ct).ConfigureAwait(false);
            return epoch;
        }

        public async Task<bool> SaveAllIfCapacityUnchangedAsync(TenantId tenantId, IReadOnlyList<CalendarEvent> calendarEvents,
            IReadOnlyDictionary<ParticipantRef, long> expectedEpochs, CancellationToken ct = default)
        {
            var saved = await _inner.SaveAllIfCapacityUnchangedAsync(tenantId, calendarEvents, expectedEpochs, ct).ConfigureAwait(false);
            if (!saved) RefusedSaves++;
            return saved;
        }

        public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default) => _inner.SaveAsync(calendarEvent, ct);
        public Task<bool> SaveIfCapacityUnchangedAsync(
            CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
            => _inner.SaveIfCapacityUnchangedAsync(calendarEvent, resource, expectedEpoch, ct);
        public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.GetAsync(tenantId, id, ct);
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
            => _inner.ListAsync(tenantId, ct);
        public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.RemoveAsync(tenantId, id, ct);
    }
}
