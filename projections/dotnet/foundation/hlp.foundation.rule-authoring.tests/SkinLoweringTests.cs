using System.Text.Json.Nodes;

using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>
/// De-risk + semantics proof for the Rules-surface authoring→engine bridge (design R2/R3/R4
/// gates) — the .NET twin of the TS <c>compile.test.ts</c>. These assert the LOAD-BEARING
/// behaviors against the REAL shipped <c>Harborline.Foundation.RuleEngine</c> skins: hit-policy
/// round-trips, structural no-match enforcement, honest reified bounds (the $5000.00 edge), the
/// non-terminal catch-all flag, and the formula undeclared-ref rejection.
/// </summary>
public sealed class SkinLoweringTests
{
    private static readonly TimeProvider PreviewClock = new FixedTimeProvider(new DateTimeOffset(2026, 6, 30, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Preview_and_fired_row_probe_share_one_caller_supplied_instant()
    {
        var clock = new AdvancingTimeProvider(
            new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 0, 0, 1, TimeSpan.Zero));

        var result = SkinLowering.EvaluatePreview(AmountTable(), "invoice-route", Sample(500), clock);

        Assert.Equal("r1", result.FiredRowId);
        Assert.Equal(1, clock.Reads);
    }
    private static DecisionTableDraft AmountTable() => new()
    {
        Scope = RuleScope.Field,
        ScopeTarget = "route",
        OutputType = RuleActionKind.Compute,
        HitPolicy = HitPolicy.FirstMatch,
        Columns = new[] { new ConditionColumn("c1", "amount", ColumnValueType.Number) },
        Rows = new[]
        {
            new TableRow("r1", Cells("c1", new TableCell.Range("0", "1000")), "Auto-approve", 0),
            new TableRow("r2", Cells("c1", new TableCell.Range("1000", "5000")), "Manager", 0),
            new TableRow("r3", Cells("c1", new TableCell.Range("5000", "")), "Director", 0),
        },
        NoMatch = new NoMatchPosture.Default("Require approval"),
    };

    private static IReadOnlyDictionary<string, TableCell> Cells(string colId, TableCell cell)
        => new Dictionary<string, TableCell> { [colId] = cell };

    private static JsonObject Sample(double amount) => new() { ["amount"] = amount };

    [Fact]
    public void CompilesToRuleDefinitionAndEvaluatesFiringRowWithReifiedBounds()
    {
        var draft = AmountTable();
        // $500 -> row 1
        var r = SkinLowering.EvaluatePreview(draft, "invoice-route", Sample(500), PreviewClock);
        Assert.Equal("r1", r.FiredRowId);
        Assert.Equal("\"Auto-approve\"", r.Value?.ToJsonString());
        // $1000.00 is inclusive-low of row 2 (>= 1000), exclusive-high of row 1 (< 1000) -> row 2
        r = SkinLowering.EvaluatePreview(draft, "invoice-route", Sample(1000), PreviewClock);
        Assert.Equal("r2", r.FiredRowId);
        Assert.Equal("\"Manager\"", r.Value?.ToJsonString());
        // $5000.00 is >= 5000 so it enters row 3 (Director) — the reified boundary is honest
        r = SkinLowering.EvaluatePreview(draft, "invoice-route", Sample(5000), PreviewClock);
        Assert.Equal("r3", r.FiredRowId);
        Assert.Equal("\"Director\"", r.Value?.ToJsonString());
    }

    [Fact]
    public void ProducesLocalizableTraceForTheEvaluation()
    {
        var r = SkinLowering.EvaluatePreview(AmountTable(), "invoice-route", Sample(500), PreviewClock);
        Assert.NotEmpty(r.Trace);
        Assert.StartsWith("rule.trace.", r.Trace[0].Code, StringComparison.Ordinal);
    }

    [Fact]
    public void HitPolicyChangesWhichRowFires()
    {
        // Two overlapping rows; priority should pick the higher-priority row.
        var overlapping = AmountTable() with
        {
            HitPolicy = HitPolicy.Priority,
            Rows = new[]
            {
                new TableRow("low", Cells("c1", new TableCell.Compare(">=", "0")), "LOW", 1),
                new TableRow("high", Cells("c1", new TableCell.Compare(">=", "0")), "HIGH", 5),
            },
            NoMatch = new NoMatchPosture.Default("none"),
        };
        Assert.Equal("high", SkinLowering.EvaluatePreview(overlapping, "k", Sample(10), PreviewClock).FiredRowId);
        // Under first-match the DECLARED-order-first row wins instead.
        var firstMatch = overlapping with { HitPolicy = HitPolicy.FirstMatch };
        Assert.Equal("low", SkinLowering.EvaluatePreview(firstMatch, "k", Sample(10), PreviewClock).FiredRowId);
    }

    [Fact]
    public void PriorityCatchAllKeepsTheSameWinningRowAndValueInPreviewAndRuntime()
    {
        var table = AmountTable() with
        {
            HitPolicy = HitPolicy.Priority,
            Rows = new[]
            {
                new TableRow("conditional", Cells("c1", new TableCell.Compare(">=", "0")), "CONDITIONAL", 1),
                new TableRow("wildcard", Cells("c1", new TableCell.Any()), "WILDCARD", 10),
            },
            NoMatch = new NoMatchPosture.CatchAll(),
        };

        var preview = SkinLowering.EvaluatePreview(table, "priority-catch-all", Sample(10), PreviewClock);

        Assert.Equal("wildcard", preview.FiredRowId);
        Assert.Equal("\"WILDCARD\"", preview.Value?.ToJsonString());
    }

    [Fact]
    public void RejectsMalformedRangeBoundWithCellNamed()
    {
        var table = AmountTable() with
        {
            Rows = new[]
            {
                new TableRow("malformed", Cells("c1", new TableCell.Range("not-a-number", "100")), "x", 0),
            },
        };

        var ex = Assert.Throws<RuleCompilationException>(() =>
            SkinLowering.CompileDraft(table, "malformed-bound"));

        Assert.Equal(SkinCodes.DecisionTableBadCell, ex.Code);
        Assert.Contains("malformed/c1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BlankOtherwiseDefaultIsCompileRejectionAndSurfaceLintError()
    {
        var unresolved = AmountTable() with { NoMatch = new NoMatchPosture.Default("") };
        Assert.False(RuleLint.NoMatchResolved(unresolved));
        Assert.Contains(RuleLint.LintTable(unresolved), f => f.Code == RuleLintCodes.NoMatchUnresolved);
        // the underlying compiler ALSO rejects (no terminal via the catch-all path)
        var noCatch = AmountTable() with { NoMatch = new NoMatchPosture.CatchAll() };
        var e = Assert.Throws<RuleCompilationException>(() => SkinLowering.CompileDraft(noCatch, "k"));
        Assert.Equal(SkinCodes.NoMatchUnresolved, e.Code);
    }

    [Fact]
    public void FlagsNonTerminalCatchAll()
    {
        var midCatchAll = AmountTable() with
        {
            Rows = new[]
            {
                new TableRow("r1", Cells("c1", new TableCell.Any()), "everything", 0),
                new TableRow("r2", Cells("c1", new TableCell.Range("0", "10")), "never", 0),
            },
        };
        Assert.Contains(RuleLint.LintTable(midCatchAll), f => f.Code == RuleLintCodes.NonTerminalCatchAll);
    }

    [Fact]
    public void DetectsIntervalGapAdvisory()
    {
        var gapped = AmountTable() with
        {
            Rows = new[]
            {
                new TableRow("r1", Cells("c1", new TableCell.Range("0", "1000")), "A", 0),
                new TableRow("r2", Cells("c1", new TableCell.Range("2000", "3000")), "B", 0),
            },
        };
        Assert.Contains(RuleLint.LintTable(gapped), f => f.Code == RuleLintCodes.Gap);
    }

    private static FormulaDraft Overtime() => new()
    {
        Scope = RuleScope.Field,
        ScopeTarget = "pay",
        OutputType = RuleActionKind.Compute,
        Inputs = new[]
        {
            new FormulaInputDecl("i1", "hours", ColumnValueType.Number),
            new FormulaInputDecl("i2", "rate", ColumnValueType.Number),
        },
        Expression = new FormulaExpr.Binary("*",
            new FormulaExpr.Ref("hours"),
            new FormulaExpr.Ref("rate")),
    };

    [Fact]
    public void CompilesAndEvaluatesValidFormula()
    {
        var sample = new JsonObject { ["hours"] = 10, ["rate"] = 20 };
        var r = SkinLowering.EvaluatePreview(Overtime(), "overtime", sample, PreviewClock);
        Assert.Equal("200", r.Value?.ToJsonString());
        Assert.False(r.FiredRowProbed); // a formula preview never probes a fired row
    }

    [Fact]
    public void RejectsUndeclaredReference()
    {
        var bad = Overtime() with { Expression = new FormulaExpr.Ref("bonus") };
        var e = Assert.Throws<RuleCompilationException>(() => SkinLowering.CompileDraft(bad, "overtime"));
        Assert.Equal(SkinCodes.FormulaUndeclaredRef, e.Code);
    }
}

file sealed class FixedTimeProvider(DateTimeOffset instant) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => instant;
}

file sealed class AdvancingTimeProvider(params DateTimeOffset[] instants) : TimeProvider
{
    private int next;
    public int Reads { get; private set; }
    public override DateTimeOffset GetUtcNow()
    {
        Reads++;
        return instants[Math.Min(next++, instants.Length - 1)];
    }
}
