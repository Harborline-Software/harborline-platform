using System.Text;
using System.Text.Json;
using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

// T-615 S2 (DES-0015): a Records field names a field-runtime kind and at most one value domain,
// both admitted by the field runtime rather than by a Records copy of its rules.
public sealed class RecordsFieldBindingTests
{
    [Fact]
    public async Task records_ck_5_field_compiles_to_its_admitted_kind_schema()
    {
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("order",
        [
            new("quantity", "Quantity", Binding(new("count", "1.0.0", new Dictionary<string, string>()))),
        ]);

        var result = await Compiler(kinds).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        using var document = JsonDocument.Parse(schema.JsonSchemaText);
        var quantity = document.RootElement.GetProperty("properties").GetProperty("quantity").GetProperty("anyOf")[0];
        Assert.Equal("integer", quantity.GetProperty("type").GetString());
        Assert.Equal("count", quantity.GetProperty("x-harborline-field-kind").GetProperty("kind_id").GetString());
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"quantity":3}"""))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"quantity":"3"}"""))).IsValid);
    }

    [Fact]
    [Trait("Holds", "records-auth-32")]
    public async Task records_auth_32_two_source_domain_refuses_before_registration()
    {
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("order",
        [
            new("status", "Status", Binding(
                new("text", "1.0.0", new Dictionary<string, string>()),
                new ValueDomainDefinition(["open", "closed"], new TaxonomySchemeReference("status", "1.0.0")))),
        ]);

        var result = await Compiler(kinds).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("field.value_domain_source_count", refusal.Code);
        Assert.Equal("/fields/0/binding/constraints/value_domain", refusal.JsonPointer);
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    [Trait("Holds", "records-ck-6")]
    public async Task records_ck_6_a_value_domain_names_exactly_one_of_a_literal_set_a_taxonomy_scheme_or_a_record_query()
    {
        // Oracle: DES-0015 records-ck-6 lists the three sources; the code and pointer are the slice record's literals.
        var literal = new ValueDomainDefinition(LiteralValues: ["open", "closed"]);
        var scheme = new ValueDomainDefinition(TaxonomyScheme: new("status", "1.0.0"));
        var query = new ValueDomainDefinition(RecordQuery: new("eam.site", "active"));
        foreach (var domain in new[] { literal, scheme, query })
        {
            var registry = new InMemorySchemaRegistry(fieldKindRuntime: Kinds());
            var admitted = await Compiler(Kinds()).CompileAndRegisterAsync(Status(domain), null, registry);
            Assert.Empty(admitted.Refusals);
            Assert.NotNull(admitted.Schema);
        }

        var twoSources = new[]
        {
            new ValueDomainDefinition(literal.LiteralValues, scheme.TaxonomyScheme),
            new ValueDomainDefinition(literal.LiteralValues, RecordQuery: query.RecordQuery),
            new ValueDomainDefinition(TaxonomyScheme: scheme.TaxonomyScheme, RecordQuery: query.RecordQuery),
        };
        foreach (var domain in twoSources)
        {
            var registry = new InMemorySchemaRegistry(fieldKindRuntime: Kinds());
            var refused = await Compiler(Kinds()).CompileAndRegisterAsync(Status(domain), null, registry);
            Assert.Null(refused.Schema);
            Assert.Equal([("field.value_domain_source_count", "/fields/0/binding/constraints/value_domain")],
                refused.Refusals.Select(refusal => (refusal.Code, refusal.JsonPointer)));
            Assert.Empty(await Registered(registry));
        }
    }

    [Fact]
    [Trait("Holds", "records-ck-6")]
    public void records_ck_6_the_value_domain_declares_only_the_three_sources_so_a_catalogue_coordinate_is_not_a_fourth()
    {
        // ADR-0103 decision 1, mitigated by construction: the declaration has no member a CatalogueFieldSource
        // coordinate could occupy. Oracle: the three source names of DES-0015 records-ck-6, spelled here.
        Assert.Equal(
            ["LiteralValues", "RecordQuery", "TaxonomyScheme"],
            typeof(ValueDomainDefinition).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Holds", "records-eng-4")]
    public async Task records_eng_4_every_structural_refusal_of_a_definition_is_returned_in_field_order()
    {
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("order",
        [
            new("code", "Code", Binding(new("missing", "1.0.0", new Dictionary<string, string>()))),
            new("status", "Status", Binding(
                new("text", "1.0.0", new Dictionary<string, string>()),
                new ValueDomainDefinition(["open", "closed"], new TaxonomySchemeReference("status", "1.0.0")))),
            new("lines", "Lines", new(new("count", "1.0.0", new Dictionary<string, string>()),
                new FieldConstraintDefinition(false, 2, 1, [], null))),
        ]);

        var result = await Compiler(kinds).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        Assert.Equal(
        [
            ("field.kind_unresolved", "/fields/0/binding/kind"),
            ("field.value_domain_source_count", "/fields/1/binding/constraints/value_domain"),
            ("field.constraint_intersection_empty", "/fields/2/binding/constraints"),
        ], result.Refusals.Select(refusal => (refusal.Code, refusal.JsonPointer)));
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public async Task records_ck_5_unresolved_kind_refuses_before_registration()
    {
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("order",
        [
            new("quantity", "Quantity", Binding(new("missing", "1.0.0", new Dictionary<string, string>()))),
        ]);

        var result = await Compiler(kinds).CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("field.kind_unresolved", refusal.Code);
        Assert.Equal("/fields/0/binding/kind", refusal.JsonPointer);
        Assert.Empty(await Registered(registry));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task records_ck_5_bound_field_without_the_field_runtime_refuses_before_registration(
        bool withKinds, bool withAdmission)
    {
        var kinds = Kinds();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var candidate = new RecordTypeDefinition("order",
        [
            new("quantity", "Quantity", Binding(new("count", "1.0.0", new Dictionary<string, string>()))),
        ]);
        var compiler = new RecordTypeSchemaCompiler(
            new RecordsIntentValidator(),
            withKinds ? kinds : null,
            withAdmission ? new SharedValueDomainAdmission() : null);

        var result = await compiler.CompileAndRegisterAsync(candidate, null, registry);

        Assert.Null(result.Schema);
        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("records.field.runtime_required", refusal.Code);
        Assert.Equal("/fields/0/binding", refusal.JsonPointer);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
        Assert.Empty(await Registered(registry));
    }

    [Fact]
    public async Task A_definition_without_a_field_list_compiles_to_an_empty_object_schema()
    {
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: Kinds());

        var result = await Compiler(Kinds()).CompileAndRegisterAsync(new("order", null!), null, registry);

        Assert.Empty(result.Refusals);
        var schema = Assert.IsType<Schema>(result.Schema);
        using var document = JsonDocument.Parse(schema.JsonSchemaText);
        Assert.Empty(document.RootElement.GetProperty("properties").EnumerateObject());
    }

    private static FieldKindRuntime Kinds() => new(new FieldKindRegistry([
        new("count", "1.0.0", null, FieldScalarValueShape.Integer),
        new("text", "1.0.0", null, FieldScalarValueShape.Text),
    ]));

    private static RecordTypeSchemaCompiler Compiler(IFieldKindRuntime kinds)
        => new(new RecordsIntentValidator(), kinds, new SharedValueDomainAdmission());

    private static RecordTypeDefinition Status(ValueDomainDefinition domain)
        => new("order", [new("status", "Status", Binding(new("text", "1.0.0", new Dictionary<string, string>()), domain))]);

    private static FieldBindingDefinition Binding(FieldKindReference kind, ValueDomainDefinition? domain = null)
        => new(kind, new FieldConstraintDefinition(false, 0, 1, [], domain));

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }
}
