using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615: a field declares its offline conflict policy (records-ck-16, L1011: <c>ask</c>, the default, <c>additive</c> or
/// <c>latest_observation</c>, exact lowercase, never last-write-wins) and a Record Type declares its category tags
/// (records-ck-23, L103: many per type, independent of the package label). Both members are optional, so every existing
/// body and digest is byte-identical; an explicit <c>ask</c> and an empty tag list canonicalize to absent. Declaration only:
/// the offline runtime that raises and settles a conflict is records-eng-25. Expected bodies, codes and pointers are
/// literals spelled independently of the producer serializer.
/// </summary>
public sealed class RecordTypeConflictPolicyAndCategoriesTests
{
    // The asset class's third field, "Notes", has no binding; a conflict policy goes before display_name in ordinal key order.
    private const string NotesField = "{\"display_name\":\"Notes\"";

    private static string WithNotesConflictPolicy(string json, string value) =>
        json.Replace(NotesField, "{\"conflict_policy\":" + value + ",\"display_name\":\"Notes\"", StringComparison.Ordinal);

    // categories sorts before class_id, the legacy body's first member.
    private static string WithCategories(string json, string tags) =>
        json.Replace("{\"class_id\"", "{\"categories\":" + tags + ",\"class_id\"", StringComparison.Ordinal);

    // In the created body amendment_policy comes first, so categories goes between it and class_id.
    private static string WithCreatedCategories(string json, string tags) =>
        json.Replace("},\"class_id\"", "},\"categories\":" + tags + ",\"class_id\"", StringComparison.Ordinal);

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
    [Trait("Holds", "records-ck-23")]
    public void Category_tags_round_trip_byte_identically_in_authored_order()
    {
        var body = WithCategories(AssetClassJson, "[\"safety\",\"finance\",\"Safety\"]");

        var document = Read(body);

        Assert.Equal(["safety", "finance", "Safety"], document.Categories);
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

    [Theory]
    [Trait("Holds", "records-ck-23")]
    [InlineData("[\"\"]", "records.categories.blank", "/categories/0")]
    [InlineData("[\"safety\",\"  \"]", "records.categories.blank", "/categories/1")]
    [InlineData("[\"safety\",\"finance\",\"safety\"]", "records.categories.duplicate", "/categories/2")]
    public void A_blank_or_repeated_tag_is_refused_at_every_phase(string tags, string code, string pointer)
    {
        var body = WithCategories(AssetClassJson, tags);

        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Equal([(code, pointer)], Admit(body, phase));
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public void Every_blank_and_repeated_tag_is_named_with_its_own_pointer()
    {
        var body = WithCategories(AssetClassJson, "[\"a\",\"\",\"a\",\"b\",\"b\",\"a\"]");

        Assert.Equal(
            [
                ("records.categories.blank", "/categories/1"),
                ("records.categories.duplicate", "/categories/2"),
                ("records.categories.duplicate", "/categories/4"),
                ("records.categories.duplicate", "/categories/5"),
            ],
            Admit(body, DefinitionAdmissionPhase.Author));
    }

    [Theory]
    [Trait("Holds", "records-ck-23")]
    [InlineData("[null]", "/categories/0")]
    [InlineData("[\"safety\",null]", "/categories/1")]
    public void A_null_tag_is_a_malformed_document_at_its_pointer(string tags, string pointer) =>
        Assert.Equal([("records.document_invalid", pointer)], Admit(WithCategories(AssetClassJson, tags), DefinitionAdmissionPhase.Author));

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

        Assert.NotEqual(plainPublication.Revision.Document.BodyJson, declaredPublication.Revision.Document.BodyJson);
        Assert.Equal(plainPublication.Schema.Id, declaredPublication.Schema.Id);
    }

    [Fact]
    [Trait("Holds", "records-ck-23")]
    public async Task A_draft_with_a_repeated_tag_is_refused_before_the_catalogue_changes()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var tagged = Read(CreatedAssetClassJson) with { Categories = ["safety", "safety"] };

        var refusal = await Assert.ThrowsAsync<DefinitionRefusalException>(async () =>
            await host.Records.SaveDraftAsync("eam.asset-class", tagged, "1.0.0", 1, "resave"));

        Assert.Equal([("records.categories.duplicate", "/categories/1")], refusal.Refusals.Select(r => (r.Code, r.Pointer)).ToArray());
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
    }

    private static RecordTypeDocument Read(string json) => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));

    private static string Canonical(RecordTypeDocument document) =>
        Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document));

    private static (string Code, string Pointer)[] Admit(string body, DefinitionAdmissionPhase phase) =>
        RecordTypeDefinitionStore.Admission(Window, new RecordsIntentValidator())(
                new DefinitionDocument(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), phase)
            .Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
