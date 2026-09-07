using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Skins;


using Xunit;

namespace Harborline.Foundation.RuleEngine.Tests;

/// <summary>
/// ADR 0146 D2 — decision-table + formula authoring-skin COMPILE rejections + happy-path lowering. The
/// byte-identical routing outcomes live in the shared conformance corpus (decision-table-skin.json /
/// formula-skin.json); this asserts the fail-closed rejections (which throw at compile, so cannot be corpus
/// outcome cases) and the exact lowered shape (board F1: hit policy resolved inside the compiled rule, an
/// explicit no-match, reified interval bounds).
/// </summary>
public sealed class SkinTests
{
    private static DecisionTableSkin MinimalTable(
        IReadOnlyList<string>? inputs = null,
        IReadOnlyList<DecisionRow>? rows = null,
        NoMatch? noMatch = null,
        HitPolicy policy = HitPolicy.FirstMatch)
        => new(
            RuleId: "t",
            Scope: RuleScope.Field,
            ScopeTarget: "out",
            Action: RuleActionKind.Compute,
            HitPolicy: policy,
            Inputs: inputs ?? new[] { "x" },
            Rows: rows ?? new[] { new DecisionRow(new[] { DecisionCell.Compare(">=", JsonValue.Create(1)) }, JsonValue.Create("hi")) },
            NoMatch: noMatch ?? NoMatch.WithDefault(JsonValue.Create("lo")));

    // ── decision-table rejections ────────────────────────────────────────────

    [Fact]
    public void DecisionTable_NoInputs_Rejected()
    {
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(inputs: Array.Empty<string>())));
        Assert.Equal(SkinCodes.DecisionTableNoInputs, ex.Code);
    }

    [Fact]
    public void DecisionTable_NoRows_Rejected()
    {
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(rows: Array.Empty<DecisionRow>())));
        Assert.Equal(SkinCodes.DecisionTableEmpty, ex.Code);
    }

    [Fact]
    public void DecisionTable_RaggedRow_Rejected()
    {
        // one input column but a row with two cells.
        var rows = new[] { new DecisionRow(new[] { DecisionCell.Wildcard, DecisionCell.Wildcard }, JsonValue.Create("x")) };
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(rows: rows)));
        Assert.Equal(SkinCodes.DecisionTableBadRow, ex.Code);
    }

    [Fact]
    public void DecisionTable_BadCompareOp_Rejected()
    {
        var rows = new[] { new DecisionRow(new[] { DecisionCell.Compare("~=", JsonValue.Create(1)) }, JsonValue.Create("x")) };
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(rows: rows)));
        Assert.Equal(SkinCodes.DecisionTableBadCell, ex.Code);
    }

    [Fact]
    public void DecisionTable_NoExplicitNoMatch_Rejected()
    {
        // Neither a declared default nor a catch-all → a silent null on no-match is forbidden (board F1).
        var noMatch = new NoMatch(HasDefault: false, RequireCatchAll: false);
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(noMatch: noMatch)));
        Assert.Equal(SkinCodes.NoMatchUnresolved, ex.Code);
    }

    [Fact]
    public void DecisionTable_CatchAllRequiredButAbsent_Rejected()
    {
        // RequireCatchAll but no all-Any row present → rejected.
        var rows = new[] { new DecisionRow(new[] { DecisionCell.Compare(">=", JsonValue.Create(1)) }, JsonValue.Create("hi")) };
        var ex = Assert.Throws<RuleCompilationException>(() => DecisionTableCompiler.Compile(MinimalTable(rows: rows, noMatch: NoMatch.CatchAll)));
        Assert.Equal(SkinCodes.NoMatchUnresolved, ex.Code);
    }

    // ── decision-table happy path (exact lowered shape) ──────────────────────

    [Fact]
    public void DecisionTable_Priority_LowersToOrderedIfCascade()
    {
        // Declared order B(1), A(2); priority reorders A ahead of B; catch-all is the terminal else.
        var skin = new DecisionTableSkin(
            RuleId: "tier",
            Scope: RuleScope.Field, ScopeTarget: "tier", Action: RuleActionKind.Compute,
            HitPolicy: HitPolicy.Priority,
            Inputs: new[] { "score" },
            Rows: new[]
            {
                new DecisionRow(new[] { DecisionCell.Compare(">=", JsonValue.Create(80)) }, JsonValue.Create("B"), Priority: 1),
                new DecisionRow(new[] { DecisionCell.Compare(">=", JsonValue.Create(90)) }, JsonValue.Create("A"), Priority: 2),
                new DecisionRow(new[] { DecisionCell.Wildcard }, JsonValue.Create("F"), Priority: 0),
            },
            NoMatch: NoMatch.CatchAll);

        var rule = DecisionTableCompiler.Compile(skin);
        Assert.Equal(RuleTier.JsonLogic, rule.Tier);
        Assert.Equal(RuleActionKind.Compute, rule.Action);

        var expected = JsonNode.Parse("""
            { "if": [
              { ">=": [ { "var": "score" }, 90 ] }, "A",
              { ">=": [ { "var": "score" }, 80 ] }, "B",
              "F" ] }
            """);
        Assert.Equal(Canonical(expected), Canonical(JsonNode.Parse(rule.Expression)));
    }

    [Fact]
    public void DecisionTable_ReifiesRangeUpperBound()
    {
        var skin = MinimalTable(
            rows: new[]
            {
                new DecisionRow(new[] { DecisionCell.Range(JsonValue.Create(0), JsonValue.Create(100)) }, JsonValue.Create("in")),
            },
            noMatch: NoMatch.WithDefault(JsonValue.Create("out")));
        var rule = DecisionTableCompiler.Compile(skin);
        var expected = JsonNode.Parse("""
            { "if": [
              { "and": [ { ">=": [ { "var": "x" }, 0 ] }, { "<": [ { "var": "x" }, 100 ] } ] }, "in",
              "out" ] }
            """);
        Assert.Equal(Canonical(expected), Canonical(JsonNode.Parse(rule.Expression)));
    }

    // ── formula rejections + happy path ──────────────────────────────────────

    [Fact]
    public void Formula_EmptyExpression_Rejected()
    {
        var skin = new FormulaSkin("f", RuleScope.Field, "out", RuleActionKind.Compute, Array.Empty<FormulaInput>(), Expression: null);
        var ex = Assert.Throws<RuleCompilationException>(() => FormulaCompiler.Compile(skin));
        Assert.Equal(SkinCodes.FormulaEmpty, ex.Code);
    }

    [Fact]
    public void Formula_UndeclaredRef_Rejected()
    {
        // References `price` but only `qty` is declared.
        var expr = JsonNode.Parse("""{ "*": [ { "var": "qty" }, { "var": "price" } ] }""");
        var skin = new FormulaSkin("f", RuleScope.Field, "total", RuleActionKind.Compute,
            new[] { new FormulaInput("qty", "number") }, expr);
        var ex = Assert.Throws<RuleCompilationException>(() => FormulaCompiler.Compile(skin));
        Assert.Equal(SkinCodes.FormulaUndeclaredRef, ex.Code);
        Assert.Contains("price", ex.Message);
    }

    [Fact]
    public void Formula_AllRefsDeclared_Compiles()
    {
        var expr = JsonNode.Parse("""{ "*": [ { "var": "qty" }, { "var": "price" } ] }""");
        var skin = new FormulaSkin("f", RuleScope.Field, "total", RuleActionKind.Compute,
            new[] { new FormulaInput("qty", "number"), new FormulaInput("price", "number") }, expr);
        var rule = FormulaCompiler.Compile(skin);
        Assert.Equal(RuleActionKind.Compute, rule.Action);
        Assert.Equal(Canonical(expr), Canonical(JsonNode.Parse(rule.Expression)));
    }

    private static string Canonical(JsonNode? node)
    {
        var sb = new System.Text.StringBuilder();
        Conformance.CanonicalJson.Write(node, sb);
        return sb.ToString();
    }
}
