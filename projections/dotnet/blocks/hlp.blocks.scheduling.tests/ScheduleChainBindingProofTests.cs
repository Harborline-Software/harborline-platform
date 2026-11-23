using System.Globalization;
using Harborline.Blocks.Scheduling.Definitions;
using Xunit;

namespace Harborline.Blocks.Scheduling.Tests;

public sealed class ScheduleChainBindingProofTests
{
    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
    private static DueOccurrenceRequest Valid()
    {
        ScheduleChainNode node = new("a", null, "PT0S", new("PT1H", "PT1H"));
        var chain = new RelativeChainExpansionRequest(new("visits", 1, "visit", [node]), "series", "subject",
            [new("visit", 1, Instant("2026-07-01T08:00:00Z"), AnchorSnapshotState.Present, ["visit/r1"])],
            new(0, [], []), Instant("2026-07-01T09:00:00Z"), "UTC");
        var definition = new ScheduleDefinition(null, "Visits", new(ScheduleStartKind.Form, "visit"),
            Basis: new EventAnchoredChainBasis("visit", [node]));
        return new(new("series", "subject", definition, Timezone: "UTC"), Instant("2026-07-01T00:00:00Z"),
            Instant("2026-07-02T00:00:00Z"), [], [], chain);
    }
    private static void Positive(DueOccurrenceRequest request)
    {
        var occurrence = Assert.Single(new RelativeChainDueOccurrenceSource().DeriveDue(request, Instant("2026-07-01T09:00:00Z")));
        Assert.Equal("rc1/series/a/2026-07-01T08%3A00%3A00.0000000%2B00%3A00/r1", occurrence.OccurrenceId);
        Assert.Equal("series", occurrence.SeriesId);
        Assert.Equal("subject", occurrence.SubjectRef);
        Assert.Equal(Instant("2026-07-01T08:00:00Z"), occurrence.Due);
    }
    [Fact(DisplayName = "scheduling-eng-3 scheduling-run-2 chain-binding matching and missing chain")]
    public void MatchingAndMissing()
    {
        var request = Valid();
        Positive(request);
        Assert.Equal("scheduling.occurrence.anchor_required", Assert.Throws<OccurrenceRefusalException>(() =>
            new RelativeChainDueOccurrenceSource().DeriveDue(request with { Chain = null }, Instant("2026-07-01T09:00:00Z"))).Code);
    }
    [Theory(DisplayName = "scheduling-eng-3 scheduling-run-2 chain-binding rejects each mismatched coordinate")]
    [InlineData("instance")]
    [InlineData("subject")]
    [InlineData("timezone")]
    [InlineData("anchor")]
    [InlineData("nodes")]
    public void MismatchRefusesAndCorrectionRestores(string coordinate)
    {
        var request = Valid();
        var chain = request.Chain!;
        var changed = coordinate switch
        {
            "instance" => chain with { ChainInstanceId = "other" },
            "subject" => chain with { SubjectRef = "other" },
            "timezone" => chain with { TimezoneId = "Etc/UTC" },
            "anchor" => chain with { Definition = chain.Definition with { Anchor = "other" } },
            "nodes" => chain with { Definition = chain.Definition with { Nodes = [chain.Definition.Nodes[0] with { Offset = "PT1H" }] } },
            _ => throw new ArgumentOutOfRangeException(nameof(coordinate))
        };
        Assert.Equal("scheduling.occurrence.chain_binding_invalid", Assert.Throws<OccurrenceRefusalException>(() =>
            new RelativeChainDueOccurrenceSource().DeriveDue(request with { Chain = changed }, Instant("2026-07-01T09:00:00Z"))).Code);
        Positive(request with { Chain = chain });
    }
}
