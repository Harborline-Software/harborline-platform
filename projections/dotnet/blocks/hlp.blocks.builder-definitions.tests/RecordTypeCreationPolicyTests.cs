using System.Text;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// Plan W2 (T-615 producer), decision D1: a Record Type declares its creation gate (records-ck-22: <c>form_only</c>, the
/// default, <c>role</c> or <c>engine</c>, exact lowercase) and, for a role gate only, its one <c>creation_role_id</c>. Both
/// members are optional, so every existing body, digest and the goal-4 canonical literal are byte-identical; an explicit
/// <c>form_only</c> canonicalizes to absent. Publication resolves the creation role through Access's role vocabulary,
/// failing closed; the bind path never re-checks it. Codes, pointers and bytes are literals.
/// </summary>
public sealed class RecordTypeCreationPolicyTests
{
    private const string ClassIdMember = "\"class_id\":\"eam.equipment\",";

    // AssetClassJson with creation members inserted in canonical (ordinal) key order, right after class_id.
    private static string WithCreation(string members) => AssetClassJson.Replace(ClassIdMember, ClassIdMember + members, StringComparison.Ordinal);

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public void A_body_without_creation_members_round_trips_byte_identically_and_reads_as_form_only()
    {
        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(AssetClassJson));

        Assert.Null(document.CreationGate);
        Assert.Null(document.CreationRoleId);
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.FormOnly, null), RecordCreationPolicy.Of(document));
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public void An_explicit_form_only_canonicalizes_to_the_absent_default()
    {
        var spelled = WithCreation("\"creation_gate\":\"form_only\",");

        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(spelled));

        Assert.Equal(RecordCreationGate.FormOnly, document.CreationGate);
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(
            RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(AssetClassJson)) with { CreationGate = RecordCreationGate.FormOnly })));
    }

    [Theory]
    [Trait("Holds", "records-ck-22")]
    [InlineData("\"creation_gate\":\"role\",\"creation_role_id\":\"asset-author\",", "Role", "asset-author")]
    [InlineData("\"creation_gate\":\"engine\",", "Engine", null)]
    public void A_role_or_engine_gate_round_trips_byte_identically(string members, string gate, string? roleId)
    {
        var body = WithCreation(members);

        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body));

        Assert.Equal(Enum.Parse<RecordCreationGate>(gate), document.CreationGate);
        Assert.Equal(roleId, document.CreationRoleId);
        Assert.Equal(body, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
    }

    [Theory]
    [Trait("Holds", "records-ck-22")]
    [InlineData("\"creation_gate\":\"Role\",")]
    [InlineData("\"creation_gate\":\"FORM_ONLY\",")]
    [InlineData("\"creation_gate\":\"form-only\",")]
    [InlineData("\"creation_gate\":\"formOnly\",")]
    [InlineData("\"creation_gate\":\"FormOnly\",")]
    [InlineData("\"creation_gate\":\" role\",")]
    [InlineData("\"creation_gate\":\"\",")]
    [InlineData("\"creation_gate\":\"nobody\",")]
    [InlineData("\"creation_gate\":1,")]
    [InlineData("\"creation_gate\":0,")]
    [InlineData("\"creation_gate\":true,")]
    [InlineData("\"creation_gate\":[\"role\"],")]
    [InlineData("\"creation_role_id\":7,")]
    public void An_unknown_creation_spelling_is_a_malformed_document_at_every_phase(string members)
    {
        var body = WithCreation(members);

        foreach (var phase in new[] { DefinitionAdmissionPhase.Author, DefinitionAdmissionPhase.Publish, DefinitionAdmissionPhase.Install })
            Assert.Equal([("records.document_invalid", "")], Admit(body, phase));
    }

    [Theory]
    [Trait("Holds", "records-ck-22")]
    [InlineData("\"creation_gate\":\"role\",", "records.creation_role.required")]
    [InlineData("\"creation_gate\":\"role\",\"creation_role_id\":\"\",", "records.creation_role.required")]
    [InlineData("\"creation_gate\":\"role\",\"creation_role_id\":\"  \",", "records.creation_role.required")]
    [InlineData("\"creation_role_id\":\"asset-author\",", "records.creation_role.unexpected")]
    [InlineData("\"creation_gate\":\"form_only\",\"creation_role_id\":\"asset-author\",", "records.creation_role.unexpected")]
    [InlineData("\"creation_gate\":\"engine\",\"creation_role_id\":\"asset-author\",", "records.creation_role.unexpected")]
    public void A_role_gate_names_exactly_its_one_creation_role_outside_the_author_stage(string members, string code)
    {
        var body = WithCreation(members);

        Assert.Empty(Admit(body, DefinitionAdmissionPhase.Author));
        Assert.Equal([(code, "/creation_role_id")], Admit(body, DefinitionAdmissionPhase.Publish));
        Assert.Equal([(code, "/creation_role_id")], Admit(body, DefinitionAdmissionPhase.Install));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public void A_complete_role_gate_and_an_absent_gate_admit_at_publication()
    {
        Assert.Empty(Admit(WithCreation("\"creation_gate\":\"role\",\"creation_role_id\":\"asset-author\","), DefinitionAdmissionPhase.Publish));
        Assert.Empty(Admit(AssetClassJson, DefinitionAdmissionPhase.Publish));
        Assert.Empty(Admit(WithCreation("\"creation_gate\":\"engine\","), DefinitionAdmissionPhase.Publish));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public async Task The_authoring_boundary_stores_the_declared_policy_and_publication_resolves_the_role()
    {
        var host = Host();

        var draft = await host.Records.CreateDraftAsync(RoleGatedAssetClass("1.0.0"), "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        // Independent fresh-authoring literal includes ADR-0097 defaults; legacy round-trip literals stay absent.
        Assert.Equal(CreatedAssetClassJson.Replace(ClassIdMember,
            ClassIdMember + "\"creation_gate\":\"role\",\"creation_role_id\":\"asset-author\",", StringComparison.Ordinal),
            draft.Revision.Document.BodyJson);
        Assert.Equal(DefinitionStatus.Published, published.Revision.Status);
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.Role, "asset-author"),
            await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class"));
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.Role, "asset-author"),
            await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class", "1.0.0"));
    }

    [Theory]
    [Trait("Holds", "records-ck-22")]
    [InlineData("tenant-admin")]
    [InlineData("Asset-Author")]
    [InlineData("asset-author ")]
    public async Task Publishing_a_role_gate_whose_role_is_not_in_the_vocabulary_refuses_and_publishes_nothing(string roleId)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { CreationGate = RecordCreationGate.Role, CreationRoleId = roleId }, "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([("records.creation_role.unknown", "/creation_role_id")], Pairs(refused.Refusals));
        Assert.Null(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
        Assert.Null(await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class"));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public async Task Without_a_role_vocabulary_publication_fails_closed_and_diagnostics_skip_the_check()
    {
        var host = Host();
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);
        await bare.CreateDraftAsync(RoleGatedAssetClass("1.0.0"), "create");
        var draft = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(
            (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")))[^1].Document.BodyJson));

        Assert.Empty((await bare.DiagnoseAsync(Tenant, draft)).Refusals);
        Assert.Empty((await host.Records.DiagnoseAsync(Tenant, draft)).Refusals);
        Assert.Equal([("records.creation_role.unknown", "/creation_role_id")],
            Pairs((await host.Records.DiagnoseAsync(Tenant, draft with { CreationRoleId = "nobody" })).Refusals));
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => bare.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());
        Assert.Equal([("records.creation_role.unknown", "/creation_role_id")], Pairs(refused.Refusals));
        Assert.Null(await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"));
    }

    [Fact]
    [Trait("Holds", "records-eng-1")]
    public async Task The_bind_path_never_re_resolves_the_creation_role()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(RoleGatedAssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        // A host that later binds without the vocabulary (or whose vocabulary dropped the role) still binds the published type.
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);

        Assert.NotNull(await bare.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class"));
        Assert.NotNull(await bare.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class", "1.0.0"));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public async Task An_existing_body_without_the_member_still_publishes_binds_and_reads_as_form_only()
    {
        var host = Host();
        await host.Catalogue.SaveDraftAsync(new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0",
            AssetClassJson), 0, "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Equal(AssetClassJson, published.Revision.Document.BodyJson);
        Assert.NotNull(await host.Records.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class"));
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.FormOnly, null), await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class"));
        Assert.Null(await host.Records.GetCreationPolicyAsync(Tenant, "eam.unpublished-type"));
        Assert.Null(await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class", "9.9.9"));
        Assert.Null(await host.Records.GetCreationPolicyAsync("tenant-b", "eam.asset-class"));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public async Task The_policy_is_read_per_published_version_and_the_head_is_the_newest()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(RoleGatedAssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var head = (await host.Records.GetPublishedHeadAsync(Tenant, "eam.asset-class"))!;
        await host.Records.SaveDraftAsync("eam.asset-class", head with { CreationGate = RecordCreationGate.Role, CreationRoleId = "meter-author" },
            "1.1.0", 2, "draft-1.1.0");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.1.0", 3, "publish-1.1.0");

        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.Role, "meter-author"), await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class"));
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.Role, "asset-author"), await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class", "1.0.0"));
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.Role, "meter-author"), await host.Records.GetCreationPolicyAsync(Tenant, "eam.asset-class", "1.1.0"));
    }

    [Fact]
    [Trait("Holds", "records-ck-22")]
    public void An_explicit_null_creation_gate_reads_as_the_absent_default()
    {
        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(WithCreation("\"creation_gate\":null,")));

        Assert.Null(document.CreationGate);
        Assert.Equal(new RecordCreationPolicy(RecordCreationGate.FormOnly, null), RecordCreationPolicy.Of(document));
        Assert.Equal(AssetClassJson, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
        Assert.Empty(Admit(WithCreation("\"creation_gate\":null,"), DefinitionAdmissionPhase.Publish));
    }

    [Theory]
    [Trait("Holds", "records-ck-41")]
    [InlineData("asset-author", "vocabulary", false)]
    [InlineData("tenant-admin", "vocabulary", true)]
    [InlineData("asset-author", "none", true)]
    public async Task Installing_a_role_gated_type_resolves_its_creation_role_and_fails_closed(string roleId, string vocabulary, bool refused)
    {
        var host = Host();
        var store = vocabulary == "none"
            ? new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window)
            : host.Records;
        var body = WithCreation($"\"creation_gate\":\"role\",\"creation_role_id\":\"{roleId}\",");

        var report = await store.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(body))), NoDependencies);

        Assert.Equal(DefinitionAdmissionPhase.Install, report.Stage);
        var expected = refused
            ? new[] { ("records.creation_role.unknown", "/creation_role_id") }
            : Array.Empty<(string, string)>();
        Assert.Equal(expected, Pairs(report.Refusals));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task Installing_a_type_without_a_role_gate_needs_no_vocabulary()
    {
        var host = Host();
        var bare = new RecordTypeDefinitionStore(host.Catalogue, host.Compiler, host.Defaults, host.Registry, Window);

        var report = await bare.AdmitInstallAsync(Tenant,
            new("eam.asset-class", "1.0.0", PlatformPackageContent.PresentJson(Encoding.UTF8.GetBytes(AssetClassJson))), NoDependencies);

        Assert.Empty(report.Refusals);
    }

    [Fact]
    public void The_policy_of_a_null_document_is_a_caller_error() =>
        Assert.Throws<ArgumentNullException>(() => RecordCreationPolicy.Of(null!));

    private static (string Code, string Pointer)[] Admit(string body, DefinitionAdmissionPhase phase) =>
        Pairs(RecordTypeDefinitionStore.Admission(Window, new RecordsIntentValidator())(
            new DefinitionDocument(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), phase));

    private static (string Code, string Pointer)[] Pairs(IReadOnlyList<DefinitionRefusal> refusals) =>
        refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
