using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 7 (DES-0015 records-ck-10..12, records-auth-14..16, records-auth-33; ADR-0026; ADR-0054; L1424,
/// L1426; owner ruling 2026-10-07). A reference is a field naming exactly one target type or one target Class, with
/// a declared cardinality and delete behaviour, an optional required trait on the target, and at most one parent
/// edge per type. Its stored value is a qualified reference, the target's type with its id.
/// </summary>
public sealed class RecordReferenceTests
{
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    [Fact]
    [Trait("Holds", "records-ck-10")]
    public async Task a_type_targeted_reference_publishes_and_its_value_is_a_qualified_reference_of_that_type()
    {
        var host = await WithAssetClass();
        await Create(host, "Work Order", Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block));

        var published = await host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "publish");

        Assert.True(await Valid(host, published, """{"asset":{"type":"eam.asset-class","id":"A-1"}}"""));
        Assert.False(await Valid(host, published, """{"asset":{"type":"eam.pump","id":"A-1"}}"""));
        Assert.False(await Valid(host, published, """{"asset":{"type":"eam.asset-class"}}"""));
        Assert.False(await Valid(host, published, """{"asset":{"type":"eam.asset-class","id":""}}"""));
        Assert.False(await Valid(host, published, """{"asset":"A-1"}"""));
    }

    [Fact]
    [Trait("Holds", "records-ck-10")]
    public async Task a_class_targeted_many_reference_admits_any_member_type_qualified()
    {
        var host = Host();
        await Create(host, "Work Order", Reference("equipment", null, "eam.equipment", ReferenceCardinality.Many, ReferenceDeleteBehavior.Orphan));

        var published = await host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "publish");

        Assert.True(await Valid(host, published, """{"equipment":[{"type":"eam.pump","id":"P-1"},{"type":"eam.valve","id":"V-9"}]}"""));
        Assert.False(await Valid(host, published, """{"equipment":{"type":"eam.pump","id":"P-1"}}"""));
        Assert.False(await Valid(host, published, """{"equipment":[{"type":"","id":"P-1"}]}"""));
    }

    [Fact]
    [Trait("Holds", "records-ck-10")]
    public void a_reference_field_round_trips_its_named_wire_values()
    {
        const string field = """{"display_name":"Asset","field_key":"asset","reference":{"cardinality":"many","on_delete":"cascade","parent":false,"required_trait_id":"locatable","target_class_id":"eam.equipment"}}""";
        var body = AssetClassJson.Replace("""{"display_name":"Notes","field_key":"notes"}""", field, StringComparison.Ordinal);

        var document = RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(body));

        Assert.Equal(new RecordReferenceDefinition(null, "eam.equipment", ReferenceCardinality.Many, ReferenceDeleteBehavior.Cascade, "locatable"),
            document.Fields[2].Reference);
        Assert.Equal(body, Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(document)));
    }

    public static TheoryData<string, FieldDefinition[], string, string> Malformed() => new()
    {
        { "both targets", [Reference("asset", "eam.asset-class", "eam.equipment", ReferenceCardinality.One, ReferenceDeleteBehavior.Block)], "records.reference.target_ambiguous", "/fields/0/reference" },
        { "no target", [Reference("asset", null, " ", ReferenceCardinality.One, ReferenceDeleteBehavior.Block)], "records.reference.target_ambiguous", "/fields/0/reference" },
        { "no cardinality", [Reference("asset", "eam.asset-class", null, null, ReferenceDeleteBehavior.Block)], "records.reference.cardinality_required", "/fields/0/reference/cardinality" },
        { "no delete behaviour", [Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, null)], "records.reference.on_delete_required", "/fields/0/reference/on_delete" },
        { "blank trait", [Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: " ")], "records.reference.trait_invalid", "/fields/0/reference/required_trait_id" },
        { "bound reference", [Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block) with { Binding = new(new("text", "1.0.0", new Dictionary<string, string>()), new(false, 0, 1, [], null)) }], "records.reference.binding_conflict", "/fields/0/reference" },
        { "many parents", [Reference("site", "eam.site", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, parent: true), Reference("building", "eam.building", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, parent: true)], "records.reference.parent_ambiguous", "/fields/1/reference/parent" },
        { "many-valued parent", [Reference("site", "eam.site", null, ReferenceCardinality.Many, ReferenceDeleteBehavior.Block, parent: true)], "records.reference.parent_cardinality", "/fields/0/reference/cardinality" },
    };

    [Theory]
    [Trait("Holds", "records-auth-33")]
    [MemberData(nameof(Malformed))]
    public async Task a_malformed_reference_refuses_before_any_write(string _, FieldDefinition[] fields, string code, string pointer)
    {
        var host = Host();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => Create(host, "Space", fields).AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
    }

    [Theory]
    [Trait("Holds", "records-ck-10")]
    [InlineData("eam.asset-class", null, "/fields/0/reference/target_type_id")]
    [InlineData("eam.pump", null, "/fields/0/reference/target_type_id")]
    [InlineData(null, "eam.plant", "/fields/0/reference/target_class_id")]
    public async Task an_unpublished_or_unknown_target_does_not_publish(string? type, string? @class, string pointer)
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "asset-draft");
        await Create(host, "Work Order", Reference("asset", type, @class, ReferenceCardinality.One, ReferenceDeleteBehavior.Block));

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "publish").AsTask());

        Assert.Equal([("records.reference.target_unresolved", pointer)], Pairs(refused));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.work-order")));
    }

    [Fact]
    [Trait("Holds", "records-ck-10")]
    public async Task another_tenants_type_is_not_a_target()
    {
        var host = await WithAssetClass();
        await Create(host, "Work Order", Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block), tenant: "tenant-b", classId: null);

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync("tenant-b", "eam.work-order", "1.0.0", 1, "publish").AsTask());

        Assert.Contains(("records.reference.target_unresolved", "/fields/0/reference/target_type_id"), Pairs(refused));
    }

    [Fact]
    [Trait("Holds", "records-auth-34")]
    public async Task a_target_type_must_declare_the_required_trait()
    {
        var host = Host();
        await Create(host, "Site", [], traits: [new("locatable", "1.0.0", [])]);
        await host.Records.PublishAsync(Tenant, "eam.site", "1.0.0", 1, "site");
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "asset");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "asset-publish");
        await Create(host, "Visit", Reference("site", "eam.site", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: "locatable"));
        await Create(host, "Audit", Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: "locatable"));

        await host.Records.PublishAsync(Tenant, "eam.visit", "1.0.0", 1, "visit");
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, "eam.audit", "1.0.0", 1, "audit").AsTask());

        Assert.Equal([("records.reference.trait_absent", "/fields/0/reference/required_trait_id")], Pairs(refused));
    }

    [Fact]
    [Trait("Holds", "records-ck-12")]
    public async Task a_type_may_be_its_own_parent_and_its_own_trait_satisfies_the_requirement()
    {
        var host = Host();
        await Create(host, "Space",
            Reference("within", "eam.space", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Cascade, parent: true, trait: "locatable"),
            traits: [new("locatable", "1.0.0", [])]);

        var published = await host.Records.PublishAsync(Tenant, "eam.space", "1.0.0", 1, "publish");

        Assert.True(await Valid(host, published, """{"within":{"type":"eam.space","id":"S-1"}}"""));
    }

    private static async Task<TestHost> WithAssetClass()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "asset");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "asset-publish");
        return host;
    }

    private static ValueTask<RecordTypeDraft> Create(TestHost host, string name, FieldDefinition field,
        IReadOnlyList<TraitReference>? traits = null, string tenant = Tenant, string? classId = "eam.equipment")
        => Create(host, name, [field], traits, tenant, classId);

    private static ValueTask<RecordTypeDraft> Create(TestHost host, string name, FieldDefinition[] fields,
        IReadOnlyList<TraitReference>? traits = null, string tenant = Tenant, string? classId = "eam.equipment")
        => host.Records.CreateDraftAsync(new(tenant, "eam", name, "1.0.0", fields, Contract, traits,
            ClassId: classId ?? "eam.equipment", RecordClass: RecordClass.Transactional, PackageId: "eam-core"), "create-" + name);

    private static FieldDefinition Reference(string key, string? type, string? @class, ReferenceCardinality? cardinality,
        ReferenceDeleteBehavior? onDelete, string? trait = null, bool parent = false)
        => new(key, key, Reference: new(type, @class, cardinality, onDelete, trait, parent));

    private static async Task<bool> Valid(TestHost host, RecordTypePublication published, string record)
        => (await host.Registry.ValidateAsync(published.Schema.Id, Encoding.UTF8.GetBytes(record))).IsValid;

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
