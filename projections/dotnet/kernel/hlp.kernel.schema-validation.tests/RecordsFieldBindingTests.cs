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
        var quantity = document.RootElement.GetProperty("properties").GetProperty("quantity");
        Assert.Equal("integer", quantity.GetProperty("type").GetString());
        Assert.Equal("count", quantity.GetProperty("x-harborline-field-kind").GetProperty("kind_id").GetString());
        Assert.True((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"quantity":3}"""))).IsValid);
        Assert.False((await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes("""{"quantity":"3"}"""))).IsValid);
    }

    [Fact]
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

    private static FieldBindingDefinition Binding(FieldKindReference kind, ValueDomainDefinition? domain = null)
        => new(kind, new FieldConstraintDefinition(false, 0, 1, [], domain));

    private static async Task<List<Schema>> Registered(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync()) schemas.Add(schema);
        return schemas;
    }
}
