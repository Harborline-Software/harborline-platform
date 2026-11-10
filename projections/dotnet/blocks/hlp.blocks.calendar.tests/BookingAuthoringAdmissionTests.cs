using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;
using Harborline.Blocks.Calendar.Models;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>
/// T-606/member-editors: the Resource and Bookable both UI lanes author from nothing (conformance/hlp.ui.booking-authoring) are
/// admitted by the producer, and the refusals the lanes render by field are the producer's own. The fixture is written from the
/// Booking grammar and is not read by BookingDefinitionAdmission, so it is an outside oracle (T-724 ruling 121).
/// </summary>
public sealed class BookingAuthoringAdmissionTests
{
    private static readonly JsonNode Fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "booking-authoring-fixtures", "fixtures.yaml")))!;
    private static JsonNode Case(string id) => Fixture["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!;
    private static JsonObject Body(JsonNode? body) => body!.DeepClone().AsObject();

    [Fact(DisplayName = "booking-eng-20, booking-auth-1, -2, -3, -5: the Resource both lanes author from nothing is admitted with no refusal")]
    [Trait("Holds", "booking-eng-20"), Trait("Holds", "booking-auth-1"), Trait("Holds", "booking-auth-2"), Trait("Holds", "booking-auth-3"), Trait("Holds", "booking-auth-5")]
    public void AuthoredResourceIsAdmitted()
    {
        var body = Body(Case("booking-authoring.resource-from-nothing")["expected"]!["body"]);

        Assert.Empty(BookingDefinitionTests.Admit(DefinitionKind.Resources, body));
        var parsed = BookingResourceDefinition.Parse(body.ToJsonString());
        Assert.Equal((CapacityKind.Pool, (int?)2), (parsed.CapacityKind, parsed.PoolSize));
    }

    [Fact(DisplayName = "booking-eng-20, booking-auth-6, -7, -10: the Bookable both lanes author from nothing is admitted with no refusal")]
    [Trait("Holds", "booking-eng-20"), Trait("Holds", "booking-auth-6"), Trait("Holds", "booking-auth-7"), Trait("Holds", "booking-auth-10")]
    public void AuthoredBookableIsAdmitted() =>
        Assert.Empty(BookingDefinitionTests.Admit(DefinitionKind.Bookables, Body(Case("booking-authoring.bookable-from-nothing")["expected"]!["body"])));

    [Fact(DisplayName = "booking-eng-20: the refusals the lanes render by field are exactly what admission returns for that draft")]
    [Trait("Holds", "booking-eng-20")]
    public void RenderedRefusalsAreTheProducers()
    {
        var item = Case("booking-authoring.refusals-by-field");
        var expected = item["input"]!["diagnostics"]!.AsArray().Select(entry => (entry!["code"]!.GetValue<string>(), entry["pointer"]!.GetValue<string>())).ToArray();

        BookingDefinitionTests.AssertRefusals(BookingDefinitionTests.Admit(DefinitionKind.Resources, Body(item["input"]!["draft"]!["body"])), expected);
    }
}
