using System.Text.Json;

using Harborline.Contracts.Forms;
using Xunit;

namespace Harborline.Contracts.Tests;

public sealed class FormsContractTests
{
    [Fact]
    public void Surface_contains_all_frozen_exports_and_exact_closed_layout_values()
    {
        Assert.Equal(84, FormsContractSurface.Exports.Count);
        Assert.Contains(nameof(FormDefinition), FormsContractSurface.Exports);
        Assert.Contains(nameof(OptionsOutcome), FormsContractSurface.Exports);
        Assert.Contains(nameof(FormViewFieldRules), FormsContractSurface.Exports);
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 8], LayoutGap.AllowedValues.Order());
        Assert.Contains(RuleActionKind.Options, Enum.GetValues<RuleActionKind>());
        Assert.Contains(OutputType.Options, Enum.GetValues<OutputType>());
        Assert.Equal(FormsContractConstants.HARBORLINE_JSONLOGIC_V1, "harborline-jsonlogic/v1");
    }

    [Fact]
    public void Options_rule_outcome_round_trips_as_raw_renderer_neutral_values()
    {
        const string json = """
            {"ruleId":"inspection.available-results","target":"field:result","outputType":"Options","options":{"state":"Resolved","options":["PASS","FAIL"]}}
            """;

        var outcome = FormsJson.Deserialize<RuleOutcome>(json);

        Assert.Equal(OutputType.Options, outcome.OutputType);
        Assert.True(outcome.Options.HasValue);
        var optionOutcome = outcome.Options.Value;
        Assert.NotNull(optionOutcome);
        Assert.True(optionOutcome.Options.HasValue);
        var optionValues = optionOutcome.Options.Value;
        Assert.NotNull(optionValues);
        Assert.Equal(2, optionValues.Count);
        Assert.True(JsonDocument.Parse(FormsJson.Serialize(outcome)).RootElement
            .GetProperty("options").TryGetProperty("options", out _));
    }

    [Fact]
    public void Additive_object_properties_are_ignored_and_camel_case_is_preserved()
    {
        const string json = """
            {"defaultLocale":"en","values":{"en":"Inspection"},"future":{"revision":2}}
            """;

        var value = FormsJson.Deserialize<InternationalizedText>(json);
        var roundTrip = FormsJson.Serialize(value);

        Assert.Equal("en", value.DefaultLocale);
        Assert.DoesNotContain("future", roundTrip, StringComparison.Ordinal);
        Assert.Contains("defaultLocale", roundTrip, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_control_hints_are_forward_compatible()
    {
        var value = FormsJson.Deserialize<ControlHint>("\"future-spatial-control\"");

        Assert.Equal("future-spatial-control", value.Value);
        Assert.Equal("\"future-spatial-control\"", FormsJson.Serialize(value));
    }

    [Fact]
    public void Unknown_closed_values_fail_closed()
    {
        var error = Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<FormDefinitionStatus>("\"Archived\""));

        Assert.Equal("unknown-closed-value", error.Code);
    }

    [Fact]
    public void Unknown_discriminators_fail_closed()
    {
        var error = Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<FormItem>("""{"kind":"script","key":"unsafe"}"""));

        Assert.Equal("unknown-discriminator", error.Code);
    }

    [Fact]
    public void Members_from_another_discriminated_variant_are_rejected()
    {
        var error = Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<FormItem>("""{"kind":"field","key":"result","items":[]}"""));

        Assert.Equal("variant-member-mismatch", error.Code);
        Assert.Equal("items", error.Detail);
    }

    [Fact]
    public void Recursive_form_items_round_trip()
    {
        const string json = """
            {"kind":"collection","key":"photos","cardinality":{"min":1,"max":8},"items":[{"kind":"field","key":"photo"}]}
            """;

        var value = FormsJson.Deserialize<FormItem>(json);
        var collection = Assert.IsType<CollectionFormItem>(value);

        Assert.Single(collection.Items);
        Assert.IsType<FieldFormItem>(collection.Items[0]);
        Assert.IsType<CollectionFormItem>(FormsJson.Deserialize<FormItem>(FormsJson.Serialize<FormItem>(value)));
    }

    [Fact]
    public void Missing_and_null_required_properties_are_distinct_failures()
    {
        var missing = Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<InternationalizedText>("""{"values":{"en":"Inspection"}}"""));
        var invalidNull = Assert.Throws<FormsWireException>(() =>
            FormsJson.Deserialize<InternationalizedText>("""{"defaultLocale":null,"values":{"en":"Inspection"}}"""));

        Assert.Equal("missing-required-property", missing.Code);
        Assert.Equal("invalid-string", invalidNull.Code);
    }

    [Fact]
    public void Numeric_layout_vocabulary_is_exact()
    {
        Assert.Equal(6, FormsJson.Deserialize<LayoutGap>("6").Value);
        Assert.Equal("unknown-closed-value",
            Assert.Throws<FormsWireException>(() => FormsJson.Deserialize<LayoutGap>("7")).Code);
    }

    [Fact]
    public void Definition_authoring_payload_deserializes_without_a_host_dependency()
    {
        const string json = """
            {"id":"inspection.metro.v1","version":"1.0.0","status":"Published","tenant":"acme","owner":{"scheme":"system","value":"harborline"},"schemaRef":"sha256:cafef00d","overlay":{"fields":{},"sections":[],"rules":[]},"createdAt":"2026-08-08T00:00:00Z","updatedAt":"2026-08-08T00:00:00Z","fieldsMeta":{"condition":{"type":"radio","required":true,"validations":[{"code":"required"}],"options":["PASS","FAIL"]}}}
            """;

        var definition = FormsJson.Deserialize<FormDefinition>(json);

        Assert.Equal("inspection.metro.v1", definition.Id);
        Assert.Equal(FormDefinitionStatus.Published, definition.Status);
        Assert.Empty(definition.Overlay.Rules);
        Assert.True(definition.FieldsMeta.HasValue);
        Assert.Equal("radio", definition.FieldsMeta.Value!["condition"].Type);
    }
}
