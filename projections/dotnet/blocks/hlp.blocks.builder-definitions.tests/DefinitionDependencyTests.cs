using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-615 Definition Dependency edges that target the Catalogue Class (DES-0004 §2; ADR-0054; L769). Publishing a Record Type
/// stores its edges in the publication's own commit, and one read answers both directions. Every expected edge is a literal:
/// the end types are the platform seed's <c>platform.definition-type.*</c> ids, the reasons are the DES-0004 §1 member names
/// that carry each reference, and the kinds and versions follow the slice record's acceptance.
/// </summary>
public sealed class DefinitionDependencyTests
{
    private const string Tenant = "tenant-a";
    private const string RecordType = "platform.definition-type.record-type";
    private const string Class = "platform.definition-type.class";
    private const string Trait = "platform.definition-type.trait";
    private static readonly DefinitionContractVersion Contract = new(1, 0);
    private static readonly DefinitionContractWindow Window = new(1, 0, 1);
    private static readonly TraitReference Locatable = new("locatable", "1.0.0", []);

    [Fact]
    [Trait("Holds", "L769")]
    public async Task each_target_of_a_published_type_lists_it_as_a_dependent()
    {
        var host = NewHost();
        await PublishClass(host, "Meters");
        // The work order's stream exists before the meter's, so dependents are ordered by id, not by insertion.
        await host.Records.CreateDraftAsync(Type("Work Order", [Locatable],
            Field("meter", new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable")),
            Field("group", new(null, "eam.meters", ReferenceCardinality.Many, ReferenceDeleteBehavior.Orphan))), "wo");
        await PublishType(host, Type("Meter", [Locatable]));
        await host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "wo-publish");

        var workOrder = new CatalogueDefinitionReference(RecordType, "eam.work-order", "1.0.0");
        var meter = new CatalogueDefinitionReference(RecordType, "eam.meter", "1.0.0");
        Assert.Equal([
                new DefinitionDependency(workOrder, new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
                new DefinitionDependency(workOrder, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "traits"),
                new DefinitionDependency(workOrder, new(RecordType, "eam.meter"), DefinitionDependencyKind.Derived, "reference_target_type_id"),
                new DefinitionDependency(workOrder, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "reference_trait_id"),
                new DefinitionDependency(workOrder, new(Class, "eam.meters"), DefinitionDependencyKind.Derived, "reference_target_class_id"),
            ], (await Read(host, RecordType, "eam.work-order")).DependsOn);
        Assert.Empty((await Read(host, RecordType, "eam.work-order")).Dependents);

        Assert.Equal([new DefinitionDependency(workOrder, new(RecordType, "eam.meter"), DefinitionDependencyKind.Derived, "reference_target_type_id")],
            (await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Equal([new DefinitionDependency(workOrder, new(Class, "eam.meters"), DefinitionDependencyKind.Derived, "reference_target_class_id")],
            (await Read(host, Class, "eam.meters")).Dependents);
        Assert.Equal([
                new DefinitionDependency(meter, new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
                new DefinitionDependency(workOrder, new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
            ], (await Read(host, Class, "eam.equipment")).Dependents);
        Assert.Equal([
                new DefinitionDependency(meter, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "traits"),
                new DefinitionDependency(workOrder, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "traits"),
                new DefinitionDependency(workOrder, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "reference_trait_id"),
            ], (await Read(host, Trait, "locatable")).Dependents);
        Assert.Empty((await Read(host, Trait, "locatable")).DependsOn);
    }

    [Fact]
    public async Task a_published_class_depends_on_nothing()
    {
        var host = NewHost();

        Assert.Empty((await Read(host, Class, "eam.equipment")).DependsOn);
        Assert.Empty((await Read(host, Class, "eam.equipment")).Dependents);
    }

    [Fact]
    public async Task saving_a_draft_writes_no_edges()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await host.Records.CreateDraftAsync(Type("Work Order", null, ToMeter("meter")), "wo");

        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Empty((await Read(host, RecordType, "eam.work-order")).DependsOn);
    }

    [Fact]
    public async Task a_refused_publication_writes_no_edges()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await host.Records.CreateDraftAsync(Type("Work Order", null, ToMeter("meter")) with { ClassId = "eam.missing" }, "wo");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "wo-publish").AsTask());

        Assert.Equal([("records.class.unresolved", "/class_id")], Pairs(refused));
        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Empty((await Read(host, Class, "eam.missing")).Dependents);
        Assert.Empty((await Read(host, RecordType, "eam.work-order")).DependsOn);
    }

    [Fact]
    public async Task a_later_published_version_replaces_the_edges_of_the_earlier_one()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await PublishType(host, Type("Gauge", null));
        await PublishType(host, Type("Work Order", null, ToMeter("meter")));
        var next = (await host.Records.GetPublishedHeadAsync(Tenant, "eam.work-order"))! with
        {
            Fields = [Field("gauge", new("eam.gauge", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block))],
        };
        await host.Records.SaveDraftAsync("eam.work-order", next, "2.0.0", 2, "wo-2");
        await host.Records.PublishAsync(Tenant, "eam.work-order", "2.0.0", 3, "wo-2-publish");

        var workOrder = new CatalogueDefinitionReference(RecordType, "eam.work-order", "2.0.0");
        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Equal([new DefinitionDependency(workOrder, new(RecordType, "eam.gauge"), DefinitionDependencyKind.Derived, "reference_target_type_id")],
            (await Read(host, RecordType, "eam.gauge")).Dependents);
        Assert.Equal([
                new DefinitionDependency(workOrder, new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
                new DefinitionDependency(workOrder, new(RecordType, "eam.gauge"), DefinitionDependencyKind.Derived, "reference_target_type_id"),
            ], (await Read(host, RecordType, "eam.work-order")).DependsOn);

        // The published head is the highest semantic version, so a lower version published later does not take over.
        await host.Records.SaveDraftAsync("eam.work-order", next with { Fields = [ToMeter("meter")] }, "1.1.0", 4, "wo-1.1");
        await host.Records.PublishAsync(Tenant, "eam.work-order", "1.1.0", 5, "wo-1.1-publish");

        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Single((await Read(host, RecordType, "eam.gauge")).Dependents);
    }

    [Fact]
    public async Task a_replayed_publication_keeps_one_edge_set()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await host.Records.CreateDraftAsync(Type("Work Order", null, ToMeter("meter")), "wo");
        await host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "wo-publish");
        var first = await Read(host, RecordType, "eam.work-order");

        await host.Records.PublishAsync(Tenant, "eam.work-order", "1.0.0", 1, "wo-publish");
        await host.Catalogue.PublishAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.work-order"), "1.0.0", 2, "already-published");

        Assert.Equal(first.DependsOn, (await Read(host, RecordType, "eam.work-order")).DependsOn);
        Assert.Equal(2, first.DependsOn.Count);
        Assert.Single((await Read(host, RecordType, "eam.meter")).Dependents);
    }

    // Clause 1: the store itself refuses an end that names no catalogue definition, even for a caller that publishes without
    // the Records boundary's own edge checks, and commits nothing.
    [Theory]
    [InlineData("class", "records.class.unresolved", "/class_id")]
    [InlineData("type", "records.reference.target_unresolved", "/fields/1/reference/target_type_id")]
    [InlineData("class-target", "records.reference.target_unresolved", "/fields/1/reference/target_class_id")]
    [InlineData("trait", "records.reference.trait_absent", "/fields/1/reference/required_trait_id")]
    [InlineData("trait-not-among-others", "records.reference.trait_absent", "/fields/1/reference/required_trait_id")]
    [InlineData("type-requiring-a-trait", "records.reference.target_unresolved", "/fields/1/reference/target_type_id")]
    public async Task an_end_naming_no_catalogue_definition_refuses_at_the_commit(string end, string code, string pointer)
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", end == "trait-not-among-others" ? [new("other", "1.0.0", [])] : null));
        var type = end switch
        {
            "class" => Type("Work Order", null, ToMeter("meter")) with { ClassId = "eam.missing" },
            "type" => Type("Work Order", null, Field("missing", new("eam.missing", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block))),
            "type-requiring-a-trait" => Type("Work Order", null,
                Field("missing", new("eam.missing", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable"))),
            "class-target" => Type("Work Order", null, Field("missing", new(null, "eam.missing", ReferenceCardinality.One, ReferenceDeleteBehavior.Block))),
            _ => Type("Work Order", null, Field("meter", new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable"))),
        };
        await host.Records.CreateDraftAsync(type, "wo");
        var key = RecordTypeDefinitionStore.KeyOf(Tenant, "eam.work-order");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() => host.Catalogue.PublishAsync(key, "1.0.0", 1, "raw").AsTask());

        Assert.Equal(DefinitionAdmissionPhase.Publish, refused.Stage);
        Assert.Equal([(code, pointer)], Pairs(refused));
        Assert.Null(await host.Catalogue.GetPublishedHeadAsync(key));
        Assert.Single(await host.Catalogue.ListHistoryAsync(key));
        Assert.Empty((await Read(host, RecordType, "eam.work-order")).DependsOn);
        Assert.DoesNotContain((await Read(host, Class, "eam.equipment")).Dependents, edge => edge.FromDefinitionId.DefinitionId == "eam.work-order");
    }

    [Fact]
    public async Task every_unresolved_end_is_reported_together()
    {
        var host = NewHost();
        await host.Records.CreateDraftAsync(Type("Work Order", null,
            Field("missing", new("eam.missing", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block)),
            Field("gone", new(null, "eam.gone", ReferenceCardinality.One, ReferenceDeleteBehavior.Block))) with { ClassId = "eam.nowhere" }, "wo");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Catalogue.PublishAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.work-order"), "1.0.0", 1, "raw").AsTask());

        Assert.Equal([("records.class.unresolved", "/class_id"),
            ("records.reference.target_unresolved", "/fields/1/reference/target_type_id"),
            ("records.reference.target_unresolved", "/fields/2/reference/target_class_id")], Pairs(refused));
    }

    [Fact]
    public async Task a_pinned_cross_package_reference_is_a_declared_edge_at_its_pinned_version()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null) with { Exposes = new(1) });
        await PublishType(host, Type("Meter", null) with { Exposes = new(1) }, "2.0.0", 2);
        await host.Records.CreateDraftAsync(new(Tenant, "fleet", "Work Order", "1.0.0", [ToMeter("meter")], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional, PackageId: "fleet-ops", Requires: [new("eam-core@1")]), "wo");
        var draft = (await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "fleet.work-order")))[^1];
        var pinned = await host.Records.PinReferencesAsync(Tenant,
            RecordTypeDefinitionJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(draft.Document.BodyJson)));
        await host.Records.SaveDraftAsync("fleet.work-order", pinned, "1.0.0", 1, "pin");
        await host.Records.PublishAsync(Tenant, "fleet.work-order", "1.0.0", 2, "wo-publish");

        Assert.Equal([new DefinitionDependency(new(RecordType, "fleet.work-order", "1.0.0"), new(RecordType, "eam.meter", "2.0.0"),
            DefinitionDependencyKind.Declared, "reference_target_type_id")], (await Read(host, RecordType, "eam.meter")).Dependents);
    }

    [Fact]
    public async Task a_cross_package_reference_the_slice_9_gate_refuses_writes_no_edge()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null) with { Exposes = new(1) });
        await host.Records.CreateDraftAsync(new(Tenant, "fleet", "Work Order", "1.0.0", [ToMeter("meter")], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Transactional, PackageId: "fleet-ops"), "wo");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Records.PublishAsync(Tenant, "fleet.work-order", "1.0.0", 1, "wo-publish").AsTask());

        Assert.Equal([("records.reference.pin_required", "/fields/0/reference/pin")], Pairs(refused));
        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
    }

    [Fact]
    public async Task a_self_reference_is_an_edge_to_the_type_itself_and_takes_its_own_trait_revision()
    {
        var host = NewHost();
        await PublishType(host, Type("Site", [Locatable],
            Field("parent", new("eam.site", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable", Parent: true))));

        var site = new CatalogueDefinitionReference(RecordType, "eam.site", "1.0.0");
        Assert.Equal([
                new DefinitionDependency(site, new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
                new DefinitionDependency(site, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "traits"),
                new DefinitionDependency(site, new(RecordType, "eam.site"), DefinitionDependencyKind.Derived, "reference_target_type_id"),
                new DefinitionDependency(site, new(Trait, "locatable", "1.0.0"), DefinitionDependencyKind.Derived, "reference_trait_id"),
            ], (await Read(host, RecordType, "eam.site")).DependsOn);
        Assert.Equal([new DefinitionDependency(site, new(RecordType, "eam.site"), DefinitionDependencyKind.Derived, "reference_target_type_id")],
            (await Read(host, RecordType, "eam.site")).Dependents);
    }

    [Fact]
    public async Task a_self_reference_requiring_a_trait_the_type_does_not_declare_refuses()
    {
        var host = NewHost();
        await host.Records.CreateDraftAsync(Type("Site", null,
            Field("parent", new("eam.site", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable"))), "site");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Catalogue.PublishAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.site"), "1.0.0", 1, "raw").AsTask());

        Assert.Equal([("records.reference.trait_absent", "/fields/1/reference/required_trait_id")], Pairs(refused));
    }

    [Fact]
    public async Task a_required_trait_takes_the_revision_its_target_type_declares()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", [new("other", "1.0.0", []), new("locatable", "2.0.0", [])]));
        await PublishType(host, Type("Work Order", null,
            Field("meter", new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable"))));

        Assert.Equal([new DefinitionDependency(new(RecordType, "eam.work-order", "1.0.0"), new(Trait, "locatable", "2.0.0"),
            DefinitionDependencyKind.Derived, "reference_trait_id")], (await Read(host, Trait, "locatable")).Dependents
            .Where(edge => edge.FromDefinitionId.DefinitionId == "eam.work-order"));
    }

    [Fact]
    public async Task a_required_trait_on_a_class_target_is_unpinned()
    {
        var host = NewHost();
        await PublishClass(host, "Meters");
        await PublishType(host, Type("Work Order", null,
            Field("meter", new(null, "eam.meters", ReferenceCardinality.One, ReferenceDeleteBehavior.Block, RequiredTraitId: "locatable"))));

        Assert.Equal([new DefinitionDependency(new(RecordType, "eam.work-order", "1.0.0"), new(Trait, "locatable"),
            DefinitionDependencyKind.Derived, "reference_trait_id")], (await Read(host, Trait, "locatable")).Dependents);
    }

    [Fact]
    public async Task two_references_to_one_target_are_one_definition_grain_edge()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await PublishType(host, Type("Work Order", null, ToMeter("meter"), ToMeter("spare_meter")));

        Assert.Equal([new DefinitionDependency(new(RecordType, "eam.work-order", "1.0.0"), new(RecordType, "eam.meter"),
            DefinitionDependencyKind.Derived, "reference_target_type_id")], (await Read(host, RecordType, "eam.meter")).Dependents);
    }

    [Fact]
    public async Task another_tenants_edges_are_never_read()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await host.Classes.CreateDraftAsync(new("tenant-b", "eam", "Equipment", "1.0.0", Contract, "eam-core"), "b-equipment");
        await host.Classes.PublishAsync("tenant-b", "eam.equipment", "1.0.0", 1, "b-equipment-publish");
        await host.Records.CreateDraftAsync(Type("Meter", null) with { Tenant = "tenant-b" }, "b-meter");
        await host.Records.PublishAsync("tenant-b", "eam.meter", "1.0.0", 1, "b-meter-publish");
        await host.Records.CreateDraftAsync(Type("Work Order", null, ToMeter("meter")) with { Tenant = "tenant-b" }, "b-wo");
        await host.Records.PublishAsync("tenant-b", "eam.work-order", "1.0.0", 1, "b-wo-publish");

        Assert.Empty((await Read(host, RecordType, "eam.meter")).Dependents);
        Assert.Single((await host.Catalogue.ReadDependenciesAsync("tenant-b", RecordType, "eam.meter")).Dependents);
    }

    [Fact]
    public async Task a_read_matches_both_the_type_and_the_id_of_an_end()
    {
        var host = NewHost();
        await PublishType(host, Type("Meter", null));
        await PublishType(host, Type("Work Order", null, ToMeter("meter")));

        Assert.Empty((await Read(host, Class, "eam.meter")).Dependents);
        Assert.Empty((await Read(host, Class, "eam.work-order")).DependsOn);
        Assert.Empty((await Read(host, RecordType, "eam.equipment")).Dependents);
    }

    [Theory]
    [InlineData("", RecordType, "eam.meter", "definition.tenant_required", "/tenant")]
    [InlineData(Tenant, " ", "eam.meter", "definition.type_required", "/definitionType")]
    [InlineData(Tenant, RecordType, "", "definition.id_required", "/definitionId")]
    public async Task a_read_without_its_identity_refuses(string tenant, string type, string id, string code, string pointer)
    {
        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            NewHost().Catalogue.ReadDependenciesAsync(tenant, type, id).AsTask());

        Assert.Equal([(code, pointer)], Pairs(refused));
    }

    [Fact]
    public async Task a_read_honours_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NewHost().Catalogue.ReadDependenciesAsync(Tenant, RecordType, "eam.meter", cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ((IVersionedDefinitionStore)new Unsupported()).ReadDependenciesAsync(Tenant, RecordType, "eam.meter", cancellation.Token).AsTask());
    }

    [Fact]
    public async Task a_store_without_dependency_derivations_refuses_to_read_rather_than_answer_none()
    {
        var bare = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window),
        });

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            bare.ReadDependenciesAsync(Tenant, Class, "eam.equipment").AsTask());
        var unsupported = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            ((IVersionedDefinitionStore)new Unsupported()).ReadDependenciesAsync(Tenant, Class, "eam.equipment").AsTask());

        Assert.Equal([("definition.dependencies_unsupported", "/dependencies")], Pairs(refused));
        Assert.Equal([("definition.dependencies_unsupported", "/dependencies")], Pairs(unsupported));
        Assert.Equal(DefinitionAdmissionPhase.Author, unsupported.Stage);
    }

    [Fact]
    public async Task a_registry_without_a_derivation_stores_no_edges_for_its_publications()
    {
        var host = NewHost(new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
        {
            [DefinitionKind.Classes] = ClassDefinitionStore.Dependencies,
        });
        await PublishType(host, Type("Meter", null));

        Assert.Empty((await Read(host, Class, "eam.equipment")).Dependents);
        Assert.Empty((await Read(host, RecordType, "eam.meter")).DependsOn);
    }

    [Fact]
    public void a_derivation_for_a_registry_the_store_does_not_admit_refuses_construction()
    {
        var admissions = new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window) };

        var unadmitted = Assert.Throws<DefinitionRefusalException>(() => new InMemoryVersionedDefinitionStore(admissions,
            new Dictionary<DefinitionKind, DefinitionDependencyDerivation> { [DefinitionKind.Records] = RecordTypeDefinitionStore.Dependencies }));
        var missing = Assert.Throws<DefinitionRefusalException>(() => new InMemoryVersionedDefinitionStore(admissions,
            new Dictionary<DefinitionKind, DefinitionDependencyDerivation> { [DefinitionKind.Classes] = null! }));

        Assert.Equal([("definition.registry_unknown", "/dependencies")], Pairs(unadmitted));
        Assert.Equal([("definition.registry_unknown", "/dependencies")], Pairs(missing));
    }

    [Fact]
    public void one_bad_derivation_beside_a_good_one_refuses_construction()
    {
        var admissions = new Dictionary<DefinitionKind, DefinitionAdmission> { [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window) };

        var refused = Assert.Throws<DefinitionRefusalException>(() => new InMemoryVersionedDefinitionStore(admissions,
            new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
            {
                [DefinitionKind.Classes] = ClassDefinitionStore.Dependencies,
                [DefinitionKind.Records] = RecordTypeDefinitionStore.Dependencies,
            }));

        Assert.Equal([("definition.registry_unknown", "/dependencies")], Pairs(refused));
    }

    [Fact]
    public async Task dependents_sharing_an_id_across_registries_are_ordered_by_registry()
    {
        // A Class derivation that names the Equipment Class lets two registries' sources share the id eam.thing.
        var host = NewHost(new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Dependencies,
            [DefinitionKind.Classes] = (published, _) => published.Key.DefinitionId == "eam.thing"
                ? [new(new(Class, "eam.thing", published.Version), new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "test")]
                : [],
        });
        await PublishClass(host, "Thing");
        await PublishType(host, Type("Thing", null));

        Assert.Equal([
                new DefinitionDependency(new(RecordType, "eam.thing", "1.0.0"), new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "class_id"),
                new DefinitionDependency(new(Class, "eam.thing", "1.0.0"), new(Class, "eam.equipment"), DefinitionDependencyKind.Derived, "test"),
            ], (await Read(host, Class, "eam.equipment")).Dependents);
    }

    [Fact]
    public async Task a_derivation_that_returns_nothing_publishes_nothing()
    {
        var host = NewHost(new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
        {
            [DefinitionKind.Records] = (_, _) => null!,
            [DefinitionKind.Classes] = ClassDefinitionStore.Dependencies,
        });
        await host.Records.CreateDraftAsync(Type("Meter", null), "meter");

        var refused = await Assert.ThrowsAsync<DefinitionRefusalException>(() =>
            host.Records.PublishAsync(Tenant, "eam.meter", "1.0.0", 1, "meter-publish").AsTask());

        Assert.Equal([("definition.admission_invalid", "/dependencies")], Pairs(refused));
        Assert.Null(await host.Catalogue.GetPublishedHeadAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.meter")));
    }

    [Fact]
    public async Task a_derivation_sees_the_published_document_and_the_heads_of_its_commit()
    {
        DefinitionDocument? seen = null;
        DefinitionRevision? equipment = null;
        var host = NewHost(new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
        {
            [DefinitionKind.Records] = (published, head) =>
            {
                seen = published;
                equipment = head(ClassDefinitionStore.KeyOf(Tenant, "eam.equipment"));
                return [];
            },
            [DefinitionKind.Classes] = ClassDefinitionStore.Dependencies,
        });
        await PublishType(host, Type("Meter", null));

        Assert.Equal(new DefinitionKey(Tenant, DefinitionKind.Records, "eam.meter"), seen!.Key);
        Assert.Equal("1.0.0", seen.Version);
        Assert.Equal(DefinitionStatus.Published, equipment!.Status);
        Assert.Equal("eam.equipment", equipment.Document.Key.DefinitionId);
    }

    private static FieldDefinition Field(string key, RecordReferenceDefinition reference) => new(key, key, Reference: reference);

    private static FieldDefinition ToMeter(string key)
        => Field(key, new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block));

    // Every fixture type has the plain field "code" first, so its references start at /fields/1.
    private static NewRecordType Type(string name, IReadOnlyList<TraitReference>? traits, params FieldDefinition[] fields)
        => new(Tenant, "eam", name, "1.0.0", [new("code", "Code"), .. fields], Contract, traits,
            ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core");

    private static async Task PublishType(Host host, NewRecordType type, string version = "1.0.0", long expectedRevision = 0)
    {
        if (expectedRevision == 0)
        {
            var draft = await host.Records.CreateDraftAsync(type, "create-" + type.Name);
            await host.Records.PublishAsync(type.Tenant, draft.RecordTypeId, version, 1, "publish-" + type.Name);
            return;
        }
        var id = RecordsCatalogueIdentity.Mint(type.Section, type.Name);
        var head = (await host.Records.GetPublishedHeadAsync(type.Tenant, id))!;
        await host.Records.SaveDraftAsync(id, head, version, expectedRevision, "save-" + type.Name + version);
        await host.Records.PublishAsync(type.Tenant, id, version, expectedRevision + 1, "publish-" + type.Name + version);
    }

    private static async Task PublishClass(Host host, string name)
    {
        var draft = await host.Classes.CreateDraftAsync(new(Tenant, "eam", name, "1.0.0", Contract, "eam-core"), "class-" + name);
        await host.Classes.PublishAsync(Tenant, draft.Document.Key.DefinitionId, "1.0.0", 1, "class-publish-" + name);
    }

    private static ValueTask<DefinitionDependencies> Read(Host host, string type, string id)
        => host.Catalogue.ReadDependenciesAsync(Tenant, type, id);

    private static (string Code, string Pointer)[] Pairs(DefinitionRefusalException refused)
        => refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)).ToArray();

    private sealed record Host(InMemoryVersionedDefinitionStore Catalogue, RecordTypeDefinitionStore Records, ClassDefinitionStore Classes);

    private static Host NewHost(IReadOnlyDictionary<DefinitionKind, DefinitionDependencyDerivation>? dependencies = null)
    {
        var kinds = new FieldKindRuntime(new FieldKindRegistry([]));
        var validator = new RecordsIntentValidator(new Traits());
        var compiler = new RecordTypeSchemaCompiler(validator, kinds, new SharedValueDomainAdmission());
        var catalogue = new InMemoryVersionedDefinitionStore(new Dictionary<DefinitionKind, DefinitionAdmission>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Admission(Window, validator),
            [DefinitionKind.Classes] = ClassDefinitionStore.Admission(Window),
        }, dependencies ?? new Dictionary<DefinitionKind, DefinitionDependencyDerivation>
        {
            [DefinitionKind.Records] = RecordTypeDefinitionStore.Dependencies,
            [DefinitionKind.Classes] = ClassDefinitionStore.Dependencies,
        });
        var classes = new ClassDefinitionStore(catalogue);
        classes.CreateDraftAsync(new(Tenant, "eam", "Equipment", "1.0.0", Contract, "eam-core"), "equipment").AsTask().GetAwaiter().GetResult();
        classes.PublishAsync(Tenant, "eam.equipment", "1.0.0", 1, "equipment-publish").AsTask().GetAwaiter().GetResult();
        var records = new RecordTypeDefinitionStore(catalogue, compiler, new RecordFieldDefaults(kinds),
            new InMemorySchemaRegistry(null, fieldKindRuntime: kinds), Window);
        return new(catalogue, records, classes);
    }

    // Slotless Trait revisions, so a type can declare a Trait a reference requires.
    private sealed class Traits : IRecordTraitSource
    {
        public TraitDefinition? Resolve(string traitId, string version)
            => (traitId, version) is ("locatable", "1.0.0") or ("locatable", "2.0.0") or ("other", "1.0.0") ? new(traitId, version, []) : null;
    }

    // A store that does not implement the read, so the interface default answers.
    private sealed class Unsupported : IVersionedDefinitionStore
    {
        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId, string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
