using System.Text;
using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Conformance;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

public sealed class FieldPresetConformanceTests
{
    [Theory(DisplayName = "rules-auth-17 producer parity: source matches the literal authored fixture")]
    [InlineData("Number", "NonNegative")]
    [InlineData("Text", "NotBlank")]
    [InlineData("Boolean", "MustBeTrue")]
    public void Producer_source_matches_literal_fixture(string kind, string preset)
    {
        var row = Case(kind);
        var expected = RuleDefinitionCodec.Parse(row["source"]!.ToJsonString(), RuleIntentPhase.Author).Document!;
        var document = FieldRuleBindings.Preset(expected.Envelope, "amount", Enum.Parse<ColumnValueType>(kind), Enum.Parse<FieldPreset>(preset));

        Assert.Equal(new[] { Enum.Parse<FieldPreset>(preset) }, FieldRuleBindings.PresetsFor(Enum.Parse<ColumnValueType>(kind)));
        Assert.Equivalent(expected, document, strict: true);
        Assert.Equal(Canonical(row["source"]!), RuleDefinitionCodec.SerializeCanonical(document));
    }

    [Theory(DisplayName = "rules-auth-17 producer parity: all incompatible kind/preset pairs refuse")]
    [InlineData("Number", "NotBlank")]
    [InlineData("Number", "MustBeTrue")]
    [InlineData("Text", "NonNegative")]
    [InlineData("Text", "MustBeTrue")]
    [InlineData("Boolean", "NonNegative")]
    [InlineData("Boolean", "NotBlank")]
    public void Producer_refuses_incompatible_pair(string kind, string preset)
    {
        var envelope = RuleDefinitionCodec.Parse(Case("Number")["source"]!.ToJsonString(), RuleIntentPhase.Author).Document!.Envelope;
        Assert.Throws<ArgumentException>(() => FieldRuleBindings.Preset(envelope, "amount", Enum.Parse<ColumnValueType>(kind), Enum.Parse<FieldPreset>(preset)));
    }

    [Theory(DisplayName = "rules-auth-17 producer parity: ordinary source publishes with literal lowered expression")]
    [InlineData("Number", "NonNegative")]
    [InlineData("Text", "NotBlank")]
    [InlineData("Boolean", "MustBeTrue")]
    public void Producer_publishes_literal_lowered_expression(string kind, string preset)
    {
        var row = Case(kind);
        var envelope = RuleDefinitionCodec.Parse(row["source"]!.ToJsonString(), RuleIntentPhase.Author).Document!.Envelope;
        var document = FieldRuleBindings.Preset(envelope, "amount", Enum.Parse<ColumnValueType>(kind), Enum.Parse<FieldPreset>(preset));
        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.True(JsonNode.DeepEquals(row["lowered"], result.Lowered), result.Lowered?.ToJsonString());
        Assert.Equal(Canonical(row["source"]!["envelope"]!), Canonical(JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(document))!["envelope"]!));
        Assert.Equal(Canonical(row["source"]!["envelope"]!), Canonical(JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(result.Document!))!["envelope"]!));
    }

    [Fact(DisplayName = "rules-auth-17 producer parity: Number remains an ordinary editable Formula at threshold 5")]
    public void Producer_number_remains_editable()
    {
        var row = Case("Number");
        var envelope = RuleDefinitionCodec.Parse(row["source"]!.ToJsonString(), RuleIntentPhase.Author).Document!.Envelope;
        var document = FieldRuleBindings.Preset(envelope, "amount", ColumnValueType.Number, FieldPreset.NonNegative);
        var formula = Assert.IsType<FormulaDraft>(document.Draft);
        var call = Assert.IsType<FormulaExpr.Call>(formula.Expression);
        var threshold = Assert.IsType<FormulaExpr.Literal>(call.Args[1]);
        var edit = Fixture()["numberEdit"]!;
        document = document with
        {
            Draft = formula with
            {
                Expression = call with { Args = [call.Args[0], threshold with { Value = edit["threshold"]!.GetValue<string>() }] },
            },
        };
        var result = RuleIntentValidator.Validate(document, RuleIntentPhase.Publish);

        Assert.True(result.IsValid);
        Assert.Empty(result.Diagnostics);
        Assert.True(JsonNode.DeepEquals(edit["lowered"], result.Lowered), result.Lowered?.ToJsonString());
        Assert.Equal(Canonical(row["source"]!["envelope"]!), Canonical(JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(document))!["envelope"]!));
        Assert.Equal(Canonical(row["source"]!["envelope"]!), Canonical(JsonNode.Parse(RuleDefinitionCodec.SerializeCanonical(result.Document!))!["envelope"]!));
    }

    private static JsonNode Case(string kind) => Fixture()["cases"]!.AsArray().Single(row => row!["kind"]!.GetValue<string>() == kind)!;

    private static JsonNode Fixture()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        return JsonNode.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "conformance", "hlp.foundation.rule-authoring", "field-presets.json")))!;
    }

    private static string Canonical(JsonNode source)
    {
        var text = new StringBuilder();
        CanonicalJson.Write(source, text);
        return text.ToString();
    }
}
