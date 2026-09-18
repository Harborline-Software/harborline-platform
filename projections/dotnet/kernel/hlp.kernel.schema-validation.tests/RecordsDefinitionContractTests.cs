using System.Text;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsDefinitionContractTests
{

    [Fact]
    public async Task Compiler_registers_one_stable_schema_identity_used_for_runtime_validation()
    {
        var registry = new InMemorySchemaRegistry();
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestKinds.Text);
        var definition = ValidDefinition() with
        {
            Fields =
            [
                Field("Serial number", "serial_number") with
                {
                    Pattern = "^[a-z]+$",
                    ValueDomain = new ValueDomainDefinition(LiteralValues: ["assigned", "unassigned"]),
                    Constraints = new FieldConstraintDefinition(true, 1, 1, [], null),
                },
            ],
        };

        var first = await compiler.CompileAndRegisterAsync(definition);
        var replay = await compiler.CompileAndRegisterAsync(RecordsDefinitionJson.Deserialize(
            RecordsDefinitionJson.SerializeCanonical(definition)));
        var registered = await registry.GetAsync(first.Id);
        var valid = await registry.ValidateAsync(
            first.Id,
            Encoding.UTF8.GetBytes("""{"serial_number":"assigned"}"""));
        var invalid = await registry.ValidateAsync(
            first.Id,
            Encoding.UTF8.GetBytes("""{"serial_number":"outside-domain"}"""));

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(first.Id, registered?.Id);
        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Contains(invalid.Errors, error =>
            error.JsonPointer == "/serial_number" && error.Code is "enum" or "pattern");
    }

    [Fact]
    public void Field_kind_defaults_materialize_once_with_provenance_and_preserve_explicit_edits()
    {
        var materializer = new RecordsFieldKindDefaultMaterializer(
        [
            new AdmittedFieldKind(
                "email",
                "1.0.0",
                new FieldGovernanceDefinition(true, true, false, "personal")),
            new AdmittedFieldKind(
                "phone",
                "2.0.0",
                new FieldGovernanceDefinition(true, true, false, "personal")),
        ]);
        var definition = ValidDefinition() with
        {
            Fields =
            [
                Field("Email", "email") with
                {
                    Kind = new FieldKindReference("email", "1.0.0", new Dictionary<string, string>()),
                },
            ],
        };

        var created = materializer.Materialize(definition);
        var materialized = Assert.Single(created.Fields);
        Assert.Equal(new FieldGovernanceDefinition(true, true, false, "personal"), materialized.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("email", "1.0.0"), materialized.KindDefaultProvenance);

        var explicitlyEdited = created with
        {
            Fields =
            [
                materialized with
                {
                    Kind = new FieldKindReference("phone", "2.0.0", new Dictionary<string, string>()),
                    Governance = new FieldGovernanceDefinition(false, false, false, "internal"),
                },
            ],
        };

        var replay = materializer.Materialize(explicitlyEdited);

        var preserved = Assert.Single(replay.Fields);
        Assert.Equal(new FieldGovernanceDefinition(false, false, false, "internal"), preserved.Governance);
        Assert.Equal(new FieldKindDefaultProvenance("email", "1.0.0"), preserved.KindDefaultProvenance);

        var unresolvedKind = explicitlyEdited with
        {
            Fields =
            [
                explicitlyEdited.Fields[0] with
                {
                    Kind = new FieldKindReference("phone", "3.0.0", new Dictionary<string, string>()),
                },
            ],
        };
        var refusal = Assert.Throws<RecordsDefinitionAdmissionException>(
            () => materializer.Materialize(unresolvedKind));
        Assert.Contains(refusal.Refusals, item =>
            item is { Code: "records.field.kind_unresolved", JsonPointer: "/fields/0/kind" });
    }

    [Fact]
    public void Record_type_identity_is_required()
    {
        var definition = ValidDefinition() with { RecordTypeId = "" };

        var result = new RecordsIntentValidator().Validate(definition);

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("records.definition.record_type_id_required", refusal.Code);
        Assert.Equal("/record_type_id", refusal.JsonPointer);
    }

    [Fact]
    public void Record_type_identity_is_immutable_within_one_definition_version()
    {
        var published = ValidDefinition();
        var changed = published with { RecordTypeId = "records.replacement" };

        var result = new RecordsIntentValidator().Validate(changed, published);

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("records.definition.record_type_id_immutable", refusal.Code);
        Assert.Equal("/record_type_id", refusal.JsonPointer);
    }

    [Fact]
    public void Complete_typed_definition_round_trips_without_an_opaque_body()
    {
        var definition = ValidDefinition() with
        {
            RecordClass = RecordClassKind.Master,
            Classes = [new RecordClassDefinition("class.master", "Master data")],
            Fields =
            [
                new RecordFieldDefinition
                {
                    Name = "Serial number",
                    Key = "serial_number",
                    Kind = new FieldKindReference("text", "1.0.0", new Dictionary<string, string> { ["max_length"] = "80" }),
                    RequiredCondition = new RuleExpression("hl-rules/1", "true"),
                    DefaultExpression = new RuleExpression("hl-rules/1", "'unassigned'"),
                    ValueDomain = new ValueDomainDefinition(LiteralValues: ["unassigned", "assigned"]),
                    Pattern = "^[a-z]+$",
                    Governance = new FieldGovernanceDefinition(false, false, false, "internal"),
                    WriteRoleId = "role.asset-writer",
                    ConflictPolicy = FieldConflictPolicy.Ask,
                    IsIdentity = true,
                },
            ],
            Traits =
            [
                new TraitDefinition(
                    "trait.identifiable",
                    "1.0.0",
                    "Identifiable",
                    [new TraitSlotDefinition("identity", true, true, new FieldConstraintDefinition(true, 1, 1, ["role.asset-reader"], null))]),
            ],
            TraitBindings = [new TraitSlotBinding("trait.identifiable", "1.0.0", "identity", "serial_number")],
            UniqueConstraints = [new UniqueConstraintDefinition("asset-identity", ["serial_number"])],
            Policies = new RecordTypePolicies("reason-required", "enabled", "tenant-visible", "retain-7-years"),
            RetentionClockFieldKey = "created_at",
            CreationGate = RecordCreationGate.FormOnly,
            OfflineCaptureMode = OfflineRecordCaptureMode.CaptureThenConfirm,
            Categories = ["equipment"],
            LifecycleWorkflowId = "workflow.asset-lifecycle",
            PermissionFloor = new PermissionFloorDefinition(["role.asset-reader"], ["role.asset-writer"], ["role.asset-manager"]),
            Standings = [new StandingDefinition("owner", new RuleExpression("hl-rules/1", "owner_id == me"))],
            Measures = [new MeasureDefinition("age_days", "measure.asset-age")],
            MergePolicyId = "merge.asset-by-serial",
            EffectiveDating = true,
        };

        var json = RecordsDefinitionJson.SerializeCanonical(definition);
        var decoded = RecordsDefinitionJson.Deserialize(json);

        Assert.Equal(json, RecordsDefinitionJson.SerializeCanonical(decoded));
        Assert.Contains("\"record_type_id\":\"records.asset\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"body\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Admission_returns_every_field_addressable_structural_refusal()
    {
        var definition = ValidDefinition() with
        {
            Fields =
            [
                Field("Duplicate", "duplicate"),
                Field("Duplicate display names are legal", "duplicate"),
                Field("Mixed domain", "mixed") with
                {
                    ValueDomain = new ValueDomainDefinition(
                        LiteralValues: ["a"],
                        TaxonomyScheme: new TaxonomySchemeReference("scheme.a", "1.0.0")),
                },
                Field("Untargeted reference", "owner") with
                {
                    Reference = new FieldReferenceDefinition(
                        null,
                        null,
                        null,
                        ReferenceCardinality.One,
                        ReferenceDeleteBehavior.Block),
                },
                Field("Translated identity", "translated_identity") with
                {
                    IsIdentity = true,
                    IsTranslatable = true,
                },
            ],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.False(result.IsAdmitted);
        Assert.Equal(
            [
                ("records.field.identity_duplicate", "/fields/0/key"),
                ("records.field.identity_duplicate", "/fields/1/key"),
                ("records.field.value_domain_source_count", "/fields/2/value_domain"),
                ("records.field.reference_target_count", "/fields/3/reference"),
                ("records.field.identity_translatable", "/fields/4/is_translatable"),
            ],
            result.Refusals.Select(refusal => (refusal.Code, refusal.JsonPointer)));
    }

    [Fact]
    public void Admission_refuses_invalid_scoped_identities_and_undeclared_cross_package_edges()
    {
        var definition = ValidDefinition() with
        {
            ClassId = "",
            Classes =
            [
                new RecordClassDefinition("class.duplicate", "First"),
                new RecordClassDefinition("class.duplicate", "Second"),
            ],
            Fields =
            [
                Field("Trait-only reference", "owner") with
                {
                    Reference = new FieldReferenceDefinition(
                        null,
                        null,
                        "trait.owner",
                        ReferenceCardinality.One,
                        ReferenceDeleteBehavior.Block),
                },
                Field("Cross-package reference", "site") with
                {
                    Reference = new FieldReferenceDefinition(
                        "records.site",
                        null,
                        null,
                        ReferenceCardinality.One,
                        ReferenceDeleteBehavior.Block,
                        TargetPackageId: "package-sites"),
                },
            ],
            Traits =
            [
                new TraitDefinition(
                    "trait.duplicate-slots",
                    "1.0.0",
                    "Duplicate slots",
                    [
                        new TraitSlotDefinition("slot", false, false, Constraints()),
                        new TraitSlotDefinition("slot", false, false, Constraints()),
                    ]),
            ],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.definition.class_count", JsonPointer: "/class_id" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.class.identity_duplicate", JsonPointer: "/classes/0/class_id" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.reference_target_count", JsonPointer: "/fields/0/reference" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.cross_package_dependency_missing", JsonPointer: "/fields/1/reference/target_package_id" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.slot_identity_duplicate", JsonPointer: "/traits/0/slots/0/slot_key" });
    }

    [Fact]
    public void Raw_authoring_intent_refuses_forbidden_members_with_stable_pointers()
    {
        const string json = """
            {
              "record_type_id": "records.asset",
              "class_ids": ["class.master", "class.reference"],
              "can_be_inspected": true,
              "fields": [
                {
                  "key": "status",
                  "enum": ["open", "closed"],
                  "retention_policy": "field-owned",
                  "value_domain": { "pattern": "^(open|closed)$" }
                }
              ]
            }
            """;

        var result = new RecordsIntentValidator().ValidateJson(json);

        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.definition.class_count", JsonPointer: "/class_ids" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.definition.foreign_behavior_flag", JsonPointer: "/can_be_inspected" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.inline_membership_forbidden", JsonPointer: "/fields/0/enum" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.retention_forbidden", JsonPointer: "/fields/0/retention_policy" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.pattern_membership_forbidden", JsonPointer: "/fields/0/value_domain/pattern" });
    }

    [Fact]
    public void Trait_targeted_reference_uses_one_shared_target_predicate()
    {
        var reference = new FieldReferenceDefinition(
            "records.person",
            null,
            "trait.employee",
            ReferenceCardinality.One,
            ReferenceDeleteBehavior.Block);
        var target = new RecordReferenceTargetFacts(
            "records.person",
            "class.master",
            new HashSet<string>(["trait.customer"], StringComparer.Ordinal));

        Assert.False(RecordsReferenceAdmission.TargetMatches(reference, target));
        var result = RecordsReferenceAdmission.Validate(reference, target, "/fields/0/reference");
        Assert.Contains(result.Refusals, refusal =>
            refusal is
            {
                Code: "records.reference.target_trait_missing",
                JsonPointer: "/fields/0/reference/required_trait_id",
            });
    }

    [Fact]
    public void Trait_admission_refuses_unresolved_ambiguous_unfilled_and_incompatible_slots_together()
    {
        var domainA = new ValueDomainDefinition(LiteralValues: ["a"]);
        var domainB = new ValueDomainDefinition(LiteralValues: ["b"]);
        var definition = ValidDefinition() with
        {
            Fields =
            [
                Field("Shared", "shared") with
                {
                    Constraints = new FieldConstraintDefinition(true, 1, 1, ["reader"], domainA),
                },
                Field("Alternate", "alternate") with
                {
                    Constraints = new FieldConstraintDefinition(true, 1, 1, ["reader"], domainA),
                },
            ],
            Traits =
            [
                Trait("trait.a", "slot", domainA),
                Trait("trait.b", "slot", domainB),
                Trait("trait.unfilled", "slot", domainA),
            ],
            TraitBindings =
            [
                new TraitSlotBinding("trait.a", "2.0.0", "slot", "shared"),
                new TraitSlotBinding("trait.a", "1.0.0", "slot", "shared"),
                new TraitSlotBinding("trait.a", "1.0.0", "slot", "alternate"),
                new TraitSlotBinding("trait.b", "1.0.0", "slot", "shared"),
            ],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.version_unresolved", JsonPointer: "/trait_bindings/0/trait_version" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.binding_ambiguous", JsonPointer: "/trait_bindings/2" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.slot_unfilled", JsonPointer: "/traits/2/slots/0" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.constraint_intersection_empty", JsonPointer: "/fields/0/constraints" });
    }

    [Fact]
    public void Refinements_and_trait_bindings_may_narrow_but_never_widen()
    {
        var floor = new FieldConstraintDefinition(
            true,
            1,
            3,
            ["reader"],
            new ValueDomainDefinition(LiteralValues: ["a", "b"]));
        var widened = new FieldConstraintDefinition(
            false,
            0,
            4,
            ["reader", "admin"],
            new ValueDomainDefinition(LiteralValues: ["a", "b", "c"]));
        var definition = ValidDefinition() with
        {
            Fields =
            [
                Field("Floor", "floor") with { Constraints = floor },
                Field("Widened refinement", "widened") with
                {
                    RefinesFieldKey = "floor",
                    Constraints = widened,
                },
                Field("Widened slot binding", "slot_field") with { Constraints = widened },
            ],
            Traits =
            [
                new TraitDefinition(
                    "trait.floor",
                    "1.0.0",
                    "Floor",
                    [new TraitSlotDefinition("slot", true, true, floor)]),
            ],
            TraitBindings = [new TraitSlotBinding("trait.floor", "1.0.0", "slot", "slot_field")],
        };

        var result = new RecordsIntentValidator().Validate(definition);

        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.field.refinement_widens", JsonPointer: "/fields/1/constraints" });
        Assert.Contains(result.Refusals, refusal =>
            refusal is { Code: "records.trait.binding_widens", JsonPointer: "/fields/2/constraints" });
    }

    private static RecordTypeDefinition ValidDefinition() => new()
    {
        Envelope = new RecordDefinitionEnvelope(
            DefinitionId: "definition.asset",
            Version: "1.0.0",
            TenantId: "tenant-a",
            PackageId: "package-a",
            Provenance: "test"),
        RecordTypeId = "records.asset",
        Name = "Asset",
        Key = "asset",
        ClassId = "class.master",
    };

    private static RecordFieldDefinition Field(string name, string key) => new()
    {
        Name = name,
        Key = key,
        Kind = new FieldKindReference("text", "1.0.0", new Dictionary<string, string>()),
    };

    private static TraitDefinition Trait(string id, string slot, ValueDomainDefinition domain) => new(
        id,
        "1.0.0",
        id,
        [new TraitSlotDefinition(slot, true, true, new FieldConstraintDefinition(true, 1, 1, ["reader"], domain))]);

    private static FieldConstraintDefinition Constraints() => new(false, 0, null, [], null);
}
