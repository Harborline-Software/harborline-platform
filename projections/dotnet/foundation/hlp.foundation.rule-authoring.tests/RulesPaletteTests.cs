using System.Text.Json;

using Harborline.Foundation.RuleEngine.Functions;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>T-590 slice 1: the palette is generated from the R1 register and the Records fields.</summary>
public sealed class RulesPaletteTests
{
    /// <summary>The Records facts behind the T-712 reference-form fixture.</summary>
    internal static readonly RecordFieldSet FixtureRecords = new(
        [new("total", "total", ColumnValueType.Number), new("x", "x", ColumnValueType.Number), new("z", "z", ColumnValueType.Number), new("f", "f", ColumnValueType.Number, Section: "s")],
        [new("lines", [new("y", "y", ColumnValueType.Number), new("amount", "amount", ColumnValueType.Number)])]);

    [Fact(DisplayName = "rules-auth-20, rules-auth-3: the generated palette produces every reference-form entry of the editor contract fixture")]
    public void Generated_palette_produces_every_fixture_reference_form()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        var forms = fixture.RootElement.GetProperty("referenceForms");
        var produced = new HashSet<RulesPaletteReference>();
        foreach (var item in forms.GetProperty("cases").EnumerateArray())
        {
            var palette = RulesPaletteGenerator.Generate(FixtureRecords,
                Enum.Parse<RuleScope>(item.GetProperty("scope").GetString()!), item.GetProperty("scopeTarget").GetString()!);
            var reference = item.GetProperty("ref").GetString()!;
            Assert.True(palette.References.Any(entry => entry.Id == reference),
                $"{item.GetProperty("id").GetString()}: the generated palette cannot produce '{reference}'");
            produced.UnionWith(palette.References);
        }
        foreach (var entry in forms.GetProperty("palette").EnumerateArray())
        {
            var expected = new RulesPaletteReference(entry.GetProperty("id").GetString()!, entry.GetProperty("label").GetString()!,
                Enum.Parse<ColumnValueType>(entry.GetProperty("valueType").GetString()!));
            Assert.Contains(expected, produced);
        }
    }

    [Fact(DisplayName = "rules-auth-20, rules-auth-13: references follow the rule's scope and name only Records fields")]
    public void References_follow_scope_and_name_only_records_fields()
    {
        var field = RulesPaletteGenerator.Generate(FixtureRecords, RuleScope.Field, "total").References.Select(r => r.Id).ToList();
        Assert.DoesNotContain(field, id => id.StartsWith("row.", StringComparison.Ordinal) || id.StartsWith("parent.", StringComparison.Ordinal));
        Assert.Contains("section.s.f", field);
        Assert.DoesNotContain("field.f", field);

        var row = RulesPaletteGenerator.Generate(FixtureRecords, RuleScope.Row, "lines/amount").References.Select(r => r.Id).ToList();
        Assert.Contains("row.y", row);
        Assert.Contains("parent.z", row);
        Assert.DoesNotContain(row, id => id.StartsWith("field.", StringComparison.Ordinal));

        var empty = RulesPaletteGenerator.Generate(new RecordFieldSet([], []), RuleScope.Field, "total").References;
        Assert.Empty(empty);
    }

    // T-1194 item 5: the pre-PR vocabulary is a literal pin, independent of the live register.
    private static readonly string[] PreviewOperations =
        ["var", "missing", "missing_some", "==", "!=", "===", "!==", "!", "!!", "and", "or", "if", ">", ">=", "<", "<=", "+", "-", "*", "/", "%", "min", "max", "in", "cat", "agg", "money.add", "money.sub", "money.mul", "date.add", "date.diff", "date.today", "coding.is"];

    [Fact(DisplayName = "T-1194: rules-auth-11 retains its pre-revision-7 operation declaration")]
    public void Preview_environment_operations_stay_pinned()
    {
        Assert.Equal(PreviewOperations, RulesPreviewEnvironment.Declaration.Operations);
        Assert.Equal(PreviewOperations, RulesPreviewEnvironment.Admitted.Declaration.Operations);
    }

    [Fact(DisplayName = "rules-auth-20, rules-eng-27: the function palette contains only the preview borrower's authorable built-ins")]
    public void Function_palette_stays_within_preview_operations()
    {
        var palette = RulesPaletteGenerator.Generate(FixtureRecords, RuleScope.Field, "total");
        Assert.Equal(PreviewOperations.Skip(1), palette.Functions.Select(f => f.Key));
        Assert.All(palette.Functions, function => Assert.Same(function.Reference, BuiltInFunctionRegister.Resolve(function.Key)));
    }

    [Fact(DisplayName = "T-1194: guided formula choices retain the preview borrower's pre-revision-7 operations")]
    public void Formula_choices_stay_pinned()
    {
        Assert.Equal(PreviewOperations.Skip(1), FormulaCallOps.All);
        foreach (var operation in new[] { "some", "all", "none", "startsWith", "endsWith" })
            Assert.False(FormulaCallOps.IsSupported(operation));
    }

    internal static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "conformance", "hlp.blocks.builder-definitions", "rules-editor-contract-fixtures.json");
    }
}
