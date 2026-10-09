using System.Text;
using System.Text.Json.Nodes;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-618 preparatory consumer (DES-0015). Replays the frozen Records editor contract fixtures against the existing
/// Record Type producer: G1 and R1-R7 as authored, and the producer-observable facts of P2-P4. Requests are read from
/// the fixture and parsed by the producer's own body parser; expected ids, revisions, bytes, codes and pointers are
/// the fixture's literals. The lane behaviour of P1-P4 has no Records adapter yet and is not asserted here.
/// </summary>
public sealed class RecordsEditorContractFixtureTests
{
    private const string GrammarFile = "records-current-grammar.json";
    private const string RefusalFile = "records-refusal-cases.json";
    private const string ParityFile = "records-parity-inputs.json";
    private const string BaseCase = "G1-asset-class";

    // P1 is lane rendering only. P2 and P4 replay only their producer facts; their lane behaviour stays deferred.
    private static readonly string[] ProducerReplayed =
    [
        "G1-asset-class", "R1-punctuation-name", "R2-zero-revision-collision", "R3-duplicate-field-key",
        "R4-duplicate-field-key-raw-complete-body", "R5-reference-binding-conflict", "R6-unknown-kind",
        "R7-same-name-other-section-positive", "P2-identity-revision-reset", "P3-kind-version-change",
        "P4-read-only-and-ordering",
    ];
    private static readonly string[] DeferredToNativeLanes = ["P1-refusal-rendering-parity"];

    private static readonly string[] RequestMembers =
        ["tenant", "section", "name", "version", "contract", "classId", "recordClass", "packageId", "retentionClockFieldId", "fields"];
    private static readonly string[] ReplaceMembers = ["name", "section", "fields"];
    private static readonly string[] PreconditionMembers = ["operation", "requestId", "createRequestFrom", "replace", "expectedRevision"];
    private static readonly string[] RefusedMembers = ["outcome", "stage", "refusals", "source"];
    private static readonly string[] AdmittedMembers = ["outcome", "mintedRecordTypeId", "catalogueKeysAfter"];

    [Fact]
    public void every_manifest_case_is_replayed_here_or_named_as_a_native_lane_deferral()
    {
        var manifest = Load("manifest.json");
        var ids = new List<string>();
        foreach (var file in manifest["files"]!.AsArray())
        {
            if (file!["cases"] is not JsonArray listed) continue;
            var cases = Load(Text(file!["path"]))["cases"]!.AsArray().Select(row => Text(row!["id"])).ToArray();
            Assert.Equal(listed.Select(Text), cases);
            ids.AddRange(cases);
        }

        // An inventory binding only: it proves no case is silently skipped, not that any lane is at parity.
        Assert.Equal(manifest["caseCount"]!.GetValue<int>(), ids.Count);
        Assert.Equal(ids.Order(StringComparer.Ordinal), ProducerReplayed.Concat(DeferredToNativeLanes).Order(StringComparer.Ordinal));
        var refusalIds = Load(RefusalFile)["cases"]!.AsArray().Select(row => Text(row!["id"])).ToArray();
        Assert.All(Case(ParityFile, "P1-refusal-rendering-parity")["inputs"]!.AsArray(), input => Assert.Contains(Text(input), refusalIds));
    }

    [Fact]
    public async Task g1_creates_the_fixture_canonical_body_at_its_catalogue_identity_and_round_trips_it()
    {
        var g1 = Case(GrammarFile, BaseCase);
        var catalogue = g1["catalogue"]!;
        var create = g1["createRequest"]!;
        RequireOperation(create["operation"], "RecordTypeDefinitionStore.CreateDraftAsync(NewRecordType, requestId)");
        var canonical = Text(g1["canonicalJsonText"]);
        var host = Host();

        var created = await host.Records.CreateDraftAsync(ToNewRecordType(BaseRequest()), Text(create["requestId"]));

        Assert.Equal(Text(create["mintedRecordTypeId"]), created.RecordTypeId);
        Assert.Equal(new DefinitionKey(Text(catalogue["tenant"]), Enum.Parse<DefinitionKind>(Text(catalogue["definitionKind"])),
            Text(catalogue["definitionId"])), created.Revision.Document.Key);
        Assert.Equal(Text(catalogue["definitionVersion"]), created.Revision.Document.Version);
        Assert.Equal(catalogue["expectedCreateRevision"]!.GetValue<long>(), created.Revision.Revision);
        Assert.Equal(Enum.Parse<DefinitionStatus>(Text(catalogue["expectedStatus"])), created.Revision.Status);
        Assert.Equal(canonical, created.Revision.Document.BodyJson);
        Assert.True(JsonNode.DeepEquals(g1["expectedStoredBody"], JsonNode.Parse(created.Revision.Document.BodyJson)));
        Assert.Equal(canonical, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(
            RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(canonical)))));
    }

    [Theory]
    [InlineData("R1-punctuation-name")]
    [InlineData("R3-duplicate-field-key")]
    [InlineData("R5-reference-binding-conflict")]
    [InlineData("R6-unknown-kind")]
    public async Task a_create_refusal_case_refuses_its_complete_list_and_writes_nothing(string id)
    {
        var refusal = Case(RefusalFile, id);
        RequireOperation(refusal["operation"], "CreateDraftAsync");
        Assert.StartsWith("ListKeysAsync", Text(refusal["postcondition"]), StringComparison.Ordinal);
        var host = Host();
        await RunPreconditionsAsync(host, refusal);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(Request(refusal), Text(refusal["requestId"])).AsTask());

        AssertRefused(refusal["expected"]!, refused);
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(host.Registry));
    }

    [Fact]
    public async Task r2_a_name_that_slugs_to_a_held_id_refuses_as_a_collision_and_the_original_request_replays()
    {
        var r2 = Case(RefusalFile, "R2-zero-revision-collision");
        RequireOperation(r2["operation"], "CreateDraftAsync");
        var name = Text(r2["replace"]!["name"]);
        Assert.Equal("again-" + name, Text(r2["requestId"]));
        var host = Host();
        await RunPreconditionsAsync(host, r2);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(Request(r2), Text(r2["requestId"])).AsTask());
        AssertRefused(r2["expected"]!, refused);
        foreach (var equivalent in r2["alsoEquivalentNames"]!.AsArray().Select(Text))
        {
            var again = await Assert.ThrowsAsync<DefinitionRefusalException>(
                () => host.Records.CreateDraftAsync(Expand(new JsonObject { ["name"] = equivalent }), "again-" + equivalent).AsTask());
            AssertRefused(r2["expected"]!, again);
        }

        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        var original = r2["preconditions"]!.AsArray().Single()!;
        var replay = await host.Records.CreateDraftAsync(Request(original), Text(original["requestId"]));
        Assert.Equal(original["expectedRevision"]!.GetValue<long>(), replay.Revision.Revision);
    }

    [Fact]
    public async Task r4_the_raw_catalogue_refuses_a_complete_body_with_a_duplicate_field_key()
    {
        var r4 = Case(RefusalFile, "R4-duplicate-field-key-raw-complete-body");
        RequireOperation(r4["operation"], "IVersionedDefinitionStore.SaveDraftAsync (raw catalogue)");
        var raw = r4["rawSave"]!;
        var key = raw["key"]!;
        var document = new DefinitionDocument(
            new DefinitionKey(Text(key["tenant"]), Enum.Parse<DefinitionKind>(Text(key["kind"])), Text(key["definitionId"])),
            Text(raw["versionId"]), Text(raw["version"]), Text(raw["bodyJson"]));
        var host = Host();
        await RunPreconditionsAsync(host, r4);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            document, raw["expectedRevision"]!.GetValue<long>(), Text(raw["requestId"])).AsTask());

        AssertRefused(r4["expected"]!, refused);
        Assert.Single(await host.Catalogue.ListHistoryAsync(document.Key));
    }

    [Fact]
    public async Task r7_the_same_name_in_another_section_admits_as_another_type()
    {
        var r7 = Case(RefusalFile, "R7-same-name-other-section-positive");
        RequireOperation(r7["operation"], "CreateDraftAsync");
        var expected = r7["expected"]!;
        RequireMembers(expected.AsObject(), AdmittedMembers);
        Assert.Equal("admitted", Text(expected["outcome"]));
        var host = Host();
        await RunPreconditionsAsync(host, r7);

        var admitted = await host.Records.CreateDraftAsync(Request(r7), Text(r7["requestId"]));

        Assert.Equal(Text(expected["mintedRecordTypeId"]), admitted.RecordTypeId);
        Assert.Equal(expected["catalogueKeysAfter"]!.AsArray().Select(Text),
            (await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records)).Select(key => key.DefinitionId));
    }

    [Fact]
    public async Task p2_a_save_at_the_loaded_revision_after_an_external_change_refuses_as_stale()
    {
        var p2 = Case(ParityFile, "P2-identity-revision-reset");
        var loaded = Step(p2, "loaded");
        var external = Step(p2, "externalRevisionChange");
        Assert.Equal(BaseCase + " expectedStoredBody", Text(loaded["body"]));
        var identity = Text(loaded["identity"]);
        var versionId = Text(loaded["versionId"]);
        var loadedRevision = loaded["expectedRevision"]!.GetValue<long>();
        var host = Host();
        var created = await host.Records.CreateDraftAsync(ToNewRecordType(BaseRequest()),
            Text(Case(GrammarFile, BaseCase)["createRequest"]!["requestId"]));
        Assert.Equal((identity, versionId, loadedRevision), (created.RecordTypeId, created.Revision.Document.VersionId, created.Revision.Revision));

        // The external change is the source test's resave of the authored body (RecordTypeDefinitionStoreTests.cs:188).
        var authored = ToDocument(BaseRequest(), identity);
        var resaved = await host.Records.SaveDraftAsync(identity, authored, versionId, loadedRevision, "resave");
        Assert.Equal(external["expectedRevision"]!.GetValue<long>(), resaved.Revision);

        var stale = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync(identity, authored, versionId, loadedRevision, "stale-save").AsTask());

        Assert.Equal(Pairs(p2["producerFact"]!["staleSave"]!), Pairs(stale));
        // The independent history-count oracle is RecordTypeDefinitionStoreTests.cs:197, not the revision number.
        Assert.Equal(2, (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, identity))).Count);
        Assert.Empty(await Registered(host.Registry));
    }

    [Fact]
    public async Task p3_a_kind_version_without_the_retention_capability_refuses_the_clock_the_base_version_admits()
    {
        var p3 = Case(ParityFile, "P3-kind-version-change");
        var inputs = p3["inputs"]!;
        var expected = p3["expected"]!;
        // The fixture spells the added field in prose; these guards bind the literal below to that prose.
        Assert.Contains("field_key:acquired_on", Text(inputs["base"]), StringComparison.Ordinal);
        Assert.Contains("kind_id:date,version:1.0.0", Text(inputs["base"]), StringComparison.Ordinal);
        Assert.Contains("retention_clock_field_id:acquired_on", Text(inputs["base"]), StringComparison.Ordinal);
        Assert.Equal("same, with binding.kind.version 2.0.0", Text(inputs["changed"]));
        Assert.Equal("admitted", Text(expected["base"]!["outcome"]));

        var baseHost = Host();
        var admitted = await baseHost.Records.CreateDraftAsync(WithRetentionClock("1.0.0"), "create");
        Assert.Equal("acquired_on",
            RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(admitted.Revision.Document.BodyJson)).RetentionClockFieldId);

        var changedHost = Host();
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => changedHost.Records.CreateDraftAsync(WithRetentionClock("2.0.0"), "create").AsTask());
        AssertRefused(expected["changed"]!, refused);
        Assert.Empty(await changedHost.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(changedHost.Registry));
    }

    [Fact]
    public async Task p4_a_request_with_an_unusable_name_and_an_unknown_kind_refuses_only_the_name()
    {
        var p4 = Case(ParityFile, "P4-read-only-and-ordering");
        Assert.Contains("records.identity.name_required at /name", Text(p4["producerOrdering"]!["fact"]), StringComparison.Ordinal);
        var name = Case(RefusalFile, "R1-punctuation-name");
        var kind = Case(RefusalFile, "R6-unknown-kind");
        var host = Host();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(Expand(name["replace"], kind["replace"]), Text(name["requestId"])).AsTask());

        AssertRefused(name["expected"]!, refused);
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(host.Registry));
    }

    private static NewRecordType WithRetentionClock(string dateVersion)
    {
        var request = BaseRequest();
        var acquired = JsonNode.Parse("""{"field_key":"acquired_on","display_name":"Acquired on","binding":{"kind":{"kind_id":"date","version":"1.0.0","parameters":{}},"constraints":{"required":true,"minimum_count":0,"maximum_count":1,"read_role_ids":[]}}}""")!;
        acquired["binding"]!["kind"]!["version"] = dateVersion;
        request["fields"]!.AsArray().Add(acquired);
        request["retentionClockFieldId"] = "acquired_on";
        return ToNewRecordType(request);
    }

    private static async Task RunPreconditionsAsync(TestHost host, JsonNode recipe)
    {
        foreach (var row in recipe["preconditions"]!.AsArray())
        {
            var precondition = row!.AsObject();
            RequireMembers(precondition, PreconditionMembers);
            RequireOperation(precondition["operation"], "CreateDraftAsync");
            var draft = await host.Records.CreateDraftAsync(Request(precondition), Text(precondition["requestId"]));
            if (precondition["expectedRevision"] is { } revision) Assert.Equal(revision.GetValue<long>(), draft.Revision.Revision);
        }
    }

    // A literal createRequest is taken whole; createRequestFrom is G1's request with the listed members replaced wholesale.
    private static NewRecordType Request(JsonNode recipe)
    {
        if (recipe["createRequest"] is JsonObject literal)
        {
            Assert.Null(recipe["createRequestFrom"]);
            return ToNewRecordType(literal.DeepClone().AsObject());
        }
        RequireOperation(recipe["createRequestFrom"], BaseCase);
        return Expand(recipe["replace"]);
    }

    private static NewRecordType Expand(params JsonNode?[] replaces)
    {
        var request = BaseRequest();
        foreach (var replace in replaces)
        {
            var members = replace!.AsObject();
            RequireMembers(members, ReplaceMembers);
            foreach (var (member, value) in members) request[member] = value?.DeepClone();
        }
        return ToNewRecordType(request);
    }

    private static JsonObject BaseRequest()
        => Case(GrammarFile, BaseCase)["createRequest"]!["newRecordType"]!.DeepClone().AsObject();

    private static NewRecordType ToNewRecordType(JsonObject request)
    {
        var document = ToDocument(request, "");
        return new(document.Envelope.Tenant, document.Envelope.Section, document.Name, Text(request["version"]),
            document.Fields, document.Envelope.Contract, RetentionClockFieldId: document.RetentionClockFieldId,
            ClassId: document.ClassId, RecordClass: document.RecordClass, PackageId: document.Envelope.PackageId);
    }

    // The fixture spells NewRecordType's members; the body members it carries are read by the producer's own parser.
    private static RecordTypeDocument ToDocument(JsonObject request, string recordTypeId)
    {
        RequireMembers(request, RequestMembers);
        var envelope = new JsonObject();
        Copy(request, "tenant", envelope, "tenant");
        Copy(request, "section", envelope, "section");
        Copy(request, "contract", envelope, "contract");
        Copy(request, "packageId", envelope, "package_id");
        var body = new JsonObject { ["envelope"] = envelope, ["record_type_id"] = recordTypeId };
        Copy(request, "name", body, "name");
        Copy(request, "fields", body, "fields");
        Copy(request, "classId", body, "class_id");
        Copy(request, "recordClass", body, "record_class");
        Copy(request, "retentionClockFieldId", body, "retention_clock_field_id");
        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body.ToJsonString()));
        Assert.Null(RecordTypeDefinitionJson.FirstMissing(document));
        return document;
    }

    private static void Copy(JsonObject from, string member, JsonObject to, string name)
    {
        if (from[member] is { } value) to[name] = value.DeepClone();
    }

    private static void AssertRefused(JsonNode expected, DefinitionRefusalException refused)
    {
        RequireMembers(expected.AsObject(), RefusedMembers);
        Assert.Equal("refused", Text(expected["outcome"]));
        if (expected["stage"] is { } stage) Assert.Equal(Enum.Parse<DefinitionAdmissionPhase>(Text(stage)), refused.Stage);
        Assert.Equal(Pairs(expected["refusals"]!), Pairs(refused));
    }

    private static void RequireMembers(JsonObject recipe, string[] supported)
    {
        foreach (var (member, _) in recipe)
            if (!supported.Contains(member)) throw new InvalidOperationException($"The fixture recipe member '{member}' is not supported.");
    }

    private static void RequireOperation(JsonNode? actual, string supported)
    {
        if (Text(actual) != supported) throw new InvalidOperationException($"The fixture recipe '{Text(actual)}' is not supported.");
    }

    private static JsonNode Step(JsonNode parity, string step)
        => parity["inputSequence"]!.AsArray().Single(row => Text(row!["step"]) == step)!;

    private static JsonNode Case(string file, string id)
        => Load(file)["cases"]!.AsArray().Single(row => Text(row!["id"]) == id)!;

    private static JsonNode Load(string file)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "records-editor-contract-fixtures", file)))!;

    private static string Text(JsonNode? node) => node!.GetValue<string>();

    private static (string Code, string Pointer)[] Pairs(JsonNode refusals)
        => refusals.AsArray().Select(refusal => (Text(refusal!["code"]), Text(refusal!["pointer"]))).ToArray();

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }
}
