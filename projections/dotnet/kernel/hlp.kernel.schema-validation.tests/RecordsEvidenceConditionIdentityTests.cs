using System.Text.Json;
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
        Assert.Contains(compiled.Refusals, refusal => refusal.Code == "field.required_condition_invalid" && refusal.JsonPointer == "/fields/0/required_condition");
        Assert.Null(compiled.JsonSchemaText);
    }

    [Fact]
    public async Task An_attachment_field_has_no_finite_count_cap()
    {
        var field = Field();
        var capped = field with { Binding = field.Binding! with { Constraints = new(false, 0, 2, [], null) } };
        Assert.Empty((await Compiler().CompileAsync(new("case", [field]), null)).Refusals);
        Assert.Contains((await Compiler().CompileAsync(new("case", [capped]), null)).Refusals,
            refusal => refusal.Code == "field.attachment_count_cap");
    }

    private static FieldDefinition Field() => new("evidence", "Evidence", new(new("attachment", "1.0.0", new Dictionary<string, string>()), new(false, 0, null, [], null)));
    private static RecordTypeSchemaCompiler Compiler() => new(new RecordsIntentValidator(),
        new FieldKindRuntime(new FieldKindRegistry([new("attachment", "1.0.0", null, FieldScalarValueShape.ContentReference)]), 100), new SharedValueDomainAdmission());
}
