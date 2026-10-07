using System.Security.Cryptography;
using System.Text;
using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 9 (DES-0015 records-ck-41, records-auth-38, records-eng-29; ADR-0006; ADR-0028; L127). A reference into
/// another package needs the referencing envelope's <c>requires</c> entry, the target's exposure at the same interface
/// version, and a sealed pin of the target's package, id, published version, body digest and interface version.
/// Authoring writes the pin from current state; publication repeats the check and refuses a pin that no longer matches.
/// Package <c>eam-core</c> owns the exposed type <c>eam.meter</c>; package <c>fleet-ops</c> references it.
/// </summary>
public sealed class RecordCrossPackageTests
{
    private static readonly DefinitionContractVersion Contract = new(1, 0);

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task authoring_pins_the_target_and_publication_seals_the_matching_pin()
    {
        var (host, meter) = await WithExposedMeter();
        var draft = await Consumer(host, requires: "eam-core@1");

        var pinned = await host.Records.PinReferencesAsync(Tenant, draft);

        Assert.Equal(new RecordReferencePin("eam-core", "eam.meter", "1.0.0",
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(meter.Document.BodyJson))), 1),
            pinned.Fields[0].Reference!.Pin);
        await host.Records.SaveDraftAsync("fleet-ops.work-order", pinned, "1.0.0", 1, "pin");
        var published = await host.Records.PublishAsync(Tenant, "fleet-ops.work-order", "1.0.0", 2, "publish");
        Assert.Equal(pinned.Fields[0].Reference!.Pin,
            RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(published.Revision.Document.BodyJson)).Fields[0].Reference!.Pin);
    }

    [Fact]
    [Trait("Holds", "records-auth-38")]
    public async Task a_cross_package_reference_without_its_declarations_or_pin_does_not_publish()
    {
        var (host, _) = await WithExposedMeter();
        var undeclared = await host.Records.PinReferencesAsync(Tenant, await Consumer(host, requires: null));
        await host.Records.SaveDraftAsync("fleet-ops.work-order", undeclared, "1.0.0", 1, "pin");

        Assert.Equal([("records.reference.dependency_undeclared", "/fields/0/reference")], await PublishRefusals(host, 2));

        var unpinned = await Consumer(host, requires: "eam-core@1", name: "Inspection");
        Assert.Equal([("records.reference.pin_required", "/fields/0/reference/pin")], await PublishRefusals(host, 1, "fleet-ops.inspection"));
    }

    [Fact]
    [Trait("Holds", "records-auth-38")]
    public async Task an_unexposed_target_is_not_referenceable_from_another_package()
    {
        var host = Host();
        await Publish(host, "Meter", "eam-core", exposes: null);
        var draft = await Consumer(host, requires: "eam-core@1");
        var forged = draft with { Fields = [draft.Fields[0] with { Reference = draft.Fields[0].Reference! with {
            Pin = new("eam-core", "eam.meter", "1.0.0", "sha256:" + new string('0', 64), 1) } }] };
        await host.Records.SaveDraftAsync("fleet-ops.work-order", forged, "1.0.0", 1, "forge");

        Assert.Null((await host.Records.PinReferencesAsync(Tenant, draft)).Fields[0].Reference!.Pin);
        Assert.Equal([("records.reference.not_exposed", "/fields/0/reference")], await PublishRefusals(host, 2));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_pin_the_target_has_moved_past_is_stale()
    {
        var (host, _) = await WithExposedMeter();
        var pinned = await host.Records.PinReferencesAsync(Tenant, await Consumer(host, requires: "eam-core@1"));
        await host.Records.SaveDraftAsync("fleet-ops.work-order", pinned, "1.0.0", 1, "pin");
        await host.Records.SaveDraftAsync("eam.meter", (await host.Records.GetPublishedHeadAsync(Tenant, "eam.meter"))! with { Name = "Utility meter" },
            "1.1.0", 2, "meter-1.1");
        await host.Records.PublishAsync(Tenant, "eam.meter", "1.1.0", 3, "meter-1.1-publish");

        Assert.Equal([("records.reference.pin_stale", "/fields/0/reference")], await PublishRefusals(host, 2));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_requirement_at_another_interface_version_is_incompatible()
    {
        var (host, _) = await WithExposedMeter();
        var pinned = await host.Records.PinReferencesAsync(Tenant, await Consumer(host, requires: "eam-core@2"));
        await host.Records.SaveDraftAsync("fleet-ops.work-order", pinned, "1.0.0", 1, "pin");

        Assert.Equal([("records.reference.interface_incompatible", "/fields/0/reference")], await PublishRefusals(host, 2));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_pin_naming_another_interface_version_is_stale()
    {
        var (host, _) = await WithExposedMeter();
        var draft = await host.Records.PinReferencesAsync(Tenant, await Consumer(host, requires: "eam-core@1"));
        var drifted = draft with { Fields = [draft.Fields[0] with { Reference = draft.Fields[0].Reference! with {
            Pin = draft.Fields[0].Reference!.Pin! with { InterfaceVersion = 2 } } }] };
        await host.Records.SaveDraftAsync("fleet-ops.work-order", drifted, "1.0.0", 1, "drift");

        Assert.Equal([("records.reference.pin_stale", "/fields/0/reference")], await PublishRefusals(host, 2));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_reference_inside_one_package_carries_no_pin()
    {
        var (host, _) = await WithExposedMeter();
        var reading = await Publish(host, "Reading", "eam-core", exposes: null, field: ToMeter);
        Assert.Null((await host.Records.PinReferencesAsync(Tenant, reading)).Fields[0].Reference!.Pin);

        var pinned = reading with { Fields = [reading.Fields[0] with { Reference = reading.Fields[0].Reference! with {
            Pin = new("eam-core", "eam.meter", "1.0.0", "sha256:" + new string('0', 64), 1) } }] };
        await host.Records.SaveDraftAsync("eam.reading", pinned, "1.1.0", 2, "pinned");
        Assert.Equal([("records.reference.pin_unexpected", "/fields/0/reference/pin")],
            await PublishRefusals(host, 3, "eam.reading", "1.1.0"));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task authoring_diagnoses_current_state_at_the_author_stage_without_writing()
    {
        var (host, _) = await WithExposedMeter();
        var draft = await Consumer(host, requires: null);

        var report = await host.Records.DiagnoseAsync(Tenant, draft);

        Assert.Equal(DefinitionAdmissionPhase.Author, report.Stage);
        Assert.Equal([("records.reference.pin_required", "/fields/0/reference/pin")],
            report.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.Single(await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "fleet-ops.work-order")));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_class_in_another_package_is_pinned_like_a_type()
    {
        var host = Host();
        await host.Classes.CreateDraftAsync(new(Tenant, "eam", "Meters", "1.0.0", Contract, "eam-core", Exposes: new(1)), "meters");
        var meters = await host.Classes.PublishAsync(Tenant, "eam.meters", "1.0.0", 1, "meters-publish");
        await host.Records.CreateDraftAsync(new(Tenant, "fleet-ops", "Work Order", "1.0.0",
            [new("meter", "Meter", Reference: new(null, "eam.meters", ReferenceCardinality.One, ReferenceDeleteBehavior.Block))], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional, PackageId: "fleet-ops", Requires: [new("eam-core@1")]), "wo");
        var draft = (await host.Records.GetPublishedHeadAsync(Tenant, "fleet-ops.work-order")) ?? Draft(host, "fleet-ops.work-order");

        var pinned = await host.Records.PinReferencesAsync(Tenant, draft);

        Assert.Equal(new RecordReferencePin("eam-core", "eam.meters", "1.0.0",
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(meters.Document.BodyJson))), 1), pinned.Fields[0].Reference!.Pin);
        await host.Records.SaveDraftAsync("fleet-ops.work-order", pinned, "1.0.0", 1, "pin");
        await host.Records.PublishAsync(Tenant, "fleet-ops.work-order", "1.0.0", 2, "publish");
    }

    [Fact]
    [Trait("Holds", "records-ck-2")]
    public async Task a_draft_may_omit_its_package_but_a_published_type_names_it()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(new(Tenant, "fleet-ops", "Route", "1.0.0", [new("code", "Code")], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Master), "route");

        Assert.Equal([("records.package.required", "/envelope/package_id")], await PublishRefusals(host, 1, "fleet-ops.route"));
    }

    [Theory]
    [Trait("Holds", "records-ck-2")]
    [InlineData("eam-core", null, "records.package.requirement_invalid", "/envelope/requires/0")]
    [InlineData("eam-core@0", null, "records.package.requirement_invalid", "/envelope/requires/0")]
    [InlineData("@1", null, "records.package.requirement_invalid", "/envelope/requires/0")]
    [InlineData("eam-core@x", null, "records.package.requirement_invalid", "/envelope/requires/0")]
    [InlineData("eam-core@", null, "records.package.requirement_invalid", "/envelope/requires/0")]
    [InlineData(null, 0, "records.package.exposure_invalid", "/envelope/exposes/interface_version")]
    public async Task a_malformed_declaration_refuses_at_creation(string? requirement, int? exposes, string code, string pointer)
    {
        var host = Host();

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Records.CreateDraftAsync(
            new(Tenant, "fleet-ops", "Route", "1.0.0", [new("code", "Code")], Contract, PackageId: "fleet-ops",
                Requires: requirement is null ? null : [new(requirement)], Exposes: exposes is null ? null : new(exposes.Value)), "route").AsTask());

        Assert.Equal([(code, pointer)], refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.Empty(await host.Catalogue.ListKeysAsync(Tenant, DefinitionKind.Records));
    }

    private static FieldDefinition ToMeter => new("meter", "Meter",
        Reference: new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block));

    private static async Task<(TestHost Host, DefinitionRevision Meter)> WithExposedMeter()
    {
        var host = Host();
        await Publish(host, "Meter", "eam-core", exposes: new(1));
        var meter = (await host.Catalogue.GetPublishedHeadAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.meter")))!;
        return (host, meter);
    }

    private static async Task<RecordTypeDocument> Publish(TestHost host, string name, string package, RecordsExposure? exposes,
        FieldDefinition? field = null)
    {
        var created = await host.Records.CreateDraftAsync(new(Tenant, "eam", name, "1.0.0", [field ?? new("code", "Code")], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: package, Exposes: exposes), "create-" + name);
        await host.Records.PublishAsync(Tenant, created.RecordTypeId, "1.0.0", 1, "publish-" + name);
        return (await host.Records.GetPublishedHeadAsync(Tenant, created.RecordTypeId))!;
    }

    private static async Task<RecordTypeDocument> Consumer(TestHost host, string? requires, string name = "Work Order")
    {
        var created = await host.Records.CreateDraftAsync(new(Tenant, "fleet-ops", name, "1.0.0", [ToMeter], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional, PackageId: "fleet-ops",
            Requires: requires is null ? null : [new(requires)]), "create-" + name);
        return Draft(host, created.RecordTypeId);
    }

    private static RecordTypeDocument Draft(TestHost host, string id)
        => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(host.Catalogue
            .ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, id)).AsTask().GetAwaiter().GetResult()[^1].Document.BodyJson));

    private static async Task<(string Code, string Pointer)[]> PublishRefusals(TestHost host, long expected,
        string id = "fleet-ops.work-order", string version = "1.0.0")
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(
            () => host.Records.PublishAsync(Tenant, id, version, expected, "publish-" + id).AsTask());
        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        return refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
    }
}
