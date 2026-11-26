using System.Diagnostics.Metrics;
using Harborline.Blocks.BuilderDefinitions;
using Xunit;

namespace Harborline.Foundation.Definitions.Tests;

public sealed class DefinitionRefusalTelemetryTests
{
    [Fact]
    public void ThrowingListenerCannotEscapeOrStopTheRemainingRefusals()
    {
        var calls = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "Harborline.Definitions") owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) =>
        {
            calls++;
            throw new InvalidOperationException("listener fault");
        });
        listener.Start();
        var refusal = new DefinitionRefusalException(DefinitionAdmissionPhase.Publish,
            [new("fixture.first", "/one"), new("fixture.second", "/two")]);

        DefinitionRefusalTelemetry.Record("opaque-a", "definition-a", refusal);

        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(DefinitionAdmissionPhase.Author, "Author")]
    [InlineData(DefinitionAdmissionPhase.Publish, "Publish")]
    [InlineData(DefinitionAdmissionPhase.Install, "Install")]
    [InlineData(DefinitionAdmissionPhase.Render, "Render")]
    public void EachRefusalHasExactlyTheFourContentFreeAttributes(DefinitionAdmissionPhase stage, string stageName)
    {
        var measurements = new List<(long Count, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter.Name == "Harborline.Definitions" && instrument.Name == "definition.refusals")
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, count, tags, _) => measurements.Add((count, tags.ToArray())));
        listener.Start();
        var refusal = new DefinitionRefusalException(stage,
            [new("fixture.first", "/secret-sentinel", "secret-sentinel"), new("fixture.second", "/secret-sentinel")]);

        DefinitionRefusalTelemetry.Record("opaque-a", "definition-a", refusal);

        Assert.Equal("Harborline.Definitions", DefinitionRefusalTelemetry.MeterName);
        Assert.Equal("definition.refusals", DefinitionRefusalTelemetry.InstrumentName);
        Assert.Equal(2, measurements.Count);
        for (var i = 0; i < measurements.Count; i++)
        {
            Assert.Equal(1, measurements[i].Count);
            Assert.Equal(new KeyValuePair<string, object?>[]
            {
                new("tenant_id", "opaque-a"), new("stage", stageName), new("definition_id", "definition-a"),
                new("code", i == 0 ? "fixture.first" : "fixture.second")
            }, measurements[i].Tags);
        }
    }
}
