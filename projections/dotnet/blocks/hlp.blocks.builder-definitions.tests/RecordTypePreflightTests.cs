using System.Security.Cryptography;
using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Definitions;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;
using static Harborline.Blocks.BuilderDefinitions.Tests.RecordTypeDefinitionStoreTests;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>
/// T-618, the no-write Records preflight (contract proposal v3 as amended by v4, cleared by Root grant 084 on base
/// 1e1d94ba). <see cref="RecordTypeDefinitionStore.PreflightAsync"/> reports every computable refusal in stage order with
/// the draft schema and the publication-warning preview, and writes, registers and publishes nothing. Caller and host
/// faults throw before any port is called; the edge guard prunes only its lookup copy. Every code, pointer, target, order
/// and call count below is a literal taken from the contract, never from the implementation. The catalogue, registry,
/// kind runtime, trait source and domain runtime are counting spies around the real compiler and stores.
/// </summary>
public sealed class RecordTypePreflightTests
{
    private const string OtherTenant = "tenant-b";
    private const string Home = "head tenant-a/Classes/eam.equipment";
    private static readonly DefinitionContractVersion Contract = new(1, 0);
    private static readonly FieldDomainScope ScopeA = new(new TenantId(Tenant), "author-1");
    private static readonly ResolvedFieldConstraints Proof = new(false, 0, 1, [], null, [], "r1");

    [Fact]
    public async Task the_preview_writes_registers_and_publishes_nothing_for_a_clean_or_a_refused_body()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var before = await State(host);
        var ports = Spied(host, ScopeA);

        var clean = await ports.Records.PreflightAsync(Tenant, Body(Text("code")));
        var refused = await ports.Records.PreflightAsync(Tenant,
            Body(Reference("asset", "eam.ghost", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block)) with { ClassId = "eam.missing" });

        Assert.Empty(clean.Refusals);
        Assert.NotNull(clean.JsonSchemaText);
        Assert.Equal([("records.class.unresolved", "/class_id", null), ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            Triples(refused));
        Assert.Equal(0, ports.Catalogue.Mutations);
        Assert.Equal(0, ports.Registry.Registrations);
        Assert.Equal(["head tenant-a/Classes/eam.equipment", "head tenant-a/Classes/eam.missing", "head tenant-a/Records/eam.ghost"],
            ports.Catalogue.Reads);
        Assert.Contains("schema ", before, StringComparison.Ordinal);
        Assert.Equal(before, await State(host));
    }

    [Theory]
    [InlineData(null, Tenant, false, false, typeof(ArgumentNullException))]
    [InlineData("", Tenant, false, false, typeof(ArgumentException))]
    [InlineData(" \t", Tenant, false, false, typeof(ArgumentException))]
    [InlineData("", "", false, false, typeof(ArgumentException))]
    [InlineData(" ", " ", false, false, typeof(ArgumentException))]
    [InlineData(Tenant, Tenant, true, false, typeof(ArgumentNullException))]
    [InlineData(Tenant, Tenant, false, true, typeof(InvalidOperationException))]
    [InlineData(Tenant, OtherTenant, false, true, typeof(InvalidOperationException))]
    public async Task caller_and_host_faults_throw_their_exact_type_before_any_port_is_called(string? tenant, string envelopeTenant,
        bool nullDocument, bool foreignScope, Type expected)
    {
        var ports = Spied(Host(), foreignScope ? new(new TenantId(OtherTenant), "author-1") : ScopeA);
        var body = Slotted() with { Envelope = Slotted().Envelope with { Tenant = envelopeTenant } };

        var fault = await Record.ExceptionAsync(() => ports.Records.PreflightAsync(tenant!, nullDocument ? null! : body).AsTask());

        Assert.NotNull(fault);
        Assert.Equal(expected, fault.GetType());
        Untouched(ports);
    }

    [Fact]
    public async Task an_envelope_for_another_tenant_stops_at_the_catalogue_mismatch_with_no_port_called()
    {
        var host = Host();
        var foreign = Slotted() with { Envelope = Slotted().Envelope with { Tenant = OtherTenant } };
        var ports = Spied(host, ScopeA);

        var preflight = await ports.Records.PreflightAsync(Tenant, foreign);

        Assert.Equal([("definition.catalogue_mismatch", "/envelope/tenant", null)], Triples(preflight));
        Assert.Null(preflight.Document);
        Assert.Null(preflight.JsonSchemaText);
        Assert.Empty(preflight.Warnings);
        Untouched(ports);

        // Control: the same body for the asking tenant reaches every port the mismatch left untouched.
        var control = Spied(host, ScopeA);
        Assert.Empty((await control.Records.PreflightAsync(Tenant, Slotted())).Refusals);
        Assert.NotEmpty(control.Catalogue.Reads);
        Assert.NotEqual(0, control.Kinds.Binds);
        Assert.NotEqual(0, control.Traits.Resolves);
        Assert.Equal(1, control.Domains.Calls);
    }

    [Fact]
    public async Task another_tenants_published_target_is_never_read()
    {
        var host = Host();
        var other = RecordTypeDefinitionStore.KeyOf(OtherTenant, "eam.asset-class");
        await host.Catalogue.SaveDraftAsync(new(other, "1.0.0", "1.0.0",
            AssetClassJson.Replace("\"tenant\":\"tenant-a\"", "\"tenant\":\"tenant-b\"", StringComparison.Ordinal)), 0, "b-draft");
        await host.Catalogue.PublishAsync(other, "1.0.0", 1, "b-publish");
        Assert.NotNull(await host.Catalogue.GetPublishedHeadAsync(other));
        var ports = Spied(host, ScopeA);

        var preflight = await ports.Records.PreflightAsync(Tenant,
            Body(Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block)));

        Assert.Equal([("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)], Triples(preflight));
        Assert.Equal([Home, "head tenant-a/Records/eam.asset-class"], ports.Catalogue.Reads);
    }

    [Theory]
    [InlineData("null field element", "records.document_invalid", "/fields/0")]
    [InlineData("missing fields list", "records.document_invalid", "/fields")]
    [InlineData("bound field without a kind", "records.document_invalid", "/fields/0/binding/kind")]
    [InlineData("null envelope and null field element", "records.envelope_required", "/envelope")]
    [InlineData("unsupported typed enum", "records.document_invalid", "")]
    [InlineData("kind parameter keys differing only by case", "records.document_invalid", "")]
    [InlineData("foreign envelope and null field element", "records.document_invalid", "/fields/0")]
    public async Task an_incomplete_document_stops_at_its_literal_pointer_without_binding_a_kind(string @case, string code, string location)
    {
        var ports = Spied(Host(), ScopeA);
        var body = Body(Text("code"));
        var document = @case switch
        {
            "null field element" => body with { Fields = [null!] },
            "kind parameter keys differing only by case" => body with { Fields = [Text("code") with { Binding = new(
                new("text", "1.0.0", new Dictionary<string, string> { ["max_length"] = "5", ["MAX_LENGTH"] = "9" }), new(false, 0, 1, [], null)) }] },
            // Stage 0 precedes stage 1, so completeness is reported before the envelope's tenant is compared.
            "foreign envelope and null field element" => body with { Envelope = body.Envelope with { Tenant = OtherTenant }, Fields = [null!] },
            "missing fields list" => body with { Fields = null! },
            "bound field without a kind" => body with { Fields = [Text("code") with { Binding = new(null!, new(false, 0, 1, [], null)) }] },
            "null envelope and null field element" => body with { Envelope = null!, Fields = [null!] },
            _ => body with { RecordClass = (RecordClass)42 },
        };

        var preflight = await ports.Records.PreflightAsync(Tenant, document);

        Assert.Equal([(code, location, null)], Triples(preflight));
        Assert.Null(preflight.Document);
        Assert.Null(preflight.JsonSchemaText);
        Assert.Empty(preflight.Warnings);
        Untouched(ports);
    }

    [Fact]
    public async Task a_draft_without_a_package_still_compiles_and_resolves_edges_and_reports_the_package_once()
    {
        var ports = Spied(Host(), ScopeA);

        var preflight = await ports.Records.PreflightAsync(Tenant,
            Body(Text("code")) with { Envelope = new(Tenant, "eam", Contract), ClassId = "eam.missing" });

        Assert.Equal([("records.package.required", "/envelope/package_id", null), ("records.class.unresolved", "/class_id", null)],
            Triples(preflight));
        Assert.NotNull(preflight.JsonSchemaText);
        Assert.Equal(["head tenant-a/Classes/eam.missing"], ports.Catalogue.Reads);
    }

    [Fact]
    public async Task without_a_scope_a_plain_body_previews_and_only_a_trait_slot_binding_needs_the_runtime()
    {
        var plain = await Spied(Host()).Records.PreflightAsync(Tenant, Body(Text("code")));
        var ports = Spied(Host());
        var slotted = await ports.Records.PreflightAsync(Tenant, Slotted());

        Assert.Empty(plain.Refusals);
        Assert.NotNull(plain.JsonSchemaText);
        Assert.Equal([("records.retention.unset", "/retention_policy")], Warnings(plain));
        Assert.Equal([("records.field.runtime_required", "/traits/0/slot_bindings/0", null)], Triples(slotted));
        Assert.Null(slotted.JsonSchemaText);
        Assert.Equal(0, ports.Domains.Calls);
    }

    [Fact]
    public async Task cancellation_inside_the_compile_propagates_as_the_same_exception_object()
    {
        using var source = new CancellationTokenSource();
        var ports = Spied(Host(), ScopeA);
        OperationCanceledException? thrown = null;
        ports.Domains.Narrow = token =>
        {
            source.Cancel();
            try { token.ThrowIfCancellationRequested(); }
            catch (OperationCanceledException canceled) { thrown = canceled; throw; }
            return Proof;
        };

        var caught = await Assert.ThrowsAsync<OperationCanceledException>(
            () => ports.Records.PreflightAsync(Tenant, Slotted(), source.Token).AsTask());

        Assert.NotNull(thrown);
        Assert.Same(thrown, caught);
        Assert.Equal(1, ports.Domains.Calls);
    }

    [Theory]
    [InlineData("kind bind at kind defaults")]
    [InlineData("kind bind in the compile")]
    [InlineData("slot narrowing")]
    [InlineData("catalogue read")]
    public async Task a_dependency_fault_propagates_as_the_same_exception_object(string dependency)
    {
        var fault = new DependencyFault(dependency);
        var ports = Spied(Host(), ScopeA);
        var document = Slotted();
        switch (dependency)
        {
            case "kind bind at kind defaults":
                ports.Kinds.Fault = fault;
                break;
            case "kind bind in the compile":
                // A field that already carries its defaults provenance is not re-bound by kind defaults.
                ports.Kinds.Fault = fault;
                document = Body(Text("code") with { DefaultsProvenance = new("text", "1.0.0") });
                break;
            case "slot narrowing":
                ports.Domains.Narrow = _ => throw fault;
                break;
            default:
                ports.Catalogue.HeadFault = fault;
                break;
        }

        var caught = await Assert.ThrowsAsync<DependencyFault>(() => ports.Records.PreflightAsync(Tenant, document).AsTask());

        Assert.Same(fault, caught);
        // The first bind throws, so neither kind defaults nor the compile can swallow it and bind again.
        if (dependency.StartsWith("kind bind", StringComparison.Ordinal))
            Assert.Equal(1, ports.Kinds.Binds);
    }

    public static TheoryData<string, RecordTypeDocument, (string, string, string?)[], string[], bool> EdgeGuard() => new()
    {
        {
            "both targets named",
            Body(Reference("asset", "eam.asset-class", "eam.plant", ReferenceCardinality.One, ReferenceDeleteBehavior.Block)),
            [("records.reference.target_ambiguous", "/fields/0/reference", null)],
            [Home],
            false
        },
        {
            "neither target and a null record type id",
            Body(Reference("within", null, null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: "locatable")) with { RecordTypeId = null! },
            [("records.identity.record_type_id_required", "/record_type_id", null), ("records.reference.target_ambiguous", "/fields/0/reference", null)],
            [Home],
            false
        },
        {
            "blank required trait on a published target",
            Body(Reference("asset", "eam.asset-class", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: " ")),
            [("records.reference.trait_invalid", "/fields/0/reference/required_trait_id", null)],
            [Home, "head tenant-a/Records/eam.asset-class"],
            false
        },
        {
            "blank required trait on an unpublished target",
            Body(Reference("asset", "eam.ghost", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, trait: " ")),
            [("records.reference.trait_invalid", "/fields/0/reference/required_trait_id", null),
                ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            [Home, "head tenant-a/Records/eam.ghost"],
            false
        },
        {
            "missing cardinality",
            Body(Reference("asset", "eam.ghost", null, null, ReferenceDeleteBehavior.Block)),
            [("records.reference.cardinality_required", "/fields/0/reference/cardinality", null),
                ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            [Home, "head tenant-a/Records/eam.ghost"],
            false
        },
        {
            "missing on_delete",
            Body(Reference("asset", "eam.ghost", null, ReferenceCardinality.One, null)),
            [("records.reference.on_delete_required", "/fields/0/reference/on_delete", null),
                ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            [Home, "head tenant-a/Records/eam.ghost"],
            false
        },
        {
            "parent defect",
            Body(Reference("site", "eam.ghost", null, ReferenceCardinality.Many, ReferenceDeleteBehavior.Block, parent: true)),
            [("records.reference.parent_cardinality", "/fields/0/reference/cardinality", null),
                ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            [Home, "head tenant-a/Records/eam.ghost"],
            false
        },
        {
            "binding conflict",
            Body(Reference("asset", "eam.ghost", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block) with { Binding = Text("asset").Binding }),
            [("records.reference.binding_conflict", "/fields/0/reference", null),
                ("records.reference.target_unresolved", "/fields/0/reference/target_type_id", null)],
            [Home, "head tenant-a/Records/eam.ghost"],
            false
        },
        {
            "blank class",
            Body(Text("code")) with { ClassId = " " },
            [("records.class.required", "/class_id", null)],
            [],
            true
        },
    };

    [Theory]
    [MemberData(nameof(EdgeGuard))]
    public async Task the_edge_guard_prunes_only_its_lookup_copy_and_keeps_every_other_computable_refusal(string _,
        RecordTypeDocument document, (string, string, string?)[] expected, string[] reads, bool compiles)
    {
        var (host, _) = await WithPublishedTargets();
        var ports = Spied(host, ScopeA);

        var preflight = await ports.Records.PreflightAsync(Tenant, document);

        Assert.Equal(expected, Triples(preflight));
        Assert.Equal(reads, ports.Catalogue.Reads);
        // The compile reads the canonical candidate, never the pruned lookup copy: a refused reference keeps the schema null.
        Assert.Equal(compiles, preflight.JsonSchemaText is not null);
        LookupCopyOnly(document, preflight);
    }

    [Fact]
    public async Task a_blank_required_trait_still_resolves_its_published_target_and_keeps_its_edge_refusals()
    {
        var (host, meter) = await WithPublishedTargets();
        var ports = Spied(host, ScopeA);
        var document = Consumer(new("meter", "meter",
            Reference: new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, " ", Pin: PinOf(meter))));

        var preflight = await ports.Records.PreflightAsync(Tenant, document);

        Assert.Equal([
            ("records.reference.trait_invalid", "/fields/0/reference/required_trait_id", null),
            ("records.reference.dependency_undeclared", "/fields/0/reference", "eam-core/eam.meter@1.0.0"),
        ], Triples(preflight));
        Assert.Equal([Home, "head tenant-a/Records/eam.meter"], ports.Catalogue.Reads);
        LookupCopyOnly(document, preflight);
    }

    [Fact]
    public async Task an_edge_free_body_makes_no_catalogue_call()
    {
        var ports = Spied(Host(), ScopeA);

        var preflight = await ports.Records.PreflightAsync(Tenant, Body(Text("code")) with { ClassId = null });

        Assert.Equal([("records.class.required", "/class_id", null)], Triples(preflight));
        Assert.Empty(ports.Catalogue.Reads);
    }

    [Fact]
    public async Task without_a_role_vocabulary_a_role_gated_body_fails_closed()
    {
        var gated = Body(Text("code")) with { CreationGate = RecordCreationGate.Role, CreationRoleId = "asset-author" };

        var without = await Spied(Host(), ScopeA, vocabulary: false).Records.PreflightAsync(Tenant, gated);
        var with = await Spied(Host(), ScopeA).Records.PreflightAsync(Tenant, gated);

        Assert.Equal([("records.creation_role.unknown", "/creation_role_id", null)], Triples(without));
        Assert.Empty(with.Refusals);
    }

    [Fact]
    public async Task a_multi_defect_body_reports_structure_then_compile_then_edges_in_one_literal_sequence()
    {
        var (host, meter) = await WithPublishedTargets();
        var document = new RecordTypeDocument(new(Tenant, "fleet-ops", Contract, "fleet-ops", Exposes: new(0)), "Work Order",
            "fleet-ops.work-order",
            [
                new("code", "Code", new(new("text", "1.0.0", new Dictionary<string, string>()), new(false, 2, 1, [], null))),
                Reference("asset", "fleet-ops.ghost", null, null, ReferenceDeleteBehavior.Block),
                new("meter", "meter", Reference: new("eam.meter", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block, Pin: PinOf(meter))),
            ],
            RetentionClockFieldId: "missing", ClassId: "eam.missing", RecordClass: null, CreationGate: RecordCreationGate.Role,
            CreationRoleId: "ghost-author");

        var preflight = await host.Records.PreflightAsync(Tenant, document);

        Assert.Equal([
            ("records.package.exposure_invalid", "/envelope/exposes/interface_version", null),
            ("records.reference.cardinality_required", "/fields/1/reference/cardinality", null),
            ("records.record_class.required", "/record_class", null),
            ("field.constraint_intersection_empty", "/fields/0/binding/constraints", null),
            ("records.retention.clock_field_unresolved", "/retention_clock_field_id", null),
            ("records.class.unresolved", "/class_id", null),
            ("records.creation_role.unknown", "/creation_role_id", null),
            ("records.reference.target_unresolved", "/fields/1/reference/target_type_id", null),
            ("records.reference.dependency_undeclared", "/fields/2/reference", "eam-core/eam.meter@1.0.0"),
        ], Triples(preflight));
        Assert.Null(preflight.JsonSchemaText);
        Assert.Equal([("records.retention.unset", "/retention_policy")], Warnings(preflight));
    }

    [Fact]
    public async Task a_clean_body_previews_the_schema_publication_then_registers_and_only_the_retention_warning()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var draft = Read((await host.Catalogue.ListHistoryAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.asset-class")))[^1].Document.BodyJson);

        var preflight = await host.Records.PreflightAsync(Tenant, draft);
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");

        Assert.Empty(preflight.Refusals);
        Assert.Equal([("records.retention.unset", "/retention_policy")], Warnings(preflight));
        Assert.Equal(published.Schema.JsonSchemaText, preflight.JsonSchemaText);
        Assert.Equal(CreatedAssetClassJson,
            Encoding.UTF8.GetString(RecordTypeDefinitionJson.SerializeCanonical(Assert.IsType<RecordTypeDocument>(preflight.Document))));
    }

    [Fact]
    public async Task the_returned_document_is_the_canonical_materialized_candidate_not_the_typed_input()
    {
        // v4 A2: the returned Document is the canonical materialized candidate. The canonical form omits an explicit
        // form_only gate, and kind defaults add the field's defaults provenance.
        var input = Body(Text("code")) with { CreationGate = RecordCreationGate.FormOnly };

        var preflight = await Host().Records.PreflightAsync(Tenant, input);

        var document = Assert.IsType<RecordTypeDocument>(preflight.Document);
        Assert.Empty(preflight.Refusals);
        Assert.Null(document.CreationGate);
        Assert.Equal(new("text", "1.0.0"), document.Fields[0].DefaultsProvenance);
        Assert.Equal(RecordCreationGate.FormOnly, input.CreationGate);
        Assert.Null(input.Fields[0].DefaultsProvenance);
    }

    [Fact]
    public async Task an_edge_only_defect_still_returns_its_schema()
    {
        var host = Host();

        var clean = await host.Records.PreflightAsync(Tenant, Body(Text("code")));
        var edge = await host.Records.PreflightAsync(Tenant, Body(Text("code")) with { ClassId = "eam.missing" });

        Assert.Equal([("records.class.unresolved", "/class_id", null)], Triples(edge));
        Assert.NotNull(clean.JsonSchemaText);
        Assert.Equal(clean.JsonSchemaText, edge.JsonSchemaText);
    }

    [Fact]
    public void duplicate_refusals_merge_on_code_pointer_and_target_keeping_first_order()
    {
        DefinitionRefusal first = new("records.a", "/x"), second = new("records.b", "/y"),
            targeted = new("records.a", "/x", "pkg/one@1.0.0"), retargeted = new("records.a", "/x", "pkg/two@1.0.0");

        var distinct = RecordTypeDefinitionStore.Distinct(
            [first, second, new("records.a", "/x"), targeted, retargeted, new("records.a", "/x", "pkg/one@1.0.0"), new("records.b", "/y")]);

        Assert.Equal([first, second, targeted, retargeted], distinct);
    }

    [Fact]
    public async Task diagnostics_carry_no_warning_code_and_binding_returns_no_warnings()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "create");
        var published = await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "publish");
        var body = Read(published.Revision.Document.BodyJson);

        var preflight = await host.Records.PreflightAsync(Tenant, body);
        var warningCodes = preflight.Warnings.Select(warning => warning.Code).ToArray();
        var diagnosed = await host.Records.DiagnoseAsync(Tenant, body);
        var refused = await host.Records.DiagnoseAsync(Tenant, body with { ClassId = "eam.missing" });
        var bound = Assert.IsType<RecordTypePublication>(await host.Records.ResolvePublishedSchemaAsync(Tenant, "eam.asset-class"));

        Assert.Equal(["records.retention.unset"], warningCodes);
        Assert.Empty(diagnosed.Refusals);
        Assert.Equal([("records.class.unresolved", "/class_id")], refused.Refusals.Select(refusal => (refusal.Code, refusal.Pointer)));
        Assert.DoesNotContain(preflight.Refusals.Concat(diagnosed.Refusals).Concat(refused.Refusals), refusal => warningCodes.Contains(refusal.Code));
        Assert.Empty(bound.PolicyWarnings);
    }

    [Fact]
    public async Task every_preview_is_reported_at_the_publish_stage()
    {
        var stopped = await Host().Records.PreflightAsync(Tenant, Body(Text("code")) with { Fields = null! });
        var computed = await Host().Records.PreflightAsync(Tenant, Body(Text("code")));

        Assert.Equal(DefinitionAdmissionPhase.Publish, stopped.Stage);
        Assert.Equal(DefinitionAdmissionPhase.Publish, computed.Stage);
        Assert.Equal(DefinitionAdmissionPhase.Publish, new RecordTypePreflight(null, [], null, []).Stage);
    }

    // The returned document is the canonical candidate: the guard's pruning never reaches its class, references or indices.
    private static void LookupCopyOnly(RecordTypeDocument document, RecordTypePreflight preflight)
    {
        var returned = Assert.IsType<RecordTypeDocument>(preflight.Document);
        Assert.Equal(document.ClassId, returned.ClassId);
        Assert.Equal(document.Fields.Select(field => field.FieldKey), returned.Fields.Select(field => field.FieldKey));
        Assert.Equal(document.Fields.Select(field => field.Reference), returned.Fields.Select(field => field.Reference));
    }

    private static void Untouched(Ports ports)
    {
        Assert.Empty(ports.Catalogue.Reads);
        Assert.Equal(0, ports.Catalogue.Mutations);
        Assert.Equal(0, ports.Registry.Calls);
        Assert.Equal(0, ports.Kinds.Binds);
        Assert.Equal(0, ports.Traits.Resolves);
        Assert.Equal(0, ports.Domains.Calls);
    }

    private static (string Code, string Pointer, string? Target)[] Triples(RecordTypePreflight preflight)
        => preflight.Refusals.Select(refusal => (refusal.Code, refusal.Pointer, refusal.Target)).ToArray();

    private static (string Code, string Pointer)[] Warnings(RecordTypePreflight preflight)
        => preflight.Warnings.Select(warning => (warning.Code, warning.Pointer)).ToArray();

    private static RecordTypeDocument Read(string json) => RecordTypeDefinitionJson.Deserialize(Encoding.UTF8.GetBytes(json));

    // An admissible same-package body homed in the test host's published Class.
    private static RecordTypeDocument Body(params FieldDefinition[] fields) => new(new(Tenant, "eam", Contract, "eam-core"),
        "Work Order", "eam.work-order", fields, ClassId: "eam.equipment", RecordClass: RecordClass.Transactional);

    // The same body in package fleet-ops, with no requires entry.
    private static RecordTypeDocument Consumer(FieldDefinition field) => Body(field) with
    {
        Envelope = new(Tenant, "fleet-ops", Contract, "fleet-ops"),
        RecordTypeId = "fleet-ops.work-order",
    };

    // A clean body whose trait slot binding needs the domain runtime: it reaches every port when the preview runs.
    private static RecordTypeDocument Slotted() => Body(Text("street_address")) with
    {
        Traits = [new("addressable", "1.0.0", [new("street", "street_address")])],
    };

    private static FieldDefinition Text(string key)
        => new(key, key, new(new("text", "1.0.0", new Dictionary<string, string>()), new(false, 0, 1, [], null)));

    private static FieldDefinition Reference(string key, string? type, string? @class, ReferenceCardinality? cardinality,
        ReferenceDeleteBehavior? onDelete, string? trait = null, bool parent = false)
        => new(key, key, Reference: new(type, @class, cardinality, onDelete, trait, parent));

    private static RecordReferencePin PinOf(DefinitionRevision target) => new("eam-core", target.Document.Key.DefinitionId,
        target.Document.Version, "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(target.Document.BodyJson))), 1);

    // The asset class (eam-core, no traits) and the meter (eam-core, exposed at interface 1), both published.
    private static async Task<(TestHost Host, DefinitionRevision Meter)> WithPublishedTargets()
    {
        var host = Host();
        await host.Records.CreateDraftAsync(AssetClass("1.0.0"), "asset");
        await host.Records.PublishAsync(Tenant, "eam.asset-class", "1.0.0", 1, "asset-publish");
        await host.Records.CreateDraftAsync(new(Tenant, "eam", "Meter", "1.0.0", [new("code", "Code")], Contract,
            ClassId: "eam.equipment", RecordClass: RecordClass.Master, PackageId: "eam-core", Exposes: new(1)), "meter");
        await host.Records.PublishAsync(Tenant, "eam.meter", "1.0.0", 1, "meter-publish");
        return (host, (await host.Catalogue.GetPublishedHeadAsync(RecordTypeDefinitionStore.KeyOf(Tenant, "eam.meter")))!);
    }

    // Every history, head and registered schema, read from the real stores rather than through the spies.
    private static async Task<string> State(TestHost host)
    {
        var lines = new List<string>();
        foreach (var tenant in new[] { Tenant, OtherTenant })
            foreach (var kind in new[] { DefinitionKind.Records, DefinitionKind.Classes })
                foreach (var key in await host.Catalogue.ListKeysAsync(tenant, kind))
                {
                    lines.AddRange((await host.Catalogue.ListHistoryAsync(key)).Select(revision => $"history {revision}"));
                    lines.Add($"head {await host.Catalogue.GetPublishedHeadAsync(key)}");
                }
        await foreach (var schema in host.Registry.ListAsync()) lines.Add($"schema {schema.Id} {schema.JsonSchemaText}");
        return string.Join('\n', lines);
    }

    private static Ports Spied(TestHost host, FieldDomainScope? scope = null, bool vocabulary = true)
    {
        var catalogue = new SpyCatalogue(host.Catalogue);
        var registry = new SpyRegistry(host.Registry);
        var kinds = new CountingKinds(RecordWriteFieldRuntimeTests.FixtureKinds());
        var traits = new CountingTraits();
        var domains = new CountingDomains();
        var compiler = new RecordTypeSchemaCompiler(new RecordsIntentValidator(traits), kinds, new SharedValueDomainAdmission(), domains);
        return new(new RecordTypeDefinitionStore(catalogue, compiler, new RecordFieldDefaults(kinds), registry, Window, scope,
            vocabulary ? CreationRoles : null), catalogue, registry, kinds, traits, domains);
    }

    private sealed record Ports(RecordTypeDefinitionStore Records, SpyCatalogue Catalogue, SpyRegistry Registry, CountingKinds Kinds,
        CountingTraits Traits, CountingDomains Domains);

    private sealed class DependencyFault(string dependency) : Exception("Injected " + dependency + " fault.");

    // Delegates and records every read by verb and key; a mutating member is counted and refused.
    private sealed class SpyCatalogue(IVersionedDefinitionStore inner) : IVersionedDefinitionStore
    {
        public List<string> Reads { get; } = [];
        public int Mutations { get; private set; }
        public Exception? HeadFault { get; set; }

        public ValueTask<IReadOnlyList<DefinitionKey>> ListKeysAsync(string tenant, DefinitionKind kind,
            CancellationToken cancellationToken = default)
        {
            Reads.Add($"keys {tenant}/{kind}");
            return inner.ListKeysAsync(tenant, kind, cancellationToken);
        }

        public ValueTask<IReadOnlyList<DefinitionRevision>> ListHistoryAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        {
            Reads.Add("history " + Name(key));
            return inner.ListHistoryAsync(key, cancellationToken);
        }

        public ValueTask<DefinitionRevision?> GetPublishedHeadAsync(DefinitionKey key, CancellationToken cancellationToken = default)
        {
            Reads.Add("head " + Name(key));
            return HeadFault is { } fault ? throw fault : inner.GetPublishedHeadAsync(key, cancellationToken);
        }

        public ValueTask<DefinitionRevision?> ResolvePublishedAsync(DefinitionBinding binding, CancellationToken cancellationToken = default)
        {
            Reads.Add("resolve " + Name(binding.Key));
            return inner.ResolvePublishedAsync(binding, cancellationToken);
        }

        public ValueTask<DefinitionRevision?> GetPublicationReplayAsync(DefinitionKey key, string versionId, long expectedRevision,
            string requestId, CancellationToken cancellationToken = default)
        {
            Reads.Add("replay " + Name(key));
            return inner.GetPublicationReplayAsync(key, versionId, expectedRevision, requestId, cancellationToken);
        }

        public ValueTask<DefinitionRevision> SaveDraftAsync(DefinitionDocument document, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default) => throw Forbidden();

        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId,
            CancellationToken cancellationToken = default) => throw Forbidden();

        public ValueTask<DefinitionRevision> PublishAsync(DefinitionKey key, string versionId, long expectedRevision, string requestId,
            IReadOnlyList<DefinitionPublishedHeadCondition> conditions, CancellationToken cancellationToken = default) => throw Forbidden();

        public ValueTask<DefinitionRevision> RestoreAsDraftAsync(DefinitionKey key, string sourceVersionId, string draftVersionId,
            string draftVersion, long expectedRevision, string requestId, CancellationToken cancellationToken = default) => throw Forbidden();

        private InvalidOperationException Forbidden()
        {
            Mutations++;
            return new("The preview called a mutating catalogue member.");
        }

        private static string Name(DefinitionKey key) => $"{key.Tenant}/{key.Kind}/{key.DefinitionId}";
    }

    // Delegates reads; any registration is counted and refused.
    private sealed class SpyRegistry(ISchemaRegistry inner) : ISchemaRegistry
    {
        public int Calls { get; private set; }
        public int Registrations { get; private set; }

        public ValueTask<Schema?> GetAsync(SchemaId id, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.GetAsync(id, cancellationToken);
        }

        public ValueTask<Schema> RegisterAsync(string jsonSchemaText, IReadOnlyList<SchemaId>? parents = null,
            IReadOnlyList<string>? tags = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Registrations++;
            throw new InvalidOperationException("The preview registered a schema.");
        }

        public ValueTask<SchemaValidationResult> ValidateAsync(SchemaId id, ReadOnlyMemory<byte> documentBytes,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.ValidateAsync(id, documentBytes, cancellationToken);
        }

        public IAsyncEnumerable<Schema> ListAsync(string? tagFilter = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return inner.ListAsync(tagFilter, cancellationToken);
        }
    }

    private sealed class CountingKinds(IFieldKindRuntime inner) : IFieldKindRuntime
    {
        public int Binds { get; private set; }
        public Exception? Fault { get; set; }

        public ICompiledFieldKind Bind(FieldKindReference reference, string jsonPointer)
        {
            Binds++;
            return Fault is { } fault ? throw fault : inner.Bind(reference, jsonPointer);
        }
    }

    // The slotless locatable Trait and the addressable Trait with one optional slot.
    private sealed class CountingTraits : IRecordTraitSource
    {
        public int Resolves { get; private set; }

        public TraitDefinition? Resolve(string traitId, string version)
        {
            Resolves++;
            return (traitId, version) switch
            {
                ("locatable", "1.0.0") => new("locatable", "1.0.0", []),
                ("addressable", "1.0.0") => new("addressable", "1.0.0", [new("street", new(false, 0, 1, [], null), false, false)]),
                _ => null,
            };
        }
    }

    // Counts every call; narrowing proves the slot unless a test replaces it.
    private sealed class CountingDomains : IFieldDomainRuntime
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, ResolvedFieldConstraints>? Narrow { get; set; }

        public IReadOnlyList<FieldRefusal> Validate(ResolvedFieldConstraints constraints, ICompiledFieldKind kind,
            System.Text.Json.JsonElement value, string jsonPointer)
        {
            Calls++;
            throw new NotSupportedException();
        }

        public ValueTask<ResolvedValueDomain> ResolveAsync(ValueDomainDefinition domain, FieldDomainScope scope, string jsonPointer,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new NotSupportedException();
        }

        public ValueTask<ResolvedFieldConstraints> IntersectAsync(IReadOnlyList<FieldConstraintDefinition> constraints,
            FieldDomainScope scope, string jsonPointer, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new NotSupportedException();
        }

        public ValueTask<ResolvedFieldConstraints> NarrowAsync(FieldConstraintDefinition declared, FieldConstraintDefinition narrowed,
            FieldDomainScope scope, string jsonPointer, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(Narrow is { } narrow ? narrow(cancellationToken) : Proof);
        }
    }
}
