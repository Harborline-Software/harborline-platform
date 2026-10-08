using Harborline.Foundation.Definitions;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 (DES-0015 records-auth-40; ADR-0054). A published Class with zero or one published member type is reported
/// as one defect, with its members; two or more members is healthy. The test host publishes the Class
/// <c>eam.equipment</c>.
/// </summary>
public sealed class RecordsCatalogueHealthTests
{
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    [Fact]
    [Trait("Holds", "records-auth-40")]
    public async Task a_class_with_zero_or_one_published_member_is_the_same_defect_and_two_is_healthy()
    {
        var host = Host();
        var health = new RecordsCatalogueHealth(host.Catalogue);
        await host.Classes.CreateDraftAsync(new(Tenant, "eam", "Location", "1.0.0", Contract, "eam-core"), "location");
        await host.Classes.PublishAsync(Tenant, "eam.location", "1.0.0", 1, "location-publish");

        Assert.Equal([
            new RecordsHealthFinding("records.class.membership_degenerate", "eam.equipment", []),
            new RecordsHealthFinding("records.class.membership_degenerate", "eam.location", []),
        ], await health.ReportAsync(Tenant), Comparer);

        await Publish(host, "Pump", "eam.equipment");
        Assert.Equal(["eam.pump"], (await health.ReportAsync(Tenant)).Single(finding => finding.DefinitionId == "eam.equipment").Members);

        await Publish(host, "Valve", "eam.equipment");
        Assert.Equal([new RecordsHealthFinding("records.class.membership_degenerate", "eam.location", [])],
            await health.ReportAsync(Tenant), Comparer);
    }

    [Fact]
    [Trait("Holds", "records-auth-40")]
    public async Task only_published_heads_count_and_a_draft_class_is_not_reported()
    {
        var host = Host();
        var health = new RecordsCatalogueHealth(host.Catalogue);
        await Publish(host, "Pump", "eam.equipment");
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "draft-member");
        await host.Classes.CreateDraftAsync(new(Tenant, "eam", "Plant", "1.0.0", Contract, "eam-core"), "draft-class");

        Assert.Equal([new RecordsHealthFinding("records.class.membership_degenerate", "eam.equipment", ["eam.pump"])],
            await health.ReportAsync(Tenant), Comparer);

        // A type's home is its published head's: moving the pump to Plant leaves Equipment with no members.
        await host.Classes.PublishAsync(Tenant, "eam.plant", "1.0.0", 1, "plant-publish");
        await host.Records.SaveDraftAsync("eam.pump", (await Head(host, "eam.pump")) with { ClassId = "eam.plant" }, "2.0.0", 2, "move");
        await host.Records.PublishAsync(Tenant, "eam.pump", "2.0.0", 3, "move-publish");
        Assert.Equal([
            new RecordsHealthFinding("records.class.membership_degenerate", "eam.equipment", []),
            new RecordsHealthFinding("records.class.membership_degenerate", "eam.plant", ["eam.pump"]),
        ], await health.ReportAsync(Tenant), Comparer);
    }

    [Fact]
    [Trait("Holds", "records-auth-40")]
    public async Task another_tenants_types_are_not_members()
    {
        var host = Host();
        var other = new ClassDefinitionStore(host.Catalogue);
        await other.CreateDraftAsync(new("tenant-b", "eam", "Equipment", "1.0.0", Contract, "eam-core"), "b-class");
        await other.PublishAsync("tenant-b", "eam.equipment", "1.0.0", 1, "b-publish");
        await Publish(host, "Pump", "eam.equipment", "tenant-b");
        await Publish(host, "Valve", "eam.equipment", "tenant-b");

        Assert.Equal([new RecordsHealthFinding("records.class.membership_degenerate", "eam.equipment", [])],
            await new RecordsCatalogueHealth(host.Catalogue).ReportAsync(Tenant), Comparer);
        Assert.Empty(await new RecordsCatalogueHealth(host.Catalogue).ReportAsync("tenant-b"));
    }

    [Fact]
    public void the_report_refuses_a_missing_store()
        => Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => new RecordsCatalogueHealth(null!)).ParamName);

    private static async Task Publish(TestHost host, string name, string classId, string tenant = Tenant)
    {
        var draft = await host.Records.CreateDraftAsync(new(tenant, "eam", name, "1.0.0", [new("code", "Code")], Contract,
            ClassId: classId, RecordClass: RecordClass.Master, PackageId: "eam-core"), $"{tenant}-{name}");
        await host.Records.PublishAsync(tenant, draft.RecordTypeId, "1.0.0", 1, $"{tenant}-{name}-publish");
    }

    private static async Task<RecordTypeDocument> Head(TestHost host, string id)
        => (await host.Records.GetPublishedHeadAsync(Tenant, id))!;

    private static readonly FindingComparer Comparer = new();

    private sealed class FindingComparer : IEqualityComparer<RecordsHealthFinding>
    {
        public bool Equals(RecordsHealthFinding? x, RecordsHealthFinding? y)
            => x is not null && y is not null && x.Code == y.Code && x.DefinitionId == y.DefinitionId && x.Members.SequenceEqual(y.Members);

        public int GetHashCode(RecordsHealthFinding obj) => HashCode.Combine(obj.Code, obj.DefinitionId);
    }
}
