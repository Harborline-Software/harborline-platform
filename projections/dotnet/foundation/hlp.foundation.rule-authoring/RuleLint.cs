namespace Harborline.Foundation.RuleAuthoring;

// ─────────────────────────────────────────────────────────────────────────────
//  Advisory linting for the decision-table editor (design §2.3 L1 guard + §2.4 gap/overlap
//  linter). These are AUTHORING-TIME hints surfaced inline; the load-bearing rejection is still
//  the skin compiler (DecisionTableCompiler, which raises rule.skin.no_match_unresolved and
//  rejects a non-terminal catch-all). The lint mirrors those checks so the author sees the
//  problem BEFORE they hit publish, plus the gap/overlap advisories the compiler does not raise
//  (an overlap is legal under a hit policy; a gap is only a problem if Otherwise is also
//  unresolved).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Finding severity, mirroring the TS <c>'error' | 'warn' | 'info'</c> union.</summary>
public enum LintSeverity
{
    Error = 0,
    Warn = 1,
    Info = 2,
}

/// <summary>A lint finding — a stable code + scalar params (localized by the surface, never
/// English prose). <paramref name="RowId"/> anchors row-specific findings.</summary>
public sealed record TableLintFinding(
    string Code,
    LintSeverity Severity,
    string? RowId,
    IReadOnlyDictionary<string, string> Params);

/// <summary>Stable lint codes (localized off <c>rules.lint.*</c>). Byte-identical to the TS
/// <c>RuleLintCodes</c>.</summary>
public static class RuleLintCodes
{
    public const string NoMatchUnresolved = "rules.lint.no_match_unresolved";
    public const string NonTerminalCatchAll = "rules.lint.non_terminal_catch_all";
    public const string EmptyOutput = "rules.lint.empty_output";
    public const string Gap = "rules.lint.gap";
    public const string Overlap = "rules.lint.overlap";
}

/// <summary>The decision-table advisory linter — the .NET twin of the TS <c>lint.ts</c>.</summary>
public static class RuleLint
{
    private static readonly IReadOnlyDictionary<string, string> EmptyParams
        = new Dictionary<string, string>();

    private static bool IsCatchAllRow(IReadOnlyDictionary<string, TableCell> cells, IReadOnlyList<string> columnIds)
        => columnIds.All(id => !cells.TryGetValue(id, out var c) || c is TableCell.Any);

    /// <summary>True iff no-match is structurally resolved (a filled default OR a terminal
    /// catch-all row) — the §2.3 publish gate, computed for the surface so it can block publish
    /// before the compiler does.</summary>
    public static bool NoMatchResolved(DecisionTableDraft draft)
    {
        if (draft.NoMatch is NoMatchPosture.Default d) return d.Value.Trim().Length != 0;
        // catch-all posture: the LAST row must be an unconditional catch-all.
        var columnIds = draft.Columns.Select(c => c.Id).ToList();
        var last = draft.Rows.Count > 0 ? draft.Rows[^1] : null;
        return last is not null && IsCatchAllRow(last.Cells, columnIds);
    }

    /// <summary>
    /// The full advisory finding set for a table draft. Order: structural (no-match, L1) then
    /// interval gap/overlap on the first numeric column (the design's reified-interval example).
    /// </summary>
    public static IReadOnlyList<TableLintFinding> LintTable(DecisionTableDraft draft)
    {
        var findings = new List<TableLintFinding>();
        var columnIds = draft.Columns.Select(c => c.Id).ToList();

        // no-match unresolved (mirrors the compiler's publish gate)
        if (!NoMatchResolved(draft))
        {
            findings.Add(new TableLintFinding(RuleLintCodes.NoMatchUnresolved, LintSeverity.Error, null, EmptyParams));
        }

        // L1: a non-terminal catch-all — an unconditional row that isn't the last row can never let
        // rows below it fire (design §2.3; the #1831 forward-guard, surfaced).
        for (int idx = 0; idx < draft.Rows.Count; idx++)
        {
            var row = draft.Rows[idx];
            if (idx < draft.Rows.Count - 1 && IsCatchAllRow(row.Cells, columnIds))
            {
                findings.Add(new TableLintFinding(RuleLintCodes.NonTerminalCatchAll, LintSeverity.Error, row.Id,
                    new Dictionary<string, string> { ["row"] = (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
            if (row.Output.Trim().Length == 0)
            {
                findings.Add(new TableLintFinding(RuleLintCodes.EmptyOutput, LintSeverity.Error, row.Id,
                    new Dictionary<string, string> { ["row"] = (idx + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
            }
        }

        // gap / overlap on the FIRST numeric column (design §2.4 — the interval sharp edge).
        // Advisory only.
        var numericCol = draft.Columns.FirstOrDefault(c => c.ValueType == ColumnValueType.Number);
        if (numericCol is not null)
        {
            findings.AddRange(IntervalFindings(draft, numericCol.Id, columnIds));
        }
        return findings;
    }

    private sealed record Interval(string RowId, int Index, double Lo, double Hi);

    /// <summary>Collect the [lo, hi) interval of each row that constrains ONLY the numeric column
    /// (other cells any), then flag adjacent gaps + overlaps. Rows that also constrain other
    /// columns are skipped (their interval is conditional on another dimension — out of scope for
    /// the 1-D linter).</summary>
    private static IEnumerable<TableLintFinding> IntervalFindings(
        DecisionTableDraft draft, string colId, IReadOnlyList<string> columnIds)
    {
        var otherCols = columnIds.Where(id => id != colId).ToList();
        var intervals = new List<Interval>();
        for (int index = 0; index < draft.Rows.Count; index++)
        {
            var row = draft.Rows[index];
            if (!row.Cells.TryGetValue(colId, out var cell) || cell is not TableCell.Range range) continue;
            bool constrainsOthers = otherCols.Any(id => row.Cells.TryGetValue(id, out var c) && c is not TableCell.Any);
            if (constrainsOthers) continue;
            double lo = range.Lo.Trim().Length == 0 ? double.NegativeInfinity : JsNumberMirror.ToNumber(range.Lo);
            double hi = range.Hi.Trim().Length == 0 ? double.PositiveInfinity : JsNumberMirror.ToNumber(range.Hi);
            if (!double.IsNaN(lo) && !double.IsNaN(hi)) intervals.Add(new Interval(row.Id, index, lo, hi));
        }

        // OrderBy is a stable sort — the same tie behavior as TS Array.prototype.sort (ES2019+).
        var sorted = intervals.OrderBy(i => i.Lo).ToList();
        var findings = new List<TableLintFinding>();
        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var cur = sorted[i];
            if (cur.Lo > prev.Hi)
            {
                findings.Add(new TableLintFinding(RuleLintCodes.Gap, LintSeverity.Warn, null,
                    new Dictionary<string, string>
                    {
                        ["from"] = JsNumberMirror.ToDisplayString(prev.Hi),
                        ["to"] = JsNumberMirror.ToDisplayString(cur.Lo),
                    }));
            }
            else if (cur.Lo < prev.Hi)
            {
                findings.Add(new TableLintFinding(RuleLintCodes.Overlap, LintSeverity.Info, null,
                    new Dictionary<string, string>
                    {
                        ["at"] = JsNumberMirror.ToDisplayString(cur.Lo),
                        ["to"] = JsNumberMirror.ToDisplayString(prev.Hi),
                    }));
            }
        }
        return findings;
    }
}
