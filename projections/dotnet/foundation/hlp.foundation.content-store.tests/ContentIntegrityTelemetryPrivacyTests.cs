using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Harborline.Foundation.Assets.Common;

using Xunit;

namespace Harborline.Foundation.ContentStore.Tests;

[CollectionDefinition("Content integrity telemetry privacy", DisableParallelization = true)]
public sealed class ContentIntegrityTelemetryPrivacyCollection
{
}

[Collection("Content integrity telemetry privacy")]
public sealed class ContentIntegrityTelemetryPrivacyTests
{
    private const string PayloadSentinel = "PAYLOAD_PRIVACY_SENTINEL_53B1C4D8";
    private const string FilenameSentinel = "FILENAME_PRIVACY_SENTINEL_79E5A2C6";

    [Fact]
    public void Real_content_integrity_telemetry_emits_positive_signals_without_payload_or_filename_values()
    {
        using var telemetry = new TelemetryCapture();
        var log = new CapturingLogger();
        var tenant = new TenantId("privacy-test-tenant");
        var store = new InMemoryContentStore(
            new ContentStoreOptions(200_000, 400_000, 400_000, TimeSpan.FromMinutes(1)),
            new TestKeys(), new TestClock(), new InMemoryContentStoreStorage(), log);
        var payload = Encoding.UTF8.GetBytes(new string('x', 65_536) + PayloadSentinel);
        using var upload = new MemoryStream(payload);
        var stage = store.Put(tenant, upload, "text/plain", null);
        var reference = store.Commit(
            tenant, stage, new ContentOwner("privacy-record", "privacy-element"),
            "FILENAME_PRIVACY_SENTINEL_79E5A2C6.txt");
        var context = new ContentReadContext("trace-fixed", "operation-fixed", "tenant-opaque", "principal-fixed");

        var delivery = store.Read(tenant, reference, true, context: context);
        Assert.Equal(200, delivery.StatusCode);
        Assert.Equal("attachment; filename=\"FILENAME_PRIVACY_SENTINEL_79E5A2C6.txt\"", delivery.ContentDisposition);
        using (delivery.Body)
        using (var clear = new MemoryStream())
        {
            delivery.Body.CopyTo(clear);
            Assert.Equal(payload, clear.ToArray());
            Assert.Contains("PAYLOAD_PRIVACY_SENTINEL_53B1C4D8", Encoding.UTF8.GetString(clear.ToArray()));
        }

        store.CorruptCiphertext(tenant, reference.ContentId, 1);
        using var damagedBody = store.Read(tenant, reference, true, context: context).Body;
        using var discarded = new MemoryStream();
        var failure = Assert.Throws<ContentIntegrityException>(() => damagedBody.CopyTo(discarded));
        Assert.Equal(1, failure.SegmentIndex);

        var integrity = Assert.Single(telemetry.Activities.Where(activity => activity.OperationName == "content.integrity"));
        Assert.Equal(ActivityStatusCode.Error, integrity.Status);
        Assert.Equal("content.integrity", integrity.GetTagItem("error.type"));
        var measurement = Assert.Single(telemetry.Measurements.Where(item => item.Name == "content.integrity.failures"));
        Assert.Equal(1L, Assert.IsType<long>(measurement.Value));
        var firstLog = Assert.Single(log.Events);
        Assert.Equal("trace-fixed", firstLog.TraceId);
        Assert.Equal("operation-fixed", firstLog.OperationId);
        Assert.Equal("tenant-opaque", firstLog.OpaqueTenantId);
        Assert.Equal("principal-fixed", firstLog.PrincipalId);
        Assert.Equal(stage, firstLog.ItemKey);
        Assert.Equal("privacy-record", firstLog.Owner.Record);
        Assert.Equal("privacy-element", firstLog.Owner.Element);
        Assert.Equal(1, firstLog.SegmentIndex);

        AssertNoSentinels(telemetry.Activities.SelectMany(ActivityText));
        AssertNoSentinels(telemetry.Measurements.SelectMany(MeasurementText));
        AssertNoSentinels(log.Events.Select(integrityEvent => JsonSerializer.Serialize(integrityEvent)));
    }

    private static void AssertNoSentinels(IEnumerable<string> emitted)
    {
        foreach (var value in emitted)
        {
            Assert.False(value.Contains(PayloadSentinel, StringComparison.OrdinalIgnoreCase),
                "A payload sentinel reached emitted telemetry.");
            Assert.False(value.Contains(FilenameSentinel, StringComparison.OrdinalIgnoreCase),
                "A filename sentinel reached emitted telemetry.");
        }
    }

    private static IEnumerable<string> ActivityText(Activity activity)
    {
        yield return activity.Source.Name;
        yield return activity.Source.Version ?? string.Empty;
        yield return activity.OperationName;
        yield return activity.DisplayName;
        yield return activity.StatusDescription ?? string.Empty;
        yield return activity.TraceStateString ?? string.Empty;
        foreach (var tag in activity.TagObjects)
        {
            yield return tag.Key;
            foreach (var text in ValueText(tag.Value)) yield return text;
        }

        foreach (var baggage in activity.Baggage)
        {
            yield return baggage.Key;
            yield return baggage.Value ?? string.Empty;
        }

        foreach (var activityEvent in activity.Events)
        {
            yield return activityEvent.Name;
            foreach (var tag in activityEvent.Tags)
            {
                yield return tag.Key;
                foreach (var text in ValueText(tag.Value)) yield return text;
            }
        }

        foreach (var link in activity.Links)
        {
            foreach (var tag in link.Tags ?? [])
            {
                yield return tag.Key;
                foreach (var text in ValueText(tag.Value)) yield return text;
            }
        }
    }

    private static IEnumerable<string> MeasurementText(Measurement measurement)
    {
        yield return measurement.MeterName;
        yield return measurement.MeterVersion ?? string.Empty;
        yield return measurement.Name;
        yield return measurement.Unit ?? string.Empty;
        yield return measurement.Description ?? string.Empty;
        foreach (var text in ValueText(measurement.Value)) yield return text;
        foreach (var tag in measurement.MeterTags.Concat(measurement.InstrumentTags).Concat(measurement.Tags))
        {
            yield return tag.Key;
            foreach (var text in ValueText(tag.Value)) yield return text;
        }
    }

    private static IEnumerable<string> ValueText(object? value)
    {
        if (value is null) yield break;
        if (value is string text)
        {
            yield return text;
        }
        else if (value is byte[] bytes)
        {
            yield return Encoding.UTF8.GetString(bytes);
        }
        else if (value is IEnumerable sequence)
        {
            foreach (var entry in sequence)
                foreach (var item in ValueText(entry)) yield return item;
        }
        else
        {
            yield return JsonSerializer.Serialize(value);
            yield return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private sealed record Measurement(
        string MeterName, string? MeterVersion, string Name, string? Unit, string? Description,
        object Value, KeyValuePair<string, object?>[] MeterTags,
        KeyValuePair<string, object?>[] InstrumentTags, KeyValuePair<string, object?>[] Tags);

    private sealed class TelemetryCapture : IDisposable
    {
        private readonly ActivityListener _activities;
        private readonly MeterListener _meter = new();

        public ConcurrentQueue<Activity> Activities { get; } = new();
        public ConcurrentQueue<Measurement> Measurements { get; } = new();

        public TelemetryCapture()
        {
            _activities = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Harborline.Foundation.ContentStore",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
                ActivityStopped = Activities.Enqueue,
            };
            ActivitySource.AddActivityListener(_activities);
            _meter.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Harborline.Foundation.ContentStore")
                    listener.EnableMeasurementEvents(instrument);
            };
            _meter.SetMeasurementEventCallback<byte>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<short>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<int>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<long>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<float>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<double>(RecordMeasurement);
            _meter.SetMeasurementEventCallback<decimal>(RecordMeasurement);
            _meter.Start();
        }

        private void RecordMeasurement<T>(
            Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
            where T : struct
        {
            Measurements.Enqueue(new Measurement(
                instrument.Meter.Name, instrument.Meter.Version, instrument.Name, instrument.Unit, instrument.Description,
                value, instrument.Meter.Tags?.ToArray() ?? [], instrument.Tags?.ToArray() ?? [], tags.ToArray()));
        }

        public void Dispose()
        {
            _meter.Dispose();
            _activities.Dispose();
        }
    }

    private sealed class CapturingLogger : IContentIntegrityLogger
    {
        public ConcurrentQueue<ContentIntegrityLogEvent> Events { get; } = new();
        public void LogIntegrityFailure(ContentIntegrityLogEvent integrityEvent) => Events.Enqueue(integrityEvent);
    }

    private sealed class TestKeys : ITenantContentKeyProvider
    {
        public byte[] GetKey(TenantId tenantId) => SHA256.HashData(Encoding.UTF8.GetBytes("privacy-key:" + tenantId.Value));
    }

    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    }
}
