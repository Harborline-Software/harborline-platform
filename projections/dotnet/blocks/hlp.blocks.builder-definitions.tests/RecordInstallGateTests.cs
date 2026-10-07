using Harborline.Foundation.Definitions;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 slice 10 (DES-0015 records-ck-41, records-auth-38). Installation re-resolves every edge from what the pack and its
/// pinned dependency closure declare, never from the installing node's live catalogue, and reports every missing or
/// changed edge together. The work order (package <c>fleet-ops</c>) references <c>eam.meter</c> (package <c>eam-core</c>,
/// exposed at interface 1) across packages and its own <c>fleet-ops.route</c> inside the pack.
/// </summary>
public sealed class RecordInstallGateTests
{
    private const string Digest = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private static readonly DefinitionContractVersion Contract = new(1, 0);
    private static readonly RecordReferencePin Pin = new("eam-core", "eam.meter", "1.0.0", Digest, 1);
    private static readonly PinnedExposure Declared = new("eam-core", DefinitionKind.Records, "eam.meter", "1.0.0", Digest, 1);
    private static readonly RecordsInstallClosure Closure = new([new(DefinitionKind.Records, "fleet-ops.work-order"), new(DefinitionKind.Records, "fleet-ops.route")], [Declared]);

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task an_entry_whose_edges_match_the_pack_and_its_pinned_closure_installs()
    {
        // The installing host has no eam.meter at all: the closure, not the live catalogue, is the authority.
        var report = await Host().Records.AdmitInstallAsync("tenant-b", Entry(WorkOrder()), Closure);

        Assert.Equal(DefinitionAdmissionPhase.Install, report.Stage);
        Assert.Empty(report.Refusals);
    }

    public static TheoryData<string, PinnedExposure[], string> ClosureCases() => new()
    {
        { "absent", [], "records.reference.closure_missing" },
        { "other definition", [Declared with { DefinitionId = "eam.pump" }], "records.reference.closure_missing" },
        { "other version", [Declared with { Version = "1.1.0" }], "records.reference.closure_changed" },
        { "other digest", [Declared with { Digest = "sha256:" + new string('2', 64) }], "records.reference.closure_changed" },
        { "other interface", [Declared with { InterfaceVersion = 2 }], "records.reference.closure_changed" },
    };

    [Theory]
    [Trait("Holds", "records-ck-41")]
    [MemberData(nameof(ClosureCases))]
    public async Task a_pin_the_closure_does_not_declare_exactly_refuses(string _, PinnedExposure[] exposures, string code)
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b", Entry(WorkOrder()), Closure with { Exposures = exposures });

        Assert.Equal([(code, "/fields/0/reference/pin")], Pairs(report));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task an_in_pack_edge_needs_its_target_in_the_pack_or_to_be_the_type_itself()
    {
        var host = Host();

        var missing = await host.Records.AdmitInstallAsync("tenant-b", Entry(WorkOrder()), Closure with { PackDefinitions = [new(DefinitionKind.Records, "fleet-ops.work-order")] });
        Assert.Equal([("records.reference.target_unresolved", "/fields/1/reference/target_type_id")], Pairs(missing));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_record_type_self_reference_installs_without_a_pack_membership_entry()
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder(route: Field("parent", "fleet-ops.work-order", null))), Closure with { PackDefinitions = [] });

        Assert.Empty(report.Refusals);
    }

    // Oracle: catalogue identity is (kind, id); a Class and a Record Type with the same id are distinct.
    [Theory]
    [InlineData(false, DefinitionKind.Classes, "/fields/1/reference/target_type_id")]
    [InlineData(true, DefinitionKind.Records, "/fields/1/reference/target_class_id")]
    [Trait("Holds", "records-ck-41")]
    public async Task an_in_pack_definition_of_the_wrong_kind_cannot_satisfy_the_same_id(
        bool classTarget, DefinitionKind suppliedKind, string pointer)
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder(route: TargetField("route", "fleet-ops.route", classTarget))),
            Closure with { PackDefinitions = [new(suppliedKind, "fleet-ops.route")] });

        Assert.Equal([("records.reference.target_unresolved", pointer)], Pairs(report));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_class_target_with_the_record_type_id_is_not_a_self_reference()
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder(route: TargetField("parent", "fleet-ops.work-order", true))),
            Closure with { PackDefinitions = [] });

        Assert.Equal([("records.reference.target_unresolved", "/fields/1/reference/target_class_id")], Pairs(report));
    }

    [Theory]
    [InlineData(false, DefinitionKind.Classes)]
    [InlineData(true, DefinitionKind.Records)]
    [Trait("Holds", "records-ck-41")]
    public async Task a_dependency_exposure_of_the_wrong_kind_cannot_satisfy_the_same_id(
        bool classTarget, DefinitionKind suppliedKind)
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder(meter: TargetField("meter", "eam.meter", classTarget, Pin))),
            Closure with { Exposures = [Declared with { Kind = suppliedKind }] });

        Assert.Equal([("records.reference.closure_missing", "/fields/0/reference/pin")], Pairs(report));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task class_targets_install_when_both_pack_and_dependency_kinds_match()
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder(meter: TargetField("meter", "eam.meter", true, Pin),
                route: TargetField("route", "fleet-ops.route", true))),
            new([new(DefinitionKind.Classes, "fleet-ops.route")], [Declared with { Kind = DefinitionKind.Classes }]));

        Assert.Empty(report.Refusals);
    }

    public static TheoryData<string, RecordTypeDocument, string, string> Tampered() => new()
    {
        { "no requires entry", WorkOrder() with { Envelope = WorkOrder().Envelope with { Requires = [] } }, "records.reference.dependency_undeclared", "/fields/0/reference" },
        { "another interface", WorkOrder() with { Envelope = WorkOrder().Envelope with { Requires = [new("eam-core@2")] } }, "records.reference.interface_incompatible", "/fields/0/reference" },
        { "pin inside the pack", WorkOrder(meter: Field("meter", "eam.meter", Pin with { PackageId = "fleet-ops" })), "records.reference.pin_unexpected", "/fields/0/reference/pin" },
        { "pin to another target", WorkOrder(meter: Field("meter", "eam.meter", Pin with { DefinitionId = "eam.pump" })), "records.reference.pin_stale", "/fields/0/reference" },
    };

    [Theory]
    [Trait("Holds", "records-auth-38")]
    [MemberData(nameof(Tampered))]
    public async Task an_entry_whose_own_declarations_no_longer_match_its_pin_refuses(string _, RecordTypeDocument document, string code, string pointer)
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b", Entry(document), Closure);

        Assert.Equal([(code, pointer)], Pairs(report));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task every_missing_or_changed_edge_is_reported_together()
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b", Entry(WorkOrder()),
            new([new(DefinitionKind.Records, "fleet-ops.work-order")], [Declared with { Digest = "sha256:" + new string('3', 64) }]));

        Assert.Equal([
            ("records.reference.closure_changed", "/fields/0/reference/pin"),
            ("records.reference.target_unresolved", "/fields/1/reference/target_type_id"),
        ], Pairs(report));
    }

    [Fact]
    [Trait("Holds", "records-ck-41")]
    public async Task a_structurally_refused_entry_reports_its_admission_refusal_first()
    {
        var report = await Host().Records.AdmitInstallAsync("tenant-b",
            Entry(WorkOrder() with { Envelope = WorkOrder().Envelope with { PackageId = null } }), Closure);

        Assert.Equal([("records.package.required", "/envelope/package_id")], Pairs(report));
    }

    [Fact]
    public async Task the_gate_refuses_a_missing_closure()
        => Assert.Equal("closure", (await Assert.ThrowsAsync<ArgumentNullException>(
            () => Host().Records.AdmitInstallAsync(Tenant, Entry(WorkOrder()), null!).AsTask())).ParamName);

    private static RecordTypeDocument WorkOrder(FieldDefinition? meter = null, FieldDefinition? route = null) => new(
        new(Tenant, "fleet-ops", Contract, "fleet-ops", [new("eam-core@1")]), "Work Order", "fleet-ops.work-order",
        [meter ?? Field("meter", "eam.meter", Pin), route ?? Field("route", "fleet-ops.route", null)],
        ClassId: "eam.equipment", RecordClass: RecordClass.Transactional);

    private static FieldDefinition Field(string key, string target, RecordReferencePin? pin)
        => new(key, key, Reference: new(target, null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, Pin: pin));

    private static FieldDefinition TargetField(string key, string target, bool classTarget, RecordReferencePin? pin = null)
        => new(key, key, Reference: new(classTarget ? null : target, classTarget ? target : null,
            ReferenceCardinality.One, ReferenceDeleteBehavior.Block, Pin: pin));

    private static RecordTypeDefinitionPackageEntry Entry(RecordTypeDocument document)
        => new(document.RecordTypeId, "1.0.0", PlatformPackageContent.PresentJson(RecordTypeDefinitionJson.SerializeCanonical(document)));

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalReport report)
        => report.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();
}
