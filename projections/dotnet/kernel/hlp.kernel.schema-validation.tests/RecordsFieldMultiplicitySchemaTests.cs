using System.Text;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;
using TenantId = Harborline.Foundation.Assets.Common.TenantId;

namespace Harborline.Kernel.SchemaValidation.Tests;

// DES-0015 records-ck-5/37/39 and IFieldDomainRuntime.Validate: absence/null count zero,
// scalar count one, arrays count their members. These literal verdicts independently constrain
// the real compiler/registry and field runtime; neither implementation supplies the other's oracle.
public sealed class RecordsFieldMultiplicitySchemaTests
{
    private static readonly FieldDomainScope Scope = new(new TenantId("tenant-a"), "reader-a");

    [Theory]
    [InlineData(false, 0, 1, "{}", true)]
    [InlineData(false, 0, 1, "{\"value\":null}", true)]
    [InlineData(false, 0, 1, "{\"value\":\"A\"}", true)]
    [InlineData(false, 0, 1, "{\"value\":[]}", true)]
    [InlineData(false, 0, 1, "{\"value\":[\"A\"]}", true)]
    [InlineData(false, 0, 1, "{\"value\":[\"A\",\"B\"]}", false)]
    [InlineData(false, 0, 1, "{\"value\":[3]}", false)]
    [InlineData(false, 0, 1, "{\"value\":[[\"A\"]]}", false)]
    [InlineData(true, 1, 2, "{\"value\":\"A\"}", true)]
    [InlineData(true, 1, 2, "{\"value\":[\"A\"]}", true)]
    [InlineData(true, 1, 2, "{\"value\":[\"A\",\"B\"]}", true)]
    [InlineData(true, 1, 2, "{}", false)]
    [InlineData(true, 1, 2, "{\"value\":null}", false)]
    [InlineData(true, 1, 2, "{\"value\":[]}", false)]
    [InlineData(true, 1, 2, "{\"value\":[\"A\",\"B\",\"C\"]}", false)]
    [InlineData(true, 1, 2, "{\"value\":[3]}", false)]
    [InlineData(false, 2, 3, "{\"value\":[\"A\",\"B\"]}", true)]
    [InlineData(false, 2, 3, "{\"value\":[\"A\",\"B\",\"C\"]}", true)]
    [InlineData(false, 2, 3, "{}", false)]
    [InlineData(false, 2, 3, "{\"value\":null}", false)]
    [InlineData(false, 2, 3, "{\"value\":\"A\"}", false)]
    [InlineData(false, 2, 3, "{\"value\":[\"A\"]}", false)]
    [InlineData(false, 2, 3, "{\"value\":[\"A\",\"B\",\"C\",\"D\"]}", false)]
    [InlineData(false, 0, null, "{}", true)]
    [InlineData(false, 0, null, "{\"value\":null}", true)]
    [InlineData(false, 0, null, "{\"value\":\"A\"}", true)]
    [InlineData(false, 0, null, "{\"value\":[]}", true)]
    [InlineData(false, 0, null, "{\"value\":[\"A\",\"B\",\"C\",\"D\"]}", true)]
    [InlineData(false, 0, null, "{\"value\":[null]}", false)]
    [InlineData(false, 0, null, "{\"value\":[[\"A\"]]}", false)]
    [InlineData(false, 0, 0, "{}", true)]
    [InlineData(false, 0, 0, "{\"value\":null}", true)]
    [InlineData(false, 0, 0, "{\"value\":[]}", true)]
    [InlineData(false, 0, 0, "{\"value\":\"A\"}", false)]
    [InlineData(false, 0, 0, "{\"value\":[\"A\"]}", false)]
    [InlineData(true, 0, null, "{}", false)]
    [InlineData(true, 0, null, "{\"value\":null}", false)]
    [InlineData(true, 0, null, "{\"value\":[]}", false)]
    [InlineData(true, 0, null, "{\"value\":\"A\"}", true)]
    public async Task Literal_count_matrix_is_enforced_by_registry_and_runtime_independently(
        bool required, int minimum, int? maximum, string body, bool expectedValid)
    {
        var kinds = Kinds();
        var source = new LiteralSource();
        var domains = Domains(source);
        var constraints = new FieldConstraintDefinition(required, minimum, maximum, [], null);
        var binding = Binding("text", constraints);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var result = await Compiler(kinds, domains).CompileAndRegisterAsync(Candidate(binding), null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        Assert.Equal(schema.Id, (await registry.GetAsync(schema.Id))!.Id);
        Assert.Single(await Registered(registry));
        var schemaVerdict = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(body));
        Assert.Equal(expectedValid, schemaVerdict.IsValid);

        var resolved = await domains.IntersectAsync([constraints], Scope, "/value");
        Assert.Equal("tenant-a", Assert.Single(source.OpenedTenants).Value);
        using var document = JsonDocument.Parse(body);
        var value = document.RootElement.TryGetProperty("value", out var present) ? present : default;
        var runtimeVerdict = domains.Validate(resolved, kinds.Bind(binding.Kind, "/kind"), value, "/value");
        Assert.Equal(expectedValid, runtimeVerdict.Count == 0);
    }

    [Theory]
    [InlineData("text", "[\"A\",\"B\"]", true)]
    [InlineData("text", "[\"A\",3]", false)]
    [InlineData("count", "[1,2]", true)]
    [InlineData("count", "[1,2.5]", false)]
    [InlineData("amount", "[1.5,2]", true)]
    [InlineData("amount", "[1.5,\"2\"]", false)]
    [InlineData("flag", "[true,false]", true)]
    [InlineData("flag", "[true,0]", false)]
    [InlineData("text", "[null]", false)]
    [InlineData("count", "[[1]]", false)]
    public async Task Repeated_values_execute_the_original_kind_on_each_item(
        string kindId, string array, bool expectedValid)
    {
        var kinds = Kinds();
        var domains = Domains(new LiteralSource());
        var constraints = new FieldConstraintDefinition(false, 0, null, [], null);
        var binding = Binding(kindId, constraints);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var result = await Compiler(kinds, domains).CompileAndRegisterAsync(Candidate(binding), null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        Assert.Equal(expectedValid, (await registry.ValidateAsync(schema.Id,
            Encoding.UTF8.GetBytes("{\"value\":" + array + "}"))).IsValid);
        var resolved = await domains.IntersectAsync([constraints], Scope, "/value");
        using var document = JsonDocument.Parse(array);
        Assert.Equal(expectedValid, domains.Validate(resolved, kinds.Bind(binding.Kind, "/kind"),
            document.RootElement, "/value").Count == 0);
    }

    [Theory]
    [InlineData(false, -1, null)]
    [InlineData(false, 0, -1)]
    [InlineData(false, 2, 1)]
    [InlineData(true, 0, 0)]
    public async Task Impossible_floors_refuse_before_registration_with_existing_runtime_vocabulary(
        bool required, int minimum, int? maximum)
    {
        var kinds = Kinds();
        var domains = Domains(new LiteralSource());
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var invalid = new FieldConstraintDefinition(required, minimum, maximum, [], null);
        var result = await Compiler(kinds, domains).CompileAndRegisterAsync(
            Candidate(Binding("text", invalid)), null, registry);

        Assert.Null(result.Schema);
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("field.constraint_intersection_empty", refusal.Code);
        Assert.Equal("/fields/0/binding/constraints", refusal.JsonPointer);
        Assert.Empty(await Registered(registry));
        var runtimeFailure = await Assert.ThrowsAsync<FieldAdmissionException>(async () =>
            await domains.IntersectAsync([invalid], Scope, "/fields/0/binding/constraints"));
        Assert.Equal("field.constraint_intersection_empty", Assert.Single(runtimeFailure.Refusals).Code);

        var permitted = new FieldConstraintDefinition(true, 1, 2, [], null);
        var control = await Compiler(kinds, domains).CompileAndRegisterAsync(
            Candidate(Binding("text", permitted)), null, registry);
        Assert.Empty(control.Refusals);
        var schema = Assert.IsType<Schema>(control.Schema);
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\",\"B\"]}"))).IsValid);
        var resolved = await domains.IntersectAsync([permitted], Scope, "/value");
        using var values = JsonDocument.Parse("[\"A\",\"B\"]");
        Assert.Empty(domains.Validate(resolved, kinds.Bind(Binding("text", permitted).Kind, "/kind"), values.RootElement, "/value"));
    }

    [Theory]
    [InlineData("measure", "{\"measure\":3.0}", "{\"measure\":3.000}", "/measure")]
    [InlineData("measure", "{\"measure\":[3.0]}", "{\"measure\":[3.000]}", "/measure/0")]
    [InlineData("a/b~c", "{\"a/b~c\":[3.0]}", "{\"a/b~c\":[3.000]}", "/a~1b~0c/0")]
    public async Task Exact_metadata_and_numeric_spelling_survive_scalar_and_repeated_values_at_escaped_pointers(
        string key, string permitted, string refused, string pointer)
    {
        var kinds = Kinds();
        var domains = Domains(new LiteralSource());
        var constraints = new FieldConstraintDefinition(true, 1, 2, [], null);
        var binding = new FieldBindingDefinition(new("amount", "2.4.0", new Dictionary<string, string>
        {
            ["fraction_digits"] = "2",
            ["total_digits"] = "6",
        }), constraints);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var result = await Compiler(kinds, domains).CompileAndRegisterAsync(Candidate(binding, key), null, registry);
        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        using (var schemaDocument = JsonDocument.Parse(schema.JsonSchemaText))
        {
            var arms = schemaDocument.RootElement.GetProperty("properties").GetProperty(key).GetProperty("anyOf");
            AssertKindMetadata(arms[0]);
            AssertKindMetadata(arms[1].GetProperty("items"));
        }
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(permitted))).IsValid);
        var failed = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(refused));
        Assert.False(failed.IsValid);
        // anyOf can also report aggregate errors; the original item's refusal must remain lossless.
        Assert.Contains(failed.Errors, error => error.Code == "field.fraction_digits_exceeded" && error.JsonPointer == pointer);

        var resolved = await domains.IntersectAsync([constraints], Scope, "/binding/constraints");
        var compiled = kinds.Bind(binding.Kind, "/kind");
        using var allowedDocument = JsonDocument.Parse(permitted);
        using var refusedDocument = JsonDocument.Parse(refused);
        var rootPointer = key == "measure" ? "/measure" : "/a~1b~0c";
        Assert.Empty(domains.Validate(resolved, compiled, allowedDocument.RootElement.GetProperty(key), rootPointer));
        var refusal = Assert.Single(domains.Validate(resolved, compiled, refusedDocument.RootElement.GetProperty(key), rootPointer));
        Assert.Equal("field.fraction_digits_exceeded", refusal.Code);
        Assert.Equal(pointer, refusal.JsonPointer);
    }

    [Fact]
    public async Task Domain_membership_remains_runtime_only_for_structurally_lawful_repeated_values()
    {
        var kinds = Kinds();
        var source = new LiteralSource();
        var authority = new LiteralAuthority();
        var domains = new ValueDomainRuntime(source, authority, new FixedClock());
        var constraints = new FieldConstraintDefinition(true, 1, 2, [], new(["A", "B"]));
        var binding = Binding("text", constraints);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var result = await Compiler(kinds, domains).CompileAndRegisterAsync(Candidate(binding), null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        Assert.DoesNotContain("\"enum\"", schema.JsonSchemaText);
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\",\"B\"]}"))).IsValid);
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\",\"C\"]}"))).IsValid);
        var resolved = await domains.IntersectAsync([constraints], Scope, "/value");
        Assert.Equal(new[] { "A", "B" }, resolved.Values);
        Assert.Equal(new[] { "A", "B" }, authority.CheckedValues);
        Assert.Equal("tenant-a", Assert.Single(source.OpenedTenants).Value);
        var compiled = kinds.Bind(binding.Kind, "/kind");
        using var allowed = JsonDocument.Parse("[\"A\",\"B\"]");
        using var refused = JsonDocument.Parse("[\"A\",\"C\"]");
        Assert.Empty(domains.Validate(resolved, compiled, allowed.RootElement, "/value"));
        var refusal = Assert.Single(domains.Validate(resolved, compiled, refused.RootElement, "/value"));
        Assert.Equal("field.value_outside_domain", refusal.Code);
        Assert.Equal("/value/1", refusal.JsonPointer);
    }

    [Fact]
    public async Task Exact_trait_floor_is_narrowed_by_the_real_domain_runtime_before_compilation()
    {
        var kinds = Kinds();
        var source = new LiteralSource();
        var domains = Domains(source);
        var traits = new LiteralTraits(new(false, 0, 3, [], null));
        var compiler = Compiler(kinds, domains, traits);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var narrowed = TraitCandidate(new(true, 2, 2, [], null));
        var result = await compiler.CompileAndRegisterAsync(narrowed, null, registry, Scope);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\",\"B\"]}"))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("{\"value\":\"A\"}"))).IsValid);
        Assert.Equal("tenant-a", Assert.Single(source.OpenedTenants).Value);
        Assert.NotEmpty(traits.Requests);
        Assert.All(traits.Requests, request => Assert.Equal(("status-trait", "1.0.0"), request));

        var refusedRegistry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var widened = await compiler.CompileAndRegisterAsync(TraitCandidate(new(false, 0, 4, [], null)),
            null, refusedRegistry, Scope);
        Assert.Null(widened.Schema);
        Assert.Contains(widened.Refusals, refusal => refusal.Code == "field.multiplicity_widened"
            && refusal.JsonPointer == "/traits/0/slot_bindings/0");
        Assert.Empty(await Registered(refusedRegistry));
    }

    [Theory]
    [InlineData("1.0.0", false, "records.trait.blocking_slot_unbound", "/traits/0/slot_bindings")]
    [InlineData("2.0.0", true, "records.trait.version_unresolved", "/traits/0")]
    public async Task Missing_required_slot_or_different_trait_revision_never_registers_a_schema(
        string version, bool bindSlot, string code, string pointer)
    {
        var kinds = Kinds();
        var domains = Domains(new LiteralSource());
        var traits = new LiteralTraits(new(true, 1, 2, [], null));
        var compiler = Compiler(kinds, domains, traits);
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var valid = await compiler.CompileAndRegisterAsync(TraitCandidate(new(true, 1, 2, [], null)), null, registry, Scope);
        Assert.Empty(valid.Refusals);
        Assert.NotNull(valid.Schema);
        var refusedRegistry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("status-record", [new("value", "Value", Binding("text", new(true, 1, 2, [], null)))],
            [new("status-trait", version, bindSlot ? [new("status", "value")] : [])]);

        var result = await compiler.CompileAndRegisterAsync(candidate, null, refusedRegistry, Scope);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal.Code == code && refusal.JsonPointer == pointer);
        Assert.Empty(await Registered(refusedRegistry));
        Assert.Contains(("status-trait", version), traits.Requests);
    }

    [Fact]
    public async Task Required_trait_slot_bound_to_an_unbound_field_refuses_real_runtime_narrowing()
    {
        var kinds = Kinds();
        var source = new LiteralSource();
        var domains = Domains(source);
        var compiler = Compiler(kinds, domains, new LiteralTraits(new(true, 1, 2, [], null)));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var control = await compiler.CompileAndRegisterAsync(TraitCandidate(new(true, 1, 2, [], null)),
            null, registry, Scope);
        Assert.Empty(control.Refusals);
        var permitted = Assert.IsType<Schema>(control.Schema);
        Assert.True((await registry.ValidateAsync(permitted.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\"]}"))).IsValid);
        var refusedRegistry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var unbound = new RecordTypeDefinition("status-record", [new("value", "Value")],
            [new("status-trait", "1.0.0", [new("status", "value")])]);

        var result = await compiler.CompileAndRegisterAsync(unbound, null, refusedRegistry, Scope);

        Assert.Null(result.Schema);
        Assert.Contains(result.Refusals, refusal => refusal.Code == "field.requirement_dropped"
            && refusal.JsonPointer == "/traits/0/slot_bindings/0");
        Assert.Empty(await Registered(refusedRegistry));
        Assert.Equal(2, source.OpenedTenants.Count);
        Assert.All(source.OpenedTenants, tenant => Assert.Equal("tenant-a", tenant.Value));
    }

    [Fact]
    public async Task Unbound_and_qualified_reference_schema_bytes_and_content_identity_remain_unchanged()
    {
        const string unboundSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"label":{"type":"string"}},"additionalProperties":false}""";
        const string referenceSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"owner":{"type":"object","properties":{"type":{"const":"person"},"id":{"type":"string","minLength":1}},"required":["type","id"],"additionalProperties":false}},"additionalProperties":false}""";
        var kinds = Kinds();
        var compiler = Compiler(kinds, Domains(new LiteralSource()));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var unbound = await compiler.CompileAndRegisterAsync(new("label-record", [new("label", "Label")]), null, registry);
        var reference = await compiler.CompileAndRegisterAsync(new("owned-record", [new("owner", "Owner", Reference:
            new("person", null, ReferenceCardinality.One, ReferenceDeleteBehavior.Block))]), null, registry);

        Assert.Empty(unbound.Refusals);
        Assert.Empty(reference.Refusals);
        var unboundCompiled = Assert.IsType<Schema>(unbound.Schema);
        var referenceCompiled = Assert.IsType<Schema>(reference.Schema);
        Assert.Equal(unboundSchema, unboundCompiled.JsonSchemaText);
        Assert.Equal(referenceSchema, referenceCompiled.JsonSchemaText);
        Assert.Equal((await registry.RegisterAsync(unboundSchema)).Id, unboundCompiled.Id);
        Assert.Equal((await registry.RegisterAsync(referenceSchema)).Id, referenceCompiled.Id);
        Assert.True((await registry.ValidateAsync(unboundCompiled.Id, Encoding.UTF8.GetBytes("{\"label\":\"A\"}"))).IsValid);
        Assert.False((await registry.ValidateAsync(unboundCompiled.Id, Encoding.UTF8.GetBytes("{\"label\":[\"A\"]}"))).IsValid);
        Assert.True((await registry.ValidateAsync(referenceCompiled.Id, Encoding.UTF8.GetBytes("{\"owner\":{\"type\":\"person\",\"id\":\"p1\"}}"))).IsValid);
        Assert.False((await registry.ValidateAsync(referenceCompiled.Id, Encoding.UTF8.GetBytes("{\"owner\":\"p1\"}"))).IsValid);
    }

    [Fact]
    public async Task Bound_shape_correction_changes_compiled_identity_without_mutating_authored_definition()
    {
        const string oldScalarSchema = """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{"value":{"type":"string","x-harborline-field-kind":{"kind_id":"text","version":"1.0.0","parameters":{}}}},"additionalProperties":false}""";
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = Candidate(Binding("text", new(false, 0, 1, [], null)));
        var authoredBefore = JsonSerializer.Serialize(candidate);
        var old = await registry.RegisterAsync(oldScalarSchema);
        var result = await Compiler(kinds, Domains(new LiteralSource())).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Empty(result.Refusals);
        var corrected = Assert.IsType<Schema>(result.Schema);
        Assert.NotEqual(old.Id, corrected.Id);
        Assert.Equal(authoredBefore, JsonSerializer.Serialize(candidate));
        Assert.Equal("status-record", candidate.RecordTypeId);
        Assert.True((await registry.ValidateAsync(old.Id, Encoding.UTF8.GetBytes("{\"value\":\"A\"}"))).IsValid);
        Assert.False((await registry.ValidateAsync(old.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\"]}"))).IsValid);
        Assert.True((await registry.ValidateAsync(corrected.Id, Encoding.UTF8.GetBytes("{\"value\":[\"A\"]}"))).IsValid);
    }

    private static void AssertKindMetadata(JsonElement scalar)
    {
        Assert.Equal("number", scalar.GetProperty("type").GetString());
        var metadata = scalar.GetProperty("x-harborline-field-kind");
        Assert.Equal(3, metadata.EnumerateObject().Count());
        Assert.Equal("amount", metadata.GetProperty("kind_id").GetString());
        Assert.Equal("2.4.0", metadata.GetProperty("version").GetString());
        var parameters = metadata.GetProperty("parameters");
        Assert.Equal(2, parameters.EnumerateObject().Count());
        Assert.Equal("2", parameters.GetProperty("fraction_digits").GetString());
        Assert.Equal("6", parameters.GetProperty("total_digits").GetString());
    }

    private static FieldKindRuntime Kinds() => new(new FieldKindRegistry([
        new("text", "1.0.0", null, FieldScalarValueShape.Text),
        new("count", "1.0.0", null, FieldScalarValueShape.Integer),
        new("amount", "1.0.0", null, FieldScalarValueShape.Number),
        new("amount", "2.4.0", null, FieldScalarValueShape.Number),
        new("flag", "1.0.0", null, FieldScalarValueShape.Boolean),
    ]));

    private static FieldBindingDefinition Binding(string kindId, FieldConstraintDefinition constraints)
        => new(new(kindId, "1.0.0", new Dictionary<string, string>()), constraints);

    private static RecordTypeDefinition Candidate(FieldBindingDefinition binding, string key = "value")
        => new("status-record", [new(key, "Value", binding)]);

    private static RecordTypeDefinition TraitCandidate(FieldConstraintDefinition constraints)
        => new("status-record", [new("value", "Value", Binding("text", constraints))],
            [new("status-trait", "1.0.0", [new("status", "value")])]);

    private static RecordTypeSchemaCompiler Compiler(IFieldKindRuntime kinds, IFieldDomainRuntime domains, IRecordTraitSource? traits = null)
        => new(new RecordsIntentValidator(traits), kinds, new SharedValueDomainAdmission(), domains);

    private static ValueDomainRuntime Domains(LiteralSource source)
        => new(source, new LiteralAuthority(), new FixedClock());

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }

    // Only immutable source/read-authority adapters are supplied. Kind/domain interpretation is real.
    private sealed class LiteralSource : IFieldDomainSource
    {
        public List<TenantId> OpenedTenants { get; } = [];

        public ValueTask<IFieldDomainSnapshot> OpenSnapshotAsync(TenantId tenant, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedTenants.Add(tenant);
            Assert.Equal("tenant-a", tenant.Value);
            return ValueTask.FromResult<IFieldDomainSnapshot>(new LiteralSnapshot());
        }
    }

    private sealed class LiteralSnapshot : IFieldDomainSnapshot
    {
        public TenantId Tenant => new("tenant-a");
        public string Revision => "literal-snapshot-7";
        public bool IsComplete => true;
        public IReadOnlyList<FieldDomainMember>? GetTaxonomyScheme(TaxonomySchemeReference scheme) => null;
        public IReadOnlyList<FieldDomainMember>? GetRecords(string recordTypeId) => null;
    }

    private sealed class LiteralAuthority : IFieldDomainReadAuthority
    {
        public List<string> CheckedValues { get; } = [];

        public ValueTask<bool> CanReadAsync(FieldDomainScope scope, ValueDomainDefinition domain,
            FieldDomainMember member, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckedValues.Add(member.Value);
            return ValueTask.FromResult(scope.Tenant.Value == "tenant-a" && scope.Principal == "reader-a"
                && member.ResourceId == member.Value && member.Value is "A" or "B" or "C");
        }
    }

    private sealed class LiteralTraits(FieldConstraintDefinition floor) : IRecordTraitSource
    {
        public List<(string TraitId, string Version)> Requests { get; } = [];

        public TraitDefinition? Resolve(string traitId, string version)
        {
            Requests.Add((traitId, version));
            return traitId == "status-trait" && version == "1.0.0"
                ? new("status-trait", "1.0.0", [new("status", floor, true, true)]) : null;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
    }
}
