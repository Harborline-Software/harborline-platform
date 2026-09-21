using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class ExecutableFieldKindSchemaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Digit_caps_have_distinct_schema_identities_in_either_registration_order(bool reverse)
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var narrow = Project(runtime, "amount", ("total_digits", "3"), ("fraction_digits", "1"));
        var wide = Project(runtime, "amount", ("total_digits", "4"), ("fraction_digits", "1"));
        var first = await registry.RegisterAsync(reverse ? wide : narrow);
        var second = await registry.RegisterAsync(reverse ? narrow : wide);
        var narrowId = reverse ? second.Id : first.Id;
        var wideId = reverse ? first.Id : second.Id;

        Assert.NotEqual(narrowId, wideId);
        AssertFieldError(await Validate(registry, narrowId, "123.4"), "field.total_digits_exceeded", "");
        Assert.True((await Validate(registry, wideId, "123.4")).IsValid);
        Assert.True((await Validate(registry, narrowId, "12.3")).IsValid);
        AssertFieldError(await Validate(registry, narrowId, "12.30"), "field.fraction_digits_exceeded", "");
    }

    [Theory]
    [InlineData("12.3", true)]
    [InlineData("123.40", false)]
    public async Task Array_items_and_local_refs_preserve_all_refusals_and_escaped_instance_pointers(
        string number, bool valid)
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var amount = Project(runtime, "amount", ("total_digits", "3"), ("fraction_digits", "1"));
        var schema = await registry.RegisterAsync($$"""
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "$defs": { "amount": {{amount}} },
              "type": "object",
              "properties": {
                "a/b": {
                  "type": "object",
                  "properties": { "~items": { "type": "array", "items": { "$ref": "#/$defs/amount" } } }
                }
              }
            }
            """);
        var result = await Validate(registry, schema.Id, $$$"""{"a/b":{"~items":[{{{number}}}]}}""");

        Assert.Equal(valid, result.IsValid);
        if (valid) Assert.Empty(result.Errors);
        else
        {
            AssertFieldError(result, "field.total_digits_exceeded", "/a~1b/~0items/0");
            AssertFieldError(result, "field.fraction_digits_exceeded", "/a~1b/~0items/0");
        }
    }

    [Theory]
    [InlineData("-0.1", null)]
    [InlineData("1.23", null)]
    [InlineData("-0.2", "field.below_minimum")]
    [InlineData("1.24", "field.above_maximum")]
    public async Task Numeric_ranges_execute_the_shared_validator(string number, string? code)
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var schema = await registry.RegisterAsync(Project(runtime, "amount", ("minimum", "-0.1"), ("maximum", "1.23")));
        var result = await Validate(registry, schema.Id, number);
        if (code is null) Assert.True(result.IsValid);
        else AssertFieldError(result, code, "");
    }

    [Theory]
    [InlineData("\"a\"", true)]
    [InlineData("\"é\"", false)]
    public async Task Text_byte_caps_survive_schema_projection(string json, bool valid)
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var schema = await registry.RegisterAsync(Project(runtime, "text", ("max_bytes", "1")));
        var result = await Validate(registry, schema.Id, json);
        Assert.Equal(valid, result.IsValid);
        if (!valid) AssertFieldError(result, "field.max_bytes_exceeded", "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_successful_anyOf_alternative_discards_irrelevant_binding_errors(bool unrelatedFailure)
    {
        var runtime = Runtime();
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: runtime);
        var amount = Project(runtime, "amount", ("total_digits", "3"));
        var schema = await registry.RegisterAsync($$"""
            {
              "type": "object",
              "properties": { "amount": { "anyOf": [{{amount}}, {"type":"number"}] } },
              "required": ["amount", "other"]
            }
            """);
        var result = await Validate(registry, schema.Id,
            unrelatedFailure ? """{"amount":1234}""" : """{"amount":1234,"other":true}""");
        Assert.Equal(!unrelatedFailure, result.IsValid);
        Assert.DoesNotContain(result.Errors, error => error.Code?.StartsWith("field.", StringComparison.Ordinal) == true);
        if (unrelatedFailure) Assert.Contains(result.Errors, error => error.Code == "required");
        else Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task Ordinary_schemas_still_work_without_a_field_runtime()
    {
        var registry = new InMemorySchemaRegistry();
        var schema = await registry.RegisterAsync("""{"type":"number","minimum":2}""");
        Assert.True((await Validate(registry, schema.Id, "2")).IsValid);
        Assert.False((await Validate(registry, schema.Id, "1")).IsValid);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0"}""")]
    [InlineData("""{"kind_id":1,"version":"1.0.0","parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":null,"parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":null}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":[] }""")]
    [InlineData("""{"kind_id":"amount","kind_id":"text","version":"1.0.0","parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","version":"1.0.0","parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{},"parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{},"extra":true}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{"total_digits":"3","total_digits":"4"}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{"total_digits":3}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{"total_digits":"0"}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{"total_digits":"3","fraction_digits":"4"}}""")]
    [InlineData("""{"kind_id":"amount","version":"1.0.0","parameters":{"unexpected":"1"}}""")]
    [InlineData("""{"kind_id":"missing","version":"1.0.0","parameters":{}}""")]
    [InlineData("""{"kind_id":"amount","version":"2.0.0","parameters":{}}""")]
    public async Task Malformed_or_unresolved_binding_metadata_refuses_registration(string binding)
    {
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: Runtime());
        await Assert.ThrowsAsync<InvalidSchemaException>(async () =>
            await registry.RegisterAsync($$"""{"x-harborline-field-kind":{{binding}}}"""));
    }

    [Fact]
    public async Task Duplicate_binding_keywords_refuse_registration()
    {
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: Runtime());
        const string binding = """{"kind_id":"amount","version":"1.0.0","parameters":{}}""";
        await Assert.ThrowsAsync<InvalidSchemaException>(async () => await registry.RegisterAsync($$"""
            {"x-harborline-field-kind":{{binding}},"x-harborline-field-kind":{{binding}}}
            """));
    }

    private static FieldKindRuntime Runtime() => new(new FieldKindRegistry([
        new("amount", "1.0.0", null, FieldScalarValueShape.Number),
        new("text", "1.0.0", null, FieldScalarValueShape.Text),
    ]));

    private static string Project(IFieldKindRuntime runtime, string kind, params (string Name, string Value)[] parameters)
        => runtime.Bind(new(kind, "1.0.0", parameters.ToDictionary(p => p.Name, p => p.Value)), "/kind")
            .JsonSchema.GetRawText();

    private static async Task<SchemaValidationResult> Validate(InMemorySchemaRegistry registry, SchemaId id, string json)
        => await registry.ValidateAsync(id, Encoding.UTF8.GetBytes(json));

    private static void AssertFieldError(SchemaValidationResult result, string code, string pointer)
    {
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == code && error.JsonPointer == pointer);
    }
}
