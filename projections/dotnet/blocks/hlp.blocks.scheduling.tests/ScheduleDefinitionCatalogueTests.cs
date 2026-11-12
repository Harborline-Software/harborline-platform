using Harborline.Blocks.BuilderDefinitions;
using System.Text.Json;

using Harborline.Blocks.Scheduling.Definitions;
using Harborline.Foundation.Definitions;

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
    private static readonly ScheduleDefinition Definition = Schedule("Quarterly inspection");

    private readonly InMemoryVersionedDefinitionStore _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
    {
        [DefinitionKind.Schedules] = ScheduleDefinitionCatalogue.Admission,
    });

    private ScheduleDefinitionCatalogue Catalogue => new(_store);

    private static DefinitionKey Key => new(Tenant, DefinitionKind.Schedules, Id);

    [Fact(DisplayName = "scheduling-auth-18: an unadmitted module refuses at authoring")]
    public async Task UnadmittedModuleRefusesAtAuthoring()
    {
        var definition = Definition with
        {
            ProblemClass = ScheduleProblemClass.Workforce,
            Modules = [new("unknown-module")],
        };
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", definition, 0, "draft").AsTask());
        Assert.Contains(refusal.Refusals, item => item.Code.Contains("unsupported", StringComparison.Ordinal)
            && item.Pointer == "/modules/0/module");
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact(DisplayName = "scheduling-ck-20: a schedule binds to the shared store's Schedules registry, pinned by id and semantic version")]
    public async Task ScheduleBindsToTheSharedStoreSchedulesRegistry()
    {
        await PublishAsync("1.0.0", 0);
        var pin = await Catalogue.PinHeadAsync(Tenant, Id);
        Assert.Equal(new ScheduleDefinitionPin(Tenant, Id, "inspection.quarterly@1.0.0"), pin);
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal(Key, resolved.Revision.Document.Key);
        Assert.Equal("1.0.0", resolved.Revision.Document.Version);
        Assert.Equal(DefinitionStatus.Published, resolved.Revision.Status);
        Assert.Same(resolved.Revision, await _store.ResolvePublishedAsync(new(Key, pin.VersionId!)));
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
        await PublishAsync("2.0.0", 2, Schedule("Renamed"));
        var resolved = await Catalogue.ResolveAsync(pin);
        Assert.Equal("1.0.0", resolved.Revision.Document.Version);
        Assert.Equal("Quarterly inspection", resolved.Definition.Name);
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
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0")).AsTask());
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.NotFound, "/versionId")], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-eng-1: pinning a schedule with no published head refuses")]
    public async Task PinningWithoutAPublishedHeadRefuses()
    {
        await Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
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
            () => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Schedule("Edited"), 2, "edit").AsTask());
        Assert.Equal([new DefinitionRefusal("definition.version_immutable", "/versionId")], refusal.Refusals);
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        var resolved = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal("Quarterly inspection", resolved.Definition.Name);
    }

    [Fact(DisplayName = "scheduling-ck-21: restore registers a new draft, publishing it moves the head, and history is untouched")]
    public async Task RestoreMovesTheHeadThroughANewDraft()
    {
        await PublishAsync("1.0.0", 0);
        var original = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        var restored = await Catalogue.RestoreAsDraftAsync(Tenant, Id, "1.0.0", "1.1.0", 2, "restore");
        Assert.Equal(DefinitionStatus.Draft, restored.Revision.Status);
        Assert.Equal("inspection.quarterly@1.0.0", restored.Revision.RestoredFromVersionId);
        Assert.Equal("inspection.quarterly@1.1.0", restored.Revision.Document.VersionId);
        Assert.Equal("1.1.0", restored.Definition.Envelope!.Version);
        Assert.Equal("inspection.quarterly@1.0.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);

        await Catalogue.PublishAsync(Tenant, Id, "1.1.0", 3, "publish-restored");
        Assert.Equal("inspection.quarterly@1.1.0", (await Catalogue.PinHeadAsync(Tenant, Id)).VersionId);
        var after = await Catalogue.ResolveAsync(new(Tenant, Id, "inspection.quarterly@1.0.0"));
        Assert.Equal(original.Revision.Document, after.Revision.Document);
        Assert.Equal(original.Revision.Digest, after.Revision.Digest);
        Assert.Equal("1.0.0", after.Definition.Envelope!.Version);
    }

    [Theory(DisplayName = "DES-0022 ruling 8: an integer revision is not a version and refuses before anything is written")]
    [InlineData("3")]
    [InlineData("3.0")]
    [InlineData("v1.0.0")]
    public async Task IntegerRevisionVersionRefuses(string version)
    {
        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => Catalogue.SaveDraftAsync(Tenant, Id, version, Definition, 0, "draft").AsTask());
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
            () => SaveRawAsync(body));
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
            () => SaveRawAsync(body));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.VersionMismatch, pointer)], refusal.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-20: a body version equal to the store's semantic version is admitted")]
    public async Task MatchingBodyVersionIsAdmitted()
    {
        var saved = await _store.SaveDraftAsync(new DefinitionDocument(Key, "inspection.quarterly@1.0.0", "1.0.0",
            ScheduleDefinitionJson.Serialize(Definition)), 0, "draft");
        Assert.Contains("\"version\":\"1.0.0\"", saved.Document.BodyJson, StringComparison.Ordinal);
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
            () => SaveRawAsync(body));
        Assert.Equal([new DefinitionRefusal(ScheduleDefinitionCodes.BodyInvalid, "")], refusal.Refusals);
    }

    [Theory(DisplayName = "scheduling-ck-20: a version id not derived from the semantic version refuses, so a bare revision cannot pose as one")]
    [InlineData("3")]
    [InlineData("inspection.quarterly@2.0.0")]
    [InlineData("other@1.0.0")]
    public async Task VersionIdNotDerivedFromTheSemanticVersionRefuses(string versionId)
    {
        var document = new DefinitionDocument(Key, versionId, "1.0.0", ScheduleDefinitionJson.Serialize(Definition));
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
        await catalogue.SaveDraftAsync(Tenant, Id, "1.0.0", Definition, 0, "draft");
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

    [Theory(DisplayName = "scheduling-ck-20: a member spelled in another case refuses, so it cannot carry a version past the version check")]
    [InlineData("\"envelope\":{", "\"envelope\":{\"Version\":\"9.9.9\",")]
    [InlineData("\"name\":", "\"Name\":")]
    public async Task MemberInAnotherCaseRefuses(string member, string spelled)
    {
        var body = ScheduleDefinitionJson.Serialize(Definition with { Envelope = Definition.Envelope! with { Version = null } });
        Assert.Contains(member, body, StringComparison.Ordinal);
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => SaveRawAsync(body.Replace(member, spelled, StringComparison.Ordinal)));
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-8: a definition that cannot serialize refuses instead of throwing")]
    public async Task UndefinedEnumValueRefusesAtSave()
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            Definition with { MissedBehaviour = (ScheduleMissedBehaviour)99 }, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    [Fact(DisplayName = "scheduling-ck-19: an undefined provenance element refuses instead of throwing")]
    public async Task UndefinedProvenanceRefusesAtSave()
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Catalogue.SaveDraftAsync(Tenant, Id, "1.0.0",
            Definition with { Envelope = Definition.Envelope! with { Provenance = default(JsonElement) } }, 0, "draft").AsTask());
        Assert.Equal([new DefinitionRefusal("scheduling.definition.body_invalid", "")], refused.Refusals);
    }

    private static ScheduleDefinition Schedule(string name) => new(
        new ScheduleDefinitionEnvelope(Id, "1.0.0", Tenant, Harborline.Blocks.BuilderDefinitions.LayoutCascadeLayer.DomainPackage,
            JsonSerializer.SerializeToElement(new { pack = "general" }), "standard", false, [], new DefinitionContractVersion(1, 0)),
        name,
        new ScheduleStarts(ScheduleStartKind.Form, "forms.inspection"),
        DueDate: "2027-01-01T00:00:00Z", Window: new("2027-01-01T00:00:00Z", "2027-01-31T00:00:00Z"));

    private Task<DefinitionRevision> SaveRawAsync(string body)
        => _store.SaveDraftAsync(new DefinitionDocument(Key, "inspection.quarterly@1.0.0", "1.0.0", body), 0, "draft").AsTask();

    private async Task PublishAsync(string version, long expectedRevision, ScheduleDefinition? definition = null)
    {
        var body = definition ?? Definition;
        await Catalogue.SaveDraftAsync(Tenant, Id, version, body with { Envelope = body.Envelope! with { Version = version } },
            expectedRevision, "draft-" + version);
        await Catalogue.PublishAsync(Tenant, Id, version, expectedRevision + 1, "publish-" + version);
    }
}
