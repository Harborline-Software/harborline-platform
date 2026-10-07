using System.Text;
using Harborline.Foundation.Definitions;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 6 (DES-0015 records-ck-17, records-ck-18, records-auth-1, records-auth-2). A Class is an authored
/// definition under <see cref="DefinitionKind.Classes"/>, minted and scoped like a Record Type. A Record Type
/// publishes only with exactly one published Class of its tenant and a record class, neither defaulted.
/// The test host has already published the Class <c>eam.equipment</c>.
/// </summary>
public sealed class ClassDefinitionStoreTests
{
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    [Fact]
    [Trait("Holds", "records-auth-2")]
    public async Task a_class_is_minted_per_section_and_a_collision_is_loud()
    {
        var host = Host();

        var finance = await host.Classes.CreateDraftAsync(new(Tenant, "finance", "Equipment", "1.0.0", Contract), "finance");
        var collision = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Classes.CreateDraftAsync(new(Tenant, "eam", " equipment ", "1.0.0", Contract), "again").AsTask());

        Assert.Equal("finance.equipment", finance.Document.Key.DefinitionId);
        Assert.Equal("""{"class_id":"finance.equipment","envelope":{"contract":{"major":1,"minor":0},"section":"finance","tenant":"tenant-a"},"name":"Equipment"}""" + "\n",
            finance.Document.BodyJson);
        Assert.Equal([("records.identity.class_id_collision", "/class_id")], Pairs(collision));
        Assert.Equal(["eam.equipment", "finance.equipment"],
            (await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Classes)).Select(key => key.DefinitionId));
    }

    [Fact]
    [Trait("Holds", "records-eng-28")]
    public async Task a_class_id_is_never_constructed_or_changed()
    {
        var host = Host();
        var forged = new ClassDocument(new(Tenant, "eam", Contract), "Forged", "eam.forged");
        var renamed = new ClassDocument(new(Tenant, "eam", Contract), "Equipment", "eam.plant");

        var unminted = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Classes.SaveDraftAsync("eam.forged", forged, "1.0.0", 0, "forge").AsTask());
        var immutable = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Classes.SaveDraftAsync("eam.equipment", renamed, "1.1.0", 2, "rename").AsTask());

        Assert.Equal([("records.identity.class_id_unminted", "/class_id")], Pairs(unminted));
        Assert.Equal([("records.identity.class_id_immutable", "/class_id")], Pairs(immutable));
        Assert.Equal(2, (await host.Catalogue.ListHistoryAsync(ClassDefinitionStore.KeyOf(Tenant, "eam.equipment"))).Count);
    }

    [Theory]
    [Trait("Holds", "records-ck-17")]
    [InlineData(null, RecordClass.Master, "records.class.required", "/class_id")]
    [InlineData("eam.equipment", null, "records.record_class.required", "/record_class")]
    public async Task a_type_without_its_class_or_record_class_drafts_but_does_not_publish(
        string? classId, RecordClass? recordClass, string code, string pointer)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { ClassId = classId, RecordClass = recordClass }, "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([(code, pointer)], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        Assert.Empty(await Registered(host));
    }

    [Theory]
    [Trait("Holds", "records-ck-18")]
    [InlineData("eam.plant")]
    [InlineData("finance.equipment")]
    public async Task a_type_whose_class_is_unpublished_or_unknown_does_not_publish(string classId)
    {
        var host = Host();
        await host.Classes.CreateDraftAsync(new(Tenant, "eam", "Plant", "1.0.0", Contract), "plant-draft");
        await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { ClassId = classId }, "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());

        Assert.Equal([("records.class.unresolved", "/class_id")], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")));
        Assert.Empty(await Registered(host));
    }

    [Fact]
    [Trait("Holds", "records-ck-18")]
    public async Task another_tenants_class_is_not_this_tenants_home()
    {
        var host = Host();
        var other = new ClassDefinitionStore(host.Catalogue);
        await other.CreateDraftAsync(new("tenant-b", "eam", "Plant", "1.0.0", Contract), "b-plant");
        await other.PublishAsync("tenant-b", "eam.plant", "1.0.0", 1, "b-publish");
        await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { ClassId = "eam.plant" }, "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish").AsTask());

        Assert.Equal([("records.class.unresolved", "/class_id")], Pairs(refused));
    }

    [Fact]
    [Trait("Holds", "records-ck-17")]
    public async Task the_registered_validator_refuses_publishing_a_type_with_neither_member()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0") with { ClassId = null, RecordClass = null }, "create");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.PublishAsync(
            RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", 1, "raw-publish").AsTask());

        Assert.Equal([("records.class.required", "/class_id"), ("records.record_class.required", "/record_class")], Pairs(refused));
    }

    [Theory]
    [Trait("Holds", "records-ck-17")]
    [InlineData("reference", RecordClass.Reference)]
    [InlineData("master", RecordClass.Master)]
    [InlineData("transactional", RecordClass.Transactional)]
    public void the_record_class_is_a_named_wire_value(string wire, RecordClass expected)
    {
        var body = AssetClassJson.Replace("\"record_class\":\"master\"", $"\"record_class\":\"{wire}\"", StringComparison.Ordinal);

        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body));

        Assert.Equal(expected, document.RecordClass);
        Assert.Equal(body, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
    }

    [Theory]
    [Trait("Holds", "records-ck-17")]
    [InlineData("\"record_class\":1")]
    [InlineData("\"record_class\":\"shared\"")]
    public async Task an_unnamed_or_unknown_record_class_is_not_a_record_type(string member)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var body = AssetClassJson.Replace("\"record_class\":\"master\"", member, StringComparison.Ordinal);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class"), "1.0.0", "1.0.0", body), 1, "raw").AsTask());

        Assert.Equal([("records.document_invalid", "")], Pairs(refused));
    }

    [Theory]
    [Trait("Holds", "records-ck-17")]
    [InlineData("REFERENCE")]
    [InlineData("Reference")]
    [InlineData("MASTER")]
    [InlineData("Master")]
    [InlineData("TRANSACTIONAL")]
    [InlineData("Transactional")]
    [InlineData(" master ")]
    [InlineData("reference, master")]
    public async Task only_exact_lowercase_record_classes_pass_json_and_raw_store_admission(string wire)
    {
        // DES-0015 records-ck-17 names exactly reference, master and transactional.
        var body = AssetClassJson.Replace("\"record_class\":\"master\"", $"\"record_class\":\"{wire}\"", StringComparison.Ordinal);
        Assert.Throws<System.Text.Json.JsonException>(
            () => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body)));

        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var key = RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class");
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(key, "1.0.0", "1.0.0", body), 1, "raw").AsTask());

        Assert.Equal([("records.document_invalid", "")], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(key));
    }

    [Theory]
    [Trait("Holds", "records-auth-2")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"name":"Equipment","class_id":"eam.equipment","tag":"x"}""", "records.document_invalid", "")]
    [InlineData("""{"envelope":{"contract":{"major":1,"minor":0},"section":"eam","tenant":"tenant-a"},"name":"Equipment","class_id":""}""", "records.identity.class_id_required", "/class_id")]
    [InlineData("""{"name":"Equipment","class_id":"eam.equipment"}""", "records.envelope_required", "/envelope")]
    public async Task the_registered_validator_refuses_a_malformed_class_body(string body, string code, string pointer)
    {
        var host = Host();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.SaveDraftAsync(
            new(ClassDefinitionStore.KeyOf(Tenant, "eam.equipment"), "1.1.0", "1.1.0", body), 2, "raw").AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
    }

    [Fact]
    public void the_class_store_refuses_a_missing_argument()
    {
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new ClassDefinitionStore(null!)).ParamName);
        Assert.Equal("window", Assert.Throws<ArgumentNullException>(() => ClassDefinitionStore.Admission(null!)).ParamName);
    }

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private static async Task<List<Harborline.Kernel.SchemaValidation.Schema>> Registered(TestHost host)
    {
        var schemas = new List<Harborline.Kernel.SchemaValidation.Schema>();
        await foreach (var schema in host.Registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }
}
