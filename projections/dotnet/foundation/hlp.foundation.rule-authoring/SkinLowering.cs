using System.Text.Json.Nodes;

using Harborline.Foundation.RuleEngine.Compilation;
using Harborline.Foundation.RuleEngine.Explain;
using Harborline.Foundation.RuleEngine.Graph;
using Harborline.Foundation.RuleEngine.Model;
using Harborline.Foundation.RuleEngine.Skins;

namespace Harborline.Foundation.RuleAuthoring;

/// <summary>The outcome of a preview evaluation (design §2.5 / §3.1).</summary>
/// <param name="Value">The computed outcome value, when the rule produced a Resolved Value outcome.</param>
/// <param name="Outcome">The engine outcome (raw) for consumers that need the full shape.</param>
/// <param name="FiredRowId">For a decision table: the id of the row that fired, or null for the
/// Otherwise/no-match path. Meaningless unless <paramref name="FiredRowProbed"/> is true.</param>
/// <param name="FiredRowProbed">True iff the draft was a decision table (the probe ran at all) —
/// the TS bridge's <c>firedRowId === undefined</c> (formula) vs <c>null</c> (Otherwise) distinction.</param>
/// <param name="Trace">The D10 explainability trace (localizable codes — design §6.3).</param>
public sealed record PreviewResult(
    JsonNode? Value,
    RuleOutcome? Outcome,
    string? FiredRowId,
    bool FiredRowProbed,
    IReadOnlyList<RuleTraceEntry> Trace);

/// <summary>
/// The AUTHORING → ENGINE bridge for the Rules surface. Lowers the editor drafts
/// (<see cref="DecisionTableDraft"/> / <see cref="FormulaDraft"/>) onto the shipped
/// <c>Harborline.Foundation.RuleEngine</c> authoring skins, compiles them to a plain
/// <see cref="RuleDefinition"/>, and (for the live preview) evaluates a sample through the reactive
/// tier + builds the D10 explainability trace.
///
/// <para>This class OWNS NO SEMANTICS. The F1 hit-policy / no-match / reified-bounds rules are
/// enforced by <see cref="DecisionTableCompiler"/>; <c>formula_undeclared_ref</c> by
/// <see cref="FormulaCompiler"/>; evaluation by <see cref="FormRuleGraph"/>; the trace's value-leak
/// closure (board F2) by <see cref="RuleTraceBuilder"/> — all shipped. The bridge only maps the
/// author's intent into those seams and renders what they yield (design §0, §2.5, §6.3).</para>
/// </summary>
public static class SkinLowering
{
    /// <summary>Coerce an author-typed string cell/input value to the JSON the engine compares
    /// against (JS <c>Number()</c> semantics for number columns — cross-tier faithful).</summary>
    public static JsonNode? CoerceValue(string raw, ColumnValueType type)
    {
        if (type == ColumnValueType.Number)
        {
            double n = JsNumberMirror.ToNumber(raw);
            return double.IsFinite(n) ? NumNode(n) : JsonValue.Create(raw);
        }
        if (type == ColumnValueType.Boolean) return JsonValue.Create(raw == "true");
        return JsonValue.Create(raw);
    }

    /// <summary>Parse a numeric bound string to a number node, or null for a blank (open-ended) or
    /// unparseable bound — the TS bridge's <c>bound()</c>.</summary>
    private static JsonNode? Bound(string raw)
    {
        string trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        double n = JsNumberMirror.ToNumber(trimmed);
        return double.IsFinite(n) ? NumNode(n) : null;
    }

    /// <summary>Integral doubles become long-backed JSON numbers so both tiers serialize the same
    /// canonical text (TS <c>JSON.stringify(5)</c> is <c>5</c>).</summary>
    private static JsonNode NumNode(double d)
        => d == System.Math.Floor(d) && System.Math.Abs(d) < 9.007e15
            ? JsonValue.Create((long)d)
            : JsonValue.Create(d);

    private static DecisionCell CellToDecisionCell(TableCell? cell, ColumnValueType type) => cell switch
    {
        null or TableCell.Any => DecisionCell.Wildcard,
        TableCell.Range r => DecisionCell.Range(Bound(r.Lo), Bound(r.Hi)),
        TableCell.Compare c => DecisionCell.Compare(c.Op, CoerceValue(c.Value, type)),
        _ => DecisionCell.Wildcard,
    };

    /// <summary>Lowers a decision-table draft onto the shipped <see cref="DecisionTableSkin"/>.</summary>
    public static DecisionTableSkin TableDraftToSkin(DecisionTableDraft draft, string ruleId)
    {
        var rows = draft.Rows
            .Select(row => new DecisionRow(
                When: draft.Columns
                    .Select(col => CellToDecisionCell(
                        row.Cells.TryGetValue(col.Id, out var cell) ? cell : null, col.ValueType))
                    .ToList(),
                Output: CoerceValue(row.Output, ColumnValueType.Text),
                Priority: row.Priority))
            .ToList();
        return new DecisionTableSkin(
            RuleId: ruleId,
            Scope: draft.Scope,
            ScopeTarget: draft.ScopeTarget,
            Action: draft.OutputType,
            HitPolicy: draft.HitPolicy,
            Inputs: draft.Columns.Select(c => c.Input).ToList(),
            Rows: rows,
            NoMatch: draft.NoMatch is NoMatchPosture.Default d
                ? NoMatch.WithDefault(CoerceValue(d.Value, ColumnValueType.Text))
                : NoMatch.CatchAll);
    }

    private static JsonNode? ExprToJson(FormulaExpr expr) => expr switch
    {
        FormulaExpr.Ref r => new JsonObject { ["var"] = JsonValue.Create(r.Name) },
        FormulaExpr.Literal l => CoerceValue(l.Value, l.ValueType),
        FormulaExpr.Binary b => new JsonObject { [b.Op] = new JsonArray(ExprToJson(b.Left), ExprToJson(b.Right)) },
        FormulaExpr.If i => new JsonObject
        {
            ["if"] = new JsonArray(ConditionToJson(i.When), ExprToJson(i.Then), ExprToJson(i.Else)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(expr), expr.GetType().Name, "unknown formula expression kind"),
    };

    private static JsonNode ConditionToJson(FormulaCondition cond)
        => new JsonObject { [cond.Op] = new JsonArray(ExprToJson(cond.Left), ExprToJson(cond.Right)) };

    /// <summary>Lowers a formula draft onto the shipped <see cref="FormulaSkin"/>.</summary>
    public static FormulaSkin FormulaDraftToSkin(FormulaDraft draft, string ruleId)
        => new(
            RuleId: ruleId,
            Scope: draft.Scope,
            ScopeTarget: draft.ScopeTarget,
            Action: draft.OutputType,
            Inputs: draft.Inputs.Select(i => new FormulaInput(i.Ref, TypeName(i.Type))).ToList(),
            Expression: draft.Expression is null ? null : ExprToJson(draft.Expression));

    private static string TypeName(ColumnValueType t) => t switch
    {
        ColumnValueType.Number => "number",
        ColumnValueType.Boolean => "boolean",
        _ => "text",
    };

    /// <summary>
    /// Compiles a draft to a <see cref="RuleDefinition"/> via the shipped skin compiler. Throws the
    /// engine's <see cref="RuleCompilationException"/> (carrying the stable <c>rule.skin.*</c> code)
    /// on any F1/undeclared-ref rejection — the caller surfaces that code, localized, exactly where
    /// the skin compiler raised it.
    /// </summary>
    public static RuleDefinition CompileDraft(RuleDraft draft, string ruleId) => draft switch
    {
        DecisionTableDraft table => DecisionTableCompiler.Compile(TableDraftToSkin(table, ruleId)),
        FormulaDraft formula => FormulaCompiler.Compile(FormulaDraftToSkin(formula, ruleId)),
        _ => throw new ArgumentOutOfRangeException(nameof(draft), draft.GetType().Name, "unknown draft skin"),
    };

    /// <summary>
    /// Evaluates a draft against sample inputs through the REAL reactive engine (design §2.5). For a
    /// decision table it also runs a PROBE skin (identical ordering/policy, outputs replaced by row
    /// ids) so "which row fired" is derived from the engine's own semantics — never a surface
    /// re-implementation that could diverge from the compiled rule.
    /// </summary>
    public static PreviewResult EvaluatePreview(
        RuleDraft draft, string ruleId, JsonObject sample, ITraceAuthorityFilter? filter = null)
    {
        var def = CompileDraft(draft, ruleId);
        var compiled = RuleCompiler.Compile(new[] { def });
        var result = new FormRuleGraph(compiled).EvaluateInstance(RuleInstance.FromJson(sample));
        RuleOutcome? outcome = result.ByRule.Values.FirstOrDefault(o => o.RuleId == def.Id);
        var trace = RuleTraceBuilder.BuildForm(compiled, result, filter ?? PassThroughTraceFilter.Instance);
        JsonNode? value = outcome?.Value is { State: ValueState.Resolved } cv ? cv.Value : null;

        bool probed = draft is DecisionTableDraft;
        string? firedRowId = draft is DecisionTableDraft table ? ProbeFiredRow(table, sample) : null;
        return new PreviewResult(value, outcome, firedRowId, probed, trace);
    }

    private const string Otherwise = "__otherwise__";

    /// <summary>Determines which row fired by compiling a PROBE table (outputs = row ids) and
    /// evaluating it — the engine's own ordering + AND semantics, zero divergence from the real
    /// compiled rule.</summary>
    private static string? ProbeFiredRow(DecisionTableDraft draft, JsonObject sample)
    {
        var probeRows = draft.Rows.Select(r => r with { Output = r.Id }).ToList();
        var probe = draft with { Rows = probeRows, NoMatch = new NoMatchPosture.Default(Otherwise) };
        RuleDefinition def;
        try
        {
            def = CompileDraft(probe, "probe");
        }
        catch (RuleCompilationException)
        {
            return null;
        }
        var compiled = RuleCompiler.Compile(new[] { def });
        var result = new FormRuleGraph(compiled).EvaluateInstance(RuleInstance.FromJson(sample));
        foreach (var o in result.ByRule.Values)
        {
            if (o.RuleId == def.Id && o.Value is { State: ValueState.Resolved } cv)
            {
                return cv.Value is JsonValue jv && jv.TryGetValue<string>(out string? s) && s != Otherwise
                    ? s
                    : null;
            }
        }
        return null;
    }
}
