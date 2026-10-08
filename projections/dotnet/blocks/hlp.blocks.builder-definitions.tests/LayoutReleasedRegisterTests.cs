using System.Text.Json;
using Harborline.Contracts.Fields;
using Xunit;

namespace Harborline.Blocks.BuilderDefinitions.Tests;

/// <summary>Oracles: T-1012's literal control/parameter grammar and T-724 rulings 36 and 38.</summary>
public sealed class LayoutReleasedRegisterTests
{
    private static readonly LayoutHostRegisters Released = new(
        LayoutBlockKindRegistry.Platform,
        FieldControls: LayoutFieldControlRegistry.Released,
        ValidationRules: LayoutValidationRuleRegistry.Released,
        Fields: new([new("invoice.amount", FieldScalarValueShape.Number, false)]));

    [Fact(DisplayName = "layout-bound-3: released text and currency controls declare scalar shapes and immutable metadata")]
    public void ReleasedControlsDeclareShapesAndImmutableMetadata()
    {
        var text = LayoutFieldControlRegistry.Released.Find("text")!;
        var currency = LayoutFieldControlRegistry.Released.Find("currency")!;

        Assert.Equal("text", text.Id);
        Assert.Equal([FieldScalarValueShape.Text], text.ValueShapes);
        Assert.Null(text.ParameterSchema);
        Assert.Equal("currency", currency.Id);
        Assert.Equal([FieldScalarValueShape.Integer, FieldScalarValueShape.Number], currency.ValueShapes);
        Assert.Equal(["decimals", "currencyCode", "min", "max"], currency.ParameterSchema!.Value.GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Throws<NotSupportedException>(() => ((IList<FieldScalarValueShape>)currency.ValueShapes)[0] = FieldScalarValueShape.Text);
        Assert.False(LayoutFieldControlRegistry.Released.Contains("unreleased-control"));
    }

    [Fact(DisplayName = "layout-bound-3: released currency admits decimals and refuses the host's invented precision at author and publish")]
    public void CurrencyAdmitsDecimalsAndRefusesPrecision()
    {
        var valid = Currency("{\"decimals\":2,\"currencyCode\":\"EUR\",\"min\":-10,\"max\":500}");
        LayoutDefinitionAdmission.ValidateForAuthoring(valid, Released, LayoutTestAccess.GrantsAll);
        LayoutDefinitionAdmission.ValidateForPublish(LayoutBoundRegisterTests.Sealed(valid), Released, LayoutTestAccess.GrantsAll);

        var invalid = Currency("{\"precision\":2}");
        AssertParametersRefused(() => LayoutDefinitionAdmission.ValidateForAuthoring(invalid, Released, LayoutTestAccess.GrantsAll));
        AssertParametersRefused(() => LayoutDefinitionAdmission.ValidateForPublish(LayoutBoundRegisterTests.Sealed(invalid), Released, LayoutTestAccess.GrantsAll));
    }

    [Theory]
    [InlineData("{\"decimals\":\"2\"}")]
    [InlineData("{\"decimals\":1.5}")]
    [InlineData("{\"currencyCode\":2}")]
    [InlineData("{\"min\":\"0\"}")]
    [InlineData("{\"max\":false}")]
    public void CurrencyRefusesParameterShapesItsControlsDoNotRead(string parameters)
        => AssertParametersRefused(() => LayoutDefinitionAdmission.ValidateForPublish(LayoutBoundRegisterTests.Sealed(Currency(parameters)), Released, LayoutTestAccess.GrantsAll));

    [Fact(DisplayName = "layout-bound-8: the empty released rule register refuses a named rule at publication")]
    public void EmptyReleasedRulesRefuseNamedRuleAtPublication()
    {
        Assert.False(LayoutValidationRuleRegistry.Released.TryGet("rules.value-required", out var rule));
        Assert.Null(rule);
        var named = LayoutBoundRegisterTests.CaptureSurface(new(true, ["rules.value-required"]));
        var refusal = Assert.Throws<DefinitionRefusalException>(() => LayoutDefinitionAdmission.ValidateForPublish(LayoutBoundRegisterTests.Sealed(named), Released, LayoutTestAccess.GrantsAll));
        Assert.Contains(refusal.Refusals, item => item.Code == "layout.capture.validation_rule_unknown" && item.Pointer == "/blocks/0/capture/validation_rules/0");

        // Capture's own required property continues to work without inventing a named rule.
        LayoutDefinitionAdmission.ValidateForPublish(LayoutBoundRegisterTests.Sealed(LayoutBoundRegisterTests.CaptureSurface(new(true, []))), Released, LayoutTestAccess.GrantsAll);
    }

    [Fact(DisplayName = "layout-bound-3: released and host registers keep different schemas for the same control independent")]
    public void RegistersKeepSchemasForTheSameControlIndependent()
    {
        var host = new LayoutFieldControlRegistry([new("currency", [FieldScalarValueShape.Number], JsonSerializer.Deserialize<JsonElement>(
            "{\"type\":\"object\",\"properties\":{\"precision\":{\"type\":\"integer\"}},\"additionalProperties\":false}"))]);
        var decimals = JsonSerializer.Deserialize<JsonElement>("{\"decimals\":2}");
        var precision = JsonSerializer.Deserialize<JsonElement>("{\"precision\":2}");

        Assert.True(LayoutFieldControlRegistry.Released.AcceptsParameters("currency", decimals));
        Assert.False(LayoutFieldControlRegistry.Released.AcceptsParameters("currency", precision));
        Assert.True(host.AcceptsParameters("currency", precision));
        Assert.False(host.AcceptsParameters("currency", decimals));
    }

    private static LayoutDefinition Currency(string parameters)
        => LayoutBoundRegisterTests.CaptureSurface(new(null, [], Control: new("currency", JsonSerializer.Deserialize<JsonElement>(parameters))), "invoice.amount");

    private static void AssertParametersRefused(Action admit)
    {
        var refusal = Assert.Throws<DefinitionRefusalException>(admit);
        Assert.Contains(refusal.Refusals, item => item.Code == "layout.capture.control_parameters_invalid" && item.Pointer == "/blocks/0/capture/control/parameters");
    }
}
