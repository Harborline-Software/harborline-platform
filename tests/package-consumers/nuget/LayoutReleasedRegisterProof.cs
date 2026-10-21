using System;
using System.Linq;
using System.Text.Json;
using Harborline.Blocks.BuilderDefinitions;
using Harborline.Contracts.Fields;

internal static class LayoutReleasedRegisterProof
{
    internal static void Run()
    {
        var text = LayoutFieldControlRegistry.Released.Find("text");
        var currency = LayoutFieldControlRegistry.Released.Find("currency");
        if (text is null || !text.ValueShapes.SequenceEqual(new[] { FieldScalarValueShape.Text })
            || currency is null || !currency.ValueShapes.SequenceEqual(new[] { FieldScalarValueShape.Integer, FieldScalarValueShape.Number }))
            throw new InvalidOperationException("The packed Layout control register lost its released scalar declarations.");

        using var admitted = JsonDocument.Parse("{\"decimals\":2,\"currencyCode\":\"EUR\",\"min\":0,\"max\":100}");
        using var refused = JsonDocument.Parse("{\"precision\":2}");
        if (!LayoutFieldControlRegistry.Released.AcceptsParameters("currency", admitted.RootElement)
            || LayoutFieldControlRegistry.Released.AcceptsParameters("currency", refused.RootElement))
            throw new InvalidOperationException("The packed Layout currency schema disagrees with the lane controls.");
        if (LayoutValidationRuleRegistry.Released.TryGet("rules.value-required", out _))
            throw new InvalidOperationException("The packed Layout rule register invented a validation rule.");
    }
}
