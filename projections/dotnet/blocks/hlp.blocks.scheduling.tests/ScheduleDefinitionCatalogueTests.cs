using Harborline.Blocks.BuilderDefinitions;
using Harborline.Blocks.Scheduling.Definitions;

namespace Harborline.Blocks.Scheduling.Tests;

/// <summary>
/// T-491 S1, DES-0022 ruling 8: Scheduling definitions carry semantic-version heads on T-620's shared
/// versioned-definition store (scheduling-ck-20), restore moves the head (scheduling-ck-21), and the
/// engine resolves an exact pin or refuses (scheduling-eng-1). L491's integer revisions are retired.
/// </summary>
public sealed class ScheduleDefinitionCatalogueTests
{
    private const string Tenant = "tenant-a";
    private const string Id = "inspection.quarterly";
    private const string Body = "{\"name\":\"Quarterly inspection\"}";

    private readonly InMemoryVersionedDefinitionStore _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
    });

    private ScheduleDefinitionCatalogue Catalogue => new(_store);

    private static DefinitionKey Key => new(Tenant, DefinitionKind.Schedules, Id);

    [Fact(DisplayName = "scheduling-ck-20: a schedule binds to the shared store's Schedules registry, pinned by id and semantic version")]
    public async Task ScheduleBindsToTheSharedStoreSchedulesRegistry()
    {
        await PublishAsync("1.0.0", 0);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        Assert.Equal(new ScheduleDefinitionPin(Tenant, Id, "inspection.quarterly@1.0.0"), pin);
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal(Key, resolved.Document.Key);
        Assert.Equal("1.0.0", resolved.Document.Version);
        Assert.Equal(DefinitionStatus.Published, resolved.Status);
        Assert.Same(resolved, await _store.ResolvePublishedAsync(new(Key, pin.VersionId!)));
    }

    [Fact(DisplayName = "scheduling-ck-20: the head is the highest semantic version, not the latest publication")]
    public async Task HeadIsTheHighestSemanticVersion()
    {
        await PublishAsync("1.10.0", 0);
        await PublishAsync("1.9.0", 2);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        Assert.Equal("inspection.quarterly@1.10.0", pin.VersionId);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pinned version resolves its own body after the head advances")]
    public async Task PinnedVersionResolvesAfterTheHeadAdvances()
    {
        await PublishAsync("1.0.0", 0);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        await PublishAsync("2.0.0", 2, "{\"name\":\"Renamed\"}");
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal("1.0.0", resolved.Document.Version);
        Assert.Equal(Body, resolved.Document.BodyJson);
        Assert.Equal("inspection.quarterly@2.0.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);
    }

    [Theory(DisplayName = "scheduling-eng-1: an unpinned schedule refuses rather than floating to the head")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task UnpinnedResolutionRefuses(string? versionId)
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, versionId)).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.Unpinned, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pin to a version that was never published refuses")]
    public async Task MissingVersionRefuses()
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@9.9.9")).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NotFound, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: a pin to a draft refuses; drafts never resolve")]
    public async Task DraftPinRefuses()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Body, 0, "draft");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0")).AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NotFound, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: pinning a schedule with no published head refuses")]
    public async Task PinningWithoutAPublishedHeadRefuses()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Body, 0, "draft");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.PinHeadAsync(Tenant, Id).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NoPublishedHead, "/definitionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: a published version is immutable and its bytes survive an attempted edit")]
    public async Task PublishedVersionIsImmutable()
    {
        await PublishAsync("1.0.0", 0);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", "{\"name\":\"Edited\"}", 2, "edit").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_immutable", "/versionId")], refusal.Refusals);
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        var resolved = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal(Body, resolved.Document.BodyJson);
    }

    [Fact(DisplayName = "scheduling-ck-21: restore registers a new draft, publishing it moves the head, and history is untouched")]
    public async Task RestoreMovesTheHeadThroughANewDraft()
    {
        await PublishAsync("1.0.0", 0);
        var original = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        var restored = await Catalogue.RestoreAsDraftAsync(Tenant, Id, "1.0.0", "1.1.0", 2, "restore");
        Assert.Equal(DefinitionStatus.Draft, restored.Status);
        Assert.Equal("inspection.quarterly@1.0.0", restored.RestoredFromVersionId);
        Assert.Equal("inspection.quarterly@1.1.0", restored.Document.VersionId);
        Assert.Equal("inspection.quarterly@1.0.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);

        await Catalogue.PublishAsync(Tenant, Id, "1.1.0", 3, "publish-restored");
        Assert.Equal("inspection.quarterly@1.1.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);
        var after = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal(original.Document, after.Document);
        Assert.Equal(original.Digest, after.Digest);
    }

    [Theory(DisplayName = "DES-0022 ruling 8: an integer revision is not a version and refuses before anything is written")]
    [InlineData("3")]
    [InlineData("3.0")]
    [InlineData("v1.0.0")]
    public async Task IntegerRevisionVersionRefuses(string version)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, version, Body, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_invalid", "/version")], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory(DisplayName = "DES-0022 ruling 8: a body that still carries an integer revision refuses where it is read")]
    [InlineData("{\"revision\":3}", "/revision")]
    [InlineData("{\"revision\":\"3\"}", "/revision")]
    [InlineData("{\"version\":3}", "/version")]
    [InlineData("{\"envelope\":{\"revision\":3}}", "/envelope/revision")]
    [InlineData("{\"envelope\":{\"version\":3}}", "/envelope/version")]
    public async Task BodyIntegerRevisionRefuses(string body, string pointer)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", body, 0, "draft").AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Author, refusal.Stage);
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.IntegerRevisionRetired, pointer)], refusal.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory(DisplayName = "scheduling-ck-20: a body version that disagrees with the store's semantic version refuses")]
    [InlineData("{\"version\":\"2.0.0\"}", "/version")]
    [InlineData("{\"envelope\":{\"version\":\"1.0.1\"}}", "/envelope/version")]
    public async Task BodyVersionMismatchRefuses(string body, string pointer)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", body, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionMismatch, pointer)], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: a body version equal to the store's semantic version is admitted")]
    public async Task MatchingBodyVersionIsAdmitted()
    {
        var saved = await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            "{\"version\":\"1.0.0\",\"envelope\":{\"version\":\"1.0.0\"}}", 0, "draft");
        Assert.Equal(DefinitionStatus.Draft, saved.Status);
        Assert.Equal(1, saved.Revision);
    }

    [Theory(DisplayName = "scheduling-ck-20: a body that is not a single JSON object refuses")]
    [InlineData("[]")]
    [InlineData("3")]
    [InlineData("\"schedule\"")]
    [InlineData("null")]
    [InlineData("{\"revision\":1,\"revision\":2}")]
    public async Task NonObjectBodyRefuses(string body)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", body, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")], refusal.Refusals);
    }

    [Theory(DisplayName = "scheduling-ck-20: a version id not derived from the semantic version refuses, so a bare revision cannot pose as one")]
    [InlineData("3")]
    [InlineData("inspection.quarterly@2.0.0")]
    [InlineData("other@1.0.0")]
    public async Task VersionIdNotDerivedFromTheSemanticVersionRefuses(string versionId)
    {
        var document = new DefinitionDocument(Key, versionId, "1.0.0", Body);
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => _store.SaveDraftAsync(document, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionIdMismatch, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: admission runs again at publish against the same snapshot")]
    public async Task AdmissionRunsAtPublish()
    {
        var phases = new List<DefinitionAdmissionPhase>();
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Schedules] = (document, phase) =>
            {
                phases.Add(phase);
                return ScheduleDefinitionCatalogue.Admission(document, phase);
            },
        });
        var catalogue = new ScheduleDefinitionCatalogue(store);
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Body, 0, "draft");
        await catalogue.PublishAsync(Tenant, Id, "1.0.0", 1, "publish");
        Assert.Equal([DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish], phases);
    }

    [Fact(DisplayName = "the version id is the definition id and semantic version")]
    public void VersionIdIsDerived()
        => Assert.Equal("inspection.quarterly@1.2.3-rc.1", ScheduleDefinitionCatalogue.VersionIdFor(Id, "1.2.3-rc.1"));

    [Fact(DisplayName = "the catalogue requires a store")]
    public void CatalogueRequiresAStore()
        => Assert.Throws<ArgumentNullException>(() => new ScheduleDefinitionCatalogue(null!));

    [Fact(DisplayName = "resolution requires a pin")]
    public async Task ResolutionRequiresAPin()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => Catalogue.ResolveAsync(null!).AsTask());

    private async Task PublishAsync(string version, long expectedRevision, string body = Body)
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, version, body, expectedRevision, "draft-" + version);
        await Catalogue.PublishAsync(Tenant, Id, version, expectedRevision + 1, "publish-" + version);
    }
}
