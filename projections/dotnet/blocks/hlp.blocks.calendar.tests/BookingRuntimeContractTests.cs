using System.Text.Json;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.Models;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>T-605: the Allocation and hold runtime contracts, which never travel as definitions.</summary>
public sealed class BookingRuntimeContractTests
{
    private static readonly DateTimeOffset Expiry = new(2026, 10, 1, 9, 15, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact(DisplayName = "booking-ck-16: an Allocation is a catalogue record of six members, with no archive namespace, refused as a definition")]
    public void AllocationIsACatalogueRecord()
    {
        var allocation = new BookingAllocation("induction", "learner.7",
            TimeInterval.Of(Expiry, Expiry.AddMinutes(90)), "held", ["instructor", "room"], null);
        var json = JsonSerializer.SerializeToNode(allocation, SnakeCase)!.AsObject();
        Assert.Equal(["bookable_id", "subject_id", "window", "state", "held_resources", "swapped_from_id"], json.Select(member => member.Key));
        Assert.DoesNotContain(Enum.GetNames<DefinitionKind>(), name => name.Contains("Allocation", StringComparison.Ordinal));

        json["kind"] = "allocation";
        json["envelope"] = Fixtures.Envelope();
        foreach (var kind in new[] { DefinitionKind.Resources, DefinitionKind.Bookables })
            Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataNotADefinition, "/kind")],
                BookingDefinitionAdmission.Validate(Fixtures.Document(kind, json), Fixtures.Context()));
    }

    [Theory(DisplayName = "booking-ck-22: expiry wins at the kernel commit instant, never the request's start")]
    [InlineData(-1, BookingHoldState.Confirmed, null)]
    [InlineData(0, BookingHoldState.Expired, BookingHoldCodes.Expired)]
    [InlineData(1, BookingHoldState.Expired, BookingHoldCodes.Expired)]
    public void ExpiryWinsAtTheCommitInstant(int commitOffsetTicks, BookingHoldState state, string? refusal)
    {
        // The confirm began before expiry; only the instant read inside the commit decides.
        var outcome = new BookingHold("hold.1", Expiry).Confirm(Expiry.AddTicks(commitOffsetTicks));
        Assert.Equal(state, outcome.Hold.State);
        Assert.Equal(refusal, outcome.Refusal);
    }

    [Fact(DisplayName = "booking-ck-22: a hold reaches exactly one terminal outcome")]
    public void OneTerminalOutcomePerHold()
    {
        var expired = new BookingHold("hold.1", Expiry).Expire(Expiry).Hold;
        Assert.Equal(BookingHoldState.Expired, expired.State);
        var late = expired.Confirm(Expiry.AddMinutes(-1));
        Assert.Equal((BookingHoldState.Expired, BookingHoldCodes.Terminal), (late.Hold.State, late.Refusal));

        var confirmed = new BookingHold("hold.2", Expiry).Confirm(Expiry.AddMinutes(-1)).Hold;
        Assert.Equal((BookingHoldState.Confirmed, BookingHoldCodes.Terminal),
            (confirmed.Expire(Expiry).Hold.State, confirmed.Expire(Expiry).Refusal));
        Assert.Equal(BookingHoldState.Confirmed, confirmed.Cancel().Hold.State);

        var early = new BookingHold("hold.3", Expiry).Expire(Expiry.AddTicks(-1));
        Assert.Equal((BookingHoldState.Held, BookingHoldCodes.NotExpired), (early.Hold.State, early.Refusal));
        Assert.Equal(BookingHoldState.Cancelled, new BookingHold("hold.4", Expiry).Cancel().Hold.State);
    }
}
