using System.Text.Json;
using Harborline.Blocks.Calendar.Models;
using Harborline.Blocks.Calendar.Services;
using Harborline.Foundation.Assets.Common;
using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// Coverage for the Slice S2 participation / resource model — <see cref="CalendarParticipation"/>
/// (party-or-asset participant + role + status with the role↔status invariant) and the
/// <see cref="CalendarEvent"/> mutators that populate the S0-shaped <c>Participations</c> /
/// <c>ResourceRef</c> seams. The headline cases are the <b>clinic appointment shape</b>
/// (doctor-Resource-Party + patient-Attendee-Party + optional room-Resource-Asset) and the
/// <b>role↔status validation</b> (a Resource can't be Declined).
/// </summary>
public sealed class CalendarParticipationTests
{
    private static readonly TenantId Tenant = new("acme");
    private static readonly Guid Actor = Guid.NewGuid();

    private static CalendarEvent NewEvent(string title = "Appointment")
        => CalendarEvent.Create(
            Tenant, title, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2), Actor,
            timezone: "America/Los_Angeles",
            startTime: new TimeOnly(9, 0), endTime: new TimeOnly(9, 30));

    // ----------------------------------------------------------------
    // ParticipantRef — discriminated Party / Asset reference
    // ----------------------------------------------------------------

    [Fact]
    public void ParticipantRef_Party_CarriesKindAndValue()
    {
        var r = ParticipantRef.Party("party-dr-smith");
        Assert.Equal(ParticipantKind.Party, r.Kind);
        Assert.True(r.IsParty);
        Assert.False(r.IsAsset);
        Assert.Equal("party-dr-smith", r.Value);
        Assert.IsType<ParticipantRef.PartyRef>(r);
    }

    [Fact]
    public void ParticipantRef_Asset_CarriesKindAndValue()
    {
        var r = ParticipantRef.Asset("asset-room-3");
        Assert.Equal(ParticipantKind.Asset, r.Kind);
        Assert.True(r.IsAsset);
        Assert.Equal("asset-room-3", r.Value);
        Assert.IsType<ParticipantRef.AssetRef>(r);
    }

    [Fact]
    public void ParticipantRef_PartyAndAsset_WithSameValue_AreNotEqual()
    {
        // Discrimination is load-bearing: the same string under two kinds is two different things.
        Assert.NotEqual(ParticipantRef.Party("x"), (ParticipantRef)ParticipantRef.Asset("x"));
    }

    [Fact]
    public void ParticipantRef_SameKindSameValue_AreEqual()
    {
        Assert.Equal(ParticipantRef.Party("dr"), ParticipantRef.Party("dr"));
    }

    [Fact]
    public void ParticipantRef_Match_BranchesOnKind()
    {
        Assert.Equal("P:dr", ParticipantRef.Party("dr").Match(v => $"P:{v}", v => $"A:{v}"));
        Assert.Equal("A:rm", ParticipantRef.Asset("rm").Match(v => $"P:{v}", v => $"A:{v}"));
    }

    [Fact]
    public void ParticipantRef_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(() => ParticipantRef.Party(""));
        Assert.Throws<ArgumentException>(() => ParticipantRef.Asset("   "));
    }

    // ----------------------------------------------------------------
    // Role ↔ status invariant
    // ----------------------------------------------------------------

    [Fact]
    public void Resource_CannotBeDeclined()
    {
        var room = ParticipantRef.Asset("room-3");
        var ex = Assert.Throws<ArgumentException>(
            () => CalendarParticipation.Create(room, ParticipationRole.Resource, ParticipationStatus.Declined));
        Assert.Equal("status", ex.ParamName);
    }

    [Fact]
    public void Resource_CannotBeInvited()
    {
        var room = ParticipantRef.Asset("room-3");
        Assert.Throws<ArgumentException>(
            () => CalendarParticipation.Create(room, ParticipationRole.Resource, ParticipationStatus.Invited));
    }

    [Theory]
    [InlineData(ParticipationStatus.Tentative)]
    [InlineData(ParticipationStatus.Confirmed)]
    public void Resource_MayBeTentativeOrConfirmed(ParticipationStatus status)
    {
        var p = CalendarParticipation.Create(ParticipantRef.Party("dr"), ParticipationRole.Resource, status);
        Assert.Equal(status, p.Status);
    }

    [Fact]
    public void Attendee_CannotBeConfirmed()
    {
        // Confirmed is the resource-booking state, not an RSVP state.
        var patient = ParticipantRef.Party("patient-jones");
        Assert.Throws<ArgumentException>(
            () => CalendarParticipation.Create(patient, ParticipationRole.Attendee, ParticipationStatus.Confirmed));
    }

    [Theory]
    [InlineData(ParticipationStatus.Invited)]
    [InlineData(ParticipationStatus.Accepted)]
    [InlineData(ParticipationStatus.Declined)]
    [InlineData(ParticipationStatus.Tentative)]
    public void Attendee_MayHaveAnyRsvpStatus(ParticipationStatus status)
    {
        var p = CalendarParticipation.Create(ParticipantRef.Party("p"), ParticipationRole.Attendee, status);
        Assert.Equal(status, p.Status);
    }

    [Fact]
    public void IsStatusLegalFor_MatchesTheInvariant()
    {
        Assert.False(CalendarParticipation.IsStatusLegalFor(ParticipationRole.Resource, ParticipationStatus.Declined));
        Assert.False(CalendarParticipation.IsStatusLegalFor(ParticipationRole.Resource, ParticipationStatus.Invited));
        Assert.True(CalendarParticipation.IsStatusLegalFor(ParticipationRole.Resource, ParticipationStatus.Confirmed));
        Assert.False(CalendarParticipation.IsStatusLegalFor(ParticipationRole.Attendee, ParticipationStatus.Confirmed));
        Assert.True(CalendarParticipation.IsStatusLegalFor(ParticipationRole.Attendee, ParticipationStatus.Declined));
    }

    // ----------------------------------------------------------------
    // RSVP status transitions (WithStatus / SetParticipationStatus)
    // ----------------------------------------------------------------

    [Fact]
    public void WithStatus_TransitionsAndRevalidates()
    {
        var invited = CalendarParticipation.Attendee(ParticipantRef.Party("p"));
        Assert.Equal(ParticipationStatus.Invited, invited.Status);

        var accepted = invited.WithStatus(ParticipationStatus.Accepted);
        Assert.Equal(ParticipationStatus.Accepted, accepted.Status);
        Assert.Equal(invited.Participant, accepted.Participant); // same participant + role

        // Illegal transition is still rejected.
        Assert.Throws<ArgumentException>(() => accepted.WithStatus(ParticipationStatus.Confirmed));
    }

    [Fact]
    public void SetParticipationStatus_TransitionsRsvpOnTheEvent()
    {
        var ev = NewEvent();
        var patient = ParticipantRef.Party("patient-jones");
        ev.AddParticipation(CalendarParticipation.Attendee(patient), Actor);

        ev.SetParticipationStatus(patient, ParticipationRole.Attendee, ParticipationStatus.Accepted, Actor);

        var p = Assert.Single(ev.Participations, x => x.Participant == patient);
        Assert.Equal(ParticipationStatus.Accepted, p.Status);
    }

    [Fact]
    public void SetParticipationStatus_RejectsIllegalTransition()
    {
        var ev = NewEvent();
        var room = ParticipantRef.Asset("room-3");
        ev.AddParticipation(CalendarParticipation.Resource(room, ParticipationStatus.Tentative), Actor);

        // A resource can't be Declined.
        Assert.Throws<ArgumentException>(
            () => ev.SetParticipationStatus(room, ParticipationRole.Resource, ParticipationStatus.Declined, Actor));
    }

    [Fact]
    public void SetParticipationStatus_OnMissingParticipation_Throws()
    {
        var ev = NewEvent();
        Assert.Throws<InvalidOperationException>(
            () => ev.SetParticipationStatus(ParticipantRef.Party("nobody"), ParticipationRole.Attendee, ParticipationStatus.Accepted, Actor));
    }

    // ----------------------------------------------------------------
    // Multi-participation event (organizer + attendee + resource)
    // ----------------------------------------------------------------

    [Fact]
    public void Event_WithOrganizerAttendeeAndResource_HoldsAllThree()
    {
        var ev = NewEvent("Team meeting");
        var organizer = ParticipantRef.Party("party-lead");
        var attendee = ParticipantRef.Party("party-guest");
        var room = ParticipantRef.Asset("room-board");

        ev.AddParticipation(CalendarParticipation.Organizer(organizer), Actor);
        ev.AddParticipation(CalendarParticipation.Attendee(attendee), Actor);
        ev.AddParticipation(CalendarParticipation.Resource(room), Actor);

        Assert.Equal(3, ev.Participations.Count);
        Assert.Single(ev.Participations, p => p.Role == ParticipationRole.Organizer);
        Assert.Single(ev.Participations, p => p.Role == ParticipationRole.Attendee);
        var res = Assert.Single(ev.Participations, p => p.Role == ParticipationRole.Resource);
        Assert.Equal(ParticipationStatus.Confirmed, res.Status); // booked resource default
    }

    [Fact]
    public void AddParticipation_SameParticipantAndRole_ReplacesLastWriteWins()
    {
        var ev = NewEvent();
        var p = ParticipantRef.Party("p");
        ev.AddParticipation(CalendarParticipation.Create(p, ParticipationRole.Attendee, ParticipationStatus.Invited), Actor);
        ev.AddParticipation(CalendarParticipation.Create(p, ParticipationRole.Attendee, ParticipationStatus.Accepted), Actor);

        var only = Assert.Single(ev.Participations);
        Assert.Equal(ParticipationStatus.Accepted, only.Status);
    }

    [Fact]
    public void SameParty_AsAttendeeAndResource_AreDistinctParticipations()
    {
        // A doctor who is both the booked resource and an attendee of their own staff meeting.
        var ev = NewEvent();
        var dr = ParticipantRef.Party("party-dr-smith");
        ev.AddParticipation(CalendarParticipation.Resource(dr), Actor);
        ev.AddParticipation(CalendarParticipation.Attendee(dr), Actor);

        Assert.Equal(2, ev.Participations.Count);
    }

    [Fact]
    public void RemoveParticipation_RemovesByParticipantAndRole_AndClearsResourceRef()
    {
        var ev = NewEvent();
        var dr = ParticipantRef.Party("party-dr-smith");
        ev.SetResource(dr, Actor); // sets ResourceRef + a Resource participation

        Assert.Equal(dr, ev.ResourceRef);
        Assert.True(ev.RemoveParticipation(dr, ParticipationRole.Resource, Actor));
        Assert.Null(ev.ResourceRef); // participation set is authoritative
        Assert.Empty(ev.Participations);

        Assert.False(ev.RemoveParticipation(dr, ParticipationRole.Resource, Actor)); // idempotent
    }

    // ----------------------------------------------------------------
    // The clinic appointment shape
    // ----------------------------------------------------------------

    [Fact]
    public void ClinicAppointmentShape_IsExpressible()
    {
        // { doctor: Resource(Party), patient: Attendee(Party), room: Resource(Asset) }
        var ev = NewEvent("Annual checkup");
        var doctor = ParticipantRef.Party("party-dr-smith");
        var patient = ParticipantRef.Party("party-patient-jones");
        var room = ParticipantRef.Asset("asset-exam-room-2");

        ev.SetResource(doctor, Actor); // the headline scarce resource — the doctor's time
        ev.AddParticipation(CalendarParticipation.Attendee(patient), Actor);
        ev.AddParticipation(CalendarParticipation.Resource(room), Actor); // optional room

        // Doctor is the booked Party-resource (headline).
        Assert.Equal(doctor, ev.ResourceRef);
        Assert.True(ev.ResourceRef!.IsParty);
        var docPart = Assert.Single(ev.Participations, p => p.Participant == doctor);
        Assert.Equal(ParticipationRole.Resource, docPart.Role);
        Assert.Equal(ParticipationStatus.Confirmed, docPart.Status);

        // Patient is the Party-attendee, RSVP-Invited.
        var patPart = Assert.Single(ev.Participations, p => p.Participant == patient);
        Assert.Equal(ParticipationRole.Attendee, patPart.Role);
        Assert.Equal(ParticipationStatus.Invited, patPart.Status);

        // Room is the optional Asset-resource.
        var roomPart = Assert.Single(ev.Participations, p => p.Participant == room);
        Assert.Equal(ParticipationRole.Resource, roomPart.Role);
        Assert.True(roomPart.Participant.IsAsset);

        Assert.Equal(3, ev.Participations.Count);
    }

    [Fact]
    public void ClinicAppointmentShape_RoomIsOptional()
    {
        // The same shape without a room — doctor (Resource-Party) + patient (Attendee-Party) only.
        var ev = NewEvent();
        var doctor = ParticipantRef.Party("party-dr-smith");
        var patient = ParticipantRef.Party("party-patient-jones");
        ev.SetResource(doctor, Actor);
        ev.AddParticipation(CalendarParticipation.Attendee(patient), Actor);

        Assert.Equal(2, ev.Participations.Count);
        Assert.DoesNotContain(ev.Participations, p => p.Participant.IsAsset);
    }

    // ----------------------------------------------------------------
    // Store round-trip — participations survive (de)serialization
    // ----------------------------------------------------------------

    [Fact]
    public async Task Store_RoundTrips_ParticipationsAndResourceAndLocation()
    {
        var store = new InMemoryCalendarEventStore();
        var ev = NewEvent("Annual checkup");
        var doctor = ParticipantRef.Party("party-dr-smith");
        var patient = ParticipantRef.Party("party-patient-jones");
        var room = ParticipantRef.Asset("asset-exam-room-2");
        ev.SetResource(doctor, Actor);
        ev.AddParticipation(CalendarParticipation.Attendee(patient), Actor);
        ev.AddParticipation(CalendarParticipation.Resource(room, ParticipationStatus.Tentative), Actor);
        ev.SetCalendarId(CalendarId.NewId(), Actor);
        ev.SetLocation("Building A, Room 2", Actor);

        await store.SaveAsync(ev);
        var loaded = await store.GetAsync(Tenant, ev.Id);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Participations.Count);
        Assert.Equal(doctor, loaded.ResourceRef);
        Assert.True(loaded.ResourceRef!.IsParty);
        Assert.Equal("Building A, Room 2", loaded.Location);
        Assert.NotNull(loaded.CalendarId);

        // The Asset-resource's kind + tentative status survived the JSON round-trip.
        var roomPart = Assert.Single(loaded.Participations, p => p.Participant == room);
        Assert.True(roomPart.Participant.IsAsset);
        Assert.Equal(ParticipationRole.Resource, roomPart.Role);
        Assert.Equal(ParticipationStatus.Tentative, roomPart.Status);

        // The patient's RSVP-Invited Party-attendee survived too.
        var patPart = Assert.Single(loaded.Participations, p => p.Participant == patient);
        Assert.Equal(ParticipationRole.Attendee, patPart.Role);
        Assert.Equal(ParticipationStatus.Invited, patPart.Status);
    }

    [Fact]
    public void LegacyS0S1Snapshot_WithoutS2Fields_DeserializesToEmptyParticipations()
    {
        // An S0/S1-era persisted snapshot predates the S2 participation fields entirely. It must
        // still load — to an empty participation set / null refs (not null Participations, which
        // would NRE the EventsFor `.Any()` scan). This is the backward-compatibility guard for the
        // "S2 fields are not `required`" decision.
        var opts = new JsonSerializerOptions(JsonSerializerDefaults.General);
        const string legacyJson = """
        {
          "Id": "0195a1b0-0000-7000-8000-000000000001",
          "TenantId": "acme",
          "Title": "Legacy event",
          "Start": "2026-03-02",
          "End": "2026-03-02",
          "StartTime": "00:00:00",
          "EndTime": "00:00:00",
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

        var ev = JsonSerializer.Deserialize<CalendarEventSnapshot>(legacyJson, opts)!.ToEntity();

        Assert.Empty(ev.Participations);
        Assert.Null(ev.ResourceRef);
        Assert.Null(ev.CalendarId);
        Assert.Null(ev.Location);
    }
}
