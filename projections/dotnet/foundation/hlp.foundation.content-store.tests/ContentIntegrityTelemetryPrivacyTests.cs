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
    // Mirrors ContentStore.SegmentSizeBytes (private) so the sentinel lands in both segments 0 and 1.
    private const int SegmentSizeBytes = 65_536;

    [Fact]
    public async Task Real_content_integrity_telemetry_emits_positive_signals_without_payload_or_filename_values()
    {
        using var telemetry = new TelemetryCapture();
        var log = new CapturingLogger();
        var tenant = new TenantId("privacy-test-tenant");
        var store = new InMemoryContentStore(
            new ContentStoreOptions(200_000, 400_000, 400_000, TimeSpan.FromMinutes(1)),
            new TestKeys(), new TestClock(), new InMemoryContentStoreStorage(), log);
        // Segment 0 (intact) and segment 1 (corrupted below) each carry the sentinel, so a plaintext
        // copy exists in decrypted segment 0 when the segment 1 failure is emitted.
        var payload = Encoding.UTF8.GetBytes(
            PayloadSentinel + new string('x', SegmentSizeBytes - PayloadSentinel.Length) + PayloadSentinel);
        Assert.Equal(SegmentSizeBytes + PayloadSentinel.Length, payload.Length);
        using var upload = new MemoryStream(payload);
        var stage = await store.PutAsync(tenant, upload, "text/plain", null);
        var reference = await store.CommitReferenceAsync(
            tenant, stage, new ContentOwner("privacy-record", "privacy-element"),
            "FILENAME_PRIVACY_SENTINEL_79E5A2C6.txt");
        var context = new ContentReadContext("trace-fixed", "operation-fixed", "tenant-opaque", "principal-fixed");

        var delivery = await store.ReadAsync(tenant, reference, true, context: context);
        Assert.Equal(200, delivery.StatusCode);
        Assert.Equal("attachment; filename=\"FILENAME_PRIVACY_SENTINEL_79E5A2C6.txt\"", delivery.ContentDisposition);
        using (delivery.Body)
        using (var clear = new MemoryStream())
        {
            await delivery.Body.CopyToAsync(clear);
            Assert.Equal(payload, clear.ToArray());
            Assert.Contains("PAYLOAD_PRIVACY_SENTINEL_53B1C4D8", Encoding.UTF8.GetString(clear.ToArray()));
        }

        await store.CorruptCiphertextAsync(tenant, reference.ContentId, 1);
        using var damagedBody = (await store.ReadAsync(tenant, reference, true, context: context)).Body;
        using var discarded = new MemoryStream();
        var failure = await Assert.ThrowsAsync<ContentIntegrityException>(() => damagedBody.CopyToAsync(discarded));
        Assert.Equal(1, failure.SegmentIndex);

        var integrity = Assert.Single(telemetry.Activities, activity => activity.OperationName == "content.integrity");
        Assert.Equal(ActivityStatusCode.Error, integrity.Status);
        Assert.Equal("content.integrity", integrity.GetTagItem("error.type"));
        var measurement = Assert.Single(telemetry.Measurements, item => item.Name == "content.integrity.failures");
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

        // The filename check is a regression guard: the display name is not reachable at the
        // integrity emission site today.
        AssertNoSentinels(telemetry.Activities.SelectMany(ActivityText));
        AssertNoSentinels(telemetry.Measurements.SelectMany(MeasurementText));
        AssertNoSentinels(log.Events.Select(integrityEvent => JsonSerializer.Serialize(integrityEvent)));
        AssertNoSentinels([failure.Message, failure.ToString(), failure.InnerException?.ToString() ?? string.Empty]);
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

    [Fact]
    public async Task A_deferred_byte_delete_warns_operators_by_exception_type_and_names_no_item()
    {
        using var writer = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(writer);
        System.Diagnostics.Trace.Listeners.Add(listener);
        try
        {
            var tenant = new TenantId("deferred-delete-tenant");
            var clock = new SteppingClock();
            var backing = new RecordingStorage();
            var store = new InMemoryContentStore(new(1000, 1000, 1000, TimeSpan.Zero + TimeSpan.FromMinutes(1)), new TestKeys(), clock, backing);
            var stage = await store.PutAsync(tenant, new MemoryStream("hello"u8.ToArray()), "text/plain", null);
            var reference = await store.CommitReferenceAsync(tenant, stage, new("record", "0"), "a.txt");
            backing.ThrowOnDelete = new IOException("SECRET_PATH_SENTINEL");

            Assert.True(await store.RemoveReferenceAsync(tenant, reference, HoldStatus.None, "dispose"));
            clock.Advance(TimeSpan.FromMinutes(1));
            await store.SweepExpiredAsync("sweep");

            listener.Flush();
            var text = writer.ToString();
            Assert.Contains("Deferring a content byte delete to the staging sweep: IOException", text, StringComparison.Ordinal);
            Assert.Contains("Deferring an orphan byte delete to the next staging sweep: IOException", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET_PATH_SENTINEL", text, StringComparison.Ordinal);
            Assert.DoesNotContain(stage.ToString(), text, StringComparison.Ordinal);
        }
        finally
        {
            System.Diagnostics.Trace.Listeners.Remove(listener);
        }
    }

    [Fact]
    public async Task A_read_failure_marks_the_ambient_span_when_no_one_listens_to_the_content_store_source()
    {
        using var ambientSource = new ActivitySource("ambient-host-source");
        using var ambientListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "ambient-host-source",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(ambientListener);
        var tenant = new TenantId("ambient-tenant");
        var store = new InMemoryContentStore(new(300_000, 600_000, 600_000, TimeSpan.FromMinutes(1)), new TestKeys(), new TestClock(), new InMemoryContentStoreStorage());
        var payload = new byte[65_536 + 5];
        Array.Fill(payload, (byte)'a');
        var reference = await store.CommitReferenceAsync(tenant, await store.PutAsync(tenant, new MemoryStream(payload), "text/plain", null), new("record", "0"), "a.txt");
        await store.CorruptCiphertextAsync(tenant, reference.ContentId, 1);
        using var ambient = ambientSource.StartActivity("host-request")!;

        await Assert.ThrowsAsync<ContentIntegrityException>(async () => await store.ReadAsync(tenant, reference, true, new ContentByteRange(65_536, 65_538)));

        Assert.Equal(ActivityStatusCode.Error, ambient.Status);
        Assert.Equal("content.integrity", ambient.GetTagItem("error.type"));
    }

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

    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
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
