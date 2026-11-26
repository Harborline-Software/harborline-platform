using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

[CollectionDefinition("Definition refusal telemetry", DisableParallelization = true)]
public sealed class DefinitionRefusalTelemetryCollection;

[Collection("Definition refusal telemetry")]
public sealed class DefinitionRefusalTelemetryTests
{
    [Fact]
    public void PublicationFaultPreservesTheRefusalAndLaterRecordingRecovers()
    {
        // A fresh emitter is required even if another test already published the default context's counter.
        var context = new RefusalTelemetryLoadContext();
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(DefinitionRefusalTelemetryTests).Assembly.Location);
            assembly.GetType(typeof(DefinitionRefusalTelemetryTests).FullName!)!
                .GetMethod(nameof(RunPublicationFailureScenario), BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, null);
        }
        finally
        {
            context.Unload();
        }
    }

    private static void RunPublicationFailureScenario()
    {
        var original = new DefinitionRefusalException(DefinitionAdmissionPhase.Author,
            [new("fixture.publication", "/body", "secret-sentinel")]);
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (_, _) => throw original
        });
        var document = new DefinitionDocument(new("opaque-a", DefinitionKind.Rules, "definition-a"), "v1", "1.0.0", "{}");
        var publications = 0;
        using (var listener = new MeterListener())
        {
            listener.Start();
            listener.InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name != "Harborline.Definitions" || instrument.Name != "definition.refusals") return;
                publications++;
                throw new InvalidOperationException("publication fault");
            };

            var error = Assert.Throws<DefinitionRefusalException>(() => { _ = store.SaveDraftAsync(document, 0, "first"); });

            Assert.Same(original, error);
            Assert.Equal("definition.refused", error.Message);
            Assert.Equal(DefinitionAdmissionPhase.Author, error.Stage);
            Assert.Equal(new DefinitionRefusal[] { new("fixture.publication", "/body", "secret-sentinel") }, error.Refusals);
            Assert.Equal(1, publications);
        }

        using var capture = new Capture();
        var later = Assert.Throws<DefinitionRefusalException>(() => { _ = store.SaveDraftAsync(document, 0, "later"); });

        Assert.Same(original, later);
        var measurement = Assert.Single(capture.Measurements);
        Assert.Equal(1, measurement.Count);
        Assert.Equal(new KeyValuePair<string, object?>[]
        {
            new("tenant_id", "opaque-a"), new("stage", "Author"), new("definition_id", "definition-a"),
            new("code", "fixture.publication")
        }, measurement.Tags);
    }

    private sealed class RefusalTelemetryLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
            => name.Name is "Harborline.Foundation.Definitions" or "Harborline.Blocks.BuilderDefinitions"
                ? LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, name.Name + ".dll"))
                : null;
    }

    [Fact]
    public async Task UnkeyedRefusalsStayUnchangedAndEmitNothing()
    {
        using var capture = new Capture();
        var construction = Assert.Throws<DefinitionRefusalException>(() =>
            new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            {
                [(DefinitionKind)999] = (_, _) => []
            }));
        AssertRefusal(construction, DefinitionAdmissionPhase.Author, "definition.registry_unknown", "/registry");
        Assert.Empty(capture.Measurements);
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (_, _) => []
        });
        var listing = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.ListKeysAsync("opaque-a", (DefinitionKind)999));
        AssertRefusal(listing, DefinitionAdmissionPhase.Author, "definition.registry_unknown", "/registry");
        Assert.Empty(capture.Measurements);
        foreach (var blank in new string?[] { null, "", "  " })
        {
            var noId = new DefinitionDocument(new("opaque-a", DefinitionKind.Rules, blank!), "v1", "1.0.0", "{}");
            var noTenant = noId with { Key = new(blank!, DefinitionKind.Rules, "definition-a") };
            AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
                await store.SaveDraftAsync(noId, 0, "draft")), DefinitionAdmissionPhase.Author, "definition.id_required", "/definitionId");
            AssertRefusal(await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
                await store.SaveDraftAsync(noTenant, 0, "draft")), DefinitionAdmissionPhase.Author, "definition.tenant_required", "/tenant");
            Assert.Empty(capture.Measurements);
        }
    }

    [Theory]
    [InlineData("author-invalid", "Author", "definition.version_invalid", "/version", 1)]
    [InlineData("author-replay", "Author", "definition.replay_conflict", "/requestId", 1)]
    [InlineData("publish-invalid", "Publish", "definition.version_id_required", "/versionId", 1)]
    [InlineData("publish-empty-conditions", "Publish", "definition.version_id_required", "/versionId", 1)]
    [InlineData("publish-conditions", "Publish", "definition.published_head_conflict", "/dependency", 2)]
    [InlineData("replay", "Publish", "definition.replay_conflict", "/requestId", 1)]
    [InlineData("restore", "Author", "definition.version_id_required", "/sourceVersionId", 1)]
    [InlineData("history", "Author", "definition.registry_unknown", "/registry", 1)]
    [InlineData("head", "Author", "definition.registry_unknown", "/registry", 1)]
    [InlineData("resolve", "Publish", "definition.version_id_required", "/versionId", 1)]
    [InlineData("dependencies", "Author", "definition.dependencies_unsupported", "/dependencies", 1)]
    public async Task StoreRefusalBoundariesCountExactlyOnce(string operation, string stage, string code, string pointer, int count)
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (_, _) => []
        });
        var key = new DefinitionKey("opaque-a", DefinitionKind.Rules, "definition-a");
        var document = new DefinitionDocument(key, "v1", "1.0.0", "{}");
        await store.SaveDraftAsync(document, 0, "draft");
        await store.PublishAsync(key, "v1", 1, "publish");
        using var capture = new Capture();

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
        {
            switch (operation)
            {
                case "author-invalid": await store.SaveDraftAsync(document with { VersionId = "v2", Version = "bad" }, 2, "new"); break;
                case "author-replay": await store.SaveDraftAsync(document with { BodyJson = "{\"changed\":true}" }, 0, "draft"); break;
                case "publish-invalid": await store.PublishAsync(key, "", 2, "new"); break;
                case "publish-empty-conditions": await store.PublishAsync(key, "", 2, "new", []); break;
                case "publish-conditions": await store.PublishAsync(key, "v1", 2, "new",
                    [new(key with { DefinitionId = "missing-one" }, 1, "/dependency"), new(key with { DefinitionId = "missing-two" }, 1, "/dependency")]); break;
                case "replay": await store.GetPublicationReplayAsync(key, "v2", 1, "publish"); break;
                case "restore": await store.RestoreAsDraftAsync(key, "", "v2", "2.0.0", 2, "new"); break;
                case "history": await store.ListHistoryAsync(key with { Kind = (DefinitionKind)999 }); break;
                case "head": await store.GetPublishedHeadAsync(key with { Kind = (DefinitionKind)999 }); break;
                case "resolve": await store.ResolvePublishedAsync(new(key, "")); break;
                case "dependencies": await store.ReadDependenciesAsync("opaque-a", "Rules", "definition-a"); break;
                default: throw new InvalidOperationException(operation);
            }
        });

        Assert.Equal(stage, error.Stage.ToString());
        Assert.Equal("definition.refused", error.Message);
        Assert.Equal(count, error.Refusals.Count);
        Assert.All(error.Refusals, refusal => Assert.Equal(new(code, pointer), refusal));
        Assert.Equal(count, capture.Measurements.Count);
        Assert.All(capture.Measurements, measurement =>
        {
            Assert.Equal(1, measurement.Count);
            Assert.Equal(new KeyValuePair<string, object?>[]
            {
                new("tenant_id", "opaque-a"), new("stage", stage), new("definition_id", "definition-a"), new("code", code)
            }, measurement.Tags);
        });
    }

    [Fact]
    public async Task ListenerFaultPreservesTheExactMemberException()
    {
        var original = new DefinitionRefusalException(DefinitionAdmissionPhase.Author,
            [new("fixture.first", "/one", "secret-sentinel"), new("fixture.second", "/two")]);
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (_, _) => throw original
        });
        using var capture = new Capture(throwOnMeasurement: true);

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await store.SaveDraftAsync(new(new("opaque-a", DefinitionKind.Rules, "definition-a"), "v1", "1.0.0", "{}"), 0, "draft"));

        Assert.Same(original, error);
        Assert.Equal(DefinitionAdmissionPhase.Author, error.Stage);
        Assert.Equal(new DefinitionRefusal[] { new("fixture.first", "/one", "secret-sentinel"), new("fixture.second", "/two") }, error.Refusals);
        Assert.Equal(2, capture.Measurements.Count);
    }

    private static void AssertRefusal(DefinitionRefusalException error, DefinitionAdmissionPhase stage, string code, string pointer)
    {
        Assert.Equal("definition.refused", error.Message);
        Assert.Equal(stage, error.Stage);
        Assert.Equal(new DefinitionRefusal[] { new(code, pointer) }, error.Refusals);
    }

    [Theory]
    [InlineData(false, "Author")]
    [InlineData(true, "Publish")]
    public async Task MemberRefusalsAreCountedPerTenantWithoutDocumentContent(bool publish, string stage)
    {
        using var capture = new Capture();
        var key = new DefinitionKey("opaque-a", DefinitionKind.Rules, "definition-a");
        var document = new DefinitionDocument(key, "v1", "1.0.0",
            "{\"body\":\"secret-sentinel\",\"displayName\":\"secret-sentinel\"}");
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (_, phase) => publish && phase == DefinitionAdmissionPhase.Author
                ? [] : [new("fixture.first", "/secret-sentinel", "secret-sentinel"), new("fixture.second", "/body")]
        });
        if (publish) await store.SaveDraftAsync(document, 0, "draft");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
        {
            if (publish) await store.PublishAsync(key, "v1", 1, "publish");
            else await store.SaveDraftAsync(document, 0, "draft");
        });

        Assert.Equal(publish ? DefinitionAdmissionPhase.Publish : DefinitionAdmissionPhase.Author, error.Stage);
        Assert.Equal("definition.refused", error.Message);
        Assert.Equal(new DefinitionRefusal[]
        {
            new("fixture.first", "/secret-sentinel", "secret-sentinel"), new("fixture.second", "/body")
        }, error.Refusals);
        Assert.Equal(2, capture.Measurements.Count);
        for (var i = 0; i < capture.Measurements.Count; i++)
        {
            Assert.Equal(1, capture.Measurements[i].Count);
            Assert.Equal(new KeyValuePair<string, object?>[]
            {
                new("tenant_id", "opaque-a"), new("stage", stage), new("definition_id", "definition-a"),
                new("code", i == 0 ? "fixture.first" : "fixture.second")
            }, capture.Measurements[i].Tags);
        }
        Assert.DoesNotContain(capture.Measurements, measurement => measurement.Tags.Any(tag => Equals(tag.Value, "opaque-b")));
        Assert.DoesNotContain(capture.Measurements, measurement => measurement.Tags.Any(tag => tag.Value?.ToString()?.Contains("secret-sentinel", StringComparison.Ordinal) == true));
    }

    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(long Count, KeyValuePair<string, object?>[] Tags)> Measurements { get; } = [];
        public Capture(bool throwOnMeasurement = false)
        {
            _listener.InstrumentPublished = (instrument, owner) =>
            {
                if (instrument.Meter.Name == "Harborline.Definitions" && instrument.Name == "definition.refusals")
                    owner.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, count, tags, _) =>
            {
                Measurements.Add((count, tags.ToArray()));
                if (throwOnMeasurement) throw new InvalidOperationException("listener fault");
            });
            _listener.Start();
        }
        public void Dispose() => _listener.Dispose();
    }
}
