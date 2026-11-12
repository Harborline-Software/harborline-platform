using System.Text.Json;
using System.Text;
using Harborline.Contracts.Fields;
using Harborline.Foundation.FieldRuntime;
using Harborline.Kernel.SchemaValidation.Records;
using Xunit;

namespace Harborline.Kernel.SchemaValidation.Tests;

public sealed class RecordsEvidenceConditionIdentityTests
{
    [Fact]
    public void Authored_condition_is_carried_unchanged_by_field_identity()
    {
        const string expression = """{"==":[{"var":"status"},"closed"]}""";
        var field = new FieldDefinition("evidence", "Evidence") { RequiredCondition = JsonSerializer.Deserialize<JsonElement>(expression) };
        Assert.Equal(expression, field.RequiredCondition!.Value.GetRawText());
    }

    [Fact]
    public void A_new_field_has_no_required_condition_rather_than_false()
        => Assert.Null(new FieldDefinition("evidence", "Evidence").RequiredCondition);

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task A_condition_must_be_an_expression_not_a_boolean_or_another_json_shape(string expression)
    {
        var field = Field() with { RequiredCondition = JsonSerializer.Deserialize<JsonElement>(expression) };
        var compiled = await Compiler().CompileAsync(new("case", [field]), null);
        var refusal = Assert.Single(compiled.Refusals);
        Assert.Equal("field.required_condition_invalid", refusal.Code);
        Assert.Equal("/fields/0/required_condition", refusal.JsonPointer);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
        Assert.Null(compiled.JsonSchemaText);
    }

    [Fact]
    public async Task An_attachment_field_has_no_finite_count_cap()
    {
        var field = Field();
        var capped = field with { Binding = field.Binding! with { Constraints = new(false, 0, 2, [], null) } };
        Assert.Empty((await Compiler().CompileAsync(new("case", [field]), null)).Refusals);
        var refusal = Assert.Single((await Compiler().CompileAsync(new("case", [capped]), null)).Refusals);
        Assert.Equal("field.attachment_count_cap", refusal.Code);
        Assert.Equal("/fields/0/binding/constraints/maximum_count", refusal.JsonPointer);
        Assert.False(string.IsNullOrWhiteSpace(refusal.Message));
    }

    [Fact]
    public async Task An_optional_attachment_field_may_be_absent_from_the_read_body()
    {
        var kinds = new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100);
        var compiler = new RecordTypeSchemaCompiler(new RecordsIntentValidator(), kinds, new SharedValueDomainAdmission());
        var registry = new InMemorySchemaRegistry(fieldKindRuntime: kinds);
        var compiled = await compiler.CompileAndRegisterAsync(new("case", [Field()]), null, registry);
        Assert.Empty(compiled.Refusals);
        Assert.True((await registry.ValidateAsync(compiled.Schema!.Id, Encoding.UTF8.GetBytes("{}"))).IsValid);
    }

    // content-store-ck-7 with M11's ruling on Terra P1-3: a committed record stays readable after the host's store limit
    // changes, because the published record schema identity does not depend on that host fact.
    [Fact]
    public async Task Content_store_ck_7_record_schema_identity_does_not_follow_the_host_store_limit()
    {
        static RecordTypeSchemaCompiler At(long storeLimit) => new(new RecordsIntentValidator(),
            new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), storeLimit),
            new SharedValueDomainAdmission());
        var before = await At(100).CompileAsync(new("case", [Field()]), null);
        var after = await At(50).CompileAsync(new("case", [Field()]), null);
        Assert.NotNull(before.JsonSchemaText);
        Assert.Equal(before.JsonSchemaText, after.JsonSchemaText);
    }

    private static FieldDefinition Field() => new("evidence", "Evidence", new(new("attachment", "1.0.0", new Dictionary<string, string>()), new(false, 0, null, [], null)));
    private static RecordTypeSchemaCompiler Compiler() => new(new RecordsIntentValidator(),
        new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100), new SharedValueDomainAdmission());
}
