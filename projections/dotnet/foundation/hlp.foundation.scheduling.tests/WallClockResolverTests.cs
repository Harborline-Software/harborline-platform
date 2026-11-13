using System.Globalization;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class WallClockResolverTests
{
    // ADR-0123 decisions 2 and 4; RFC 5545 section 3.3.5 and Errata 4271.
    [Theory]
    [InlineData("2026-07-01T20:00:00Z", "Pacific/Auckland", "2026-07-02T08:00:00")]
    [InlineData("2026-07-01T20:00:00Z", "Pacific/Honolulu", "2026-07-01T10:00:00")]
    [InlineData("2026-03-08T06:30:00Z", "America/New_York", "2026-03-08T01:30:00")]
    [InlineData("2026-03-08T07:30:00Z", "America/New_York", "2026-03-08T03:30:00")]
    [InlineData("2026-11-01T05:30:00Z", "America/New_York", "2026-11-01T01:30:00")]
    [InlineData("2026-11-01T06:30:00Z", "America/New_York", "2026-11-01T01:30:00")]
    public void KernelInstantBecomesSeriesLocalTime(string instant, string zone, string expected)
        => Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture),
            WallClockResolver.ToLocalDateTime(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), zone));

    [Theory]
    [InlineData("2026-03-08T02:30:00", "2026-03-08T07:30:00Z")]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T05:30:00Z")]
    public void ForwardResolutionKeepsRfcGapAndOverlapPolicy(string local, string expected)
    {
        var value = DateTime.Parse(local, CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture),
            WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(value), TimeOnly.FromDateTime(value), "America/New_York"));
    }

    [Fact]
    public void UnknownZoneRefusesInBothDirections()
    {
        Assert.Throws<ArgumentException>(() => WallClockResolver.ToLocalDateTime(DateTimeOffset.UnixEpoch, "missing/zone"));
        Assert.Throws<ArgumentException>(() => WallClockResolver.ToUtcInstant(new(2026, 7, 1), new(12, 0), "missing/zone"));
    }
}
