using Harborline.Foundation.RuleAuthoring;
using Harborline.Foundation.RuleEngine.Skins;

using Xunit;

namespace Harborline.Foundation.RuleAuthoring.Tests;

/// <summary>The .NET twin of the TS <c>lint.test.ts</c>.</summary>
public sealed class RuleLintTests
{
    private static DecisionTableDraft Table() => new()
    {
        Scope = RuleScope.Field,
        ScopeTarget = "field:amount",
        OutputType = RuleActionKind.Compute,
        HitPolicy = HitPolicy.FirstMatch,
        Columns = new[] { new ConditionColumn("amount", "amount", ColumnValueType.Number) },
        Rows = new[] { Row("low", new TableCell.Range("0", "10"), "low") },
        NoMatch = new NoMatchPosture.Default("other"),
    };

    private static TableRow Row(string id, TableCell? cell, string output)
        => new(id,
            cell is null
                ? new Dictionary<string, TableCell>()
                : new Dictionary<string, TableCell> { ["amount"] = cell },
            output,
            0);

    [Fact]
    public void NoMatchResolvedAcceptsNonEmptyDefaultAndTerminalCatchAll()
    {
        Assert.True(RuleLint.NoMatchResolved(Table()));
        var catchAll = Table() with
        {
            NoMatch = new NoMatchPosture.CatchAll(),
            Rows = new[] { Row("all", null, "all") },
        };
        Assert.True(RuleLint.NoMatchResolved(catchAll));
    }

    [Fact]
    public void NoMatchResolvedRejectsEmptyDefaultsAndMissingTerminalCatchAlls()
    {
        Assert.False(RuleLint.NoMatchResolved(Table() with { NoMatch = new NoMatchPosture.Default("  ") }));
        Assert.False(RuleLint.NoMatchResolved(Table() with { NoMatch = new NoMatchPosture.CatchAll() }));
    }

    [Fact]
    public void ReportsStructuralAndIntervalFindingsInStableOrder()
    {
        var findings = RuleLint.LintTable(Table() with
        {
            NoMatch = new NoMatchPosture.Default(""),
            Rows = new[]
            {
                Row("all", null, ""),
                Row("later", new TableCell.Range("20", "30"), "later"),
            },
        });
        Assert.Equal(
            new[] { RuleLintCodes.NoMatchUnresolved, RuleLintCodes.NonTerminalCatchAll, RuleLintCodes.EmptyOutput },
            findings.Select(f => f.Code).ToArray());
    }

    [Fact]
    public void ReportsNumericGapsAndOverlapsWhileIgnoringInvalidIntervals()
    {
        var findings = RuleLint.LintTable(Table() with
        {
            Rows = new[]
            {
                Row("a", new TableCell.Range("0", "10"), "a"),
                Row("b", new TableCell.Range("5", "8"), "b"),
                Row("c", new TableCell.Range("20", "x"), "c"),
            },
        });
        Assert.Contains(RuleLintCodes.Overlap, findings.Select(f => f.Code));
    }
}
