using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

// ─────────────────────────────────────────────────────────────────────────────
//  The AUTHORING model for the Rules surface (ADR 0146 D2/D5) — the .NET mirror of the
//  Harborline App editor drafts (apps/carrier/src/rules/model.ts at the pin) and of the TS
//  @harborline-software/rule-authoring model. These are the shapes editors hold and persist;
//  they lower to the engine's authoring SKINS (DecisionTableSkin / FormulaSkin), which
//  compile to a plain RuleDefinition the ratified core evaluates. The bridge builds no
//  evaluator — the F1 hit-policy / no-match / undeclared-ref rules are ENFORCED by the skin
//  compilers, the single source of truth for rejection.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The authoring skins, one per type badge. `Condition` is documented but not authored here.</summary>
public enum RuleSkinType
{
    Condition = 0,
    Table = 1,
    Formula = 2,
}

/// <summary>The declared type of a condition column's input / a formula input.</summary>
public enum ColumnValueType
{
    Number = 0,
    Text = 1,
    Boolean = 2,
}

/// <summary>The closed comparison-operator set the decision-table compare cell accepts.</summary>
public static class CompareOps
{
    public const string Eq = "==";
    public const string Neq = "!=";
    public const string Lt = "<";
    public const string Lte = "<=";
    public const string Gt = ">";
    public const string Gte = ">=";
}

/// <summary>One condition column: watches one named input, typed.</summary>
public sealed record ConditionColumn(string Id, string Input, ColumnValueType ValueType);

/// <summary>One condition cell — mirrors the engine DecisionCell but carries author-typed strings
/// the compile step coerces.</summary>
public abstract record TableCell
{
    private TableCell() { }

    /// <summary>Matches anything (the wildcard cell).</summary>
    public sealed record Any : TableCell;

    /// <summary>Numeric interval: inclusive-low / exclusive-high; either bound may be blank (open).</summary>
    public sealed record Range(string Lo, string Hi) : TableCell;

    /// <summary>A comparison for enum/text/boolean columns over the closed operator set.</summary>
    public sealed record Compare(string Op, string Value) : TableCell;
}

/// <summary>One conditional row: a cell per column + the outcome value + an optional priority.</summary>
public sealed record TableRow(
    string Id,
    IReadOnlyDictionary<string, TableCell> Cells,
    string Output,
    int Priority);

/// <summary>The no-match posture the author resolves (silent null is structurally impossible).</summary>
public abstract record NoMatchPosture
{
    private NoMatchPosture() { }

    /// <summary>A filled Otherwise default.</summary>
    public sealed record Default(string Value) : NoMatchPosture;

    /// <summary>The last conditional row is an unconditional catch-all.</summary>
    public sealed record CatchAll : NoMatchPosture;
}

/// <summary>The persisted authoring blob for a named rule — one skin's editor state.</summary>
public abstract record RuleDraft
{
    private protected RuleDraft() { }

    public required RuleScope Scope { get; init; }

    public required string ScopeTarget { get; init; }

    /// <summary>The output action the produced rule declares (typically Compute).</summary>
    public required RuleActionKind OutputType { get; init; }
}

/// <summary>The full decision-table editor state, persisted per rule.</summary>
public sealed record DecisionTableDraft : RuleDraft
{
    public required HitPolicy HitPolicy { get; init; }

    public required IReadOnlyList<ConditionColumn> Columns { get; init; }

    public required IReadOnlyList<TableRow> Rows { get; init; }

    public required NoMatchPosture NoMatch { get; init; }
}

/// <summary>A declared, typed formula input — the closed set of names the expression may read.</summary>
public sealed record FormulaInputDecl(string Id, string Ref, ColumnValueType Type);

/// <summary>The closed arithmetic operator set (the engine ships + - * /).</summary>
public static class ArithOps
{
    public const string Add = "+";
    public const string Subtract = "-";
    public const string Multiply = "*";
    public const string Divide = "/";
}

/// <summary>One guided expression term — the constrained composition the formula editor exposes.</summary>
public abstract record FormulaExpr
{
    private FormulaExpr() { }

    public sealed record Ref(string Name) : FormulaExpr;

    public sealed record Literal(string Value, ColumnValueType ValueType) : FormulaExpr;

    public sealed record Binary(string Op, FormulaExpr Left, FormulaExpr Right) : FormulaExpr;

    public sealed record If(FormulaCondition When, FormulaExpr Then, FormulaExpr Else) : FormulaExpr;
}

/// <summary>A guided boolean condition for the If shape — one comparison over inputs/literals.</summary>
public sealed record FormulaCondition(FormulaExpr Left, string Op, FormulaExpr Right);

/// <summary>The full formula editor state, persisted per rule.</summary>
public sealed record FormulaDraft : RuleDraft
{
    public required IReadOnlyList<FormulaInputDecl> Inputs { get; init; }

    public FormulaExpr? Expression { get; init; }
}
