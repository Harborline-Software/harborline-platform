using System.Text.Json;
using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for Slice S3 persistence: the <see cref="Occupancy"/> + <see cref="ContextRef"/> fields
/// round-trip through the event store; <see cref="ResourceAvailability"/> round-trips through its
/// store; and an older (pre-S3) event snapshot still deserializes (Occupancy defaults to Bookable,
/// ScheduledAgainst to null) — the not-required backward-compat discipline.
/// </summary>
public sealed class S3PersistenceTests
{
    private static readonly TenantId Acme = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 3, 4);

    [Fact]
    public async Task EventStore_RoundTrips_OccupancyAndScheduledAgainst()
    {
        var store = new InMemoryCalendarEventStore();
        var ctx = ContextRef.Of("project", "proj-42");

        var ev = CalendarEvent.Create(Acme, "Project task", Day, Day, Actor,
            timezone: "UTC", startTime: new TimeOnly(9, 0), endTime: new TimeOnly(10, 0),
            occupancy: Occupancy.Blocking);
        ev.SetScheduledAgainst(ctx, Actor);
        await store.SaveAsync(ev);

        var loaded = await store.GetAsync(Acme, ev.Id);

        Assert.NotNull(loaded);
        Assert.Equal(Occupancy.Blocking, loaded!.Occupancy);
        Assert.Equal(ctx, loaded.ScheduledAgainst);
    }

    [Fact]
    public async Task AvailabilityStore_RoundTrips_WindowsAndExceptions()
    {
        var store = new InMemoryResourceAvailabilityStore();
        var doctor = ParticipantRef.Party("party-dr-smith");

        var avail = ResourceAvailability.Create(Acme, doctor, "America/Los_Angeles")
            .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(17, 0), "FREQ=WEEKLY;BYDAY=MO,WE,FR"))
            .AddException(new DateOnly(2026, 3, 6));
        await store.SaveAsync(avail);

        var loaded = await store.GetAsync(Acme, doctor);

        Assert.NotNull(loaded);
        Assert.Equal("America/Los_Angeles", loaded!.Timezone);
        var w = Assert.Single(loaded.Windows);
        Assert.Equal(new TimeOnly(9, 0), w.StartTime);
        Assert.Equal("FREQ=WEEKLY;BYDAY=MO,WE,FR", w.Rrule);
        Assert.Contains(new DateOnly(2026, 3, 6), loaded.ExceptionDates);
    }

    [Fact]
    public async Task AvailabilityStore_IsTenantScoped_AndKeyedByResource()
    {
        var store = new InMemoryResourceAvailabilityStore();
        var doctor = ParticipantRef.Party("party-dr-smith");
        var globex = new TenantId("globex");

        await store.SaveAsync(ResourceAvailability.Create(Acme, doctor, "UTC")
            .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(17, 0))));

        // Same resource ref, different tenant — must not be visible.
        Assert.Null(await store.GetAsync(globex, doctor));
        Assert.NotNull(await store.GetAsync(Acme, doctor));
    }

    [Fact]
    public async Task AvailabilityStore_PartyAndAsset_WithSameValue_DoNotCollide()
    {
        var store = new InMemoryResourceAvailabilityStore();
        var asParty = ParticipantRef.Party("shared-id");
        var asAsset = ParticipantRef.Asset("shared-id");

        await store.SaveAsync(ResourceAvailability.Create(Acme, asParty, "UTC")
            .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(9, 0), new TimeOnly(12, 0))));
        await store.SaveAsync(ResourceAvailability.Create(Acme, asAsset, "UTC")
            .AddWindow(AvailabilityWindow.Create(Day, new TimeOnly(13, 0), new TimeOnly(17, 0))));

        var party = await store.GetAsync(Acme, asParty);
        var asset = await store.GetAsync(Acme, asAsset);

        Assert.Equal(new TimeOnly(9, 0), Assert.Single(party!.Windows).StartTime);
        Assert.Equal(new TimeOnly(13, 0), Assert.Single(asset!.Windows).StartTime);
    }

    [Fact]
    public void OlderSnapshot_WithoutS3Fields_DeserializesToBookableDefault()
    {
        // A pre-S3 snapshot JSON (no Occupancy / ScheduledAgainst keys). It must still deserialize,
        // with Occupancy defaulting to Bookable (= 0) and ScheduledAgainst to null.
        var json = """
        {
          "Id": "0195a8d0-0000-7000-8000-000000000001",
          "TenantId": "acme",
          "Title": "Legacy appt",
          "Start": "2026-03-04",
          "End": "2026-03-04",
          "StartTime": "09:00:00",
          "EndTime": "09:30:00",
          "Timezone": "UTC",
          "Status": 0,
          "ExceptionDates": [],
          "Overrides": [],
          "CreatedAt": "2026-03-01T00:00:00+00:00",
          "UpdatedAt": "2026-03-01T00:00:00+00:00",
          "CreatedBy": "00000000-0000-0000-0000-000000000000",
          "UpdatedBy": "00000000-0000-0000-0000-000000000000",
          "Version": 0
        }
        """;

        var snapshot = JsonSerializer.Deserialize<CalendarEventSnapshot>(json, new JsonSerializerOptions(JsonSerializerDefaults.General));
        Assert.NotNull(snapshot);

        var ev = snapshot!.ToEntity();
        Assert.Equal(Occupancy.Bookable, ev.Occupancy);
        Assert.Null(ev.ScheduledAgainst);
    }
}
