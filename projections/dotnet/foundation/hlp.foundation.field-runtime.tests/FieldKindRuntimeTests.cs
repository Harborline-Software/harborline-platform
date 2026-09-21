using System.Text.Json;
using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldKindRuntimeTests
{
    [Fact]
    public void A_bound_kind_carries_its_schema_and_total_digit_validator_through_the_shared_contract()
    {
        var parameters = new Dictionary<string, string> { ["total_digits"] = "3", ["fraction_digits"] = "1" };
        var compiled = Runtime(FieldScalarValueShape.Number).Bind(new("amount", "1.0.0", parameters), "/fields/price/kind");
        parameters["total_digits"] = "100";

        Assert.Equal("number", compiled.JsonSchema.GetProperty("type").GetString());
        Assert.Equal("1e-1", compiled.JsonSchema.GetProperty("multipleOf").GetRawText());
        var binding = compiled.JsonSchema.GetProperty("x-harborline-field-kind");
        Assert.Equal("amount", binding.GetProperty("kind_id").GetString());
        Assert.Equal("1.0.0", binding.GetProperty("version").GetString());
        Assert.Equal("3", binding.GetProperty("parameters").GetProperty("total_digits").GetString());
        Assert.Equal("1", binding.GetProperty("parameters").GetProperty("fraction_digits").GetString());
        Assert.Empty(compiled.ValidateJson("12.3", "/price"));
        var refusal = Assert.Single(compiled.ValidateJson("123.4", "/price"));
        Assert.Equal("field.total_digits_exceeded", refusal.Code);
        Assert.Equal("/price", refusal.JsonPointer);
    }

    [Fact]
    public void Binding_refuses_a_scale_above_total_digits_before_values_are_submitted()
    {
        var error = Assert.Throws<FieldAdmissionException>(() => Runtime(FieldScalarValueShape.Number).Bind(
            new("amount", "1.0.0", new Dictionary<string, string> { ["total_digits"] = "3", ["fraction_digits"] = "4" }),
            "/fields/price/kind"));

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.kind_parameter_bounds_conflict", refusal.Code);
        Assert.Equal("/fields/price/kind/parameters/fraction_digits", refusal.JsonPointer);
    }

    [Fact]
    public void An_undeclared_total_digit_cap_is_not_defaulted_to_machine_decimal_precision()
    {
        var compiled = Runtime(FieldScalarValueShape.Number).Bind(new("amount", "1.0.0",
            new Dictionary<string, string> { ["fraction_digits"] = "2" }), "/kind");

        Assert.Empty(compiled.ValidateJson("123456789012345678901234567890.12", "/price"));
    }

    [Theory]
    [InlineData(FieldScalarValueShape.Text, "total_digits", "3")]
    [InlineData(FieldScalarValueShape.Boolean, "fraction_digits", "0")]
    [InlineData(FieldScalarValueShape.Number, "max_length", "5")]
    public void A_limit_for_the_wrong_scalar_shape_is_refused_at_binding(
        FieldScalarValueShape shape, string parameter, string value)
    {
        var error = Assert.Throws<FieldAdmissionException>(() => Runtime(shape).Bind(
            new("amount", "1.0.0", new Dictionary<string, string> { [parameter] = value }), "/kind"));
        Assert.Equal("field.kind_parameter_type_mismatch", Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData("-0.1000", null)]
    [InlineData("1.23", null)]
    [InlineData("1.230000000000000000000000000001", "field.above_maximum")]
    [InlineData("-0.100000000000000000000000000001", "field.below_minimum")]
    [InlineData("1e1000000000", "field.above_maximum")]
    public void Range_checks_are_exact_inclusive_and_do_not_round(string number, string? expectedCode)
    {
        var compiled = Runtime(FieldScalarValueShape.Number).Bind(new("amount", "1.0.0",
            new Dictionary<string, string> { ["minimum"] = "-0.1", ["maximum"] = "1.2300" }), "/kind");
        using var document = JsonDocument.Parse(number);
        var refusals = compiled.Validate(document.RootElement, "/price");

        if (expectedCode is null) Assert.Empty(refusals);
        else Assert.Equal(expectedCode, Assert.Single(refusals).Code);
        Assert.Equal(number, document.RootElement.GetRawText());
        Assert.Equal("1.2300", compiled.JsonSchema.GetProperty("maximum").GetRawText());
        Assert.Equal("1.2300", compiled.JsonSchema.GetProperty("x-harborline-field-kind")
            .GetProperty("parameters").GetProperty("maximum").GetString());
    }

    [Theory]
    [InlineData("\"é\"", 1, 1, "field.max_bytes_exceeded")]
    [InlineData("\"😀\"", 1, 3, "field.max_bytes_exceeded")]
    [InlineData("\"e\\u0301\"", 1, 8, "field.too_long")]
    public void Text_limits_count_scalars_and_utf8_bytes_without_normalizing(
        string json, int maxLength, int maxBytes, string expectedCode)
    {
        var parameters = new Dictionary<string, string>
        {
            ["max_length"] = maxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["max_bytes"] = maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var compiled = Runtime(FieldScalarValueShape.Text).Bind(new("amount", "1.0.0", parameters), "/kind");
        using var document = JsonDocument.Parse(json);
        var refusal = Assert.Single(compiled.Validate(document.RootElement, "/text"));
        Assert.Equal(expectedCode, refusal.Code);
        Assert.Equal("/text", refusal.JsonPointer);
        Assert.Equal(json, document.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("100e-2", true)]
    [InlineData("1.01", false)]
    [InlineData("1e-2", false)]
    [InlineData("1e1000000000", true)]
    public void Integer_shapes_use_exact_number_values_without_a_machine_integer_cap(string json, bool admitted)
    {
        var compiled = Runtime(FieldScalarValueShape.Integer).Bind(
            new("amount", "1.0.0", new Dictionary<string, string>()), "/kind");
        Assert.Equal(admitted, compiled.ValidateJson(json, "/value").Count == 0);
    }

    [Fact]
    public void Contradictory_range_and_length_bounds_refuse_during_binding()
    {
        var range = Assert.Throws<FieldAdmissionException>(() => Runtime(FieldScalarValueShape.Number).Bind(
            new("amount", "1.0.0", new Dictionary<string, string> { ["minimum"] = "2", ["maximum"] = "1" }), "/kind"));
        Assert.Equal("/kind/parameters/maximum", Assert.Single(range.Refusals).JsonPointer);
        var length = Assert.Throws<FieldAdmissionException>(() => Runtime(FieldScalarValueShape.Text).Bind(
            new("amount", "1.0.0", new Dictionary<string, string> { ["min_length"] = "2", ["max_length"] = "1" }), "/kind"));
        Assert.Equal("/kind/parameters/max_length", Assert.Single(length.Refusals).JsonPointer);
    }

    private static IFieldKindRuntime Runtime(FieldScalarValueShape shape)
        => new FieldKindRuntime(new FieldKindRegistry([new("amount", "1.0.0", null, shape)]));
}
