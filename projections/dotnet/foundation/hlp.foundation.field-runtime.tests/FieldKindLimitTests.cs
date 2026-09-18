using System.Text.Json;
using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Foundation.FieldRuntime.Tests;

public sealed class FieldKindLimitTests
{
    [Fact]
    public void Fraction_digits_above_total_digits_are_refused_at_declaration()
    {
        var error = Assert.Throws<FieldAdmissionException>(() => FieldKindLimits.FromKindParameters(
            new Dictionary<string, string> { ["total_digits"] = "3", ["fraction_digits"] = "4" },
            "/fields/price/kind/parameters"));

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.kind_parameter_bounds_conflict", refusal.Code);
        Assert.Equal("/fields/price/kind/parameters/fraction_digits", refusal.JsonPointer);
    }

    [Theory]
    [InlineData("123.4", 3, 0, "field.total_digits_exceeded", "field.fraction_digits_exceeded")]
    [InlineData("123.4", 3, 1, "field.total_digits_exceeded", null)]
    [InlineData("1.230", 3, 3, "field.total_digits_exceeded", null)]
    [InlineData("12.30", 4, 1, "field.fraction_digits_exceeded", null)]
    [InlineData("1e3", 3, 1, "field.total_digits_exceeded", null)]
    [InlineData("1e-3", 3, 2, "field.fraction_digits_exceeded", null)]
    [InlineData("1e1000000000", 3, 1, "field.total_digits_exceeded", null)]
    [InlineData("1e-1000000000", 3, 1, "field.fraction_digits_exceeded", null)]
    public void Each_exceeded_facet_has_its_own_refusal_without_changing_the_value(
        string number, int totalDigits, int fractionDigits, string firstCode, string? secondCode)
    {
        var limits = Limits(totalDigits, fractionDigits);
        using var document = JsonDocument.Parse(number);

        var refusals = limits.Validate(document.RootElement, "/items/0/price~1net");

        var expectedCodes = new List<string> { firstCode };
        if (secondCode is not null) expectedCodes.Add(secondCode);
        Assert.Equal(expectedCodes, refusals.Select(refusal => refusal.Code));
        Assert.All(refusals, refusal => Assert.Equal("/items/0/price~1net", refusal.JsonPointer));
        Assert.Equal(number, document.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("12.3", 3, 1)]
    [InlineData("-12.3", 3, 1)]
    [InlineData("0.0120", 4, 4)]
    [InlineData("0.12", 2, 2)]
    [InlineData("0.00", 2, 2)]
    [InlineData("123", 3, 0)]
    [InlineData("1.23e2", 3, 0)]
    [InlineData("12.3e-1", 3, 2)]
    public void Valid_values_honor_the_sign_leading_zero_and_decimal_counting_rules(
        string number, int totalDigits, int fractionDigits)
    {
        Assert.Empty(Limits(totalDigits, fractionDigits).ValidateJson(number, "/price"));
    }

    [Theory]
    [InlineData("total_digits", "0")]
    [InlineData("total_digits", "-1")]
    [InlineData("total_digits", "three")]
    [InlineData("fraction_digits", "-1")]
    [InlineData("fraction_digits", "1.5")]
    [InlineData("fraction_digits", " 2 ")]
    public void Malformed_facet_parameters_are_refused_without_defaults(string name, string value)
    {
        var error = Assert.Throws<FieldAdmissionException>(() => FieldKindLimits.FromKindParameters(
            new Dictionary<string, string> { [name] = value }, "/kind/parameters"));

        var refusal = Assert.Single(error.Refusals);
        Assert.Equal("field.kind_parameter_invalid", refusal.Code);
        Assert.Equal("/kind/parameters/" + name, refusal.JsonPointer);
    }

    [Fact]
    public void The_ambiguous_parameter_is_not_an_alias_for_either_facet()
    {
        var error = Assert.Throws<FieldAdmissionException>(() => FieldKindLimits.FromKindParameters(
            new Dictionary<string, string> { ["precision"] = "3" }, "/kind/parameters"));

        Assert.Equal("field.kind_parameter_unknown", Assert.Single(error.Refusals).Code);
    }

    [Theory]
    [InlineData("\"12.3\"", "field.value_type_mismatch")]
    [InlineData("null", "field.value_type_mismatch")]
    [InlineData("{}", "field.value_type_mismatch")]
    [InlineData("12..3", "field.value_malformed")]
    public void Invalid_numeric_values_have_stable_refusals(string json, string code)
    {
        var refusal = Assert.Single(Limits(3, 1).ValidateJson(json, "/price"));
        Assert.Equal(code, refusal.Code);
        Assert.Equal("/price", refusal.JsonPointer);
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "1e-1")]
    [InlineData(2, "1e-2")]
    public void Compilation_emits_multiple_of_and_keeps_both_runtime_facets(int fractionDigits, string multipleOf)
    {
        var compiled = Limits(3, fractionDigits).Compile();
        Assert.Equal(multipleOf, compiled.JsonSchemaKeywords.GetProperty("multipleOf").GetRawText());
        Assert.False(compiled.JsonSchemaKeywords.TryGetProperty("total_digits", out _));
        Assert.True(compiled.RequiresRuntimeValidation);
        Assert.Contains(compiled.ValidateJson("123.4", "/price"),
            refusal => refusal.Code == "field.total_digits_exceeded" && refusal.JsonPointer == "/price");
    }

    [Fact]
    public void Compiled_validation_preserves_fraction_overflow_that_multiple_of_cannot_express()
    {
        var compiled = Limits(4, 1).Compile();
        var refusal = Assert.Single(compiled.ValidateJson("12.30", "/price"));
        Assert.Equal("field.fraction_digits_exceeded", refusal.Code);
    }

    private static FieldKindLimits Limits(int totalDigits, int fractionDigits)
        => FieldKindLimits.FromKindParameters(new Dictionary<string, string>
        {
            ["total_digits"] = totalDigits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["fraction_digits"] = fractionDigits.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }, "/kind/parameters");
}
