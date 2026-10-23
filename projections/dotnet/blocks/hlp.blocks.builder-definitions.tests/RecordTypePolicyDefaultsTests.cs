using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T615 policy declarations only: ADR-0097 create defaults, legacy absence, save carry-forward and advisory publication
/// warnings. These tests claim neither ck-20 nor ck-21: amendment/history execution and retention authoring are separate.
/// Oracles are the agreed literal policy members/codes/pointers, unchanged legacy fixture and independently authored
/// new-create canonical body. No expected value is read from a production policy constant.
/// </summary>
public sealed class RecordTypePolicyDefaultsTests
{
    // Independently SHA-256-hashed UTF-8 of the literal CreatedAssetClassJson, including its trailing LF.
    private const string CreatedDigest = "37c5035be00babc54687bff8315c8c7a65daff77187be781753dcb724fe591b9";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task new_creation_records_the_exact_defaults_and_an_explicit_default_is_not_omitted(bool explicitPolicies)
    {
        var host = Host();
        var request = explicitPolicies
            ? AssetClass("1.0.0") with { AmendmentPolicy = new(true), HistoryPolicy = RecordHistoryPolicy.Enabled }
            : AssetClass("1.0.0");

        var created = await host.Records.CreateDraftAsync(request, "create");
        var replay = await host.Records.CreateDraftAsync(request, "create");

        Assert.Equal(CreatedAssetClassJson, created.Revision.Document.BodyJson);
        Assert.Equal(CreatedDigest, created.Revision.Digest);
        Assert.Equal(created.Revision, replay.Revision);
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        using var body = JsonDocument.Parse(created.Revision.Document.BodyJson);
        Assert.True(body.RootElement.GetProperty("amendment_policy").GetProperty("reason_required").GetBoolean());
        Assert.Equal("enabled", body.RootElement.GetProperty("history_policy").GetString());
        Assert.False(body.RootElement.TryGetProperty("retention_policy", out _));
        Assert.Equal(CreatedAssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(
            RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(CreatedAssetClassJson)))));
    }

    [Fact]
    public void an_absent_or_explicit_null_policy_is_legacy_and_never_becomes_a_default_during_serialization()
    {
        var legacy = Read(AssetClassJson);
        var explicitNull = Read("{\"amendment_policy\":null,\"history_policy\":null," + AssetClassJson[1..]);

        Assert.Null(legacy.AmendmentPolicy);
        Assert.Null(legacy.HistoryPolicy);
        Assert.Null(explicitNull.AmendmentPolicy);
        Assert.Null(explicitNull.HistoryPolicy);
        Assert.Equal(AssetClassJson, Canonical(legacy));
        Assert.Equal(AssetClassJson, Canonical(explicitNull));
    }

    [Fact]
    public async Task a_positional_resave_carries_both_latest_declarations_within_and_across_versions()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        // This is the existing editor's positional-constructor shape: it knows none of the additive init members.
        var legacyShape = Read(AssetClassJson);
        var positional = new RecordTypeDocument(legacyShape.Envelope, legacyShape.Name, legacyShape.RecordTypeId,
            legacyShape.Fields, legacyShape.Traits, legacyShape.RetentionClockFieldId, legacyShape.ClassId,
            legacyShape.RecordClass, legacyShape.CreationGate, legacyShape.CreationRoleId);

        var resaved = await host.Records.SaveDraftAsync("eam.asset-class", positional, "1.0.0", 1, "resave");
        var next = await host.Records.SaveDraftAsync("eam.asset-class", positional, "1.1.0", 2, "next");

        Assert.Equal(CreatedAssetClassJson, resaved.Document.BodyJson);
        Assert.Equal(CreatedAssetClassJson, next.Document.BodyJson);
        Assert.Equal(3, next.Revision);
    }

    [Theory]
    [InlineData("amendment")]
    [InlineData("history")]
    public async Task save_carries_only_the_policy_the_latest_draft_declares_without_defaulting_its_absent_peer(string policy)
    {
        var host = Host();
        await SeedLegacyAsync(host);
        var legacy = Read(AssetClassJson);
        var explicitPolicy = policy == "amendment"
            ? legacy with { AmendmentPolicy = new(true) }
            : legacy with { HistoryPolicy = RecordHistoryPolicy.Enabled };
        // The explicit declaration wins over the latest revision's absence; it is not a create default.
        var declared = await host.Records.SaveDraftAsync("eam.asset-class", explicitPolicy, "1.1.0", 1, "declare");
        var saved = await host.Records.SaveDraftAsync("eam.asset-class", legacy, "1.2.0", 2, "carry");
        var expected = policy == "amendment"
            ? "{\"amendment_policy\":{\"reason_required\":true}," + AssetClassJson[1..]
            : AssetClassJson.Replace("\"name\":\"Asset Class\"", "\"history_policy\":\"enabled\",\"name\":\"Asset Class\"", StringComparison.Ordinal);

        Assert.Equal(expected, declared.Document.BodyJson);
        Assert.Equal(expected, saved.Document.BodyJson);
        Assert.Equal(3, saved.Revision);
    }

    [Fact]
    public async Task a_legacy_save_publish_restore_and_install_preserve_absence_and_exact_bytes()
    {
        var host = Host();
        await SeedLegacyAsync(host);
        var saved = await host.Records.SaveDraftAsync("eam.asset-class", Read(AssetClassJson), "1.0.0", 1, "save");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 2, "publish");
        var restored = await host.Records.RestoreAsDraftAsync(Tenant, "eam.asset-class", "1.0.0", "1.1.0", 3, "restore");
        var restoredSave = await host.Records.SaveDraftAsync("eam.asset-class", Read(restored.Document.BodyJson), "1.1.0", 4, "save-restored");
        var entry = RecordTypeDefinitionPackageExporter.Export(published.Revision);
        var receiving = Host();

        var install = await receiving.Records.AdmitInstallAsync("tenant-b", entry, NoDependencies);

        Assert.Equal(AssetClassJson, saved.Document.BodyJson);
        Assert.Equal(AssetClassJson, published.Revision.Document.BodyJson);
        Assert.Equal(AssetClassJson, restored.Document.BodyJson);
        Assert.Equal(AssetClassJson, restoredSave.Document.BodyJson);
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(entry.Content.Payload.Span));
        Assert.Empty(install.Refusals);
        Assert.Empty(await receiving.Catalogue.ListKeysAsync("tenant-b", DefinitionKind.Records));
    }

    [Fact]
    public async Task a_stale_save_does_not_overwrite_a_new_policy_declaration()
    {
        var host = Host();
        await SeedLegacyAsync(host);
        await host.Records.SaveDraftAsync("eam.asset-class", Read(CreatedAssetClassJson), "1.0.0", 1, "declare");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Records.SaveDraftAsync(
            "eam.asset-class", Read(AssetClassJson), "1.0.0", 1, "stale").AsTask());

        Assert.Equal([("definition.revision_conflict", "/expectedRevision")], Pairs(refused));
        var history = await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"));
        Assert.Equal(2, history.Count);
        Assert.Equal(CreatedAssetClassJson, history[^1].Document.BodyJson);
    }

    [Fact]
    public async Task an_exact_legacy_save_replay_keeps_its_original_body_after_a_later_policy_declaration()
    {
        var host = Host();
        await SeedLegacyAsync(host);
        var first = await host.Records.SaveDraftAsync("eam.asset-class", Read(AssetClassJson), "1.0.0", 1, "resave");
        await host.Records.SaveDraftAsync("eam.asset-class", Read(CreatedAssetClassJson), "1.0.0", 2, "declare");

        var replay = await host.Records.SaveDraftAsync("eam.asset-class", Read(AssetClassJson), "1.0.0", 1, "resave");

        Assert.Equal(2, first.Revision);
        Assert.Equal(first, replay);
        Assert.Equal(AssetClassJson, replay.Document.BodyJson);
        var history = await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"));
        Assert.Equal(3, history.Count);
        Assert.Equal(CreatedAssetClassJson, history[^1].Document.BodyJson);
    }

    [Theory]
    [InlineData("\"amendment_policy\":\"reason_required\"")]
    [InlineData("\"amendment_policy\":\"reason-required\"")]
    [InlineData("\"amendment_policy\":0")]
    [InlineData("\"amendment_policy\":[]")]
    [InlineData("\"amendment_policy\":{}")]
    [InlineData("\"amendment_policy\":{\"reason_required\":false}")]
    [InlineData("\"amendment_policy\":{\"reason_required\":null}")]
    [InlineData("\"amendment_policy\":{\"reason_required\":\"true\"}")]
    [InlineData("\"amendment_policy\":{\"Reason_Required\":true}")]
    [InlineData("\"amendment_policy\":{\"reasonRequired\":true}")]
    [InlineData("\"amendment_policy\":{\"reason_required\":true,\"mode\":\"edit_in_place\"}")]
    [InlineData("\"amendment_policy\":{\"reason_required\":true,\"reason_required\":true}")]
    [InlineData("\"history_policy\":\"Enabled\"")]
    [InlineData("\"history_policy\":\"ENABLED\"")]
    [InlineData("\"history_policy\":\"enabled \"")]
    [InlineData("\"history_policy\":\"effective_dated\"")]
    [InlineData("\"history_policy\":\"disabled\"")]
    [InlineData("\"history_policy\":0")]
    [InlineData("\"history_policy\":false")]
    [InlineData("\"history_policy\":{}")]
    public async Task invalid_wire_policies_refuse_the_whole_document_with_an_empty_pointer_and_no_write(string member)
    {
        var host = Host();
        var body = "{" + member + "," + AssetClassJson[1..];
        Assert.Throws<JsonException>(() => Read(body));
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), 0, "invalid").AsTask());
        var installed = await host.Records.AdmitInstallAsync("tenant-b",
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(body))), NoDependencies);

        Assert.Equal(DefinitionAdmissionPhase.Author, refused.Stage);
        Assert.Equal([("records.document_invalid", "")], Pairs(refused));
        Assert.Equal(DefinitionAdmissionPhase.Install, installed.Stage);
        Assert.Equal([("records.document_invalid", "")], installed.Refusals.Select(item => (item.Code, item.Pointer)));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(host));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task unsupported_typed_policies_use_the_same_document_refusal_on_create_and_save(bool historyPolicy)
    {
        var host = Host();
        var request = historyPolicy
            ? AssetClass("1.0.0") with { HistoryPolicy = (RecordHistoryPolicy)42 }
            : AssetClass("1.0.0") with { AmendmentPolicy = new(false) };
        var createRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(request, "invalid-create").AsTask());
        Assert.Equal([("records.document_invalid", "")], Pairs(createRefusal));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        await SeedLegacyAsync(host);
        var document = historyPolicy
            ? Read(AssetClassJson) with { HistoryPolicy = (RecordHistoryPolicy)42 }
            : Read(AssetClassJson) with { AmendmentPolicy = new(false) };

        var saveRefusal = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Records.SaveDraftAsync(
            "eam.asset-class", document, "1.0.0", 1, "invalid-save").AsTask());

        Assert.Equal([("records.document_invalid", "")], Pairs(saveRefusal));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        Assert.Empty(await Registered(host));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task fresh_and_replayed_publication_warn_but_binding_and_diagnostics_do_not(bool legacy)
    {
        var host = Host();
        if (legacy) await SeedLegacyAsync(host);
        else await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");

        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var replay = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var head = Assert.IsType<RecordTypePublication>(await host.Records.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class"));
        var pinned = Assert.IsType<RecordTypePublication>(await host.Records.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class", "1.0.0"));
        var diagnosis = await host.Records.DiagnoseAsync(Tenant, Read(published.Revision.Document.BodyJson));

        Assert.Equal([("records.retention.unset", "/retention_policy")], published.PolicyWarnings.Select(item => (item.Code, item.Pointer)));
        Assert.Equal([("records.retention.unset", "/retention_policy")], replay.PolicyWarnings.Select(item => (item.Code, item.Pointer)));
        Assert.Equal(legacy ? AssetClassJson : CreatedAssetClassJson, published.Revision.Document.BodyJson);
        Assert.Equal(published.Revision, replay.Revision);
        Assert.Equal(published.Schema.Id, head.Schema.Id);
        Assert.Equal(published.Schema.Id, pinned.Schema.Id);
        Assert.Empty(head.PolicyWarnings);
        Assert.Empty(pinned.PolicyWarnings);
        Assert.Empty(new RecordTypePublication(published.Revision, published.Schema).PolicyWarnings);
        Assert.Empty(diagnosis.Refusals);
    }

    [Fact]
    public async Task a_new_create_export_hashes_to_its_stored_digest_against_an_independent_literal()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var exported = RecordTypeDefinitionPackageExporter.Export(published.Revision);

        Assert.Equal("eam.asset-class", exported.DefinitionId);
        Assert.Equal("1.0.0", exported.Version);
        Assert.Equal(CreatedAssetClassJson, Encoding.UTF8.GetString(exported.Content.Payload.Span));
        Assert.Equal((byte)'\n', exported.Content.Payload.Span[^1]);
        Assert.Equal(CreatedDigest, Convert.ToHexStringLower(SHA256.HashData(exported.Content.Payload.Span)));
        Assert.Equal(CreatedDigest, published.Revision.Digest);
        Assert.Equal("sha256:37c5035be00babc54687bff8315c8c7a65daff77187be781753dcb724fe591b9",
            CatalogueFieldSource.DigestOf(published.Revision));
    }

    private static ValueTask<DefinitionRevision> SeedLegacyAsync(TestHost host)
        => host.Catalogue.SaveDraftAsync(new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", AssetClassJson), 0, "legacy");

    private static RecordTypeDocument Read(string json) => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));

    private static string Canonical(RecordTypeDocument document) => Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document));

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(item => (item.Code, item.Pointer)).ToArray();

    private static async Task<List<Schema>> Registered(TestHost host)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in host.Registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }
}
