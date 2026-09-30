using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.Assets.Common;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsTraitBindingTests
{
    [Fact]
    public async Task records_ck_37_and_39_widening_slot_binding_refuses_with_the_field_runtime_code_before_registration()
    {
        var domains = new NarrowingDomainRuntime();
        var registry = Registry();
        var declared = new FieldConstraintDefinition(true, 1, 1, [], null);
        var compiler = Compiler(domains, declared);
        var candidate = Candidate(declared, new(false, 0, 1, [], null));

        var result = await compiler.CompileAndRegisterAsync(candidate, null, registry, Scope);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.requirement_dropped",
            JsonPointer: "/traits/0/slot_bindings/0",
        });
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public async Task records_ck_37_and_39_equal_slot_binding_is_admitted_by_the_field_runtime()
    {
        var domains = new NarrowingDomainRuntime();
        var registry = Registry();
        var constraints = new FieldConstraintDefinition(true, 1, 1, [], null);
        var compiler = Compiler(domains, constraints);

        var result = await compiler.CompileAndRegisterAsync(Candidate(constraints, constraints), null, registry, Scope);

        Assert.Empty(result.Refusals);
        Assert.NotNull(result.Schema);
        var call = Assert.Single(domains.NarrowCalls);
        Assert.Equal(constraints, call.Declared);
        Assert.Equal(constraints, call.Narrowed);
        Assert.Equal("/traits/0/slot_bindings/0", call.Pointer);
    }

    [Fact]
    public async Task records_ck_37_and_39_narrower_slot_binding_is_admitted_by_the_field_runtime()
    {
        var domains = new NarrowingDomainRuntime();
        var registry = Registry();
        var declared = new FieldConstraintDefinition(false, 0, 5, [], null);
        var narrowed = new FieldConstraintDefinition(true, 1, 1, [], null);
        var compiler = Compiler(domains, declared);

        var result = await compiler.CompileAndRegisterAsync(Candidate(declared, narrowed), null, registry, Scope);

        Assert.Empty(result.Refusals);
        Assert.NotNull(result.Schema);
        Assert.Equal((declared, narrowed, "/traits/0/slot_bindings/0"), Assert.Single(domains.NarrowCalls));
    }

    [Fact]
    public async Task records_ck_39_a_required_slot_bound_to_an_unbound_field_is_a_widening_the_field_runtime_refuses()
    {
        var domains = new NarrowingDomainRuntime();
        var registry = Registry();
        var declared = new FieldConstraintDefinition(true, 1, 1, [], null);
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street")],
            [new("addressable", "1.0.0", [new("street", "street_address")])]);

        var result = await Compiler(domains, declared).CompileAndRegisterAsync(candidate, null, registry, Scope);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "field.requirement_dropped",
            JsonPointer: "/traits/0/slot_bindings/0",
        });
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public async Task A_slot_binding_with_constraints_requires_its_field_domain_scope_before_registration()
    {
        var declared = new FieldConstraintDefinition(true, 1, 1, [], null);
        var registry = Registry();

        var result = await Compiler(new NarrowingDomainRuntime(), declared)
            .CompileAndRegisterAsync(Candidate(declared, declared), null, registry);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "records.field.runtime_required",
            JsonPointer: "/traits/0/slot_bindings/0",
        });
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public async Task A_slot_binding_with_constraints_requires_its_field_domain_runtime_before_registration()
    {
        var declared = new FieldConstraintDefinition(true, 1, 1, [], null);
        var registry = Registry();

        var result = await Compiler(null, declared)
            .CompileAndRegisterAsync(Candidate(declared, declared), null, registry, Scope);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "records.field.runtime_required",
            JsonPointer: "/traits/0/slot_bindings/0",
        });
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public void records_ck_37_and_39_resolve_an_exact_trait_version_and_bind_its_required_slot()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, true)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street")],
            [new("addressable", "1.0.0", [new("street", "street_address")])]);

        Assert.Empty(validator.Validate(candidate, null));
    }

    [Fact]
    public void records_ck_39_unresolved_trait_version_refuses_at_the_authored_trait_pointer()
    {
        var validator = new RecordsIntentValidator(new TraitSource());
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.version_unresolved", refusal.Code);
        Assert.Equal("/traits/0", refusal.JsonPointer);
    }

    [Fact]
    public async Task records_auth_35_blocking_required_slot_refuses_before_registration()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, true)])));
        var registry = new InMemorySchemaRegistry();
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var result = await new RecordTypeSchemaCompiler(validator).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal is
        {
            Code: "records.trait.blocking_slot_unbound",
            JsonPointer: "/traits/0/slot_bindings",
        });
        var registered = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) registered.Add(schema);
        Assert.Empty(registered);
    }

    [Fact]
    public void records_ck_37_duplicate_bindings_for_one_qualified_slot_refuse_loudly()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, false)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street")],
            [new("addressable", "1.0.0", [new("street", "street_address"), new("street", "street_address")])]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.slot_binding_ambiguous", refusal.Code);
        Assert.Equal("/traits/0/slot_bindings/1/slot_key", refusal.JsonPointer);
    }

    [Fact]
    public void records_ck_37_two_references_binding_one_qualified_slot_to_different_fields_refuse_loudly()
    {
        var validator = new RecordsIntentValidator(new TraitSource(new TraitDefinition(
            "addressable",
            "1.0.0",
            [new("street", Constraints(), true, false)])));
        var candidate = new RecordTypeDefinition(
            "location",
            [new("street_address", "Street"), new("mailing_street", "Mailing street")],
            [
                new("addressable", "1.0.0", [new("street", "street_address")]),
                new("addressable", "1.0.0", [new("street", "mailing_street")]),
            ]);

        var refusal = Assert.Single(validator.Validate(candidate, null));

        Assert.Equal("records.trait.slot_binding_ambiguous", refusal.Code);
        Assert.Equal("/traits/1/slot_bindings/0/slot_key", refusal.JsonPointer);
    }

    [Fact]
    public async Task Trait_refusals_prevent_schema_registry_mutation()
    {
        var validator = new RecordsIntentValidator(new TraitSource());
        var registry = new InMemorySchemaRegistry();
        var candidate = new RecordTypeDefinition("location", [], [new("addressable", "1.0.0", [])]);

        var result = await new RecordTypeSchemaCompiler(validator).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal.Code == "records.trait.version_unresolved");
        var registered = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) registered.Add(schema);
        Assert.Empty(registered);
    }

    private static FieldConstraintDefinition Constraints()
        => new(false, 0, 1, [], null);

    private static FieldDomainScope Scope { get; } = new(new("tenant-a"), "principal-a");

    private static RecordTypeSchemaCompiler Compiler(
        IFieldDomainRuntime? domains,
        FieldConstraintDefinition declared)
        => new(
            new RecordsIntentValidator(new TraitSource(new TraitDefinition(
                "addressable",
                "1.0.0",
                [new("street", declared, true, false)]))),
            Kinds(),
            new SharedValueDomainAdmission(),
            domains);

    private static FieldKindRuntime Kinds() => new(new FieldKindRegistry([
        new("text", "1.0.0", null, FieldScalarValueShape.Text),
    ]));

    private static InMemorySchemaRegistry Registry() => new(fieldKindRuntime: Kinds());

    private static RecordTypeDefinition Candidate(
        FieldConstraintDefinition declared,
        FieldConstraintDefinition narrowed)
        => new(
            "location",
            [new("street_address", "Street", new(new("text", "1.0.0", new Dictionary<string, string>()), narrowed))],
            [new("addressable", "1.0.0", [new("street", "street_address")])]);

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    // Records the calls Records makes and forwards each to the real field runtime, so every
    // accept/refuse decision and field.* code below is the runtime's own.
    private sealed class NarrowingDomainRuntime : IFieldDomainRuntime, IFieldDomainSource, IFieldDomainReadAuthority, IFieldDomainSnapshot
    {
        private readonly ValueDomainRuntime _inner;

        public NarrowingDomainRuntime() => _inner = new(this, this, TimeProvider.System);

        public List<(FieldConstraintDefinition Declared, FieldConstraintDefinition Narrowed, string Pointer)> NarrowCalls { get; } = [];

        public TenantId Tenant => Scope.Tenant;

        public string Revision => "r1";

        public bool IsComplete => true;

        public IReadOnlyList<FieldRefusal> Validate(
            ResolvedFieldConstraints constraints,
            ICompiledFieldKind kind,
            JsonElement value,
            string jsonPointer) => _inner.Validate(constraints, kind, value, jsonPointer);

        public ValueTask<ResolvedValueDomain> ResolveAsync(
            ValueDomainDefinition domain,
            FieldDomainScope scope,
            string jsonPointer,
            CancellationToken cancellationToken = default) => _inner.ResolveAsync(domain, scope, jsonPointer, cancellationToken);

        public ValueTask<ResolvedFieldConstraints> IntersectAsync(
            IReadOnlyList<FieldConstraintDefinition> constraints,
            FieldDomainScope scope,
            string jsonPointer,
            CancellationToken cancellationToken = default) => _inner.IntersectAsync(constraints, scope, jsonPointer, cancellationToken);

        public ValueTask<ResolvedFieldConstraints> NarrowAsync(
            FieldConstraintDefinition declared,
            FieldConstraintDefinition narrowed,
            FieldDomainScope scope,
            string jsonPointer,
            CancellationToken cancellationToken = default)
        {
            NarrowCalls.Add((declared, narrowed, jsonPointer));
            return _inner.NarrowAsync(declared, narrowed, scope, jsonPointer, cancellationToken);
        }

        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IFieldDomainSnapshot>(this);

        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);

        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme) => null;

        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => null;
    }

    private sealed class TraitSource(params TraitDefinition[] traits) : IRecordTraitSource
    {
        public TraitDefinition? Resolve(string traitId, string version)
            => traits.SingleOrDefault(trait => trait.TraitId == traitId && trait.Version == version);
    }
}
