using System.Globalization;
using Harborline.Foundation.Scheduling;
using Xunit;

namespace Harborline.Foundation.Scheduling.Tests;

public sealed class SchedulingWallClockLiteralProofTests
{
    [Theory(DisplayName = "scheduling-ck-24 scheduling-eng-3 wall-clock: RFC gap, overlap and ordinary time")]
    [InlineData("2026-03-08T02:30:00", "2026-03-08T07:30:00+00:00")]
    [InlineData("2026-11-01T01:30:00", "2026-11-01T05:30:00+00:00")]
    [InlineData("2026-07-01T12:00:00", "2026-07-01T16:00:00+00:00")]
    public void UtcInstantUsesRfcPolicy(string local, string expected)
    {
        var value = DateTime.Parse(local, CultureInfo.InvariantCulture);
        var actual = WallClockResolver.ToUtcInstant(DateOnly.FromDateTime(value), TimeOnly.FromDateTime(value), "America/New_York");
        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), actual);
        Assert.Equal(TimeSpan.Zero, actual.Offset);
    }

    [Theory(DisplayName = "scheduling-ck-24 scheduling-eng-3 wall-clock: series local date and time")]
    [InlineData("Pacific/Auckland", "2026-07-02T08:00:00")]
    [InlineData("Pacific/Honolulu", "2026-07-01T10:00:00")]
    public void LocalDateTimeUsesSeriesZone(string zone, string expected)
    {
        var instant = DateTimeOffset.Parse("2026-07-01T20:00:00Z", CultureInfo.InvariantCulture);
        var actual = WallClockResolver.ToLocalDateTime(instant, zone);
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), actual);
        Assert.Equal(DateTimeKind.Unspecified, actual.Kind);
    }

    [Fact(DisplayName = "scheduling-ck-24 scheduling-eng-3 wall-clock: unknown zone refuses both directions")]
    public void UnknownZoneThrows()
    {
        Assert.Throws<ArgumentException>(() => WallClockResolver.ToUtcInstant(new(2026, 7, 1), new(12, 0), "missing/zone"));
        Assert.Throws<ArgumentException>(() => WallClockResolver.ToLocalDateTime(DateTimeOffset.Parse("2026-07-01T20:00:00Z", CultureInfo.InvariantCulture), "missing/zone"));
    }
}
