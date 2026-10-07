using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 3 (DES-0015). A Record Type is a <see cref="DefinitionKind.Records"/> definition in the shared
/// versioned-definition store: the authoring boundary mints its section-scoped id, every save and publish runs
/// the Records identity rules and the schema compile before anything changes, and publication registers exactly
/// the compiled schema. Expected codes, pointers and bytes are literals.
/// </summary>
public sealed class RecordTypeDefinitionStoreTests
{
    internal const string Tenant = "tenant-a";
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    // The canonical body of the asset-class fixture, as the store holds it and the fixture file carries it: the
    // created bound fields carry their kind's governance defaults and that kind revision as provenance.
    internal const string AssetClassJson =
        """{"class_id":"eam.equipment","envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"fields":[{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":true},"kind":{"kind_id":"text","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"text","kind_version":"1.0.0"},"display_name":"Asset tag","field_key":"asset_tag","governance":{"classification":"internal","confidential":false,"masked":true,"personal_data":true}},{"binding":{"constraints":{"maximum_count":1,"minimum_count":0,"read_role_ids":[],"required":false},"kind":{"kind_id":"count","parameters":{},"version":"1.0.0"}},"defaults_provenance":{"kind_id":"count","kind_version":"1.0.0"},"display_name":"Quantity","field_key":"quantity"},{"display_name":"Notes","field_key":"notes"}],"name":"Asset Class","record_class":"master","record_type_id":"eam.asset-class"}""" + "\n";

    [Fact]
    [Trait("Holds", "records-ck-1")]
    public async Task a_created_record_type_publishes_reads_back_at_its_head_and_round_trips_byte_identically()
    {
        var host = Host();

        var created = await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        Assert.Equal("eam.asset-class", created.RecordTypeId);
        Assert.Equal(1, created.Revision.Revision);
        Assert.Equal(DefinitionStatus.Draft, created.Revision.Status);
        Assert.Equal(AssetClassJson, created.Revision.Document.BodyJson);

        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        Assert.Equal(DefinitionStatus.Published, published.Revision.Status);
        Assert.Equal(2, published.Revision.Revision);

        var head = Assert.IsType<RecordTypeDocument>(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(head)));
        Assert.Equal(AssetClassJson,
            Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(
                RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(AssetClassJson)))));
    }

    [Fact]
    [Trait("Holds", "records-ck-1")]
    public async Task the_shared_fixture_is_the_canonical_body_and_admits_for_install()
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "_shared", "records", "record-type.asset-class.json"));

        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(bytes));
        var report = await Host().Records.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(bytes)));
        Assert.Equal(DefinitionAdmissionPhase.Install, report.Stage);
        Assert.Empty(report.Refusals);
    }

    [Fact]
    [Trait("Holds", "records-auth-41")]
    public async Task the_minting_boundary_scopes_ids_by_section_and_refuses_a_collision_loudly()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "eam");

        // The same name in another section is another type.
        var finance = await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { Section = "finance" }, "finance");
        Assert.Equal("finance.asset-class", finance.RecordTypeId);

        // A name that slugs to an id the section already holds is never renumbered.
        foreach (var name in new[] { "Asset Class", "asset-class", "  ASSET  class " })
        {
            var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
                () => host.Records.CreateDraftAsync(AssetClass("1.0.0") with { Name = name }, "again-" + name).AsTask());
            Assert.Equal(DefinitionAdmissionPhase.Author, refused.Stage);
            Assert.Equal([("records.identity.record_type_id_collision", "/record_type_id")], Pairs(refused));
        }
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));

        // A replay of the original request is not a collision: it returns the original draft.
        var replay = await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "eam");
        Assert.Equal(1, replay.Revision.Revision);
        Assert.Equal(["eam.asset-class", "finance.asset-class"],
            (await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records)).Select(key => key.DefinitionId));
    }

    [Theory]
    [Trait("Holds", "records-auth-41")]
    [InlineData("", "Asset Class", "records.identity.section_invalid", "/envelope/section")]
    [InlineData("EAM", "Asset Class", "records.identity.section_invalid", "/envelope/section")]
    [InlineData("eam.sub", "Asset Class", "records.identity.section_invalid", "/envelope/section")]
    [InlineData("eam", " -- ", "records.identity.name_required", "/name")]
    [InlineData("eam", null, "records.identity.name_required", "/name")]
    public async Task the_minting_boundary_refuses_an_unusable_section_or_name(string section, string? name, string code, string pointer)
    {
        var host = Host();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(AssetClass("1.0.0") with { Section = section, Name = name! }, "create").AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
    }

    [Fact]
    [Trait("Holds", "records-eng-28")]
    public async Task a_client_constructed_id_is_refused_before_any_write()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var forged = Document() with { RecordTypeId = "eam.forged" };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.forged", forged, "1.0.0", 0, "forge").AsTask());

        Assert.Equal([("records.identity.record_type_id_unminted", "/record_type_id")], Pairs(refused));
        Assert.Equal(["eam.asset-class"], (await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records)).Select(key => key.DefinitionId));
    }

    [Fact]
    [Trait("Holds", "records-ck-2")]
    public async Task an_identity_change_refuses_within_a_version_and_across_versions()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        await host.Records.SaveDraftAsync("eam.asset-class", Document(), "1.1.0", 2, "draft-1.1.0");
        var renamed = Document() with { RecordTypeId = "eam.asset-kind" };

        var withinVersion = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", renamed, "1.1.0", 3, "rename-within").AsTask());
        var acrossVersions = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", renamed, "2.0.0", 3, "rename-across").AsTask());

        Assert.Equal([("records.identity.record_type_id_immutable", "/record_type_id")], Pairs(withinVersion));
        Assert.Equal([("records.identity.record_type_id_immutable", "/record_type_id")], Pairs(acrossVersions));
        Assert.Equal(3, (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"))).Count);

        // The raw store refuses the same body through the registered validator, so no caller can bypass it.
        var raw = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "2.0.0", "2.0.0",
                Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(renamed))), 3, "raw").AsTask());
        Assert.Equal([("records.identity.record_type_id_immutable", "/record_type_id")], Pairs(raw));
    }

    [Fact]
    [Trait("Holds", "records-ck-2")]
    public async Task a_missing_record_type_id_refuses_before_any_write()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", Document() with { RecordTypeId = "" }, "1.0.0", 1, "blank").AsTask());

        Assert.Equal([("records.identity.record_type_id_required", "/record_type_id")], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
    }

    [Fact]
    [Trait("Holds", "records-ck-4")]
    public async Task a_duplicate_field_key_refuses_before_any_write()
    {
        var host = Host();
        var duplicate = AssetClass("1.0.0") with
        {
            Fields = [new("asset_tag", "Asset tag"), new("asset_tag", "Asset tag again")],
        };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(duplicate, "create").AsTask());

        Assert.Equal([("records.identity.duplicate_field_key", "/fields/1/field_key")], Pairs(refused));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
    }

    [Fact]
    [Trait("Holds", "records-eng-1")]
    public async Task a_stale_expected_revision_refuses_save_and_publish_before_any_write()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        await host.Records.SaveDraftAsync("eam.asset-class", Document(), "1.0.0", 1, "resave");

        var staleSave = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", Document(), "1.0.0", 1, "stale-save").AsTask());
        var stalePublish = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "stale-publish").AsTask());

        Assert.Equal([("definition.revision_conflict", "/expectedRevision")], Pairs(staleSave));
        Assert.Equal([("definition.revision_conflict", "/expectedRevision")], Pairs(stalePublish));
        Assert.Equal(2, (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"))).Count);
        Assert.Empty(await Registered(host.Registry));
    }

    [Fact]
    [Trait("Holds", "records-ck-5")]
    public async Task a_compile_refusal_refuses_save_and_registers_nothing()
    {
        var host = Host();
        var unknownKind = AssetClass("1.0.0") with
        {
            Fields = [new("asset_tag", "Asset tag", Bound("unknown-kind", required: true))],
        };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.CreateDraftAsync(unknownKind, "create").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Author, refused.Stage);
        Assert.Equal([("field.kind_unresolved", "/fields/0/binding/kind")], Pairs(refused));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(host.Registry));
    }

    [Fact]
    [Trait("Holds", "records-eng-33")]
    public async Task publication_registers_the_schema_identity_the_runtime_validator_uses()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        Assert.Empty(await Registered(host.Registry));

        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var runtime = await host.Compiler.CompileAndRegisterAsync(Document().ToDefinition(), null, host.Registry);
        Assert.Equal(published.Schema.Id, Assert.IsType<Schema>(runtime.Schema).Id);
        Assert.Single(await Registered(host.Registry));
        Assert.True((await host.Registry.ValidateAsync(published.Schema.Id,
            Encoding.UTF8.GetBytes("""{"asset_tag":"A-1","quantity":3,"notes":"spare"}"""))).IsValid);
        Assert.False((await host.Registry.ValidateAsync(published.Schema.Id,
            Encoding.UTF8.GetBytes("""{"asset_tag":"A-1","quantity":"three"}"""))).IsValid);
        Assert.False((await host.Registry.ValidateAsync(published.Schema.Id,
            Encoding.UTF8.GetBytes("""{"asset_tag":"A-1","undeclared":1}"""))).IsValid);

        // A replayed publish returns the same revision and registers nothing new.
        var replay = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        Assert.Equal(published.Revision, replay.Revision);
        Assert.Equal(published.Schema.Id, replay.Schema.Id);
        Assert.Single(await Registered(host.Registry));
    }

    [Fact]
    [Trait("Holds", "records-ck-1")]
    public async Task a_published_version_is_immutable_and_restores_as_a_new_draft()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var overwrite = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", Document() with { Name = "Edited" }, "1.0.0", 2, "overwrite").AsTask());
        Assert.Equal([("definition.version_immutable", "/versionId")], Pairs(overwrite));

        var restored = await host.Records.RestoreAsDraftAsync(Tenant, "eam.asset-class", "1.0.0", "1.1.0", 2, "restore");
        Assert.Equal("1.0.0", restored.RestoredFromVersionId);
        Assert.Equal(AssetClassJson, restored.Document.BodyJson);
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.1.0", 3, "publish-1.1.0");

        var head = await host.Catalogue.GetPublishedHeadAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"));
        Assert.Equal("1.1.0", head!.Document.Version);
        Assert.Equal(AssetClassJson, head.Document.BodyJson);
    }

    [Fact]
    [Trait("Holds", "records-ck-1")]
    public async Task an_exported_version_admits_for_install_and_a_tampered_one_refuses()
    {
        var host = Host();
        var created = await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var draftExport = Assert.Throws<DefinitionRefusalException>(() => RecordTypeDefinitionPackageExporter.Export(created.Revision));
        Assert.Equal([("definition.published_version_required", "/versionId")], Pairs(draftExport));
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        var entry = RecordTypeDefinitionPackageExporter.Export(published.Revision);

        Assert.Equal(("eam.asset-class", "1.0.0", 5, 0, DefinitionKind.Records),
            (entry.DefinitionId, entry.Version, entry.ContentKind, entry.Primitive, entry.Kind));
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(entry.Content.Payload.Span));
        var receiving = Host();
        Assert.Empty((await receiving.Records.AdmitInstallAsync("tenant-b", entry)).Refusals);

        var retargeted = entry with { DefinitionId = "eam.asset-kind" };
        var outOfWindow = entry with
        {
            Content = PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(
                AssetClassJson.Replace("""{"major":1,"minor":0}""", """{"major":2,"minor":0}""", StringComparison.Ordinal))),
        };
        Assert.Equal([("records.identity.record_type_id_immutable", "/record_type_id")],
            Pairs(await receiving.Records.AdmitInstallAsync("tenant-b", retargeted)));
        Assert.Equal([("definition.contract.out_of_window", "/envelope/contract")],
            Pairs(await receiving.Records.AdmitInstallAsync("tenant-b", outOfWindow)));
    }

    [Fact]
    [Trait("Holds", "records-ck-1")]
    public async Task the_registered_validator_refuses_a_body_from_another_tenant_or_section()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var otherTenant = Document() with { Envelope = new("tenant-b", "eam", Contract) };
        var otherSection = Document() with { Envelope = new(Tenant, "finance", Contract) };

        var tenant = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", otherTenant, "1.0.0", 1, "tenant").AsTask());
        var section = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.SaveDraftAsync("eam.asset-class", otherSection, "1.0.0", 1, "section").AsTask());

        // The wrapper keys a body by its own envelope tenant, so another tenant's body names a stream that was never minted.
        Assert.Equal([("records.identity.record_type_id_unminted", "/record_type_id")], Pairs(tenant));
        Assert.Equal([("records.identity.section_mismatch", "/record_type_id")], Pairs(section));
        var raw = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0",
                Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(otherTenant))), 1, "raw").AsTask());
        Assert.Equal([("definition.catalogue_mismatch", "/envelope/tenant")], Pairs(raw));
    }

    [Theory]
    [Trait("Holds", "records-ck-1")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"name":"Asset Class","record_type_id":"eam.asset-class","fields":[],"undeclared":1}""", "records.document_invalid", "")]
    [InlineData("""{"name":"Asset Class","record_type_id":"eam.asset-class","fields":[]}""", "records.envelope_required", "/envelope")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"name":" ","record_type_id":"eam.asset-class","fields":[]}""", "records.identity.name_required", "/name")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"EAM","tenant":"tenant-a"},"name":"Asset Class","record_type_id":"eam.asset-class","fields":[]}""", "records.identity.section_invalid", "/envelope/section")]
    [InlineData("null", "records.document_invalid", "")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"name":"Asset Class","record_type_id":"eam.asset-class","fields":[{"display_name":"A","field_key":"a"},{"display_name":"B","field_key":"a"}]}""", "records.identity.duplicate_field_key", "/fields/1/field_key")]
    public async Task the_registered_validator_refuses_a_malformed_body_before_any_write(string body, string code, string pointer)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), 1, "raw").AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
    }

    [Fact]
    [Trait("Holds", "records-auth-41")]
    public async Task a_section_is_a_whole_id_segment_not_a_prefix()
    {
        var host = Host();
        var body = Document() with { RecordTypeId = "eamx.asset-class" };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eamx.asset-class"), "1.0.0", "1.0.0",
                Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(body))), 0, "raw").AsTask());

        Assert.Equal([("records.identity.section_mismatch", "/record_type_id")], Pairs(refused));
    }

    [Fact]
    [Trait("Holds", "records-eng-1")]
    public async Task an_unpublished_type_has_no_head_and_an_unknown_version_does_not_publish()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");

        Assert.Null(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "9.9.9", 1, "publish").AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([("definition.not_found", "/versionId")], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        Assert.Empty(await Registered(host.Registry));
    }

    [Fact]
    public async Task every_entry_point_refuses_a_missing_argument()
    {
        var host = Host();
        var compiler = host.Compiler;
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new RecordTypeDefinitionStore(null!, compiler, Defaults, host.Registry, Window)).ParamName);
        Assert.Equal("defaults", Assert.Throws<ArgumentNullException>(() => new RecordTypeDefinitionStore(host.Catalogue, compiler, null!, host.Registry, Window)).ParamName);
        Assert.Equal("compiler", Assert.Throws<ArgumentNullException>(() => new RecordTypeDefinitionStore(host.Catalogue, null!, Defaults, host.Registry, Window)).ParamName);
        Assert.Equal("registry", Assert.Throws<ArgumentNullException>(() => new RecordTypeDefinitionStore(host.Catalogue, compiler, Defaults, null!, Window)).ParamName);
        Assert.Equal("window", Assert.Throws<ArgumentNullException>(() => new RecordTypeDefinitionStore(host.Catalogue, compiler, Defaults, host.Registry, null!)).ParamName);
        Assert.Equal("window", Assert.Throws<ArgumentNullException>(() => RecordTypeDefinitionStore.Admission(null!, compiler.IntentValidator)).ParamName);
        Assert.Equal("validator", Assert.Throws<ArgumentNullException>(() => RecordTypeDefinitionStore.Admission(Window, null!)).ParamName);
        Assert.Equal("published", Assert.Throws<ArgumentNullException>(() => RecordTypeDefinitionPackageExporter.Export(null!)).ParamName);
        Assert.Equal("document", Assert.Throws<ArgumentNullException>(() => RecordTypeDefinitionJson.SerializeCanonical(null!)).ParamName);
        Assert.Equal("request", (await Assert.ThrowsAsync<ArgumentNullException>(() => host.Records.CreateDraftAsync(null!, "r").AsTask())).ParamName);
        Assert.Equal("document", (await Assert.ThrowsAsync<ArgumentNullException>(() => host.Records.SaveDraftAsync("eam.asset-class", null!, "1.0.0", 0, "r").AsTask())).ParamName);
        Assert.Equal("entry", (await Assert.ThrowsAsync<ArgumentNullException>(() => host.Records.AdmitInstallAsync(Tenant, null!).AsTask())).ParamName);
    }

    [Fact]
    [Trait("Holds", "records-ck-38")]
    public async Task creation_materializes_kind_defaults_once_with_provenance_and_keeps_an_authored_value()
    {
        var host = Host();
        var authored = new FieldGovernanceDefinition(false, true, false, "restricted");
        var request = AssetClass("1.0.0") with
        {
            Fields = [.. Document().Fields, new("serial", "Serial", Bound("text", required: false), authored)],
        };

        await host.Records.CreateDraftAsync(request, "create");

        var fields = (await Draft(host, "1.0.0")).Fields;
        Assert.Equal(new FieldGovernanceDefinition(true, false, true, "internal"), fields[0].Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), fields[0].DefaultsProvenance);
        Assert.Null(fields[1].Governance);
        Assert.Equal(new FieldKindDefaultProvenance("count", "1.0.0"), fields[1].DefaultsProvenance);
        Assert.Null(fields[2].Governance);
        Assert.Null(fields[2].DefaultsProvenance);
        Assert.Equal(authored, fields[3].Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), fields[3].DefaultsProvenance);
    }

    [Fact]
    [Trait("Holds", "records-auth-19")]
    public async Task an_edited_or_cleared_default_survives_later_saves_and_a_kind_change_keeps_its_provenance()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var created = await Draft(host, "1.0.0");
        var edited = created with
        {
            Fields =
            [
                created.Fields[0] with { Governance = new(false, false, false, null) },
                created.Fields[1],
                created.Fields[2],
            ],
        };
        await host.Records.SaveDraftAsync("eam.asset-class", edited, "1.0.0", 1, "edit");
        Assert.Equal(new FieldGovernanceDefinition(false, false, false, null), (await Draft(host, "1.0.0")).Fields[0].Governance);

        var cleared = edited with { Fields = [edited.Fields[0] with { Governance = null }, edited.Fields[1], edited.Fields[2]] };
        await host.Records.SaveDraftAsync("eam.asset-class", cleared, "1.0.0", 2, "clear");
        Assert.Null((await Draft(host, "1.0.0")).Fields[0].Governance);

        var rekinded = cleared with
        {
            Fields = [cleared.Fields[0] with { Binding = Bound("count", required: true) }, cleared.Fields[1], cleared.Fields[2]],
        };
        await host.Records.SaveDraftAsync("eam.asset-class", rekinded, "1.0.0", 3, "rekind");
        var after = (await Draft(host, "1.0.0")).Fields[0];
        Assert.Null(after.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), after.DefaultsProvenance);

        // The compile re-binds the new kind: the published schema now types asset_tag as an integer.
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 4, "publish");
        Assert.True((await host.Registry.ValidateAsync(published.Schema.Id, Encoding.UTF8.GetBytes("""{"asset_tag":7}"""))).IsValid);
        Assert.False((await host.Registry.ValidateAsync(published.Schema.Id, Encoding.UTF8.GetBytes("""{"asset_tag":"A-1"}"""))).IsValid);
    }

    [Fact]
    [Trait("Holds", "records-ck-38")]
    public async Task a_field_added_in_a_later_save_is_created_there_with_its_kinds_defaults()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var created = await Draft(host, "1.0.0");

        await host.Records.SaveDraftAsync("eam.asset-class",
            created with { Fields = [.. created.Fields, new("serial", "Serial", Bound("text", required: false))] },
            "1.0.0", 1, "add-serial");

        var serial = (await Draft(host, "1.0.0")).Fields[3];
        Assert.Equal(new FieldGovernanceDefinition(true, false, true, "internal"), serial.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("text", "1.0.0"), serial.DefaultsProvenance);
    }

    [Fact]
    [Trait("Holds", "records-ck-38")]
    public async Task a_field_whose_kind_declares_the_retention_clock_capability_may_start_the_clock()
    {
        var host = Host();
        var request = AssetClass("1.0.0") with
        {
            Fields = [.. Document().Fields, new("acquired_on", "Acquired on", Bound("date", required: true))],
            RetentionClockFieldId = "acquired_on",
        };

        await host.Records.CreateDraftAsync(request, "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Equal("acquired_on", (await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))!.RetentionClockFieldId);
    }

    [Theory]
    [Trait("Holds", "records-auth-21")]
    [InlineData("date", "2.0.0", "acquired_on", "records.retention.clock_capability_absent")]
    [InlineData("text", "1.0.0", "acquired_on", "records.retention.clock_capability_absent")]
    [InlineData(null, null, "acquired_on", "records.retention.clock_capability_absent")]
    [InlineData("date", "1.0.0", "disposed_on", "records.retention.clock_field_unresolved")]
    public async Task a_retention_clock_without_the_capability_or_without_its_field_refuses_before_any_write(
        string? kind, string? version, string clock, string code)
    {
        var host = Host();
        FieldBindingDefinition? binding = kind is null ? null : new(new(kind, version!, new Dictionary<string, string>()), new(true, 0, 1, [], null));
        var request = AssetClass("1.0.0") with
        {
            Fields = [.. Document().Fields, new("acquired_on", "Acquired on", binding)],
            RetentionClockFieldId = clock,
        };

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Records.CreateDraftAsync(request, "create").AsTask());

        Assert.Equal([(code, "/retention_clock_field_id")], Pairs(refused));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
        Assert.Empty(await Registered(host.Registry));
    }

    private static async Task<RecordTypeDocument> Draft(TestHost host, string version)
    {
        var history = await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"));
        return RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(history.Last(revision => revision.Document.Version == version).Document.BodyJson));
    }

    // One slotless Trait, so a test type can declare a trait a reference requires.
    private sealed class LocatableTraitSource : IRecordTraitSource
    {
        public TraitDefinition? Resolve(string traitId, string version)
            => traitId == "locatable" && version == "1.0.0" ? new("locatable", "1.0.0", []) : null;
    }

    internal sealed record TestHost(InMemoryVersionedDefinitionStore Catalogue, RecordTypeDefinitionStore Records,
        RecordTypeSchemaCompiler Compiler, InMemorySchemaRegistry Registry, ClassDefinitionStore Classes);

    private static readonly RecordFieldDefaults Defaults = new(new FieldKindRuntime(new FieldKindRegistry([])));

    internal static TestHost Host()
    {
        var kinds = new FieldKindRuntime(new FieldKindRegistry([
            new("count", "1.0.0", null, FieldScalarValueShape.Integer),
            new("text", "1.0.0", new FieldGovernanceDefinition(true, false, true, "internal"), FieldScalarValueShape.Text),
            new("date", "1.0.0", null, FieldScalarValueShape.Text, [FieldKindCapability.RetentionClock]),
            new("date", "2.0.0", null, FieldScalarValueShape.Text),
        ]));
        var validator = new RecordsIntentValidator(new LocatableTraitSource());
        var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window),
        });
        var classes = new ClassDefinitionStore(catalogue);
        classes.CreateDraftAsync(new(Tenant, "eam", "Equipment", "1.0.0", Contract), "equipment").AsTask().GetAwaiter().GetResult();
        classes.PublishAsync(Tenant, "eam.equipment", "1.0.0", 1, "equipment-publish").AsTask().GetAwaiter().GetResult();
        return new(catalogue, new RecordTypeDefinitionStore(catalogue, compiler, new RecordFieldDefaults(kinds), registry, Window), compiler, registry, classes);
    }

    internal static NewRecordType AssetClass(string version) => new(Tenant, "eam", "Asset Class", version,
        Document().Fields, Contract, ClassId: "eam.equipment", RecordClass: RecordClass.Master);

    private static RecordTypeDocument Document() => new(new(Tenant, "eam", Contract), "Asset Class", "eam.asset-class",
    [
        new("asset_tag", "Asset tag", Bound("text", required: true)),
        new("quantity", "Quantity", Bound("count", required: false)),
        new("notes", "Notes"),
    ], ClassId: "eam.equipment", RecordClass: RecordClass.Master);

    private static FieldBindingDefinition Bound(string kind, bool required)
        => new(new(kind, "1.0.0", new Dictionary<string, string>()), new(required, 0, 1, [], null));

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalReport report)
        => report.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Harborline.Platform.slnx"))) return directory.FullName;
        throw new InvalidOperationException("The platform repository root was not found above the test output.");
    }
}
