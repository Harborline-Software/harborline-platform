using System.Text;
using Harborline.Contracts.Authorization;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615, records-ck-14 (declaration only): a field may name the one domain role that writes it, the only place read and
/// write diverge on one field (L075, L1008). The member is optional, so every existing body and digest is byte-identical;
/// an explicit null canonicalizes to absent and a present name is kept verbatim. Publication and installation resolve the
/// name through Access's role vocabulary exactly as they resolve a creation role, failing closed without one; a blank name
/// is undefined because no gate requires the member. Enforcing the role belongs to the write path. Expected bodies, codes
/// and pointers are literals spelled independently of the producer serializer.
/// </summary>
public sealed class RecordFieldWriteRoleTests
{
    // write_role_id sorts after field_key, the last member of the quantity and notes fixture fields (neither has governance).
    private static string WithNotesWriteRole(string json, string value) =>
        Splice(json, "\"field_key\":\"notes\"}", "\"field_key\":\"notes\",\"write_role_id\":" + value + "}");

    private static string WithQuantityWriteRole(string json, string value) =>
        Splice(json, "\"field_key\":\"quantity\"}", "\"field_key\":\"quantity\",\"write_role_id\":" + value + "}");

    // Every literal body below is spliced from a literal; a missing anchor fails here rather than leaving the body unchanged.
    private static string Splice(string json, string anchor, string replacement)
    {
        Assert.Equal(1, json.Split(anchor).Length - 1);
        return json.Replace(anchor, replacement, StringComparison.Ordinal);
    }

    private static NewRecordType WithWriteRoles(NewRecordType request, string? quantity, string? notes) => request with
    {
        Fields = [request.Fields[0], request.Fields[1] with { WriteRoleId = quantity }, request.Fields[2] with { WriteRoleId = notes }],
    };

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public void A_body_without_the_member_declares_no_write_role_and_round_trips_byte_identically()
    {
        var document = Read(AssetClassJson);

        Assert.All(document.Fields, field => Assert.Null(field.WriteRoleId));
        Assert.Equal(AssetClassJson, Canonical(document));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public void A_write_role_round_trips_byte_identically_from_json_and_from_a_typed_caller()
    {
        var body = WithNotesWriteRole(AssetClassJson, "\"asset-author\"");

        var document = Read(body);
        var legacy = Read(AssetClassJson);

        Assert.Equal("asset-author", document.Fields[2].WriteRoleId);
        Assert.Null(document.Fields[0].WriteRoleId);
        Assert.Null(document.Fields[1].WriteRoleId);
        Assert.Equal(body, Canonical(document));
        Assert.Equal(body, Canonical(legacy with
        {
            Fields = [legacy.Fields[0], legacy.Fields[1], legacy.Fields[2] with { WriteRoleId = "asset-author" }],
        }));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public void An_explicit_null_write_role_reads_as_absent_and_canonicalizes_away()
    {
        var body = WithNotesWriteRole(AssetClassJson, "null");

        var document = Read(body);

        Assert.Null(document.Fields[2].WriteRoleId);
        Assert.Equal(AssetClassJson, Canonical(document));
        Assert.Empty(Admit(body, DefinitionAdmissionPhase.Publish));
    }

    // The structural admission never resolves the role; only the store boundary holds the vocabulary. A present name is
    // never trimmed or case-folded.
    [Theory]
    [Trait("Holds", "records-ck-14")]
    [InlineData("\"nobody\"")]
    [InlineData("\"Asset-Author\"")]
    [InlineData("\"asset-author \"")]
    [InlineData("\"\"")]
    [InlineData("\"  \"")]
    public void A_present_write_role_is_kept_verbatim_and_admits_structurally_at_every_phase(string value)
    {
        var body = WithNotesWriteRole(AssetClassJson, value);

        Assert.Equal(body, Canonical(Read(body)));
        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Empty(Admit(body, phase));
    }

    [Theory]
    [Trait("Holds", "records-ck-14")]
    [InlineData("7")]
    [InlineData("true")]
    [InlineData("[\"asset-author\"]")]
    [InlineData("{\"name\":\"asset-author\",\"vocabulary\":\"tax.roles\"}")]
    public void A_non_string_write_role_is_a_malformed_document_at_every_phase(string value)
    {
        var body = WithNotesWriteRole(AssetClassJson, value);

        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Equal([("records.document_invalid", "")], Admit(body, phase));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task Publishing_resolved_write_roles_stores_them_and_leaves_the_record_schema_unchanged()
    {
        var plain = Host();
        await plain.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var plainPublication = await plain.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var declared = Host();
        await declared.Records.CreateDraftAsync(WithWriteRoles(AssetClass("1.0.0"), "asset-author", "meter-author"), "create");
        var declaredPublication = await declared.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Equal(CreatedAssetClassJson, plainPublication.Revision.Document.BodyJson);
        Assert.Equal(WithNotesWriteRole(WithQuantityWriteRole(CreatedAssetClassJson, "\"asset-author\""), "\"meter-author\""),
            declaredPublication.Revision.Document.BodyJson);
        Assert.Equal(DefinitionStatus.Published, declaredPublication.Revision.Status);
        Assert.Equal(plainPublication.Schema.Id, declaredPublication.Schema.Id);
    }

    // The creation-role tests' wrong-case and padded names, plus the blank names a gate-less member cannot call required:
    // each saves as a draft and refuses at publication.
    [Theory]
    [Trait("Holds", "records-ck-14")]
    [InlineData("nobody")]
    [InlineData("Asset-Author")]
    [InlineData("asset-author ")]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Publishing_a_write_role_outside_the_vocabulary_refuses_and_publishes_nothing(string roleId)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(WithWriteRoles(AssetClass("1.0.0"), null, roleId), "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([("records.write_role.unknown", "/fields/2/write_role_id")], Pairs(refused.Refusals));
        Assert.Null(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
        Assert.Equal([DefinitionStatus.Draft],
            (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"))).Select(revision => revision.Status));
        Assert.Empty(await Registered(host));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task Without_a_role_vocabulary_publication_fails_closed_and_diagnostics_skip_the_check()
    {
        var host = Host();
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);
        await bare.CreateDraftAsync(WithWriteRoles(AssetClass("1.0.0"), null, "asset-author"), "create");
        var draft = Read((await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")))[^1].Document.BodyJson);
        var unknown = draft with { Fields = [draft.Fields[0], draft.Fields[1], draft.Fields[2] with { WriteRoleId = "nobody" }] };

        Assert.Empty((await bare.DiagnoseAsync(Tenant, unknown)).Refusals);
        Assert.Empty((await host.Records.DiagnoseAsync(Tenant, draft)).Refusals);
        Assert.Equal([("records.write_role.unknown", "/fields/2/write_role_id")],
            Pairs((await host.Records.DiagnoseAsync(Tenant, unknown)).Refusals));
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => bare.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());
        Assert.Equal([("records.write_role.unknown", "/fields/2/write_role_id")], Pairs(refused.Refusals));
        Assert.Null(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task A_type_without_write_roles_publishes_and_installs_without_a_vocabulary()
    {
        var host = Host();
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);
        await bare.CreateDraftAsync(AssetClass("1.0.0"), "create");

        var published = await bare.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var installed = await bare.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(AssetClassJson))), NoDependencies);

        Assert.Equal(DefinitionStatus.Published, published.Revision.Status);
        Assert.Empty(installed.Refusals);
    }

    [Theory]
    [Trait("Holds", "records-ck-14")]
    [InlineData("asset-author", "vocabulary", false)]
    [InlineData("nobody", "vocabulary", true)]
    [InlineData("", "vocabulary", true)]
    [InlineData("asset-author", "none", true)]
    public async Task Installing_a_type_resolves_its_write_roles_and_fails_closed(string roleId, string vocabulary, bool refused)
    {
        var host = Host();
        var store = vocabulary == "none"
            ? new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window)
            : host.Records;
        var body = WithNotesWriteRole(AssetClassJson, $"\"{roleId}\"");

        var report = await store.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(body))), NoDependencies);

        Assert.Equal(DefinitionAdmissionPhase.Install, report.Stage);
        var expected = refused
            ? new[] { ("records.write_role.unknown", "/fields/2/write_role_id") }
            : Array.Empty<(string, string)>();
        Assert.Equal(expected, Pairs(report.Refusals));
    }

    // The unresolved reference (field 1) sits before a later field's write role (field 2), so checking each field's write
    // role and reference together would interleave them; the reference field carries a write role of its own.
    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task Write_role_refusals_follow_the_creation_role_in_field_order_and_precede_reference_refusals()
    {
        var host = Host();
        var legacy = Read(AssetClassJson);
        var document = legacy with
        {
            CreationGate = RecordCreationGate.Role,
            CreationRoleId = "nobody",
            Fields =
            [
                legacy.Fields[0],
                new("owner", "Owner", Reference: new("eam.missing", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block))
                {
                    WriteRoleId = "nobody",
                },
                legacy.Fields[2] with { WriteRoleId = "" },
            ],
        };
        (string, string)[] expected =
        [
            ("records.creation_role.unknown", "/creation_role_id"),
            ("records.write_role.unknown", "/fields/1/write_role_id"),
            ("records.write_role.unknown", "/fields/2/write_role_id"),
            ("records.reference.target_unresolved", "/fields/1/reference/target_type_id"),
        ];
        // The serializer only builds the input here; every expected pair above is a literal.
        var body = Canonical(document);
        await host.Catalogue.SaveDraftAsync(new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), 0, "create");

        var diagnosed = await host.Records.DiagnoseAsync(Tenant, document);
        var published = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());
        var installed = await host.Records.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(body))), NoDependencies);

        Assert.Equal(expected, Pairs(diagnosed.Refusals));
        Assert.Equal(expected, Pairs(published.Refusals));
        Assert.Equal(expected, Pairs(installed.Refusals));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task Each_field_s_write_role_resolves_on_its_own()
    {
        var host = Host();
        async Task<(string, string)[]> Install(string quantity, string notes) => Pairs((await host.Records.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(
                WithNotesWriteRole(WithQuantityWriteRole(AssetClassJson, quantity), notes)))), NoDependencies)).Refusals);

        Assert.Equal([("records.write_role.unknown", "/fields/1/write_role_id"), ("records.write_role.unknown", "/fields/2/write_role_id")],
            await Install("\"nobody\"", "\"nobody\""));
        Assert.Equal([("records.write_role.unknown", "/fields/2/write_role_id")], await Install("\"asset-author\"", "\"nobody\""));
        Assert.Equal([("records.write_role.unknown", "/fields/1/write_role_id")], await Install("\"nobody\"", "\"meter-author\""));
        Assert.Empty(await Install("\"asset-author\"", "\"meter-author\""));
    }

    [Fact]
    [Trait("Holds", "records-ck-14")]
    public async Task A_resave_without_the_write_role_clears_it_rather_than_carrying_it_forward()
    {
        var host = Host();
        var created = await host.Records.CreateDraftAsync(WithWriteRoles(AssetClass("1.0.0"), null, "asset-author"), "create");

        var resaved = await host.Records.SaveDraftAsync("eam.asset-class", Read(CreatedAssetClassJson), "1.0.0", 1, "resave");
        // A positional resave without the policies takes the path that carries them forward; the write role still clears.
        var positional = await host.Records.SaveDraftAsync("eam.asset-class",
            Read(WithNotesWriteRole(CreatedAssetClassJson, "\"asset-author\"")), "1.0.0", 2, "restore");
        var carried = await host.Records.SaveDraftAsync("eam.asset-class", Read(AssetClassJson), "1.0.0", 3, "positional");

        Assert.Equal(WithNotesWriteRole(CreatedAssetClassJson, "\"asset-author\""), created.Revision.Document.BodyJson);
        Assert.Equal(CreatedAssetClassJson, resaved.Document.BodyJson);
        Assert.Equal(WithNotesWriteRole(CreatedAssetClassJson, "\"asset-author\""), positional.Document.BodyJson);
        Assert.Equal(CreatedAssetClassJson, carried.Document.BodyJson);
    }

    [Fact]
    [Trait("Holds", "records-eng-1")]
    public async Task The_bind_path_never_re_resolves_a_write_role()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(WithWriteRoles(AssetClass("1.0.0"), null, "asset-author"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        // A host that later binds without the vocabulary, or whose vocabulary dropped the role, still binds the published type.
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);
        var dropped = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window,
            roles: RoleVocabulary.FromApi([]));

        foreach (var store in new[] { bare, dropped })
        {
            Assert.NotNull(await store.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class"));
            Assert.NotNull(await store.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class", "1.0.0"));
        }
    }

    private static async Task<List<Schema>> Registered(TestHost host)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in host.Registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    private static (string Code, string Pointer)[] Pairs(IReadOnlyList<DefinitionRefusal> refusals) =>
        refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static RecordTypeDocument Read(string json) => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));

    private static string Canonical(RecordTypeDocument document) =>
        Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document));

    private static (string Code, string Pointer)[] Admit(string body, DefinitionAdmissionPhase phase) =>
        Pairs(RecordTypeDefinitionStore.Admission(Window, new RecordsIntentValidator())(
            new DefinitionDocument(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), phase));
}
