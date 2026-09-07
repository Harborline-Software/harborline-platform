using Harborline.Blocks.Calendar.Models;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S3 <see cref="Occupancy"/> classification — the COMMON occupancy flag
/// free/busy needs (Bookable vs. Blocking vs. Tentative). NOT the vertical billable/productive flags.
/// </summary>
public sealed class OccupancyTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 3, 10);

    [Fact]
    public void NewEvent_DefaultsToBookableOccupancy()
    {
        var ev = CalendarEvent.Create(Acme, "Appt", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        Assert.Equal(Occupancy.Bookable, ev.Occupancy);
    }

    [Fact]
    public void Create_AcceptsBlockingOccupancy_ForALunch()
    {
        var lunch = CalendarEvent.Create(Acme, "Lunch", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(12, 0), endTime: new TimeOnly(13, 0),
            occupancy: Occupancy.Blocking);

        Assert.Equal(Occupancy.Blocking, lunch.Occupancy);
    }

    [Fact]
    public void SetOccupancy_FlipsTheClassification()
    {
        var ev = CalendarEvent.Create(Acme, "Hold", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

        ev.SetOccupancy(Occupancy.Tentative, Actor);
        Assert.Equal(Occupancy.Tentative, ev.Occupancy);

        ev.SetOccupancy(Occupancy.Bookable, Actor);
        Assert.Equal(Occupancy.Bookable, ev.Occupancy);
    }

    [Fact]
    public void BookableVsBlocking_AreDistinctClassifications()
    {
        // The load-bearing distinction free/busy needs: an appointment (consumes availability) vs. an
        // internal block (busy but not bookable). Both are non-default-equal as enum values.
        Assert.NotEqual(Occupancy.Bookable, Occupancy.Blocking);
    }
}
