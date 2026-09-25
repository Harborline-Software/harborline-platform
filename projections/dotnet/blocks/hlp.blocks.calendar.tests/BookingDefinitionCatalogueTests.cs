using System.Text;
using System.Text.Json.Nodes;

using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Calendar.Booking;

using Xunit;

namespace Harborline.Blocks.Calendar.Tests;

/// <summary>T-605: Booking definitions on the shared versioned-definition store, and their pack transport.</summary>
public sealed class BookingDefinitionCatalogueTests
{
    private readonly HashSet<string> _resources = ["instructor", "room"];
    private readonly InMemoryVersionedDefinitionStore _store;

    public BookingDefinitionCatalogueTests()
    {
        var admission = BookingDefinitionAdmission.For(Fixtures.Context() with { IsAdmittedResource = _resources.Contains });
        _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Resources] = admission,
            [DefinitionKind.Bookables] = admission,
        });
    }

    [Theory(DisplayName = "booking-ck-18: the published head is the highest semantic version, not the latest save")]
    [InlineData(DefinitionKind.Resources)]
    [InlineData(DefinitionKind.Bookables)]
    public async Task PublishedHeadIsReadBySemanticVersion(DefinitionKind kind)
    {
        await PublishAsync(kind, "1.10.0", 0);
        await PublishAsync(kind, "1.9.0", 2);
        var head = await _store.GetPublishedHeadAsync(Key(kind));
        Assert.Equal("1.10.0", head!.Document.Version);
        Assert.Equal("x@1.10.0", head.Document.VersionId);
    }

    [Theory(DisplayName = "booking-ck-20: published definitions are immutable and history only appends")]
    [InlineData(DefinitionKind.Resources)]
    [InlineData(DefinitionKind.Bookables)]
    public async Task PublishedVersionsAreImmutable(DefinitionKind kind)
    {
        var published = await PublishAsync(kind, "1.0.0", 0);
        var changed = Fixtures.Document(kind, Body(kind, body => body["name"] = "Renamed"));
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => _store.SaveDraftAsync(changed, 2, "edit").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_immutable", "/versionId")], refusal.Refusals);
        var history = await _store.ListHistoryAsync(Key(kind));
        Assert.Equal([DefinitionStatus.Draft, DefinitionStatus.Published], history.Select(item => item.Status));
        var pinned = await _store.ResolvePublishedAsync(new(Key(kind), "x@1.0.0"));
        Assert.Equal(published.Document.BodyJson, pinned!.Document.BodyJson);
    }

    [Theory(DisplayName = "booking-ck-19: restoring a published version registers one new draft and publishes nothing")]
    [InlineData(DefinitionKind.Resources)]
    [InlineData(DefinitionKind.Bookables)]
    public async Task RestoreRegistersOneDraft(DefinitionKind kind)
    {
        await PublishAsync(kind, "1.0.0", 0);
        var restored = await _store.RestoreAsDraftAsync(Key(kind), "x@1.0.0", "x@1.1.0", "1.1.0", 2, "restore");
        Assert.Equal(DefinitionStatus.Draft, restored.Status);
        Assert.Equal("x@1.0.0", restored.RestoredFromVersionId);
        var history = await _store.ListHistoryAsync(Key(kind));
        Assert.Equal(3, history.Count);
        Assert.Single(history, item => item.Status == DefinitionStatus.Published);
        Assert.Equal("1.0.0", (await _store.GetPublishedHeadAsync(Key(kind)))!.Document.Version);
        Assert.Null(await _store.ResolvePublishedAsync(new(Key(kind), "x@1.1.0")));
    }

    [Fact(DisplayName = "booking-eng-23: structural admission runs on the write path at validate and again at publish; a refusal writes nothing")]
    public async Task AdmissionRunsAtValidateAndPublish()
    {
        var invalid = Fixtures.Document(DefinitionKind.Resources, Fixtures.Resource(body => body["capacity_kind"] = "pool"));
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => _store.SaveDraftAsync(invalid, 0, "save").AsTask());
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.PoolSizeInvalid, "/pool_size")], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key(DefinitionKind.Resources)));

        var bookable = Fixtures.Document(DefinitionKind.Bookables, Body(DefinitionKind.Bookables));
        await _store.SaveDraftAsync(bookable, 0, "save");
        _resources.Remove("room");
        refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            _store.PublishAsync(Key(DefinitionKind.Bookables), bookable.VersionId, 1, "publish").AsTask());
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.RequiredResourceUnknown, "/requires/1")], refusal.Refusals);
        Assert.Null(await _store.GetPublishedHeadAsync(Key(DefinitionKind.Bookables)));
        Assert.Single(await _store.ListHistoryAsync(Key(DefinitionKind.Bookables)));
    }

    [Theory(DisplayName = "booking-auth-17: an Allocation or a hold authored as a definition refuses in either namespace")]
    [InlineData(DefinitionKind.Resources, "allocation")]
    [InlineData(DefinitionKind.Bookables, "allocation")]
    [InlineData(DefinitionKind.Bookables, "hold")]
    public async Task AllocationIsNotADefinition(DefinitionKind kind, string runtimeKind)
    {
        var body = Allocation(runtimeKind);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            _store.SaveDraftAsync(Fixtures.Document(kind, body), 0, "save").AsTask());
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataNotADefinition, "/kind")], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key(kind)));
    }

    [Fact(DisplayName = "booking-ck-2,9: both definition kinds export from their published versions as provider-neutral content; a draft does not")]
    public async Task BothKindsExportFromPublishedVersions()
    {
        var resource = await PublishAsync(DefinitionKind.Resources, "1.0.0", 0);
        var bookable = await PublishAsync(DefinitionKind.Bookables, "1.0.0", 0);
        var entries = new[] { resource, bookable }.Select(BookingDefinitionPackage.Export).ToArray();
        Assert.Equal([BookingPackIdentity.ResourceContentKind, BookingPackIdentity.BookableContentKind], entries.Select(entry => entry.ContentKind));
        Assert.All(entries, entry => Assert.Equal(BookingPackIdentity.Primitive, entry.Primitive));
        Assert.True(JsonNode.DeepEquals(Fixtures.Packed((JsonObject)JsonNode.Parse(resource.Document.BodyJson)!, "x"),
            JsonNode.Parse(entries[0].Content.Payload.Span)));
        Assert.Empty(BookingDefinitionPackage.Admit(entries, Fixtures.Context()));

        var draft = await _store.SaveDraftAsync(Fixtures.Document(DefinitionKind.Resources, Body(DefinitionKind.Resources), version: "2.0.0"), 2, "draft");
        var refusal = Assert.Throws<DefinitionRefusalException>(() => BookingDefinitionPackage.Export(draft));
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.PublishedVersionRequired, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "booking-auth-18: a hold or an allocation placed in a pack refuses at export and at install")]
    public async Task RuntimeDataNeverTravels()
    {
        var published = await PublishAsync(DefinitionKind.Resources, "1.0.0", 0);
        // A revision that bypassed admission, as a hostile or broken store could hand the exporter.
        var smuggled = published with { Document = published.Document with { BodyJson = Allocation("hold").ToJsonString() } };
        var refusal = Assert.Throws<DefinitionRefusalException>(() => BookingDefinitionPackage.Export(smuggled));
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataInPack, "/kind")], refusal.Refusals);

        var entries = new[]
        {
            BookingDefinitionPackage.Export(published),
            new BookingPackEntry(BookingPackIdentity.BookableContentKind, "a1", "1.0.0",
                PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(Allocation("allocation").ToJsonString()))),
            new BookingPackEntry(BookingPackIdentity.ResourceContentKind, "h1", "1.0.0",
                PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(Allocation("hold").ToJsonString()))),
        };
        Assert.Equal([
            new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataInPack, "/entries/1"),
            new DefinitionRefusal(BookingDefinitionCodes.RuntimeDataInPack, "/entries/2"),
        ], BookingDefinitionPackage.Admit(entries, Fixtures.Context("none")));
    }

    [Fact(DisplayName = "booking-auth-11 install: a Bookable with three unresolved required resources reports three, and the pack's own Resources resolve")]
    public void InstallReportsEveryUnresolvedResource()
    {
        var bookable = Fixtures.Bookable(body => body["requires"] = new JsonArray("nurse", "pump", "line", "room"));
        var room = Fixtures.Resource();
        var entries = new[]
        {
            Entry(BookingPackIdentity.ResourceContentKind, "room", room),
            Entry(BookingPackIdentity.BookableContentKind, "procedure", bookable),
        };
        Assert.Equal([
            new DefinitionRefusal(BookingDefinitionCodes.RequiredResourceUnknown, "/entries/1/requires/0"),
            new DefinitionRefusal(BookingDefinitionCodes.RequiredResourceUnknown, "/entries/1/requires/1"),
            new DefinitionRefusal(BookingDefinitionCodes.RequiredResourceUnknown, "/entries/1/requires/2"),
        ], BookingDefinitionPackage.Admit(entries, Fixtures.Context("none")));
        Assert.Empty(BookingDefinitionPackage.Admit(entries, Fixtures.Context("nurse", "pump", "line")));
        Assert.Equal([new DefinitionRefusal(BookingDefinitionCodes.PackContentUnsupported, "/entries/0")],
            BookingDefinitionPackage.Admit([Entry(LayoutPackIdentity.ContentKind, "room", room)], Fixtures.Context()));
    }

    private static BookingPackEntry Entry(int contentKind, string id, JsonObject body)
        => new(contentKind, id, "1.0.0",
            PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(Fixtures.Packed(body, id).ToJsonString())));

    private async Task<DefinitionRevision> PublishAsync(DefinitionKind kind, string version, long revision)
    {
        var document = Fixtures.Document(kind, Body(kind), version: version);
        await _store.SaveDraftAsync(document, revision, "save " + version);
        return await _store.PublishAsync(document.Key, document.VersionId, revision + 1, "publish " + version);
    }

    private static JsonObject Body(DefinitionKind kind, Action<JsonObject>? edit = null)
        => kind == DefinitionKind.Resources ? Fixtures.Resource(edit) : Fixtures.Bookable(edit);

    private static DefinitionKey Key(DefinitionKind kind) => new(Fixtures.Tenant, kind, "x");

    private static JsonObject Allocation(string kind) => new()
    {
        ["kind"] = kind,
        ["envelope"] = Fixtures.Envelope(),
        ["bookable_id"] = "induction",
        ["subject_id"] = "learner.7",
        ["window"] = new JsonObject { ["start"] = "2026-10-01T09:00:00Z", ["end"] = "2026-10-01T10:30:00Z" },
        ["state"] = "held",
        ["held_resources"] = new JsonArray("instructor", "room"),
    };
}
