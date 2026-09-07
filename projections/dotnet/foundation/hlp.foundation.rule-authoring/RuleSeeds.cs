using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>
/// Blank authoring seeds for the Rules front-door "New" flow (design §1.2) — never a truly empty
/// editor (predefined-first): a blank table lands with one numeric column + one interval row + an
/// unresolved Otherwise (the author fills it — §2.3), a blank formula with no inputs + no
/// expression.
/// </summary>
public static class RuleSeeds
{
    private const string Base36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    /// <summary>A tiny, dependency-free id minter for columns/rows/inputs (not a security
    /// surface) — the TS <c>makeLocalId</c>'s 7-char base-36 suffix.</summary>
    public static string MakeLocalId(string prefix)
    {
        Span<char> suffix = stackalloc char[7];
        for (int i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Base36[Random.Shared.Next(Base36.Length)];
        }
        return string.Concat(prefix, "-", new string(suffix));
    }

    /// <summary>A minimal, authorable decision table: one numeric <c>amount</c> column, one
    /// interval row, an unresolved default (author must fill before publish — the F1 gate is
    /// honest from the start).</summary>
    public static DecisionTableDraft BlankTableDraft()
    {
        string colId = MakeLocalId("col");
        return new DecisionTableDraft
        {
            Scope = RuleScope.Field,
            ScopeTarget = "outcome",
            OutputType = RuleActionKind.Compute,
            HitPolicy = HitPolicy.FirstMatch,
            Columns = new[] { new ConditionColumn(colId, "amount", ColumnValueType.Number) },
            Rows = new[]
            {
                new TableRow(
                    MakeLocalId("row"),
                    new Dictionary<string, TableCell> { [colId] = new TableCell.Range("0", "") },
                    Output: "",
                    Priority: 0),
            },
            NoMatch = new NoMatchPosture.Default(""),
        };
    }

    /// <summary>A minimal formula: no declared inputs, no expression yet.</summary>
    public static FormulaDraft BlankFormulaDraft() => new()
    {
        Scope = RuleScope.Field,
        ScopeTarget = "outcome",
        OutputType = RuleActionKind.Compute,
        Inputs = Array.Empty<FormulaInputDecl>(),
        Expression = null,
    };

    /// <summary>The blank seed for a given skin type (the TS twin lands on the formula seed for
    /// anything outside the table/formula pair — characterized behavior, preserved).</summary>
    public static RuleDraft BlankDraftFor(RuleSkinType skin)
        => skin == RuleSkinType.Table ? BlankTableDraft() : BlankFormulaDraft();
}
