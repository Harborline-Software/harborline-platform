using System.Text;
using System.Text.Json;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsCompilerBindingTests
{
    [Fact]
    public async Task Numeric_literal_domain_preserves_the_authored_raw_token_in_shared_and_compiled_validation()
    {
        var admitted = new AdmittedFieldKind(
            "decimal-measurement",
            "1.0.0",
            null,
            FieldScalarValueShape.Number);
        var kinds = RecordsTestKinds.Create(admitted);
        var domains = RecordsTestDomains.CreateRuntime();
        var registry = new InMemorySchemaRegistry(
            fieldKindRuntime: kinds,
            fieldDomainRuntime: domains);
        var compiler = new RecordsDefinitionCompiler(registry, domains, kinds);
        var literalDomain = new ValueDomainDefinition(LiteralValues: ["12.30"]);
        var authored = Definition(new(
            "decimal-measurement",
            "1.0.0",
            new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    ValueDomain = literalDomain,
                },
            ],
        };

        var compilation = await compiler.CompileAndRegisterResultAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var sharedConstraints = await domains.IntersectAsync(
            [new(false, 0, 1, [], literalDomain)],
            RecordsTestDomains.Scope,
            "/fields/0/constraints",
            CancellationToken.None);
        using var submitted = JsonDocument.Parse("12.30");
        var sharedRefusals = domains.Validate(
            sharedConstraints,
            Assert.Single(compilation.FieldKinds),
            submitted.RootElement,
            "/value");
        var compiled = await Validate(registry, compilation.Schema, """{"value":12.30}""");

        Assert.Empty(sharedRefusals);
        Assert.True(compiled.IsValid);
    }

    [Theory]
    [InlineData(FieldScalarValueShape.Number, "12.30", "12.30", true)]
    [InlineData(FieldScalarValueShape.Number, "12.30", "12.3", false)]
    [InlineData(FieldScalarValueShape.Number, "12.30", "123e-1", false)]
    [InlineData(FieldScalarValueShape.Number, "123e-1", "123e-1", true)]
    [InlineData(FieldScalarValueShape.Integer, "900719925474099312345678901234567890", "900719925474099312345678901234567890", true)]
    [InlineData(FieldScalarValueShape.Integer, "900719925474099312345678901234567890", "900719925474099312345678901234567891", false)]
    [InlineData(FieldScalarValueShape.Boolean, "true", "true", true)]
    [InlineData(FieldScalarValueShape.Boolean, "true", "false", false)]
    public async Task Non_text_literal_domains_preserve_exact_json_tokens(
        FieldScalarValueShape shape,
        string literal,
        string submitted,
        bool expected)
    {
        var (registry, schema) = await CompileLiteralDomainAsync(shape, [literal]);

        var result = await Validate(registry, schema, $$"""{"value":{{submitted}}}""");

        Assert.Equal(expected, result.IsValid);
        if (!expected)
            Assert.Contains(result.Errors, error =>
                error.Code == "field.value_outside_domain" && error.JsonPointer == "/value");
    }

    [Theory]
    [InlineData(FieldScalarValueShape.Number, "\"12.30\"")]
    [InlineData(FieldScalarValueShape.Boolean, "\"true\"")]
    public async Task Literal_membership_does_not_weaken_the_admitted_scalar_shape(
        FieldScalarValueShape shape,
        string submitted)
    {
        var (registry, schema) = await CompileLiteralDomainAsync(
            shape,
            [shape == FieldScalarValueShape.Boolean ? "true" : "12.30"]);

        var result = await Validate(registry, schema, $$"""{"value":{{submitted}}}""");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "type" && error.JsonPointer == "/value");
    }

    [Fact]
    public async Task Repeated_literal_values_report_the_escaped_array_item_pointer()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind(
            "flag", "1.0.0", null, FieldScalarValueShape.Boolean));
        var domains = RecordsTestDomains.CreateRuntime();
        var registry = new InMemorySchemaRegistry(
            fieldKindRuntime: kinds,
            fieldDomainRuntime: domains);
        var authored = Definition(new("flag", "1.0.0", new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    Key = "a/b~",
                    ValueDomain = new(LiteralValues: ["true"]),
                    Constraints = new(false, 0, 2, [], null),
                },
            ],
        };
        var schema = await new RecordsDefinitionCompiler(registry, domains, kinds)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);

        var result = await Validate(registry, schema, """{"a/b~":[true,false]}""");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "field.value_outside_domain" && error.JsonPointer == "/a~1b~0/1");
    }

    [Fact]
    public async Task Numeric_trait_literal_intersections_preserve_every_authored_contribution()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind(
            "amount", "1.0.0", null, FieldScalarValueShape.Number));
        var domains = RecordsTestDomains.CreateRuntime();
        var registry = new InMemorySchemaRegistry(
            fieldKindRuntime: kinds,
            fieldDomainRuntime: domains);
        var authored = Definition(new("amount", "1.0.0", new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    ValueDomain = new(LiteralValues: ["12.30", "13.0"]),
                },
            ],
            Traits =
            [
                new("trait.amount", "1.0.0", "Amount",
                    [new("value", true, true, new(true, 1, 1, [], new(LiteralValues: ["12.30", "13.0", "14.0"]))) ]),
            ],
            TraitBindings = [new("trait.amount", "1.0.0", "value", "value")],
        };

        var schema = await new RecordsDefinitionCompiler(registry, domains, kinds)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);
        using var schemaDocument = JsonDocument.Parse(schema.JsonSchemaText);
        var contributions = schemaDocument.RootElement.GetProperty("properties").GetProperty("value")
            .GetProperty("allOf");

        Assert.Equal(2, contributions.GetArrayLength());
        Assert.All(contributions.EnumerateArray(), contribution =>
            Assert.True(contribution.TryGetProperty("x-harborline-literal-domain", out _)));
        Assert.True((await Validate(registry, schema, """{"value":12.30}""")).IsValid);
        Assert.True((await Validate(registry, schema, """{"value":13.0}""")).IsValid);
        Assert.False((await Validate(registry, schema, """{"value":14.0}""")).IsValid);
    }

    [Fact]
    public async Task Optional_literal_field_may_be_absent_and_text_domains_keep_standard_enum_behavior()
    {
        var (numericRegistry, numericSchema) = await CompileLiteralDomainAsync(
            FieldScalarValueShape.Number,
            ["12.30"]);
        var textKinds = RecordsTestKinds.Create(new AdmittedFieldKind("text", "1.0.0", null));
        var textDomains = RecordsTestDomains.CreateRuntime();
        var textRegistry = new InMemorySchemaRegistry(fieldKindRuntime: textKinds);
        var authored = Definition(new("text", "1.0.0", new Dictionary<string, string>()));
        var textDefinition = authored with
        {
            Fields = [authored.Fields[0] with { ValueDomain = new(LiteralValues: ["open"]) }],
        };
        var textSchema = await new RecordsDefinitionCompiler(textRegistry, textDomains, textKinds)
            .CompileAndRegisterAsync(textDefinition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.True((await Validate(numericRegistry, numericSchema, "{}")).IsValid);
        Assert.True((await Validate(textRegistry, textSchema, """{"value":"open"}""")).IsValid);
        Assert.False((await Validate(textRegistry, textSchema, """{"value":"closed"}""")).IsValid);
        Assert.Contains("\"enum\"", textSchema.JsonSchemaText, StringComparison.Ordinal);
        Assert.DoesNotContain("x-harborline-literal-domain", textSchema.JsonSchemaText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Literal_membership_does_not_bypass_shared_kind_caps()
    {
        var parameters = new Dictionary<string, string> { ["fraction_digits"] = "2" };
        var (registry, schema) = await CompileLiteralDomainAsync(
            FieldScalarValueShape.Number,
            ["12.300"],
            parameters);

        var result = await Validate(registry, schema, """{"value":12.300}""");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Code == "field.fraction_digits_exceeded" && error.JsonPointer == "/value");
    }

    [Fact]
    public async Task Distinct_raw_literal_tokens_have_distinct_content_addressed_schema_identities()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind(
            "amount", "1.0.0", null, FieldScalarValueShape.Number));
        var domains = RecordsTestDomains.CreateRuntime();
        var registry = new InMemorySchemaRegistry(
            fieldKindRuntime: kinds,
            fieldDomainRuntime: domains);
        var compiler = new RecordsDefinitionCompiler(registry, domains, kinds);
        var first = Definition(new("amount", "1.0.0", new Dictionary<string, string>()));
        var second = first with
        {
            Fields = [first.Fields[0] with { ValueDomain = new(LiteralValues: ["12.3"]) }],
        };
        first = first with
        {
            Fields = [first.Fields[0] with { ValueDomain = new(LiteralValues: ["12.30"]) }],
        };

        var firstSchema = await compiler.CompileAndRegisterAsync(
            first, RecordsTestDomains.Scope, CancellationToken.None);
        var secondSchema = await compiler.CompileAndRegisterAsync(
            second, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.NotEqual(firstSchema.Id, secondSchema.Id);
    }

    [Fact]
    public async Task Numeric_kind_projection_retains_exact_parameters_and_executes_trailing_zero_limits()
    {
        var admitted = new AdmittedFieldKind(
            "decimal-measurement",
            "1.0.0",
            null,
            FieldScalarValueShape.Number);
        var runtime = new FieldKindRuntime(
            new Harborline.Foundation.FieldRuntime.FieldKindRegistry([admitted]));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var compiler = new RecordsDefinitionCompiler(
            registry,
            RecordsTestDomains.CreateRuntime(),
            runtime);
        var definition = Definition(new(
            "decimal-measurement",
            "1.0.0",
            new Dictionary<string, string>
            {
                ["total_digits"] = "4",
                ["fraction_digits"] = "2",
                ["minimum"] = "1.2300",
            }));

        var schema = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        using var schemaDocument = JsonDocument.Parse(schema.JsonSchemaText);
        var binding = schemaDocument.RootElement
            .GetProperty("properties")
            .GetProperty("value")
            .GetProperty("x-harborline-field-kind");
        var valid = await Validate(registry, schema, """{"value":12.30}""");
        var overflow = await Validate(registry, schema, """{"value":12.300}""");

        Assert.Equal("decimal-measurement", binding.GetProperty("kind_id").GetString());
        Assert.Equal("1.0.0", binding.GetProperty("version").GetString());
        Assert.Equal("4", binding.GetProperty("parameters").GetProperty("total_digits").GetString());
        Assert.Equal("2", binding.GetProperty("parameters").GetProperty("fraction_digits").GetString());
        Assert.Equal("1.2300", binding.GetProperty("parameters").GetProperty("minimum").GetString());
        Assert.Equal("1.2300", schemaDocument.RootElement.GetProperty("properties")
            .GetProperty("value").GetProperty("minimum").GetRawText());
        Assert.True(valid.IsValid);
        Assert.False(overflow.IsValid);
        Assert.Contains(overflow.Errors, error =>
            error.Code == "field.total_digits_exceeded" && error.JsonPointer == "/value");
        Assert.Contains(overflow.Errors, error =>
            error.Code == "field.fraction_digits_exceeded" && error.JsonPointer == "/value");
    }

    [Fact]
    public async Task Pure_compile_registers_nothing_and_explicit_registration_preserves_the_schema_contract()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind("developer-code", "1.0.0", null));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), kinds);
        var definition = Definition(new("developer-code", "1.0.0", new Dictionary<string, string>()));

        var schemaText = await compiler.CompileSchemaAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);

        Assert.Empty(await RegisteredSchemas(registry));

        var registered = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var replay = await registry.RegisterAsync(schemaText);
        var valid = await registry.ValidateAsync(
            registered.Id,
            Encoding.UTF8.GetBytes("""{"value":"harbor"}"""));

        Assert.Equal(schemaText, registered.JsonSchemaText);
        Assert.Equal(registered.Id, replay.Id);
        Assert.True(valid.IsValid);
    }

    [Fact]
    public async Task Compiler_result_returns_the_definition_and_exact_binding_used_by_its_schema()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind(
            "developer-code", "1.0.0", null, FieldScalarValueShape.Text));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(
            registry, RecordsTestDomains.CreateRuntime(), kinds);
        var definition = Definition(new(
            "developer-code", "1.0.0", new Dictionary<string, string>()));

        var result = await compiler.CompileAndRegisterResultAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var valid = await registry.ValidateAsync(
            result.Schema.Id, Encoding.UTF8.GetBytes("""{"value":"harbor"}"""));
        var wrongShape = await registry.ValidateAsync(
            result.Schema.Id, Encoding.UTF8.GetBytes("""{"value":true}"""));

        Assert.Equal(
            RecordsDefinitionJson.SerializeCanonical(definition),
            RecordsDefinitionJson.SerializeCanonical(result.Definition));
        Assert.Equal(FieldScalarValueShape.Text, Assert.Single(result.FieldKinds).Kind.ValueShape);
        Assert.Null(result.Policies);
        Assert.True(valid.IsValid);
        Assert.False(wrongShape.IsValid);
    }

    [Theory]
    [InlineData("""{"value":12.5}""", true)]
    [InlineData("""{"value":"12.5"}""", false)]
    public async Task Custom_kind_name_uses_its_registered_numeric_shape(string payload, bool expected)
    {
        var kinds = RecordsTestKinds.Create(
            new AdmittedFieldKind("developer-measurement", "1.0.0", null, FieldScalarValueShape.Number));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), kinds);
        var definition = Definition(new("developer-measurement", "1.0.0", new Dictionary<string, string>()));

        var schema = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var result = await registry.ValidateAsync(schema.Id, Encoding.UTF8.GetBytes(payload));

        Assert.Equal(expected, result.IsValid);
    }

    [Fact]
    public void Invalid_registered_scalar_shape_is_refused()
    {
        var invalidShape = (FieldScalarValueShape)int.MaxValue;

        var error = Assert.Throws<FieldAdmissionException>(() =>
            RecordsTestKinds.Create(new AdmittedFieldKind("developer-invalid", "1.0.0", null, invalidShape)));

        Assert.Contains(error.Refusals, refusal => refusal.Code == "field.kind_registration_invalid");
    }

    [Fact]
    public async Task Numeric_kind_refuses_translation_instead_of_compiling_a_locale_to_number_map()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind("score", "1.0.0", null, FieldScalarValueShape.Integer));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), kinds);
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

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => compiler
            .CompileSchemaAsync(definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

        Assert.Contains(error.Refusals, refusal =>
            refusal is
            {
                Code: "records.field.translatable_shape_incompatible",
                JsonPointer: "/fields/0/is_translatable",
            });
        Assert.Empty(await RegisteredSchemas(registry));
    }

    [Fact]
    public async Task Numeric_kind_refuses_a_reference_instead_of_compiling_it_as_text()
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind("score", "1.0.0", null, FieldScalarValueShape.Integer));
        var compiler = new RecordsDefinitionCompiler(
            new InMemorySchemaRegistry(fieldKindRuntime: kinds),
            RecordsTestDomains.CreateRuntime(),
            kinds);
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

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => compiler
            .CompileSchemaAsync(definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

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
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind("text", "1.0.0", null));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(
            registry,
            RecordsTestDomains.CreateRuntime(),
            kinds);
        var definition = Definition(new("text", "1.0.0", new Dictionary<string, string>())) with
        {
            RecordTypeId = "",
        };

        var error = await Assert.ThrowsAsync<RecordsDefinitionAdmissionException>(() => compiler
            .CompileSchemaAsync(definition, RecordsTestDomains.Scope, CancellationToken.None).AsTask());

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
        var kinds = RecordsTestKinds.Create(
            new AdmittedFieldKind("developer-versioned", "1.0.0", null, FieldScalarValueShape.Boolean),
            new AdmittedFieldKind("developer-versioned", "2.0.0", null, FieldScalarValueShape.Integer));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), kinds);

        var booleanSchema = await compiler.CompileAndRegisterAsync(
            Definition(new("developer-versioned", "1.0.0", new Dictionary<string, string>())),
            RecordsTestDomains.Scope, CancellationToken.None);
        var integerSchema = await compiler.CompileAndRegisterAsync(
            Definition(new("developer-versioned", "2.0.0", new Dictionary<string, string>())),
            RecordsTestDomains.Scope, CancellationToken.None);

        Assert.True((await Validate(registry, booleanSchema, """{"value":true}""")).IsValid);
        Assert.False((await Validate(registry, booleanSchema, """{"value":1}""")).IsValid);
        Assert.True((await Validate(registry, integerSchema, """{"value":1}""")).IsValid);
        Assert.False((await Validate(registry, integerSchema, """{"value":true}""")).IsValid);
    }

    [Fact]
    public async Task Text_max_bytes_executes_through_the_records_compiled_schema()
    {
        var runtime = RecordsTestKinds.Create(new AdmittedFieldKind("short-text", "1.0.0", null));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), runtime);
        var definition = Definition(new(
            "short-text",
            "1.0.0",
            new Dictionary<string, string> { ["max_bytes"] = "4" }));

        var schema = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        var valid = await Validate(registry, schema, """{"value":"cafe"}""");
        var overflow = await Validate(registry, schema, """{"value":"café"}""");

        Assert.True(valid.IsValid);
        Assert.False(overflow.IsValid);
        Assert.Contains(overflow.Errors, error =>
            error.Code == "field.max_bytes_exceeded" && error.JsonPointer == "/value");
    }

    [Fact]
    public async Task Repeated_translated_projection_keeps_the_scalar_binding_nested_at_each_locale_value()
    {
        var runtime = RecordsTestKinds.Create(new AdmittedFieldKind("translated-text", "1.0.0", null));
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var compiler = new RecordsDefinitionCompiler(registry, RecordsTestDomains.CreateRuntime(), runtime);
        var authored = Definition(new(
            "translated-text",
            "1.0.0",
            new Dictionary<string, string> { ["max_bytes"] = "3" }));
        var definition = authored with
        {
            Fields =
            [
                authored.Fields[0] with
                {
                    IsTranslatable = true,
                    Constraints = new(false, 0, 2, [], null),
                },
            ],
        };

        var schema = await compiler.CompileAndRegisterAsync(
            definition, RecordsTestDomains.Scope, CancellationToken.None);
        using var document = JsonDocument.Parse(schema.JsonSchemaText);
        var scalar = document.RootElement.GetProperty("properties").GetProperty("value")
            .GetProperty("items").GetProperty("additionalProperties");
        var overflow = await Validate(registry, schema, """{"value":[{"en":"four"}]}""");

        Assert.Equal("translated-text", scalar.GetProperty("x-harborline-field-kind")
            .GetProperty("kind_id").GetString());
        Assert.False(overflow.IsValid);
        Assert.Contains(overflow.Errors, error =>
            error.Code == "field.max_bytes_exceeded" && error.JsonPointer == "/value/0/en");
    }

    [Fact]
    public void Compiler_requires_a_shared_field_kind_runtime()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordsDefinitionCompiler(
            new InMemorySchemaRegistry(),
            RecordsTestDomains.CreateRuntime(),
            null!));
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

    private static async Task<(InMemorySchemaRegistry Registry, Schema Schema)> CompileLiteralDomainAsync(
        FieldScalarValueShape shape,
        IReadOnlyList<string> literals,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        var kinds = RecordsTestKinds.Create(new AdmittedFieldKind("literal-kind", "1.0.0", null, shape));
        var domains = RecordsTestDomains.CreateRuntime();
        var registry = new InMemorySchemaRegistry(
            fieldKindRuntime: kinds,
            fieldDomainRuntime: domains);
        var authored = Definition(new(
            "literal-kind",
            "1.0.0",
            parameters ?? new Dictionary<string, string>()));
        var definition = authored with
        {
            Fields = [authored.Fields[0] with { ValueDomain = new(LiteralValues: literals) }],
        };
        var schema = await new RecordsDefinitionCompiler(registry, domains, kinds)
            .CompileAndRegisterAsync(definition, RecordsTestDomains.Scope, CancellationToken.None);
        return (registry, schema);
    }

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
