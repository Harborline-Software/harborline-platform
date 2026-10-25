using System.Text;
using System.Text.Json;
using Harborline.Kernel.SchemaValidation;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615: a field declares its offline conflict policy (records-ck-16, L1011: <c>ask</c>, the default, <c>additive</c> or
/// <c>latest_observation</c>, exact lowercase, never last-write-wins) and a Record Type declares its category tags
/// (records-ck-23, L103: many per type, independent of the package label; any text tag is kept verbatim, because how tags
/// compare is not ruled). Both members are optional, so every existing body and digest is byte-identical; an explicit
/// <c>ask</c>, an explicit null and an empty tag list canonicalize to absent, and save does not carry either declaration
/// forward. Declaration only: the offline runtime that raises and settles a conflict is records-eng-25. Expected bodies,
/// codes and pointers are literals spelled independently of the producer serializer.
/// </summary>
public sealed class RecordTypeConflictPolicyAndCategoriesTests
{
    // The asset class's third field, "Notes", has no binding; a conflict policy goes before display_name in ordinal key order.
    private const string NotesField = "{\"display_name\":\"Notes\"";

    private static string WithNotesConflictPolicy(string json, string value) =>
        Splice(json, NotesField, "{\"conflict_policy\":" + value + ",\"display_name\":\"Notes\"");

    // categories sorts before class_id, the legacy body's first member.
    private static string WithCategories(string json, string tags) =>
        Splice(json, "{\"class_id\"", "{\"categories\":" + tags + ",\"class_id\"");

    // asset_tag is bound, so it passes through the kind defaults on creation; conflict_policy sorts before defaults_provenance.
    private static string WithAssetTagConflictPolicy(string json, string value) =>
        Splice(json, "\"defaults_provenance\":{\"kind_id\":\"text\"", "\"conflict_policy\":" + value + ",\"defaults_provenance\":{\"kind_id\":\"text\"");

    // Every literal body below is spliced from a literal; a missing anchor fails here rather than leaving the body unchanged.
    private static string Splice(string json, string anchor, string replacement)
    {
        Assert.Equal(1, json.Split(anchor).Length - 1);
        return json.Replace(anchor, replacement, StringComparison.Ordinal);
    }

    // In the created body amendment_policy comes first, so categories goes between it and class_id.
    private static string WithCreatedCategories(string json, string tags) =>
        Splice(json, "},\"class_id\"", "},\"categories\":" + tags + ",\"class_id\"");

    [Fact]
    [Trait("Holds", "records-ck-16")]
    [Trait("Holds", "records-ck-23")]
    public void A_body_without_either_member_reads_as_ask_and_untagged_and_round_trips_byte_identically()
    {
        var document = Read(AssetClassJson);

        Assert.All(document.Fields, field => Assert.Null(field.ConflictPolicy));
        Assert.Null(document.Categories);
        Assert.Equal(AssetClassJson, Canonical(document));
    }

    [Theory]
    [Trait("Holds", "records-ck-16")]
    [InlineData("\"additive\"", FieldConflictPolicy.Additive)]
    [InlineData("\"latest_observation\"", FieldConflictPolicy.LatestObservation)]
    public void A_non_default_conflict_policy_round_trips_byte_identically(string spelling, FieldConflictPolicy expected)
    {
        var body = WithNotesConflictPolicy(AssetClassJson, spelling);

        var document = Read(body);

        Assert.Equal(expected, document.Fields[2].ConflictPolicy);
        Assert.Null(document.Fields[0].ConflictPolicy);
        Assert.Equal(body, Canonical(document));
    }

    [Fact]
    [Trait("Holds", "records-ck-16")]
    public void An_explicit_ask_canonicalizes_to_the_absent_default_from_json_and_from_a_typed_caller()
    {
        var spelled = Read(WithNotesConflictPolicy(AssetClassJson, "\"ask\""));
        var legacy = Read(AssetClassJson);
        var typed = legacy with
        {
            Fields = [legacy.Fields[0] with { ConflictPolicy = FieldConflictPolicy.Ask }, legacy.Fields[1], legacy.Fields[2]],
        };

        Assert.Equal(FieldConflictPolicy.Ask, spelled.Fields[2].ConflictPolicy);
        Assert.Equal(AssetClassJson, Canonical(spelled));
        Assert.Equal(AssetClassJson, Canonical(typed));
    }

    [Fact]
    [Trait("Holds", "records-ck-16")]
    public void An_explicit_null_conflict_policy_reads_as_the_absent_default()
    {
        var body = WithNotesConflictPolicy(AssetClassJson, "null");

        var document = Read(body);

        Assert.Null(document.Fields[2].ConflictPolicy);
        Assert.Equal(AssetClassJson, Canonical(document));
        Assert.Empty(Admit(body, DefinitionAdmissionPhase.Publish));
    }

    [Theory]
    [Trait("Holds", "records-ck-16")]
    [InlineData("\"Ask\"")]
    [InlineData("\"ASK\"")]
    [InlineData("\"Additive\"")]
    [InlineData("\"latest-observation\"")]
    [InlineData("\"latestObservation\"")]
    [InlineData("\"LatestObservation\"")]
    [InlineData("\" additive\"")]
    [InlineData("\"\"")]
    [InlineData("\"last_write_wins\"")]
    [InlineData("\"latest_write\"")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("[\"ask\"]")]
    public void An_unknown_conflict_policy_spelling_is_a_malformed_document_at_every_phase(string spelling)
    {
        var body = WithNotesConflictPolicy(AssetClassJson, spelling);

        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Equal([("records.document_invalid", "")], Admit(body, phase));
    }

    [Fact]
    [Trait("Holds", "records-ck-16")]
    public void A_typed_caller_cannot_serialize_an_undefined_conflict_policy()
    {
        var legacy = Read(AssetClassJson);
        var undefined = legacy with
        {
            Fields = [legacy.Fields[0], legacy.Fields[1], legacy.Fields[2] with { ConflictPolicy = (FieldConflictPolicy)3 }],
        };

        Assert.Throws<JsonException>(() => RecordTypeDefinitionJson.SerializeCanonical(undefined));
    }

    [Fact]
    [Trait("Holds", "records-ck-16")]
    public async Task An_undefined_typed_conflict_policy_is_the_same_document_refusal_on_create_and_save()
    {
        var host = Host();
        var request = AssetClass("1.0.0");
        request = request with { Fields = [request.Fields[0], request.Fields[1], request.Fields[2] with { ConflictPolicy = (FieldConflictPolicy)3 }] };

        var createRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Records.CreateDraftAsync(request, "invalid-create").AsTask());

        Assert.Equal([("records.document_invalid", "")], Pairs(createRefusal));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var created = Read(CreatedAssetClassJson);
        var undefined = created with { Fields = [created.Fields[0], created.Fields[1], created.Fields[2] with { ConflictPolicy = (FieldConflictPolicy)3 }] };

        var saveRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Records.SaveDraftAsync("eam.asset-class", undefined, "1.0.0", 1, "invalid-save").AsTask());

        Assert.Equal([("records.document_invalid", "")], Pairs(saveRefusal));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        Assert.Empty(await Registered(host));
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public void Category_tags_round_trip_byte_identically_in_authored_order()
    {
        var body = WithCategories(AssetClassJson, "[\"safety\",\"finance\",\"site / north\",\"caf\\u00E9\"]");

        var document = Read(body);

        Assert.Equal(["safety", "finance", "site / north", "caf\u00E9"], document.Categories);
        Assert.Equal(body, Canonical(document));
        Assert.Empty(Admit(body, DefinitionAdmissionPhase.Publish));
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public void An_empty_tag_list_canonicalizes_to_absent_from_json_and_from_a_typed_caller()
    {
        Assert.Equal(AssetClassJson, Canonical(Read(WithCategories(AssetClassJson, "[]"))));
        Assert.Equal(AssetClassJson, Canonical(Read(AssetClassJson) with { Categories = [] }));
    }

    // How tags compare is not ruled (L103 is open), so no blank, trimming, normalization, character or duplicate rule is
    // adopted: every present text tag is admitted in every phase and kept verbatim.
    [Theory]
    [Trait("Holds", "records-ck-23")]
    [InlineData("[\"\"]")]
    [InlineData("[\"safety\",\"  \"]")]
    [InlineData("[\"safety\",\"finance\",\"safety\"]")]
    [InlineData("[\"safety\",\"Safety\"]")]
    [InlineData("[\" safety\",\"safety \"]")]
    [InlineData("[\"\\u200B\",\"safe\\u0007ty\"]")]
    [InlineData("[\"cafe\\u0301\"]")]
    public void Any_text_tag_is_admitted_at_every_phase_and_kept_verbatim_while_comparison_is_unruled(string tags)
    {
        var body = WithCategories(AssetClassJson, tags);

        Assert.Equal(body, Canonical(Read(body)));
        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Empty(Admit(body, phase));
    }

    [Theory]
    [Trait("Holds", "records-ck-23")]
    [InlineData("[null]", "/categories/0")]
    [InlineData("[\"safety\",null]", "/categories/1")]
    public void A_null_tag_is_a_malformed_document_at_its_pointer(string tags, string at) =>
        Assert.Equal([("records.document_invalid", at)], Admit(WithCategories(AssetClassJson, tags), DefinitionAdmissionPhase.Author));

    [Theory]
    [Trait("Holds", "records-ck-23")]
    [InlineData("[1]")]
    [InlineData("\"safety\"")]
    [InlineData("{\"safety\":true}")]
    public void A_non_text_tag_list_is_a_malformed_document(string tags) =>
        Assert.Equal([("records.document_invalid", "")], Admit(WithCategories(AssetClassJson, tags), DefinitionAdmissionPhase.Author));

    [Fact]
    [Trait("Holds", "records-ck-16")]
    [Trait("Holds", "records-ck-23")]
    public async Task The_authoring_boundary_stores_and_publishes_both_declarations_unchanged()
    {
        var host = Host();
        var request = AssetClass("1.0.0");
        request = request with
        {
            Fields = [request.Fields[0], request.Fields[1], request.Fields[2] with { ConflictPolicy = FieldConflictPolicy.LatestObservation }],
            Categories = ["safety", "finance"],
        };
        var expected = WithCreatedCategories(WithNotesConflictPolicy(CreatedAssetClassJson, "\"latest_observation\""), "[\"safety\",\"finance\"]");

        var draft = await host.Records.CreateDraftAsync(request, "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Equal(expected, draft.Revision.Document.BodyJson);
        Assert.Equal(expected, published.Revision.Document.BodyJson);
    }

    [Fact]
    [Trait("Holds", "records-ck-16")]
    [Trait("Holds", "records-ck-23")]
    public async Task The_declarations_change_the_definition_but_not_the_schema_its_records_validate_against()
    {
        var plain = Host();
        await plain.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var plainPublication = await plain.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var declared = Host();
        var request = AssetClass("1.0.0");
        await declared.Records.CreateDraftAsync(request with
        {
            Fields = [request.Fields[0] with { ConflictPolicy = FieldConflictPolicy.Additive }, request.Fields[1], request.Fields[2]],
            Categories = ["safety"],
        }, "create");
        var declaredPublication = await declared.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Equal(CreatedAssetClassJson, plainPublication.Revision.Document.BodyJson);
        Assert.Equal(WithCreatedCategories(WithAssetTagConflictPolicy(CreatedAssetClassJson, "\"additive\""), "[\"safety\"]"),
            declaredPublication.Revision.Document.BodyJson);
        Assert.Equal(plainPublication.Schema.Id, declaredPublication.Schema.Id);
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public async Task A_draft_with_a_null_tag_is_refused_before_the_catalogue_changes()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var tagged = Read(CreatedAssetClassJson) with { Categories = ["safety", null!] };

        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await host.Records.SaveDraftAsync("eam.asset-class", tagged, "1.0.0", 1, "resave"));

        Assert.Equal([("records.document_invalid", "/categories/1")], refusal.Refusals.Select(r => (r.Code, r.Pointer)).ToArray());
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public async Task A_resave_without_the_declarations_clears_them_rather_than_carrying_them_forward()
    {
        var host = Host();
        var request = AssetClass("1.0.0");
        await host.Records.CreateDraftAsync(request with
        {
            Fields = [request.Fields[0], request.Fields[1], request.Fields[2] with { ConflictPolicy = FieldConflictPolicy.Additive }],
            Categories = ["safety"],
        }, "create");

        var resaved = await host.Records.SaveDraftAsync("eam.asset-class", Read(CreatedAssetClassJson), "1.0.0", 1, "resave");

        Assert.Equal(CreatedAssetClassJson, resaved.Document.BodyJson);
    }

    private static async Task<List<Schema>> Registered(TestHost host)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in host.Registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refusal) =>
        refusal.Refusals.Select(r => (r.Code, r.Pointer)).ToArray();

    private static RecordTypeDocument Read(string json) => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));

    private static string Canonical(RecordTypeDocument document) =>
        Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document));

    private static (string Code, string Pointer)[] Admit(string body, DefinitionAdmissionPhase phase) =>
        RecordTypeDefinitionStore.Admission(Window, new RecordsIntentValidator())(
                new DefinitionDocument(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), phase)
            .Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
