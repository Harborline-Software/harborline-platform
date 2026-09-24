using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// T-626 — the availability substrate contract (DES-0033 rows eng-7, eng-9/ck-9, eng-11/ck-3,
/// eng-12): one composition shared with Booking; the mandatory bounded window; derived pool
/// capacity; the required-resource conjunction with buffers; and derivation per read, never stored.
/// </summary>
public sealed class AvailabilityRuntimeTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly ParticipantRef Doctor = ParticipantRef.Party("party-dr-smith");
    private static readonly ParticipantRef Room = ParticipantRef.Asset("asset-room-101");
    private static readonly ParticipantRef Bay = ParticipantRef.Asset("asset-bay-pool");
    private static readonly DateOnly Day = new(2026, 3, 4);

    private sealed record Sut(
        CountingAvailabilityStore Availability,
        ISharedCalendarStore SharedCalendars,
        ICalendarSubscriptionStore Subscriptions,
        CountingEventStore Events,
        FreeBusyService Composition,
        IAvailabilityRuntime Runtime,
        IBookingService Booking);

    private static Sut NewSut()
    {
        var rrule = new InMemoryRruleExpansionService();
        var availStore = new CountingAvailabilityStore();
        var sharedStore = new InMemorySharedCalendarStore();
        var subStore = new InMemoryCalendarSubscriptionStore();
        var eventStore = new CountingEventStore();
        var composition = new FreeBusyService(
            availStore, new AvailabilityExpansionService(rrule), eventStore, new CalendarEventExpansionService(rrule),
            new SharedCalendarResolver(subStore, sharedStore), visibilityPolicy: null);
        var booking = new BookingService(composition, availStore, eventStore, new DefaultPaddingPolicy(EventPadding.None), new FixedRequester(Actor));
        return new Sut(availStore, sharedStore, subStore, eventStore, composition, composition, booking);
    }

    private static DateTimeOffset Utc(int hour, int minute = 0) => new(Day.Year, Day.Month, Day.Day, hour, minute, 0, TimeSpan.Zero);

    private static async Task SeedNineToFive(Sut sut, ParticipantRef resource)
        => await sut.Availability.SaveAsync(
            ResourceAvailability.Create(Acme, resource, "UTC")
                .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(17, 0))));

    private static async Task<CalendarEvent> Occupy(Sut sut, ParticipantRef resource, int fromHour, int toHour, Occupancy occupancy = Occupancy.Bookable)
    {
        var ev = CalendarEvent.Create(Acme, $"{occupancy} {fromHour}-{toHour}", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(fromHour, 0), endTime: new TimeOnly(toHour, 0), occupancy: occupancy);
        ev.SetResource(resource, Actor);
        await sut.Events.SaveAsync(ev);
        return ev;
    }

    private static AvailabilityRequest Window(DateTimeOffset? from, DateTimeOffset? to, params ResourceCapacity[] resources)
        => new(from, to, resources);

    // ================================================================
    // eng-9 / ck-9 — the window is mandatory, bounded and never defaulted
    // ================================================================

    [Theory]
    [InlineData(false, true, AvailabilityRefusal.WindowUnbounded, "from")]
    [InlineData(true, false, AvailabilityRefusal.WindowUnbounded, "to")]
    public async Task Window_MissingAnEndpoint_IsRefused_BeforeAnyStoreRead(bool hasFrom, bool hasTo, string code, string pointer)
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        sut.Availability.Reads = 0;

        var answer = await sut.Runtime.Read(Acme, Window(hasFrom ? Utc(10) : null, hasTo ? Utc(11) : null, ResourceCapacity.Exclusive(Doctor)));

        Assert.Equal(new AvailabilityRefusal(code, pointer), answer.Refusal);
        Assert.False(answer.Available);
        Assert.Empty(answer.Resources);
        Assert.Equal(0, sut.Availability.Reads);
        Assert.Equal(0, sut.Events.Reads);
    }

    [Fact]
    public async Task Window_Inverted_IsRefused_BeforeAnyStoreRead()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        sut.Availability.Reads = 0;

        var answer = await sut.Runtime.Read(Acme, Window(Utc(11), Utc(10), ResourceCapacity.Exclusive(Doctor)));

        Assert.Equal(new AvailabilityRefusal(AvailabilityRefusal.WindowInverted, "to"), answer.Refusal);
        Assert.Equal(0, sut.Availability.Reads);
        Assert.Equal(0, sut.Events.Reads);
    }

    [Fact]
    public async Task Window_Bounded_ReadsExactlyTheRequestedWindow()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);

        // 10-12 is inside 9-17: the free set is exactly the window, not the whole supply.
        var answer = await sut.Runtime.Read(Acme, Window(Utc(10), Utc(12), ResourceCapacity.Exclusive(Doctor)));

        Assert.Null(answer.Refusal);
        var read = Assert.Single(answer.Resources);
        var free = Assert.Single(read.Free);
        Assert.Equal(new TimeInterval(Utc(10), Utc(12)), free);
        Assert.Equal(1, sut.Availability.Reads);
    }

    // ================================================================
    // eng-7 — one composition: Booking's gate and free/busy agree on a shared holiday
    // ================================================================

    [Fact]
    public async Task SharedHoliday_YieldsTheSameUnavailableInterval_InFreeBusyAndInTheBookingGate()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        var holidays = SharedCalendar.Create(Acme, "Clinic Holidays").AddException(ExceptionSpan.Create(Day, Day, "Closed"));
        await sut.SharedCalendars.SaveAsync(holidays);
        await sut.Subscriptions.SubscribeAsync(CalendarSubscription.Create(Acme, Doctor, holidays.Id));

        var freeBusy = await sut.Composition.FreeBusy(Acme, Doctor, Utc(0), Utc(23));
        var runtime = await sut.Runtime.Read(Acme, Window(Utc(0), Utc(23), ResourceCapacity.Exclusive(Doctor)));
        var booked = await sut.Booking.Book(Acme, Doctor, "On the holiday", Utc(10), Utc(11));

        Assert.Empty(freeBusy.FreeSlots);
        Assert.Equal(freeBusy.FreeSlots, runtime.Resources[0].Free);
        Assert.Equal(Unavailability.OutsideSupply, runtime.Resources[0].Unavailability);
        Assert.Equal(BookingOutcome.NoAvailability, booked.RejectionReason);
    }

    // ================================================================
    // eng-11 / ck-3 — pool capacity derived from supply and occupancy
    // ================================================================

    [Fact]
    public async Task Pool_OfTwo_ReportsRemainingUnits_AndRefusesTheThird()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Bay);
        var pool = ResourceCapacity.Pool(Bay, size: 2);
        var candidate = Window(Utc(10), Utc(11), pool);   // the third candidate's interval
        var view = Window(Utc(9), Utc(17), pool);         // a calendar-view read over the day

        var empty = await sut.Runtime.Read(Acme, candidate);
        Assert.Equal(2, empty.Resources[0].Remaining);
        Assert.True(empty.Available);

        await Occupy(sut, Bay, 10, 11);                                   // one allocation
        var one = await sut.Runtime.Read(Acme, candidate);
        Assert.Equal(1, one.Resources[0].Remaining);
        Assert.True(one.Available);
        var oneView = await sut.Runtime.Read(Acme, view);
        Assert.Equal(new TimeInterval(Utc(9), Utc(17)), Assert.Single(oneView.Resources[0].Free));   // still free to a second

        var hold = await Occupy(sut, Bay, 10, 12, Occupancy.Tentative);   // an active hold
        var full = await sut.Runtime.Read(Acme, candidate);
        Assert.Equal(0, full.Resources[0].Remaining);
        Assert.False(full.Available);
        Assert.Equal(Unavailability.CapacityExhausted, full.Resources[0].Unavailability);
        var fullView = await sut.Runtime.Read(Acme, view);
        Assert.Equal(new TimeInterval(Utc(10), Utc(11)), Assert.Single(fullView.Resources[0].Busy));   // full only where depth reaches two
        Assert.Equal(new[] { new TimeInterval(Utc(9), Utc(10)), new TimeInterval(Utc(11), Utc(17)) }, fullView.Resources[0].Free);

        // Releasing the hold changes the next read with no invalidation call in between.
        hold.Cancel(Actor);
        await sut.Events.SaveAsync(hold);
        var released = await sut.Runtime.Read(Acme, candidate);
        Assert.Equal(1, released.Resources[0].Remaining);
        Assert.True(released.Available);
    }

    [Fact]
    public async Task Exclusive_StillRefusesTheSecondOverlap()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        await Occupy(sut, Doctor, 10, 11);

        var answer = await sut.Runtime.Read(Acme, Window(Utc(10, 30), Utc(11, 30), ResourceCapacity.Exclusive(Doctor)));

        Assert.False(answer.Available);
        Assert.Equal(0, answer.Resources[0].Remaining);
        Assert.Equal(Unavailability.CapacityExhausted, answer.Resources[0].Unavailability);
    }

    // ================================================================
    // eng-12 — the required set is a conjunction over the interval plus each resource's buffers
    // ================================================================

    [Fact]
    public async Task RequiredSet_MixedExclusiveAndPool_IsAvailableOnlyWhenEveryResourceCoversTheBufferedInterval()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        await SeedNineToFive(sut, Room);
        await SeedNineToFive(sut, Bay);
        var doctor = ResourceCapacity.Exclusive(Doctor);
        var room = ResourceCapacity.Exclusive(Room, EventPadding.Of(TimeSpan.Zero, TimeSpan.FromMinutes(30)));   // 30-min cleanup
        var bay = ResourceCapacity.Pool(Bay, size: 2);

        // Positive control: all free.
        var allFree = await sut.Runtime.Read(Acme, Window(Utc(10), Utc(11), doctor, room, bay));
        Assert.True(allFree.Available);
        Assert.All(allFree.Resources, r => Assert.True(r.Available));

        // The room is taken 11:15-12:00: outside the 10-11 interval, inside the room's cleanup buffer.
        var ev = CalendarEvent.Create(Acme, "Room turnover clash", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(11, 15), endTime: new TimeOnly(12, 0), occupancy: Occupancy.Bookable);
        ev.SetResource(Room, Actor);
        await sut.Events.SaveAsync(ev);

        var oneBusy = await sut.Runtime.Read(Acme, Window(Utc(10), Utc(11), doctor, room, bay));
        Assert.False(oneBusy.Available);
        Assert.Equal(Unavailability.CapacityExhausted, oneBusy.Resources.Single(r => r.Resource == Room).Unavailability);
        Assert.True(oneBusy.Resources.Single(r => r.Resource == Doctor).Available);
        Assert.True(oneBusy.Resources.Single(r => r.Resource == Bay).Available);

        // Reordering the set changes nothing.
        var reordered = await sut.Runtime.Read(Acme, Window(Utc(10), Utc(11), bay, room, doctor));
        Assert.False(reordered.Available);
        static (ParticipantRef, int, Unavailability, string) Shape(ResourceAvailabilityRead r)
            => (r.Resource, r.Remaining, r.Unavailability, string.Join(";", r.Free));
        Assert.Equal(
            oneBusy.Resources.Select(Shape).OrderBy(s => s.Item1.Value).ToArray(),
            reordered.Resources.Select(Shape).OrderBy(s => s.Item1.Value).ToArray());
    }

    // ================================================================
    // eng-7 / eng-8 — two reads spanning a stored allocation differ with no invalidation
    // ================================================================

    [Fact]
    public async Task TwoReads_SpanningAnAllocation_DifferWithoutInvalidation()
    {
        var sut = NewSut();
        await SeedNineToFive(sut, Doctor);
        var request = Window(Utc(9), Utc(17), ResourceCapacity.Exclusive(Doctor));

        var before = await sut.Runtime.Read(Acme, request);
        var booked = await sut.Booking.Book(Acme, Doctor, "Checkup", Utc(10), Utc(11));
        var after = await sut.Runtime.Read(Acme, request);

        Assert.True(booked.Success);
        Assert.Equal(new TimeInterval(Utc(9), Utc(17)), Assert.Single(before.Resources[0].Free));
        Assert.Equal(new[] { new TimeInterval(Utc(9), Utc(10)), new TimeInterval(Utc(11), Utc(17)) }, after.Resources[0].Free);
        Assert.Equal(new TimeInterval(Utc(10), Utc(11)), Assert.Single(after.Resources[0].Busy));
    }

    // ================================================================
    // IntervalMath depth sweep — the one runnable check behind pool capacity
    // ================================================================

    [Fact]
    public void WhereDepthAtLeast_FindsOnlyTheSpansWhereEnoughIntervalsOverlap()
    {
        var a = new TimeInterval(Utc(9), Utc(12));
        var b = new TimeInterval(Utc(10), Utc(14));
        var c = new TimeInterval(Utc(11), Utc(13));
        var d = new TimeInterval(Utc(14), Utc(15));   // adjacent to b, half-open: no overlap

        Assert.Equal(new[] { new TimeInterval(Utc(9), Utc(15)) }, IntervalMath.WhereDepthAtLeast([a, b, c, d], 1));
        Assert.Equal(new[] { new TimeInterval(Utc(10), Utc(13)) }, IntervalMath.WhereDepthAtLeast([a, b, c, d], 2));
        Assert.Equal(new[] { new TimeInterval(Utc(11), Utc(12)) }, IntervalMath.WhereDepthAtLeast([a, b, c, d], 3));
        Assert.Empty(IntervalMath.WhereDepthAtLeast([a, b, c, d], 4));
        Assert.Equal(3, IntervalMath.MaxDepth([a, b, c, d], new TimeInterval(Utc(9), Utc(17))));
        Assert.Equal(1, IntervalMath.MaxDepth([a, b, c, d], new TimeInterval(Utc(13), Utc(17))));
        Assert.Equal(0, IntervalMath.MaxDepth([a, b, c, d], new TimeInterval(Utc(15), Utc(17))));
    }

    /// <summary>Counts reads so the window refusal can be proven to happen before any store is touched.</summary>
    private sealed class CountingAvailabilityStore : IResourceAvailabilityStore
    {
        private readonly InMemoryResourceAvailabilityStore _inner = new();
        public int Reads;

        public Task SaveAsync(ResourceAvailability availability, CancellationToken ct = default) => _inner.SaveAsync(availability, ct);
        public Task<ResourceAvailability?> GetAsync(TenantId tenantId, ParticipantRef resourceRef, CancellationToken ct = default)
        { Reads++; return _inner.GetAsync(tenantId, resourceRef, ct); }
        public Task<IReadOnlyList<ResourceAvailability>> ListAsync(TenantId tenantId, CancellationToken ct = default)
        { Reads++; return _inner.ListAsync(tenantId, ct); }
        public Task<bool> RemoveAsync(TenantId tenantId, ParticipantRef resourceRef, CancellationToken ct = default) => _inner.RemoveAsync(tenantId, resourceRef, ct);
    }

    private sealed class CountingEventStore : ICalendarEventStore
    {
        private readonly InMemoryCalendarEventStore _inner = new();
        public int Reads;

        public Task SaveAsync(CalendarEvent calendarEvent, CancellationToken ct = default) => _inner.SaveAsync(calendarEvent, ct);
        public Task<CalendarEvent?> GetAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default)
        { Reads++; return _inner.GetAsync(tenantId, id, ct); }
        public Task<IReadOnlyList<CalendarEvent>> ListAsync(TenantId tenantId, CancellationToken ct = default)
        { Reads++; return _inner.ListAsync(tenantId, ct); }
        public Task<bool> RemoveAsync(TenantId tenantId, CalendarEventId id, CancellationToken ct = default) => _inner.RemoveAsync(tenantId, id, ct);
        public Task<long> GetCapacityEpochAsync(TenantId tenantId, ParticipantRef resource, CancellationToken ct = default)
            => _inner.GetCapacityEpochAsync(tenantId, resource, ct);
        public Task<bool> SaveIfCapacityUnchangedAsync(
            CalendarEvent calendarEvent, ParticipantRef resource, long expectedEpoch, CancellationToken ct = default)
            => _inner.SaveIfCapacityUnchangedAsync(calendarEvent, resource, expectedEpoch, ct);
    }
}
