using System.Text.Json.Nodes;

using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Registry;

using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

public sealed class RuleDefinitionCatalogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"rules-catalogue-{Guid.NewGuid():N}");
    private readonly InMemoryVersionedDefinitionStore _store;
    private readonly FileJournalDefinitionLifecycleStore _lifecycle;
    private readonly RuleDefinitionCatalog _catalog;
    private static readonly DefinitionKey Key = new("tenant-a", DefinitionKind.Rules, "amount-rule");

    public RuleDefinitionCatalogTests()
    {
        _store = new(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit,
        });
        _lifecycle = new(Path.Combine(_directory, "lifecycle.json"));
        _catalog = new(_store, _lifecycle);
    }

    [Fact]
    public async Task FormulaPublicationUsesTheSharedHistoryAndAnImmutableConsumerPin()
    {
        var draft = await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft-a");
        var published = await _catalog.PublishAsync(Key, "version-a", draft.Revision, "publish-a");
        var replay = await _catalog.PublishAsync(Key, "version-a", draft.Revision, "publish-a");

        Assert.Equal(published, replay);
        Assert.Equal(DefinitionStatus.Published, published.Status);
        Assert.Equal(published, await _store.ResolvePublishedAsync(new(Key, "version-a")));
        Assert.Equal(new[] { 1L, 2L }, (await _store.ListHistoryAsync(Key)).Select(item => item.Revision));

        var loaded = await _catalog.LoadVersionAsync(Key, "version-a");
        Assert.NotNull(loaded);
        Assert.Equal("Amount rule", loaded.Source.Name);
        Assert.Equal("1", Assert.IsType<FormulaExpr.Literal>(Assert.IsType<FormulaDraft>(loaded.Source.Draft).Expression).Value);
    }

    [Theory]
    [InlineData("1.0.0-alpha.10")]
    [InlineData("1.0.0+build.01")]
    [InlineData("2147483648.0.0")]
    public async Task RulesPublicationUsesTheSharedStoresSemanticVersionContract(string version)
    {
        await _catalog.SaveDraftJsonAsync(Source(version: version), "version-a", 0, "draft");
        var published = await _catalog.PublishAsync(Key, "version-a", 1, "publish");

        Assert.Equal(version, published.Document.Version);
        Assert.Equal(version, (await _catalog.LoadVersionAsync(Key, "version-a"))!.Source.Envelope.Version.ToString());
    }

    [Fact]
    public async Task MalformedVersionRefusesBeforeAnySharedHistoryWrite()
    {
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(Source(version: "99.bad.0"), "version-a", 0, "bad-version"));

        Assert.Contains(error.Refusals, item => item.Code == "definition.version_invalid");
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact]
    public async Task RulesCatalogueCannotPublishThroughAnotherRegisteredKindsAdmission()
    {
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit,
            [DefinitionKind.Forms] = (_, _) => Array.Empty<DefinitionRefusal>(),
        });
        var formsKey = Key with { Kind = DefinitionKind.Forms };
        await store.SaveDraftAsync(new(formsKey, "form-version", "1.0.0", "{}"), 0, "form-draft");
        var catalog = new RuleDefinitionCatalog(store, _lifecycle);

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await catalog.PublishAsync(formsKey, "form-version", 1, "rules-publish"));

        Assert.Contains(error.Refusals, refusal => refusal.Code == "definition.registry_unknown"
            && refusal.Pointer == "/registry");
        Assert.Null(await store.ResolvePublishedAsync(new(formsKey, "form-version")));
        Assert.Single(await store.ListHistoryAsync(formsKey));
    }

    [Fact]
    public async Task InvalidRuleRefusesBeforeAnySharedHistoryWrite()
    {
        var source = JsonNode.Parse(Source())!;
        source["draft"]!["expression"]!["value"] = new string('x', 4_097);
        source["draft"]!["expression"]!["valueType"] = "Text";

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(source.ToJsonString(), "version-a", 0, "bad-expression"));

        Assert.Contains(error.Refusals, item => item.Code == "rule.compile.literal_too_long"
            && item.Pointer == "/draft/expression");
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory]
    [InlineData("tier", "unknown", "rule.compile.unsupported_tier", "/tier")]
    [InlineData("tier", "PowerFx", "rule.compile.unsupported_tier", "/tier")]
    [InlineData("scope", "unknown", "rule.compile.bad_grammar", "/draft/scope")]
    [InlineData("outputType", "unknown", "rule.compile.unknown_action", "/draft/outputType")]
    public async Task MalformedRuleDiscriminantsCannotCreateASharedStream(
        string member, string value, string code, string pointer)
    {
        var source = JsonNode.Parse(Source())!;
        (member == "tier" ? source : source["draft"]!)[member] = value;

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(source.ToJsonString(), "version-a", 0, "invalid"));

        Assert.Contains(error.Refusals, refusal => refusal.Code == code && refusal.Pointer == pointer);
        Assert.Empty(await _store.ListHistoryAsync(Key));
        Assert.Empty(await _store.ListKeysAsync("tenant-a", DefinitionKind.Rules));
    }

    [Fact]
    public async Task ACompiledCycleCannotCreateASharedStream()
    {
        var source = JsonNode.Parse(Source())!;
        source["draft"]!["inputs"] = JsonNode.Parse("""[{"id":"total","ref":"field.total","type":"Number"}]""");
        source["draft"]!["expression"] = JsonNode.Parse("""{"kind":"Ref","name":"field.total"}""");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(source.ToJsonString(), "version-a", 0, "cycle"));

        Assert.Contains(error.Refusals, refusal => refusal.Code == "rule.compile.cycle"
            && refusal.Pointer == "/draft/expression");
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact]
    public async Task DecisionTablePublishesOnceAndRoundTripsThroughTheRealStore()
    {
        string source = TableSource();
        await _catalog.SaveDraftJsonAsync(source, "table-a", 0, "draft-table");
        var published = await _catalog.PublishAsync(Key, "table-a", 1, "publish-table");
        var replay = await _catalog.PublishAsync(Key, "table-a", 1, "publish-table");
        var loaded = (await _catalog.LoadVersionAsync(Key, "table-a"))!;

        Assert.Equal(published, replay);
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        var table = Assert.IsType<DecisionTableDraft>(loaded.Source.Draft);
        Assert.Equal("low", table.Rows[0].Output);
        Assert.Equal("100", Assert.IsType<TableCell.Range>(table.Rows[0].Cells["amount"]).Hi);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source),
            JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(loaded.Source))));
    }

    [Theory]
    [InlineData("kind", "unknown", "/draft/rows/0/cells/amount/kind")]
    [InlineData("hi", "not-a-number", "/draft/rows/0/cells/amount/hi")]
    public async Task InvalidTableCellsCannotCreateASharedStream(string member, string value, string pointer)
    {
        var source = JsonNode.Parse(TableSource())!;
        source["draft"]!["rows"]![0]!["cells"]!["amount"]![member] = value;

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(source.ToJsonString(), "table-a", 0, "invalid-table"));

        Assert.Contains(error.Refusals, refusal => refusal.Code == "rule.skin.decision_table_bad_cell"
            && refusal.Pointer == pointer);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact]
    public async Task EqualVersionWithDifferentBodyCannotReplaceAnImmutablePin()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft-a");
        var original = await _catalog.PublishAsync(Key, "version-a", 1, "publish-a");
        var immutable = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.SaveDraftJsonAsync(Source(value: "2"), "version-a", 2, "replace"));
        Assert.Contains(immutable.Refusals, refusal => refusal.Code == "definition.version_immutable");
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        await _catalog.SaveDraftJsonAsync(Source(value: "2"), "version-b", 2, "draft-b");

        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.PublishAsync(Key, "version-b", 3, "publish-b"));

        Assert.Contains(error.Refusals, refusal => refusal.Code == "definition.version_conflict");
        Assert.Equal(original, await _store.ResolvePublishedAsync(new(Key, "version-a")));
        Assert.Null(await _store.ResolvePublishedAsync(new(Key, "version-b")));
        Assert.Equal(3, (await _store.ListHistoryAsync(Key)).Count);
    }

    [Fact]
    public async Task InterleavedPublishersFenceThenRetryWithoutLosingEitherVersion()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft-a");
        await _catalog.SaveDraftJsonAsync(Source(version: "2.0.0", value: "2"), "version-b", 1, "draft-b");

        async Task<(string Id, DefinitionRevision? Published)> Attempt(string id)
        {
            try { return (id, await _catalog.PublishAsync(Key, id, 2, "publish-" + id)); }
            catch (DefinitionRefusalException error)
            {
                Assert.Contains(error.Refusals, refusal => refusal.Code == "definition.revision_conflict");
                return (id, null);
            }
        }
        var attempts = await Task.WhenAll(Task.Run(() => Attempt("version-a")), Task.Run(() => Attempt("version-b")));
        var winner = Assert.Single(attempts, attempt => attempt.Published is not null);
        var loser = Assert.Single(attempts, attempt => attempt.Published is null);
        await _catalog.PublishAsync(Key, loser.Id, 3, "retry-" + loser.Id);
        var replay = await _catalog.PublishAsync(Key, winner.Id, 2, "publish-" + winner.Id);

        Assert.Equal(winner.Published, replay);
        Assert.NotNull(await _store.ResolvePublishedAsync(new(Key, "version-a")));
        Assert.NotNull(await _store.ResolvePublishedAsync(new(Key, "version-b")));
        Assert.Equal(new[] { 1L, 2L, 3L, 4L }, (await _store.ListHistoryAsync(Key)).Select(item => item.Revision));
    }

    [Fact]
    public async Task LatestUsesNumericPrereleaseOrderingFromTheSharedStore()
    {
        await _catalog.SaveDraftJsonAsync(Source(version: "1.0.0-alpha.9"), "alpha-9", 0, "draft-9");
        await _catalog.PublishAsync(Key, "alpha-9", 1, "publish-9");
        await _catalog.SaveDraftJsonAsync(Source(version: "1.0.0-alpha.10"), "alpha-10", 2, "draft-10");
        await _catalog.PublishAsync(Key, "alpha-10", 3, "publish-10");

        var resolved = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);

        Assert.Equal("alpha-10", resolved.Snapshot!.Revision.Document.VersionId);
        Assert.Equal("alpha-9", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0-alpha.9"),
            RuleResolveScope.Production)).Snapshot!.Revision.Document.VersionId);
    }

    [Fact]
    public async Task RestoreUsesNewMetadataWhilePreservingPublishedBodyBytesAndPins()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft");
        var published = await _catalog.PublishAsync(Key, "version-a", 1, "publish");

        var restored = await _catalog.RestoreAsDraftAsync(Key, "version-a", "version-b", "2.0.0", 2, "restore");

        Assert.Equal(DefinitionStatus.Draft, restored.Status);
        Assert.Equal(published.Document.BodyJson, restored.Document.BodyJson);
        Assert.Equal("2.0.0", (await _catalog.LoadVersionAsync(Key, "version-b"))!.Source.Envelope.Version.ToString());
        Assert.Equal(published, await _store.ResolvePublishedAsync(new(Key, "version-a")));
        Assert.Null(await _store.ResolvePublishedAsync(new(Key, "version-b")));
    }

    [Fact]
    public async Task PublishedBodiesAreDetachedFromLoadedMutableCollections()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft");
        await _catalog.PublishAsync(Key, "version-a", 1, "publish");
        var loaded = (await _catalog.LoadVersionAsync(Key, "version-a"))!;
        loaded.Source.Envelope.Provenance["id"] = "altered";

        var reloaded = (await _catalog.LoadVersionAsync(Key, "version-a"))!;
        Assert.Equal("finance", reloaded.Source.Envelope.Provenance["id"]!.GetValue<string>());
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
    }

    [Fact]
    public async Task LatestAndPinnedPoliciesUsePublishedVersionsAndProductionNeverReturnsADraft()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft-a");
        await _catalog.PublishAsync(Key, "version-a", 1, "publish-a");
        await _catalog.SaveDraftJsonAsync(Source(version: "2.0.0", value: "2"), "version-b", 2, "draft-b");

        var latest = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);
        var draft = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Draft, RuleResolveScope.Production);
        var pinnedDraft = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("2.0.0"), RuleResolveScope.Production);

        Assert.Equal(RuleResolutionStatus.Resolved, latest.Status);
        Assert.Equal("version-a", latest.Snapshot!.Revision.Document.VersionId);
        Assert.Equal(RuleResolutionStatus.DraftRefused, draft.Status);
        Assert.Equal(RuleResolutionStatus.DraftRefused, pinnedDraft.Status);
        await _catalog.PublishAsync(Key, "version-b", 3, "publish-b");
        var pinned = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0"), RuleResolveScope.Production);
        Assert.Equal("version-a", pinned.Snapshot!.Revision.Document.VersionId);
        Assert.Equal("version-b", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production))
            .Snapshot!.Revision.Document.VersionId);
    }

    [Fact]
    public async Task LatestUsesPublishedHeadWithoutReadingHistory()
    {
        var store = new CountingDefinitionStore(new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit,
        }));
        var catalog = new RuleDefinitionCatalog(store, _lifecycle);
        await catalog.SaveDraftJsonAsync(Source(), "published", 0, "draft-published");
        await catalog.PublishAsync(Key, "published", 1, "publish");
        await catalog.SaveDraftJsonAsync(Source(version: "2.0.0", value: "2"), "draft", 2, "draft-newer");
        store.HistoryReads = 0;

        var latest = await catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production);

        Assert.Equal("published", latest.Snapshot!.Revision.Document.VersionId);
        Assert.Equal(0, store.HistoryReads);
    }

    [Fact]
    public async Task PinnedReadsHistoryAndKeepsThePublishedRevisionImmutable()
    {
        var store = new CountingDefinitionStore(new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = RuleDefinitionCatalog.Admit,
        }));
        var catalog = new RuleDefinitionCatalog(store, _lifecycle);
        await catalog.SaveDraftJsonAsync(Source(), "published", 0, "draft-published");
        await catalog.PublishAsync(Key, "published", 1, "publish");
        await catalog.SaveDraftJsonAsync(Source(value: "2"), "same-label-draft", 2, "draft-same-label");
        store.HistoryReads = 0;

        var pinned = await catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0"), RuleResolveScope.Production);

        Assert.Equal("published", pinned.Snapshot!.Revision.Document.VersionId);
        Assert.Equal(1, store.HistoryReads);
    }

    [Fact]
    public async Task BodyHasNoSharedIdentityMetadataAndRestoreRetainsAllOtherSource()
    {
        var draft = await _catalog.SaveDraftJsonAsync(Source(), "a", 0, "draft");
        var metadata = JsonNode.Parse(draft.Document.BodyJson)!["envelope"]!.AsObject();
        Assert.False(metadata.ContainsKey("id"));
        Assert.False(metadata.ContainsKey("tenant"));
        Assert.False(metadata.ContainsKey("version"));
        await _catalog.PublishAsync(Key, "a", 1, "publish");
        await _catalog.RestoreAsDraftAsync(Key, "a", "b", "2.0.0", 2, "restore");
        var restored = (await _catalog.LoadVersionAsync(Key, "b"))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(Source(version: "2.0.0")),
            JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(restored.Source))));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("tenant")]
    [InlineData("version")]
    public async Task EmbeddedStoreMetadataRefusesInsteadOfOverridingTheHeader(string member)
    {
        var parsed = RuleDefinitionCodec.Parse(Source(), RuleIntentPhase.Author).Document!;
        var body = JsonNode.Parse(RuleDefinitionCodec.SerializeBody(parsed))!;
        body["envelope"]![member] = "unexpected";
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _store.SaveDraftAsync(new(Key, "bad", "1.0.0", body.ToJsonString()), 0, "bad"));
        Assert.Contains(error.Refusals, refusal => refusal.Code == RuleDefinitionCodes.UnknownMember
            && refusal.Pointer == "/envelope/" + member);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Theory]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Publish)]
    public void DirectAdmissionKeepsDuplicatePrecedenceAndRejectsOtherKinds(DefinitionAdmissionPhase phase)
    {
        string body = RuleDefinitionCodec.SerializeBody(RuleDefinitionCodec.Parse(Source(), RuleIntentPhase.Author).Document!);
        string duplicate = body.Replace("\"envelope\":{", "\"envelope\":{\"version\":\"a\",\"version\":\"b\",", StringComparison.Ordinal);
        var refusal = Assert.Single(RuleDefinitionCatalog.Admit(new(Key, "a", "1.0.0", duplicate), phase));
        Assert.Equal(new DefinitionRefusal(RuleDefinitionCodes.DuplicateMember, "/envelope/version"), refusal);
        var foreign = Assert.Single(RuleDefinitionCatalog.Admit(new(Key with { Kind = DefinitionKind.Forms }, "a", "1.0.0", body), phase));
        Assert.Equal(new DefinitionRefusal("definition.registry_unknown", "/registry"), foreign);
    }

    [Theory]
    [InlineData(DefinitionAdmissionPhase.Author)]
    [InlineData(DefinitionAdmissionPhase.Publish)]
    public void AdmissionRunsTheCompilerOnTheExactBodyAtBothPhases(DefinitionAdmissionPhase phase)
    {
        var source = RuleDefinitionCodec.Parse(Source(), RuleIntentPhase.Author).Document!;
        var body = JsonNode.Parse(RuleDefinitionCodec.SerializeBody(source))!;
        body["draft"]!["expression"]!["valueType"] = "Text";
        body["draft"]!["expression"]!["value"] = new string('x', 4_097);
        string bytes = body.ToJsonString();
        var document = new DefinitionDocument(Key, "a", "1.0.0", bytes);
        Assert.Equal(new DefinitionRefusal("rule.compile.literal_too_long", "/draft/expression"),
            Assert.Single(RuleDefinitionCatalog.Admit(document, phase)));
        Assert.Equal(bytes, document.BodyJson);
    }

    [Theory]
    [InlineData("1.0.0-alpha.10")]
    [InlineData("1.0.0+build.01")]
    [InlineData("2147483648.0.0")]
    public async Task ConsumerPinsPreserveGenericVersionLabels(string version)
    {
        var source = JsonNode.Parse(Source(version: version))!;
        await _catalog.SaveDraftJsonAsync(source.ToJsonString(), "a", 0, "draft");
        await _catalog.PublishAsync(Key, "a", 1, "publish");
        var resolved = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned(version), RuleResolveScope.Production);
        Assert.Equal("a", resolved.Snapshot!.Revision.Document.VersionId);
        Assert.Equal(version, resolved.Snapshot.Source.Envelope.Version);
    }

    [Fact]
    public async Task PersistedInvalidSourceDiagnosesWithoutChangingHistory()
    {
        var unvalidatedStore = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
            { [DefinitionKind.Rules] = (_, _) => Array.Empty<DefinitionRefusal>() });
        string body = RuleDefinitionCodec.SerializeBody(RuleDefinitionCodec.Parse(Source(), RuleIntentPhase.Author).Document!)
            .Replace("JsonLogic", "PowerFx", StringComparison.Ordinal);
        await unvalidatedStore.SaveDraftAsync(new(Key, "bad", "1.0.0", body), 0, "bad");
        var catalog = new RuleDefinitionCatalog(unvalidatedStore, _lifecycle);
        var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await catalog.LoadVersionAsync(Key, "bad"));
        Assert.Contains(error.Refusals, refusal => refusal.Code == "rule.compile.unsupported_tier" && refusal.Pointer == "/tier");
        Assert.Equal(body, Assert.Single(await unvalidatedStore.ListHistoryAsync(Key)).Document.BodyJson);
    }

    [Theory]
    [InlineData("99.bad.0")]
    [InlineData("1.0.0-alpha.01")]
    public async Task PolicyLabelsUseGenericAdmissionAtResolve(string version)
    {
        var resolve = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned(version), RuleResolveScope.Production));
        Assert.Contains(new DefinitionRefusal("definition.version_invalid", "/versionPolicy/version"), resolve.Refusals);
        Assert.Empty(await _store.ListHistoryAsync(Key));
    }

    [Fact]
    public async Task SandboxDraftUsesCurrentHistoryWhileLatestAndPinsKeepPublication()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "a", 0, "draft-a");
        await _catalog.PublishAsync(Key, "a", 1, "publish");
        await _catalog.SaveDraftJsonAsync(Source(value: "2"), "b", 2, "draft-b");
        Assert.Equal("b", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Draft, RuleResolveScope.Sandbox)).Snapshot!.Revision.Document.VersionId);
        Assert.Equal("a", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Sandbox)).Snapshot!.Revision.Document.VersionId);
        Assert.Equal("a", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0"), RuleResolveScope.Production)).Snapshot!.Revision.Document.VersionId);
        await _lifecycle.ArchiveAsync(new(Key.Tenant, Key.Kind, Key.DefinitionId));
        Assert.Equal(RuleResolutionStatus.Resolved, (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production)).Status);
        Assert.NotNull(await _catalog.LoadVersionAsync(Key, "a"));
        await _lifecycle.UnarchiveAsync(new(Key.Tenant, Key.Kind, Key.DefinitionId));
        Assert.Equal(RuleResolutionStatus.Resolved, (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production)).Status);
    }

    [Fact]
    public async Task ArchivingDoesNotRevokeAnExistingPublishedPin()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "version-a", 0, "draft");
        await _catalog.PublishAsync(Key, "version-a", 1, "publish");
        await _lifecycle.ArchiveAsync(new(Key.Tenant, Key.Kind, Key.DefinitionId));

        var pinned = await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0"),
            RuleResolveScope.Production);

        Assert.Equal(RuleResolutionStatus.Resolved, pinned.Status);
        Assert.Equal("version-a", pinned.Snapshot!.Revision.Document.VersionId);
        Assert.Equal(2, (await _store.ListHistoryAsync(Key)).Count);
        Assert.Empty(await _catalog.ListAsync(Key.Tenant));
        Assert.Single(await _catalog.ListAsync(Key.Tenant, includeArchived: true));
    }

    [Fact]
    public async Task InputAndLoadedCollectionsCannotMutatePublishedHistory()
    {
        var source = RuleDefinitionCodec.Parse(Source(), RuleIntentPhase.Author).Document!;
        var inputs = new List<FormulaInputDecl> { new("amount", "field.amount", ColumnValueType.Number) };
        source = source with { Draft = ((FormulaDraft)source.Draft) with
            { Inputs = inputs, Expression = new FormulaExpr.Ref("field.amount") } };
        await _catalog.CreateJsonAsync(RuleDefinitionCodec.SerializeCanonical(source), "a", 0, "create");
        var published = await _catalog.PublishAsync(Key, "a", 1, "publish");
        inputs.Clear();
        source.Envelope.Provenance.Clear();
        var loaded = (await _catalog.LoadVersionAsync(Key, "a"))!;
        var loadedInputs = Assert.IsType<FormulaDraft>(loaded.Source.Draft).Inputs;
        Assert.Equal("field.amount", Assert.Single(loadedInputs).Ref);
        if (loadedInputs is FormulaInputDecl[] mutable) mutable[0] = new("changed", "field.changed", ColumnValueType.Text);
        var reloaded = (await _catalog.LoadVersionAsync(Key, "a"))!;
        Assert.Equal("field.amount", Assert.Single(Assert.IsType<FormulaDraft>(reloaded.Source.Draft).Inputs).Ref);
        Assert.Equal(published, await _store.ResolvePublishedAsync(new(Key, "a")));
    }

    [Fact]
    public async Task PublicationFailurePropagatesWithoutAppendingOrConsumingReplay()
    {
        var failure = new IOException("publication unavailable");
        bool fail = true;
        var store = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Rules] = (document, phase) => phase == DefinitionAdmissionPhase.Publish && fail
                ? throw failure : RuleDefinitionCatalog.Admit(document, phase),
        });
        var catalog = new RuleDefinitionCatalog(store, _lifecycle);
        await catalog.CreateJsonAsync(Source(), "a", 0, "create");
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(async () =>
            await catalog.PublishAsync(Key, "a", 1, "publish")));
        Assert.Single(await store.ListHistoryAsync(Key));
        fail = false;
        var published = await catalog.PublishAsync(Key, "a", 1, "publish");
        Assert.Equal(published, await catalog.PublishAsync(Key, "a", 1, "publish"));
    }

    [Fact]
    public async Task OlderPublicationArrivalCannotLowerSharedHead()
    {
        await _catalog.SaveDraftJsonAsync(Source(version: "2.0.0"), "new", 0, "new");
        await _catalog.PublishAsync(Key, "new", 1, "publish-new");
        await _catalog.SaveDraftJsonAsync(Source(), "old", 2, "old");
        await _catalog.PublishAsync(Key, "old", 3, "publish-old");
        Assert.Equal("new", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest,
            RuleResolveScope.Production)).Snapshot!.Revision.Document.VersionId);
        Assert.Equal("old", (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Pinned("1.0.0"),
            RuleResolveScope.Production)).Snapshot!.Revision.Document.VersionId);
    }

    [Fact]
    public async Task LoadKeepsTheOpenDraftWhenAnOlderVersionPublishesLater()
    {
        await _catalog.SaveDraftJsonAsync(Source(), "old", 0, "old");
        await _catalog.SaveDraftJsonAsync(Source(version: "2.0.0"), "current", 1, "current");
        await _catalog.PublishAsync(Key, "old", 2, "publish-old");
        Assert.Equal("current", (await _catalog.LoadAsync(Key))!.Revision.Document.VersionId);
        Assert.Equal("current", Assert.Single(await _catalog.ListAsync(Key.Tenant)).Revision.Document.VersionId);
        await _catalog.PublishAsync(Key, "current", 3, "publish-current");
        Assert.Equal("current", (await _catalog.LoadAsync(Key))!.Revision.Document.VersionId);
    }

    private static string TableSource()
        => MakeTableSource();

    [Fact]
    public async Task CreateFencesZeroAndCollisionCannotOverwriteDraft()
    {
        var created = await _catalog.CreateJsonAsync(Source(), "a", 0, "create");
        Assert.Equal(created, await _catalog.CreateJsonAsync(Source(), "a", 0, "create"));
        var collision = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.CreateJsonAsync(Source(value: "2"), "b", 0, "collision"));
        Assert.Contains(collision.Refusals, r => r.Code == "definition.revision_conflict");
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.CreateJsonAsync(Source(value: "2"), "b", 1, "not-create"));
        Assert.Equal(created, (await _catalog.LoadAsync(Key))!.Revision);
        Assert.Single(await _store.ListHistoryAsync(Key));
    }

    [Fact]
    public async Task ListUsesExactTenantStableNameIdentityAndDetachedCurrentDrafts()
    {
        foreach (var (tenant, id, name) in new[] { ("tenant-a", "z", "Same"), ("tenant-a", "a", "Same"),
            ("tenant-a", "b", "Alpha"), ("tenant-ab", "foreign", "First") })
        {
            var source = JsonNode.Parse(Source())!;
            source["envelope"]!["tenant"] = tenant;
            source["envelope"]!["id"] = id;
            source["name"] = name;
            await _catalog.CreateJsonAsync(source.ToJsonString(), "a", 0, "create-" + id);
        }
        var rows = await _catalog.ListAsync("tenant-a");
        Assert.Equal(new[] { "b", "a", "z" }, rows.Select(r => r.Source.Envelope.Id));
        rows[0].Source.Envelope.Provenance.Clear();
        Assert.NotEmpty((await _catalog.LoadAsync(new("tenant-a", DefinitionKind.Rules, "b")))!.Source.Envelope.Provenance);
        await _catalog.CreateJsonAsync(Source(), "a", 0, "create");
        await _catalog.PublishAsync(Key, "a", 1, "publish");
        await _catalog.SaveDraftJsonAsync(Source(version: "2.0.0"), "b", 2, "edit");
        Assert.Equal("b", (await _catalog.LoadAsync(Key))!.Revision.Document.VersionId);
        Assert.Equal(DefinitionStatus.Draft, Assert.Single(await _catalog.ListAsync("tenant-a"),
            r => r.Source.Envelope.Id == Key.DefinitionId).Revision.Status);
    }

    [Fact]
    public async Task DuplicateHasIndependentIdentityHistoryAndArchiveNamespace()
    {
        await _catalog.CreateJsonAsync(Source(), "a", 0, "create");
        var published = await _catalog.PublishAsync(Key, "a", 1, "publish");
        var copy = await _catalog.DuplicateAsync(Key, "a", "copy", "Copy", "copy-a", "3.0.0", 0, "duplicate");
        var copyKey = Key with { DefinitionId = "copy" };
        Assert.Equal(copyKey, copy.Document.Key);
        Assert.Equal("3.0.0", copy.Document.Version);
        Assert.Equal(DefinitionStatus.Draft, copy.Status);
        Assert.Single(await _store.ListHistoryAsync(copyKey));
        Assert.Equal(published, await _store.ResolvePublishedAsync(new(Key, "a")));
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.DuplicateAsync(Key, "a", "copy", "Other", "copy-b", "4.0.0", 0, "collision"));
        await _catalog.ArchiveAsync(copyKey);
        Assert.Single(await _catalog.ListAsync("tenant-a"));
        Assert.Equal(2, (await _catalog.ListAsync("tenant-a", includeArchived: true)).Count);
        Assert.False(await _lifecycle.IsArchivedAsync(new("tenant-ab", DefinitionKind.Rules, "copy")));
        Assert.False(await _lifecycle.IsArchivedAsync(new("tenant-a", DefinitionKind.Forms, "copy")));
        Assert.NotNull(await _catalog.LoadAsync(copyKey));
    }

    [Fact]
    public async Task UnknownRuleReadsNullAndMutationsRefuseWithoutCreatingHistory()
    {
        Assert.Null(await _catalog.LoadAsync(Key));
        Assert.Null(await _catalog.LoadVersionAsync(Key, "missing"));
        Assert.Equal(RuleResolutionStatus.NotFound,
            (await _catalog.ResolveAsync(Key, RuleVersionPolicy.Latest, RuleResolveScope.Production)).Status);
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await _catalog.PublishAsync(Key, "missing", 0, "publish"));
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () => await _catalog.ArchiveAsync(Key));
        await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await _catalog.DuplicateAsync(Key, "missing", "copy", "Copy", "a", "1.0.0", 0, "duplicate"));
        Assert.Empty(await _store.ListKeysAsync(Key.Tenant, DefinitionKind.Rules));
    }

    private static string MakeTableSource()
    {
        var source = JsonNode.Parse(Source())!;
        source["draft"] = JsonNode.Parse("""
            {
              "kind":"Table", "scope":"Field", "scopeTarget":"total", "outputType":"Compute",
              "hitPolicy":"Priority", "columns":[{"id":"amount","input":"field.amount","valueType":"Number"}],
              "rows":[{"id":"low","cells":{"amount":{"kind":"Range","lo":"0","hi":"100"}},"output":"low","priority":1}],
              "noMatch":{"kind":"Default","value":"high"}
            }
            """);
        return source.ToJsonString();
    }

    public static IEnumerable<object[]> SharedSources()
    {
        var cases = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "definition-intent-cases.json")))!["cases"]!.AsArray();
        foreach (var row in cases)
            yield return [row!["id"]!.GetValue<string>(), row["sourceJson"]!.GetValue<string>(),
                row["expected"]!["valid"]!.GetValue<bool>(), row["expected"]!["code"]?.GetValue<string>() ?? "",
                row["expected"]!["location"]?.GetValue<string>() ?? ""];
    }

    [Theory]
    [MemberData(nameof(SharedSources))]
    public async Task SharedIntentCorpusCommitsOnlyAdmittedSource(string id, string source, bool valid, string code, string location)
    {
        if (!valid)
        {
            var error = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
                await _catalog.CreateJsonAsync(source, "a", 0, id));
            Assert.Equal(new DefinitionRefusal(code, location), Assert.Single(error.Refusals));
            Assert.Empty(await _store.ListKeysAsync("tenant-a", DefinitionKind.Rules));
            return;
        }
        var created = await _catalog.CreateJsonAsync(source, "a", 0, id);
        var published = await _catalog.PublishAsync(created.Document.Key, "a", 1, "publish");
        Assert.Equal(published, await _catalog.PublishAsync(created.Document.Key, "a", 1, "publish"));
        var loaded = (await _catalog.LoadVersionAsync(created.Document.Key, "a"))!;
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source), JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(loaded.Source))));
    }

    [Fact]
    public async Task AdvisoryOverlapLintDoesNotPreventSharedPublication()
    {
        var source = JsonNode.Parse(TableSource())!;
        var rows = source["draft"]!["rows"]!.AsArray();
        var overlap = rows[0]!.DeepClone();
        overlap["id"] = "overlap";
        rows.Add(overlap);
        var document = RuleDefinitionCodec.Parse(source.ToJsonString(), RuleIntentPhase.Author).Document!;
        Assert.Contains(RuleLint.LintTable(Assert.IsType<DecisionTableDraft>(document.Draft)), r => r.Code == RuleLintCodes.Overlap);
        await _catalog.CreateJsonAsync(source.ToJsonString(), "a", 0, "create");
        Assert.Equal(DefinitionStatus.Published, (await _catalog.PublishAsync(Key, "a", 1, "publish")).Status);
    }

    private static string Source(string version = "1.0.0", string value = "1")
    {
        var source = JsonNode.Parse("""
            {
              "envelope": {
                "id": "amount-rule", "version": "1.0.0", "tenant": "tenant-a",
                "cascadeLayer": "domain-package", "provenance": {"kind": "package", "id": "finance"},
                "requires": []
              },
              "name": "Amount rule", "tier": "JsonLogic",
              "draft": {
                "kind": "Formula", "scope": "Field", "scopeTarget": "total", "outputType": "Compute",
                "inputs": [], "expression": {"kind": "Literal", "value": "1", "valueType": "Number"}
              }
            }
            """)!;
        source["envelope"]!["version"] = version;
        source["draft"]!["expression"]!["value"] = value;
        return source.ToJsonString();
    }

    public void Dispose()
    {
        _lifecycle.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class CountingDefinitionStore(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        public int HistoryReads { get; set; }

        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind,
            CancellationToken cancellationToken = default) => inner.ListKeysAsync(tenant, kind, cancellationToken);

        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default) => inner.SaveDraftAsync(document, expectedRevision, requestId, cancellationToken);

        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default) => inner.PublishAsync(key, versionId, expectedRevision, requestId, cancellationToken);

        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId,
            string draftVersionId, string draftVersion, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default) => inner.RestoreAsDraftAsync(key, sourceVersionId, draftVersionId, draftVersion, expectedRevision, requestId, cancellationToken);

        public async ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key,
            CancellationToken cancellationToken = default)
        {
            HistoryReads++;
            return await inner.ListHistoryAsync(key, cancellationToken);
        }

        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key,
            CancellationToken cancellationToken = default) => inner.GetPublishedHeadAsync(key, cancellationToken);

        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding,
            CancellationToken cancellationToken = default) => inner.ResolvePublishedAsync(binding, cancellationToken);
    }
}
