using LeaseNs = Harborline.Kernel.Lease;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleReservationCoordinatorSupplementalTests
{
    [Fact]
    public async Task ReserveAsync_DuplicateReservationIdWithDifferentSlot_ReturnsPriorOutcome()
    {
        var coordinator = new ScheduleReservationCoordinator(new PermissiveLeaseCoordinator());
        var first = Slot("stable", "room-a", 0);
        var replay = Slot("stable", "room-a", 120);

        Assert.True((await coordinator.ReserveAsync(first, CancellationToken.None)).Success);
        Assert.True((await coordinator.ReserveAsync(replay, CancellationToken.None)).Success);
        Assert.Equal(first, Assert.Single(coordinator.ListForResource("room-a")));
    }

    [Fact]
    public async Task ReserveAsync_DuplicateReservationIdAcrossResources_CommitsExactlyOnce()
    {
        var coordinator = new ScheduleReservationCoordinator(new PermissiveLeaseCoordinator());

        Assert.True((await coordinator.ReserveAsync(Slot("global-id", "room-a", 0), CancellationToken.None)).Success);
        Assert.True((await coordinator.ReserveAsync(Slot("global-id", "room-b", 0), CancellationToken.None)).Success);
        Assert.Single(coordinator.ListForResource("room-a"));
        Assert.Empty(coordinator.ListForResource("room-b"));
    }

    private static SlotReservation Slot(string id, string resource, int startMinutes)
    {
        var start = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(startMinutes);
        return new SlotReservation(id, resource, start, start.AddHours(1), "holder");
    }

    private sealed class PermissiveLeaseCoordinator : LeaseNs.ILeaseCoordinator
    {
        public Task<LeaseNs.Lease?> AcquireAsync(string resourceId, TimeSpan duration, CancellationToken ct) =>
            Task.FromResult<LeaseNs.Lease?>(new LeaseNs.Lease(
                Guid.NewGuid().ToString("N"),
                resourceId,
                "test-node",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow + duration,
                []));

        public Task ReleaseAsync(LeaseNs.Lease lease, CancellationToken ct) => Task.CompletedTask;

        public bool Holds(string resourceId) => false;

        public IReadOnlyCollection<LeaseNs.Lease> HeldLeases => [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
