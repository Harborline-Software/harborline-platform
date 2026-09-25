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

    [Fact(DisplayName = "rules-auth-20, rules-eng-27: the function half of the palette is the register's authorable built-ins")]
    public void Function_half_is_the_register()
    {
        var palette = RulesPaletteGenerator.Generate(FixtureRecords, RuleScope.Field, "total");
        Assert.Equal(BuiltInFunctionRegister.Functions.Where(f => f.Key != "var").Select(f => f.Key), palette.Functions.Select(f => f.Key));
        Assert.Equal(palette.Functions.Select(f => f.Key), FormulaCallOps.All);
        Assert.All(palette.Functions, function => Assert.Same(function.Reference, BuiltInFunctionRegister.Resolve(function.Key)));
    }

    internal static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "repository.yaml"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "conformance", "hlp.blocks.builder-definitions", "rules-editor-contract-fixtures.json");
    }
}
