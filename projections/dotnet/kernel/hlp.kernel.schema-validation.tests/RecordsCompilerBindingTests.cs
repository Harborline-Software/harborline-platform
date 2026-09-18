using System.Text;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsCompilerBindingTests
{
    [Fact]
    public async Task Pure_compile_registers_nothing_and_explicit_registration_preserves_the_schema_contract()
    {
        var registry = new InMemorySchemaRegistry();
        var kinds = new FieldKindRegistry([new("developer-code", "1.0.0", null)]);
        var compiler = new RecordsDefinitionCompiler(registry, kinds);
        var definition = Definition(new("developer-code", "1.0.0", new Dictionary<string, string>()));

        var schemaText = compiler.CompileSchema(definition);

        Assert.Empty(await RegisteredSchemas(registry));

        var registered = await compiler.CompileAndRegisterAsync(definition);
        var replay = await registry.RegisterAsync(schemaText);
        var valid = await registry.ValidateAsync(
            registered.Id,
            Encoding.UTF8.GetBytes("""{"value":"harbor"}"""));

        Assert.Equal(schemaText, registered.JsonSchemaText);
        Assert.Equal(registered.Id, replay.Id);
        Assert.True(valid.IsValid);
    }

    [Theory]
    [InlineData("""{"value":12.5}""", true)]
    [InlineData("""{"value":"12.5"}""", false)]
    public async Task Custom_kind_name_uses_its_registered_numeric_shape(string payload, bool expected)
    {
        var registry = new InMemorySchemaRegistry();
        var kinds = new FieldKindRegistry(
        [
            new("developer-measurement", "1.0.0", null, FieldScalarValueShape.Number),
        ]);
        var compiler = new RecordsDefinitionCompiler(registry, kinds);
        var definition = Definition(new("developer-measurement", "1.0.0", new Dictionary<string, string>()));

        var schema = await compiler.CompileAndRegisterAsync(definition);
        var result = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(payload));

        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void Invalid_registered_scalar_shape_is_refused()
    {
        var invalidShape = (FieldScalarValueShape)int.MaxValue;

        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FieldKindRegistry([new("developer-invalid", "1.0.0", null, invalidShape)]));

        Assert.Equal("kinds", error.ParamName);
    }

    [Fact]
    public async Task Numeric_kind_refuses_translation_instead_of_compiling_a_locale_to_number_map()
    {
        var registry = new InMemorySchemaRegistry();
        var kinds = new FieldKindRegistry([new("score", "1.0.0", null, FieldScalarValueShape.Integer)]);
        var compiler = new RecordsDefinitionCompiler(registry, kinds);
        var authored = Definition(new("score", "1.0.0", new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    IsTranslatable = true,
                },
            ],
        };

        var error = Assert.Throws<RecordsDefinitionAdmissionException>(() => compiler.CompileSchema(definition));

        Assert.Contains(error.Refusals, refusal =>
            refusal is
            {
                Code: "records.field.translatable_shape_incompatible",
                JsonPointer: "/fields/0/is_translatable",
            });
        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public void Numeric_kind_refuses_a_reference_instead_of_compiling_it_as_text()
    {
        var kinds = new FieldKindRegistry([new("score", "1.0.0", null, FieldScalarValueShape.Integer)]);
        var compiler = new RecordsDefinitionCompiler(new InMemorySchemaRegistry(), kinds);
        var authored = Definition(new("score", "1.0.0", new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    Reference = new(
                        "records.target",
                        null,
                        null,
                        ReferenceCardinality.One,
                        ReferenceDeleteBehavior.Block),
                },
            ],
        };

        var error = Assert.Throws<RecordsDefinitionAdmissionException>(() => compiler.CompileSchema(definition));

        Assert.Contains(error.Refusals, refusal =>
            refusal is
            {
                Code: "records.field.reference_shape_incompatible",
                JsonPointer: "/fields/0/reference",
            });
    }

    [Fact]
    public async Task Pure_compile_refuses_malformed_intent_without_registry_mutation()
    {
        var registry = new InMemorySchemaRegistry();
        var compiler = new RecordsDefinitionCompiler(
            registry,
            new FieldKindRegistry([new("text", "1.0.0", null)]));
        var definition = Definition(new("text", "1.0.0", new Dictionary<string, string>())) with
        {
            RecordTypeId = "",
        };

        var error = Assert.Throws<RecordsDefinitionAdmissionException>(() => compiler.CompileSchema(definition));

        Assert.Contains(error.Refusals, refusal =>
            refusal is
            {
                Code: "records.definition.record_type_id_required",
                JsonPointer: "/record_type_id",
            });
        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Exact_versions_of_one_kind_keep_their_distinct_registered_shapes()
    {
        var registry = new InMemorySchemaRegistry();
        var kinds = new FieldKindRegistry(
        [
            new("developer-versioned", "1.0.0", null, FieldScalarValueShape.Boolean),
            new("developer-versioned", "2.0.0", null, FieldScalarValueShape.Integer),
        ]);
        var compiler = new RecordsDefinitionCompiler(registry, kinds);

        var booleanSchema = await compiler.CompileAndRegisterAsync(
            Definition(new("developer-versioned", "1.0.0", new Dictionary<string, string>())));
        var integerSchema = await compiler.CompileAndRegisterAsync(
            Definition(new("developer-versioned", "2.0.0", new Dictionary<string, string>())));

        Assert.True((await Validate(registry, booleanSchema, """{"value":true}""")).IsValid);
        Assert.False((await Validate(registry, booleanSchema, """{"value":1}""")).IsValid);
        Assert.True((await Validate(registry, integerSchema, """{"value":1}""")).IsValid);
        Assert.False((await Validate(registry, integerSchema, """{"value":true}""")).IsValid);
    }

    private static RecordTypeDefinition Definition(FieldKindReference kind) => new()
    {
        Envelope = new("definition.compiler-binding", "1.0.0", "tenant-a", "package-a", "test"),
        RecordTypeId = "records.compiler-binding",
        Name = "Compiler binding",
        Key = "compiler_binding",
        ClassId = "class.example",
        Fields =
        [
            new RecordFieldDefinition
            {
                Name = "Value",
                Key = "value",
                Kind = kind,
            },
        ],
    };

    private static async Task<IReadOnlyList<Schema>> RegisteredSchemas(InMemorySchemaRegistry registry)
    {
        var schemas = new List<Schema>();
        await foreach (var schema in registry.ListAsync())
            schemas.Add(schema);
        return schemas;
    }

    private static ValueTask<SchemaValidationResult> Validate(
        InMemorySchemaRegistry registry,
        Schema schema,
        string payload)
        => registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(payload));
}
