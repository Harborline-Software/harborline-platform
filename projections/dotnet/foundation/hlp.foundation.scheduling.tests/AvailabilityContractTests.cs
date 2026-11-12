using Harborline.Foundation.Scheduling.Models;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class AvailabilityContractTests
{
    [Fact]
    public void MovedAvailabilityContractRetainsItsGuardsAndHalfOpenIntervals()
    {
        Assert.Throws<ArgumentException>(() => ParticipantRef.Party(" "));
        Assert.Throws<ArgumentException>(() => ParticipantRef.Asset(""));
        Assert.Throws<ArgumentNullException>(() => ResourceCapacity.Exclusive(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceCapacity.Pool(ParticipantRef.Asset("room"), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPadding.Of(TimeSpan.FromSeconds(-1), TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => EventPadding.Of(TimeSpan.Zero, TimeSpan.FromSeconds(-1)));
        var start = DateTimeOffset.Parse("2027-01-01T00:00:00Z");
        Assert.Throws<ArgumentException>(() => TimeInterval.Of(start, start));
        var interval = TimeInterval.Of(start, start.AddHours(1));
        Assert.True(interval.Contains(start));
        Assert.False(interval.Contains(start.AddHours(1)));
        Assert.False(interval.Overlaps(TimeInterval.Of(start.AddHours(1), start.AddHours(2))));
        Assert.Equal(TimeSpan.FromHours(1), interval.Duration);
        var resource = ParticipantRef.Asset("room");
        Assert.Equal(3, ResourceCapacity.Pool(resource, 3).ConcurrentLimit);
        Assert.Equal(1, ResourceCapacity.Exclusive(resource).ConcurrentLimit);
        Assert.False(new AvailabilityAnswer(new("refused", "/window"), []).Available);
        Assert.False(new AvailabilityAnswer(null, [new(resource, [], [], 0, Unavailability.CapacityExhausted)]).Available);
    }
}
