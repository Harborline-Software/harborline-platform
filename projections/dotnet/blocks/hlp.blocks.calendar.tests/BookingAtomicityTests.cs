using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// The claim is atomic in the PRODUCER (T-659; DES-0025 <c>booking-eng-24</c>; ADR 0095 ruling 8):
/// the capacity recheck and the commit are one epoch-conditional write, so a prior availability read
/// is advisory and cannot authorize a save. These cases fence that here, in the block that owns the
/// invariant — no host lock is in scope, and the interleavings are forced, not raced on a sleep.
/// </summary>
public sealed class BookingAtomicityTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private static readonly DateOnly Day = new(2026, 3, 4);

    private static DateTimeOffset Utc(int hour) => new(2026, 3, 4, hour, 0, 0, TimeSpan.Zero);

    /// <summary>A booking service over <paramref name="events"/>, with the doctor available 9-5 on the Day.</summary>
    private static async Task<(IBookingService Booking, IAvailabilityRuntime Runtime)> SutAsync(ICalendarEventStore events)
    {
        var rrule = new InMemoryRruleExpansionService();
        var availability = new InMemoryResourceAvailabilityStore();
        await availability.SaveAsync(
            ResourceAvailability.Create(Acme, Doctor, "UTC")
                .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(17, 0))));
        var runtime = new FreeBusyService(
            availability, new AvailabilityExpansionService(rrule), events, new CalendarEventExpansionService(rrule));
        var booking = new BookingService(
            runtime, availability, events, new DefaultPaddingPolicy(EventPadding.None), new FixedRequester(Actor));
        return (booking, runtime);
    }

    private static async Task<int> RemainingAsync(IAvailabilityRuntime runtime, int hour, int limit)
    {
        var read = await runtime.Read(Acme, new AvailabilityRequest(
            Utc(hour), Utc(hour + 1),
            [limit == 1 ? ResourceCapacity.Exclusive(Doctor) : ResourceCapacity.Pool(Doctor, limit)]));
        return read.Resources[0].Remaining;
    }

    // ----------------------------------------------------------------
    // Acceptance 1 - two concurrent claims on one exclusive slot yield exactly one success,
    // proved here and not through a host lock.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_claims_on_one_exclusive_slot_yield_exactly_one_success()
    {
        // The rendezvous holds BOTH claims at their occupancy read until each has read: the exact
        // check-then-write window the api's in-process lock used to close from outside.
        var store = new RendezvousEventStore(parties: 2);
        var (booking, runtime) = await SutAsync(store);

        var outcomes = await Task.WhenAll(
            booking.Book(Acme, Doctor, "Claim A", Utc(10), Utc(11)),
            booking.Book(Acme, Doctor, "Claim B", Utc(10), Utc(11)));

        Assert.True(store.AllReadBeforeAnyWrote, "the interleaving under test did not occur");
        Assert.Single(outcomes, o => o.Success);
        var loser = Assert.Single(outcomes, o => !o.Success);
        Assert.Equal(BookingOutcome.SlotConflict, loser.RejectionReason);
        Assert.Single(await store.ListAsync(Acme));
        Assert.Equal(0, await RemainingAsync(runtime, 10, limit: 1));
    }

    // ----------------------------------------------------------------
    // Acceptance 2 - a stale capacity read refuses rather than saving.
    // ----------------------------------------------------------------

    [Fact]
    public async Task A_save_against_a_stale_capacity_epoch_is_refused_and_writes_nothing()
    {
        var store = new InMemoryCalendarEventStore();
        var (booking, _) = await SutAsync(store);

        var epoch = await store.GetCapacityEpochAsync(Acme, Doctor);
        Assert.True((await booking.Book(Acme, Doctor, "The racer", Utc(14), Utc(15))).Success);

        // The epoch the claim read is now stale. The same event the producer would once have saved
        // unconditionally is refused by the store, and nothing is written.
        var claim = Bookable("The stale claim", Utc(10), Utc(11));
        Assert.False(await store.SaveIfCapacityUnchangedAsync(claim, Doctor, epoch));
        Assert.Null(await store.GetAsync(Acme, claim.Id));
        Assert.Single(await store.ListAsync(Acme));

        // Conditional on the CURRENT epoch the same save is accepted, so the refusal is the stale
        // epoch and not the event.
        Assert.True(await store.SaveIfCapacityUnchangedAsync(
            claim, Doctor, await store.GetCapacityEpochAsync(Acme, Doctor)));
        Assert.NotNull(await store.GetAsync(Acme, claim.Id));
    }

    [Fact]
    public async Task A_prior_read_alone_cannot_authorize_a_save_the_claim_rechecks()
    {
        // A competing write lands between the capacity read and the save that follows it. The first
        // commit is refused on the stale epoch; the claim rechecks and only then commits.
        var store = new RacingEventStore(races: 1);
        var (booking, _) = await SutAsync(store);

        var outcome = await booking.Book(Acme, Doctor, "Rechecked claim", Utc(10), Utc(11));

        Assert.True(outcome.Success);
        Assert.Equal(1, store.RefusedSaves);
        Assert.Equal(2, store.CapacityReads);
    }

    [Fact]
    public async Task A_claim_that_never_wins_the_epoch_refuses_and_writes_nothing()
    {
        // Unbounded contention: every save loses its epoch. The claim refuses SLOT_CONFLICT; it never
        // falls back to an unconditional save.
        var store = new RacingEventStore(races: int.MaxValue);
        var (booking, _) = await SutAsync(store);

        var outcome = await booking.Book(Acme, Doctor, "Never lands", Utc(10), Utc(11));

        Assert.False(outcome.Success);
        Assert.Equal(BookingOutcome.SlotConflict, outcome.RejectionReason);
        Assert.DoesNotContain(await store.ListAsync(Acme), e => e.Title == "Never lands");
    }

    // ----------------------------------------------------------------
    // Acceptance 3 - release and expiry restore capacity without double counting.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Release_restores_capacity_once_however_often_it_is_released()
    {
        var store = new InMemoryCalendarEventStore();
        var (booking, runtime) = await SutAsync(store);

        var booked = await booking.Book(Acme, Doctor, "Checkup", Utc(10), Utc(11));
        Assert.True(booked.Success);
        Assert.Equal(0, await RemainingAsync(runtime, 10, limit: 1));
        Assert.Equal(BookingOutcome.SlotConflict,
            (await booking.Book(Acme, Doctor, "Second", Utc(10), Utc(11))).RejectionReason);

        // Release: the one place comes back.
        Assert.True(await store.RemoveAsync(Acme, booked.Event!.Id));
        Assert.Equal(1, await RemainingAsync(runtime, 10, limit: 1));

        // Releasing again is idempotent and adds nothing: still ONE place, so the first re-claim
        // succeeds and the second is refused.
        Assert.False(await store.RemoveAsync(Acme, booked.Event.Id));
        Assert.Equal(1, await RemainingAsync(runtime, 10, limit: 1));
        Assert.True((await booking.Book(Acme, Doctor, "Re-claim", Utc(10), Utc(11))).Success);
        Assert.Equal(BookingOutcome.SlotConflict,
            (await booking.Book(Acme, Doctor, "One too many", Utc(10), Utc(11))).RejectionReason);
    }

    [Fact]
    public async Task An_expired_hold_restores_its_place_exactly_once_in_a_pool()
    {
        // A pool of two with two tentative holds is exhausted; expiring ONE hold returns exactly one
        // place, and expiring it a second time does not return a third.
        var store = new InMemoryCalendarEventStore();
        var (_, runtime) = await SutAsync(store);
        var first = Hold("Hold A", Utc(10), Utc(11));
        var second = Hold("Hold B", Utc(10), Utc(11));
        await store.SaveAsync(first);
        await store.SaveAsync(second);
        Assert.Equal(0, await RemainingAsync(runtime, 10, limit: 2));

        // Expiry: the hold is cancelled, so it occupies nothing.
        first.Cancel(Actor);
        await store.SaveAsync(first);
        Assert.Equal(1, await RemainingAsync(runtime, 10, limit: 2));

        // Expiring the same hold again releases nothing further - the place is not counted twice.
        first.Cancel(Actor);
        await store.SaveAsync(first);
        Assert.Equal(1, await RemainingAsync(runtime, 10, limit: 2));

        // And the pool's second place really does come back when its hold expires too.
        second.Cancel(Actor);
        await store.SaveAsync(second);
        Assert.Equal(2, await RemainingAsync(runtime, 10, limit: 2));
    }

    [Fact]
    public async Task A_release_moves_the_capacity_epoch_so_a_claim_that_read_before_it_rechecks()
    {
        var store = new InMemoryCalendarEventStore();
        var (booking, _) = await SutAsync(store);
        var booked = await booking.Book(Acme, Doctor, "Checkup", Utc(10), Utc(11));
        Assert.True(booked.Success);

        var epochBeforeRelease = await store.GetCapacityEpochAsync(Acme, Doctor);
        Assert.True(await store.RemoveAsync(Acme, booked.Event!.Id));

        Assert.NotEqual(epochBeforeRelease, await store.GetCapacityEpochAsync(Acme, Doctor));
        Assert.False(await store.SaveIfCapacityUnchangedAsync(
            Bookable("Read before the release", Utc(10), Utc(11)), Doctor, epochBeforeRelease));
    }

    // ----------------------------------------------------------------
    // Fixtures
    // ----------------------------------------------------------------

    private static CalendarEvent Bookable(string title, DateTimeOffset startUtc, DateTimeOffset endUtc)
        => Event(title, startUtc, endUtc, Occupancy.Bookable);

    private static CalendarEvent Hold(string title, DateTimeOffset startUtc, DateTimeOffset endUtc)
        => Event(title, startUtc, endUtc, Occupancy.Tentative);

    private static CalendarEvent Event(string title, DateTimeOffset startUtc, DateTimeOffset endUtc, Occupancy occupancy)
    {
        var ev = CalendarEvent.Create(
            tenantId: Acme, title: title,
            start: DateOnly.FromDateTime(startUtc.UtcDateTime), end: DateOnly.FromDateTime(endUtc.UtcDateTime),
            createdBy: Actor, timezone: "UTC",
            startTime: TimeOnly.FromDateTime(startUtc.UtcDateTime), endTime: TimeOnly.FromDateTime(endUtc.UtcDateTime),
            occupancy: occupancy, padding: EventPadding.None);
        ev.SetResource(Doctor, Actor, ParticipationStatus.Confirmed);
        return ev;
    }

    /// <summary>
    /// Holds every claim at its occupancy read until <c>parties</c> of them have read, so both claims
    /// derive their capacity from the same pre-write occupancy. Deterministic: no sleeps, and the
    /// wait is bounded so a claim that never arrives fails the test instead of hanging it.
    /// </summary>
    private sealed class RendezvousEventStore : ICalendarEventStore
    {
        private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
        private readonly InMemoryCalendarEventStore _inner = new();
        private readonly TaskCompletionSource _allRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int _parties;
        private int _reads;
        private int _writes;

        public RendezvousEventStore(int parties) => _parties = parties;

        /// <summary>True when every party had read the occupancy before the first write landed.</summary>
        public bool AllReadBeforeAnyWrote { get; private set; }

        public async Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
        {
            var events = await _inner.ListAsync(tenantId, ct).ConfigureAwait(false);
            if (Interlocked.Increment(ref _reads) == _parties)
            {
                AllReadBeforeAnyWrote = Volatile.Read(ref _writes) == 0;
                _allRead.SetResult();
            }
            await _allRead.Task.WaitAsync(Bound, ct).ConfigureAwait(false);
            return events;
        }

        public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writes);
            return _inner.SaveAsync(calendarEvent, ct);
        }

        public Task<bool> SaveIfCapacityUnchangedAsync(
            CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writes);
            return _inner.SaveIfCapacityUnchangedAsync(calendarEvent, resource, expectedEpoch, ct);
        }

        public Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
            => _inner.GetCapacityEpochAsync(tenantId, resource, ct);
        public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.GetAsync(tenantId, id, ct);
        public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.RemoveAsync(tenantId, id, ct);
    }

    /// <summary>
    /// Lands a competing write on the resource after each of the first <c>races</c> capacity reads -
    /// the racer the producer cannot see. Every save conditional on a read it overtook must be refused.
    /// </summary>
    private sealed class RacingEventStore : ICalendarEventStore
    {
        private readonly InMemoryCalendarEventStore _inner = new();
        private readonly int _races;

        public RacingEventStore(int races) => _races = races;

        public int CapacityReads { get; private set; }
        public int RefusedSaves { get; private set; }

        public async Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
        {
            var epoch = await _inner.GetCapacityEpochAsync(tenantId, resource, ct).ConfigureAwait(false);
            CapacityReads++;
            if (CapacityReads <= _races)
            {
                // A blocking event well away from the claimed slot: it moves the epoch without ever
                // making the claim's own slot unavailable, so a refusal can only be the stale read.
                await _inner.SaveAsync(Event($"Racer {CapacityReads}", Utc(15), Utc(16), Occupancy.Blocking), ct)
                    .ConfigureAwait(false);
            }
            return epoch;
        }

        public async Task<bool> SaveIfCapacityUnchangedAsync(
            CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
        {
            var saved = await _inner.SaveIfCapacityUnchangedAsync(calendarEvent, resource, expectedEpoch, ct)
                .ConfigureAwait(false);
            if (!saved) RefusedSaves++;
            return saved;
        }

        public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default)
            => _inner.SaveAsync(calendarEvent, ct);
        public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.GetAsync(tenantId, id, ct);
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
            => _inner.ListAsync(tenantId, ct);
        public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
            => _inner.RemoveAsync(tenantId, id, ct);
    }
}
