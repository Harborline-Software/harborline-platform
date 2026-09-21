using System.Text;
using System.Text.Json;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsDomainProofTests
{
    [Fact]
    public async Task Disjoint_complete_sources_refuse_the_bound_field()
    {
        var scheme = new TaxonomySchemeReference("workflow-state", "1.0.0");
        var boundary = new DomainBoundary();
        boundary.Schemes[scheme] = [Member("open")];
        boundary.Records["records.visible-state"] = [Member("closed", "{\"active\":true}")];
        var definition = Definition() with
        {
            Fields = [Field("state")],
            Traits =
            [
                Trait("taxonomy", new(TaxonomyScheme: scheme)),
                Trait("query", new(RecordQuery: Query())),
            ],
            TraitBindings = [Binding("taxonomy", "state"), Binding("query", "state")],
        };

        var result = await Validator(boundary).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.constraint_intersection_empty",
            JsonPointer: "/fields/0/constraints",
        });
    }

    [Fact]
    public async Task Unreadable_extra_members_cannot_hide_value_domain_widening()
    {
        var scheme = new TaxonomySchemeReference("floor", "1.0.0");
        var boundary = new DomainBoundary
        {
            CanRead = (scope, member) => member.Value != "hidden-extra",
        };
        boundary.Schemes[scheme] = [Member("shared")];
        boundary.Records["records.visible-state"] =
        [
            Member("shared", "{\"active\":true}"),
            Member("hidden-extra", "{\"active\":true}"),
        ];
        var floor = Constraint(domain: new(TaxonomyScheme: scheme));
        var widened = Constraint(domain: new(RecordQuery: Query()));
        var definition = Definition() with
        {
            Fields =
            [
                Field("floor") with { Constraints = floor },
                Field("child") with { RefinesFieldKey = "floor", Constraints = widened },
            ],
        };

        var result = await Validator(boundary).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.value_domain_widened",
            JsonPointer: "/fields/1/constraints",
        });
    }

    [Theory]
    [InlineData("required", "field.requirement_dropped")]
    [InlineData("multiplicity", "field.multiplicity_widened")]
    [InlineData("roles", "field.read_roles_widened")]
    public async Task Explicit_refinements_use_shared_widening_vocabulary(string widening, string code)
    {
        var floor = new FieldConstraintDefinition(
            widening == "required", 1, 3, ["reader"], null);
        var narrowed = widening switch
        {
            "required" => floor with { Required = false },
            "multiplicity" => floor with { MinimumCount = 0, MaximumCount = 4 },
            _ => floor with { ReadRoleIds = ["reader", "admin"] },
        };
        var definition = Definition() with
        {
            Fields =
            [
                Field("floor") with { Constraints = floor },
                Field("child") with { RefinesFieldKey = "floor", Constraints = narrowed },
            ],
        };

        var result = await Validator(new DomainBoundary()).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal =>
            refusal.Code == code && refusal.JsonPointer == "/fields/1/constraints");
    }

    [Fact]
    public async Task Standalone_field_domain_is_proved_as_a_refinement()
    {
        var definition = Definition() with
        {
            Fields =
            [
                Field("floor") with
                {
                    Constraints = Constraint(new(LiteralValues: ["accepted"])),
                },
                Field("child") with
                {
                    RefinesFieldKey = "floor",
                    ValueDomain = new(LiteralValues: ["accepted", "widened"]),
                },
            ],
        };

        var result = await Validator(new DomainBoundary()).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.value_domain_widened",
            JsonPointer: "/fields/1/value_domain",
        });
    }

    [Fact]
    public async Task Missing_refinement_constraints_inherit_the_complete_floor()
    {
        var definition = Definition() with
        {
            Fields =
            [
                Field("floor") with
                {
                    Constraints = new(true, 1, 1, [], new(LiteralValues: ["accepted"])),
                },
                Field("child") with { RefinesFieldKey = "floor" },
            ],
        };
        var compiler = Compiler(new DomainBoundary(), out var registry);

        var schema = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var admitted = await registry.ValidateAsync(schema.Id,
            Encoding.UTF8.GetBytes("{\"floor\":\"accepted\",\"child\":\"accepted\"}"));
        var missing = await registry.ValidateAsync(schema.Id,
            Encoding.UTF8.GetBytes("{\"floor\":\"accepted\"}"));
        var outside = await registry.ValidateAsync(schema.Id,
            Encoding.UTF8.GetBytes("{\"floor\":\"accepted\",\"child\":\"other\"}"));

        Assert.True(admitted.IsValid);
        Assert.False(missing.IsValid);
        Assert.False(outside.IsValid);
    }

    [Fact]
    public async Task Dangling_and_cyclic_refinements_are_explicit_refusals()
    {
        var dangling = Definition() with
        {
            Fields = [Field("child") with { RefinesFieldKey = "missing" }],
        };
        var cyclic = Definition() with
        {
            Fields =
            [
                Field("first") with { RefinesFieldKey = "second" },
                Field("second") with { RefinesFieldKey = "first" },
            ],
        };
        var validator = Validator(new DomainBoundary());

        var danglingResult = await validator.ValidateAsync(
            dangling, RecordsTestDomains.Scope, CancellationToken.None);
        var cyclicResult = await validator.ValidateAsync(
            cyclic, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(danglingResult.Refusals, refusal => refusal is
        {
            Code: "records.field.refinement_unresolved",
            JsonPointer: "/fields/0/refines_field_key",
        });
        Assert.Contains(cyclicResult.Refusals, refusal =>
            refusal.Code == "records.field.refinement_cycle");
    }

    [Fact]
    public async Task Invalid_scope_refuses_zero_field_definitions_before_source_access()
    {
        var boundary = new DomainBoundary();
        var validator = Validator(boundary);
        var sourced = Definition() with { Fields = [Field("state")] };

        var wrongTenant = await validator.ValidateAsync(
            sourced, new(new TenantId("tenant-b"), "actor"), CancellationToken.None);
        var missingPrincipal = await validator.ValidateAsync(
            sourced, new(RecordsTestDomains.Tenant, ""), CancellationToken.None);
        var validZeroField = await validator.ValidateAsync(
            Definition(), RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(wrongTenant.Refusals, refusal => refusal.Code == "field.value_domain_tenant_mismatch");
        Assert.Contains(missingPrincipal.Refusals, refusal => refusal.Code == "field.value_domain_principal_required");
        Assert.True(validZeroField.IsAdmitted);
        Assert.Equal(0, boundary.Opens);
    }

    [Fact]
    public async Task Malformed_typed_input_refuses_before_source_access()
    {
        var boundary = new DomainBoundary();
        var definition = Definition() with
        {
            Fields = [Field("state") with { Kind = null! }],
        };

        var result = await Validator(boundary).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "records.definition.null_forbidden",
            JsonPointer: "/fields/0/kind",
        });
        Assert.Equal(0, boundary.Opens);
    }

    [Fact]
    public async Task Records_structural_refusals_are_not_replaced_by_source_failures()
    {
        var boundary = new DomainBoundary
        {
            OpenException = new InvalidOperationException("must not open"),
        };
        var definition = Definition() with
        {
            RecordTypeId = "",
            Fields = [Field("state")],
        };

        var result = await Validator(boundary).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "records.definition.record_type_id_required",
            JsonPointer: "/record_type_id",
        });
        Assert.Equal(0, boundary.Opens);
    }

    [Theory]
    [InlineData(false, true, "field.value_domain_snapshot_incomplete")]
    [InlineData(true, false, "field.value_domain_source_unresolved")]
    public async Task Incomplete_or_unresolved_sources_refuse_before_registration(
        bool complete,
        bool sourcePresent,
        string code)
    {
        var scheme = new TaxonomySchemeReference("workflow-state", "1.0.0");
        var boundary = new DomainBoundary { IsComplete = complete };
        if (sourcePresent) boundary.Schemes[scheme] = [Member("open")];
        var definition = Definition() with
        {
            Fields = [Field("state") with { ValueDomain = new(TaxonomyScheme: scheme) }],
        };
        var compiler = Compiler(boundary, out var registry);

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => compiler
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

        Assert.Contains(error.Refusals, refusal => refusal.Code == code);
        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Unbound_optional_slots_still_require_complete_domain_proofs()
    {
        var definition = Definition() with
        {
            Traits =
            [
                new("optional", "1.0.0", "Optional",
                    [new("state", false, false, Constraint(new(TaxonomyScheme: new("missing", "1.0.0"))))]),
            ],
        };

        var result = await Validator(new DomainBoundary()).ValidateAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.value_domain_source_unresolved",
            JsonPointer: "/traits/0/slots/0/constraints",
        });
    }

    [Fact]
    public async Task Changing_snapshot_revision_refuses_before_registration()
    {
        var boundary = new DomainBoundary { RevisionForOpen = open => $"snapshot-{open}" };
        var definition = Definition() with { Fields = [Field("first"), Field("second")] };
        var compiler = Compiler(boundary, out var registry);

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => compiler
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

        Assert.Contains(error.Refusals, refusal => refusal.Code == "records.field.snapshot_revision_inconsistent");
        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Cancellation_and_source_faults_propagate_without_registration()
    {
        var cancellationBoundary = new DomainBoundary { PauseOpen = true };
        var cancellationCompiler = Compiler(cancellationBoundary, out var cancellationRegistry);
        using var cancellation = new CancellationTokenSource();
        var pending = cancellationCompiler.CompileAndRegisterAsync(
            Definition() with { Fields = [Field("state")] },
            RecordsTestDomains.Scope,
            cancellation.Token).AsTask();
        await cancellationBoundary.OpenEntered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(await RegisteredSchemas(cancellationRegistry));

        var faultBoundary = new DomainBoundary { OpenException = new InvalidOperationException("source failed") };
        var faultCompiler = Compiler(faultBoundary, out var faultRegistry);
        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => faultCompiler
            .CompileAndRegisterAsync(
                Definition() with { Fields = [Field("state")] },
                RecordsTestDomains.Scope,
                CancellationToken.None).AsTask());
        Assert.Equal("source failed", fault.Message);
        Assert.Empty(await RegisteredSchemas(faultRegistry));
    }

    [Fact]
    public async Task Caller_collection_mutation_during_source_await_cannot_change_the_compiled_candidate()
    {
        var boundary = new DomainBoundary { PauseOpen = true };
        var compiler = Compiler(boundary, out _);
        var fields = new List<RecordFieldDefinition> { Field("stable") };
        var definition = Definition() with { Fields = fields };

        var pending = compiler.CompileSchemaAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask();
        await boundary.OpenEntered.Task;
        fields.Clear();
        fields.Add(Field("changed"));
        boundary.ReleaseOpen.TrySetResult();
        var schema = await pending;

        using var document = JsonDocument.Parse(schema);
        var properties = document.RootElement.GetProperty("properties");
        Assert.True(properties.TryGetProperty("stable", out _));
        Assert.False(properties.TryGetProperty("changed", out _));
    }

    [Fact]
    public async Task Readable_subsets_do_not_change_static_schema_identity_or_authored_sources()
    {
        var query = Query();
        var boundary = new DomainBoundary
        {
            CanRead = (scope, member) => scope.Principal != "restricted" || member.Value != "hidden",
        };
        boundary.Records[query.RecordTypeId] =
        [
            Member("visible", "{\"active\":true}"),
            Member("hidden", "{\"active\":true}"),
        ];
        var definition = Definition() with
        {
            Fields = [Field("state") with { ValueDomain = new(RecordQuery: query) }],
        };
        var compiler = Compiler(boundary, out _);

        var restricted = await compiler.CompileSchemaAsync(
            definition, new(RecordsTestDomains.Tenant, "restricted"), CancellationToken.None);
        var complete = await compiler.CompileSchemaAsync(
            definition, new(RecordsTestDomains.Tenant, "complete"), CancellationToken.None);

        Assert.Equal(complete, restricted);
        Assert.DoesNotContain("\"enum\"", complete, StringComparison.Ordinal);
        Assert.Equal(query, definition.Fields[0].ValueDomain?.RecordQuery);
    }

    [Fact]
    public async Task Raw_domain_errors_use_shared_codes_and_exact_nested_pointers()
    {
        const string duplicateDomain = """
            {
              "envelope":{"definition_id":"definition.example","version":"1.0.0","tenant_id":"tenant-a","package_id":"package-a","provenance":"test"},
              "record_type_id":"records.example","name":"Example","key":"example","class_id":"class.example",
              "fields":[{"name":"State","key":"state","kind":{"kind_id":"text","version":"1.0.0","parameters":{}},
                "constraints":{"required":false,"minimum_count":0,"maximum_count":1,"read_role_ids":[],
                  "value_domain":{"literal_values":["a"],"literal_values":["b"]}}}]
            }
            """;
        const string malformedUnboundSlot = """
            {
              "envelope":{"definition_id":"definition.example","version":"1.0.0","tenant_id":"tenant-a","package_id":"package-a","provenance":"test"},
              "record_type_id":"records.example","name":"Example","key":"example","class_id":"class.example",
              "traits":[{"trait_id":"trait.example","version":"1.0.0","name":"Example","slots":[
                {"slot_key":"state","required":false,"blocks_release":false,
                 "constraints":{"required":false,"minimum_count":0,"maximum_count":1,"read_role_ids":[],
                   "value_domain":{"literal_values":"a"}}}]}]
            }
            """;
        var validator = Validator(new DomainBoundary());

        var duplicate = await validator.ValidateJsonAsync(
            duplicateDomain, RecordsTestDomains.Scope, CancellationToken.None);
        var malformed = await validator.ValidateJsonAsync(
            malformedUnboundSlot, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Contains(duplicate.Refusals, refusal => refusal is
        {
            Code: "field.value_domain_member_duplicate",
            JsonPointer: "/fields/0/constraints/value_domain/literal_values",
        });
        Assert.Contains(malformed.Refusals, refusal => refusal is
        {
            Code: "field.value_domain_shape_invalid",
            JsonPointer: "/traits/0/slots/0/constraints/value_domain/literal_values",
        });
    }

    private static RecordsIntentValidator Validator(DomainBoundary boundary)
        => new(RecordsTestDomains.CreateRuntime(boundary, boundary), RecordsTestKinds.Text);

    private static RecordsDefinitionCompiler Compiler(
        DomainBoundary boundary,
        out InMemorySchemaRegistry registry)
    {
        registry = new(fieldKindRuntime: RecordsTestKinds.Text);
        return new(registry, RecordsTestDomains.CreateRuntime(boundary, boundary), RecordsTestKinds.Text);
    }

    private static RecordTypeDefinition Definition() => new()
    {
        Envelope = new("definition.example", "1.0.0", "tenant-a", "package-a", "test"),
        RecordTypeId = "records.example",
        Name = "Example",
        Key = "example",
        ClassId = "class.example",
    };

    private static RecordFieldDefinition Field(string key) => new()
    {
        Name = key,
        Key = key,
        Kind = new("text", "1.0.0", new Dictionary<string, string>()),
    };

    private static FieldConstraintDefinition Constraint(ValueDomainDefinition? domain = null)
        => new(true, 1, 1, ["reader"], domain);

    private static TraitDefinition Trait(string id, ValueDomainDefinition domain)
        => new(id, "1.0.0", id, [new("state", true, true, Constraint(domain))]);

    private static TraitSlotBinding Binding(string trait, string field)
        => new(trait, "1.0.0", "state", field);

    private static RecordQueryValueSource Query()
        => new("records.visible-state", "{\"==\":[{\"var\":\"active\"},true]}");

    private static FieldDomainMember Member(string value, string fields = "{}")
        => new(value, value, JsonSerializer.Deserialize<JsonElement>(fields));

    private static async Task<IReadOnlyList<Schema>> RegisteredSchemas(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    private sealed class DomainBoundary : IFieldDomainSource, IFieldDomainReadAuthority
    {
        internal Dictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> Schemes { get; } = [];
        internal Dictionary<string, IReadOnlyList<FieldDomainMember>> Records { get; } = [];
        internal bool IsComplete { get; init; } = true;
        internal bool PauseOpen { get; init; }
        internal Exception? OpenException { get; init; }
        internal Func<int, string> RevisionForOpen { get; init; } = _ => "snapshot-1";
        internal Func<FieldDomainScope, FieldDomainMember, bool>? CanRead { get; init; }
        internal int Opens { get; private set; }
        internal TaskCompletionSource OpenEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseOpen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(
            TenantId tenant,
            CancellationToken cancellationToken = default)
        {
            Opens++;
            if (OpenException is not null) throw OpenException;
            if (PauseOpen)
            {
                OpenEntered.TrySetResult();
                await ReleaseOpen.Task.WaitAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new Snapshot(tenant, RevisionForOpen(Opens), IsComplete, Schemes, Records);
        }

        public ValueTask<bool> CanReadAsync(
            FieldDomainScope scope,
            ValueDomainDefinition domain,
            FieldDomainMember member,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CanRead?.Invoke(scope, member) ?? true);
        }
    }

    private sealed record Snapshot(
        TenantId Tenant,
        string Revision,
        bool IsComplete,
        IReadOnlyDictionary<TaxonomySchemeReference, IReadOnlyList<FieldDomainMember>> Schemes,
        IReadOnlyDictionary<string, IReadOnlyList<FieldDomainMember>> Records) : IFieldDomainSnapshot
    {
        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme)
            => Schemes.GetValueOrDefault(scheme);

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId)
            => Records.GetValueOrDefault(recordTypeId);
    }
}
