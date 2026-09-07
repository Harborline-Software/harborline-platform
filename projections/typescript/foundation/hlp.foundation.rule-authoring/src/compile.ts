/**
 * The AUTHORING → ENGINE bridge for the Rules surface. Lowers the Harborline App editor drafts
 * (`DecisionTableDraft` / `FormulaDraft`) onto the shipped `@harborline-software/rule-engine` authoring skins,
 * compiles them to a plain `RuleDefinition`, and (for the live preview) evaluates a sample through
 * the reactive tier + builds the D10 explainability trace.
 *
 * This module OWNS NO SEMANTICS. The F1 hit-policy / no-match / reified-bounds rules are enforced by
 * `compileDecisionTable`; `formula_undeclared_ref` by `compileFormula`; evaluation by `FormRuleGraph`;
 * the trace's value-leak closure (board F2) by `buildFormTrace` — all shipped. The surface only maps
 * the author's intent into those seams and renders what they yield (design §0, §2.5, §6.3).
 */

import {
  buildFormTrace,
  compile,
  compileDecisionTable,
  compileFormula,
  CompileError,
  FormRuleGraph,
  passThroughTraceFilter,
  RuleInstance,
  type DecisionCell,
  type DecisionRow,
  type DecisionTableSkin,
  type FormulaInput,
  type FormulaSkin,
  type Json,
  type RuleDefinition,
  type RuleOutcome,
  type RuleTraceEntry,
  type TraceAuthorityFilter,
} from '@harborline-software/rule-engine'

import {
  outputActionKind,
  type ColumnValueType,
  type DecisionTableDraft,
  type FormulaCondition,
  type FormulaDraft,
  type FormulaExpr,
  type RuleDraft,
  type TableCell,
  type TableRow,
} from './model.js'

/** Coerce an author-typed string cell/input value to the JSON the engine compares against. */
export function coerceValue(raw: string, type: ColumnValueType): Json {
  if (type === 'number') {
    const n = Number(raw)
    return Number.isFinite(n) ? n : raw
  }
  if (type === 'boolean') return raw === 'true'
  return raw
}

/** Parse a numeric bound string to a number, or `null` for a blank (open-ended) bound. */
function bound(raw: string): number | null {
  const trimmed = raw.trim()
  if (trimmed === '') return null
  const n = Number(trimmed)
  return Number.isFinite(n) ? n : null
}

function cellToDecisionCell(cell: TableCell | undefined, type: ColumnValueType): DecisionCell {
  if (!cell || cell.kind === 'any') return { kind: 'any' }
  if (cell.kind === 'range') {
    return { kind: 'range', loInclusive: bound(cell.lo), hiExclusive: bound(cell.hi) }
  }
  return { kind: 'compare', op: cell.op, value: coerceValue(cell.value, type) }
}

/** Lowers a decision-table draft onto the shipped `DecisionTableSkin`. */
export function tableDraftToSkin(draft: DecisionTableDraft, ruleId: string): DecisionTableSkin {
  const rows: DecisionRow[] = draft.rows.map((row) => ({
    when: draft.columns.map((col) => cellToDecisionCell(row.cells[col.id], col.valueType)),
    output: coerceValue(row.output, 'text'),
    priority: row.priority,
  }))
  return {
    ruleId,
    scope: draft.scope,
    scopeTarget: draft.scopeTarget,
    action: outputActionKind(draft.outputType),
    hitPolicy: draft.hitPolicy,
    inputs: draft.columns.map((c) => c.input),
    rows,
    noMatch:
      draft.noMatch.kind === 'default'
        ? { kind: 'default', value: coerceValue(draft.noMatch.value, 'text') }
        : { kind: 'catch-all' },
  }
}

function exprToJson(expr: FormulaExpr): Json {
  switch (expr.kind) {
    case 'ref':
      return { var: expr.ref }
    case 'literal':
      return coerceValue(expr.value, expr.valueType)
    case 'binary':
      return { [expr.op]: [exprToJson(expr.left), exprToJson(expr.right)] }
    case 'if':
      return { if: [conditionToJson(expr.when), exprToJson(expr.then), exprToJson(expr.else)] }
  }
}

function conditionToJson(cond: FormulaCondition): Json {
  return { [cond.op]: [exprToJson(cond.left), exprToJson(cond.right)] }
}

/** Lowers a formula draft onto the shipped `FormulaSkin`. */
export function formulaDraftToSkin(draft: FormulaDraft, ruleId: string): FormulaSkin {
  const inputs: FormulaInput[] = draft.inputs.map((i) => ({ ref: i.ref, type: i.type }))
  return {
    ruleId,
    scope: draft.scope,
    scopeTarget: draft.scopeTarget,
    action: outputActionKind(draft.outputType),
    inputs,
    expression: draft.expression === null ? null : exprToJson(draft.expression),
  }
}

/**
 * Compiles a draft to a `RuleDefinition` via the shipped skin compiler. Throws the engine's
 * {@link CompileError} (carrying the stable `rule.skin.*` code) on any F1/undeclared-ref rejection —
 * the caller surfaces that code, localized, exactly where the skin compiler raised it.
 */
export function compileDraft(draft: RuleDraft, ruleId: string): RuleDefinition {
  return draft.skin === 'table'
    ? compileDecisionTable(tableDraftToSkin(draft, ruleId))
    : compileFormula(formulaDraftToSkin(draft, ruleId))
}

/** The outcome of a preview evaluation (design §2.5 / §3.1). */
export interface PreviewResult {
  /** The computed outcome value, when the rule produced a Value outcome. */
  value?: Json
  /** The engine outcome (raw) for consumers that need the full shape. */
  outcome?: RuleOutcome
  /** For a decision table: the id of the row that fired, or `null` for the Otherwise/no-match path. */
  firedRowId?: string | null
  /** The D10 explainability trace (localizable codes — design §6.3). */
  trace: RuleTraceEntry[]
}

/** Compiles + evaluates a single rule over sample inputs, returning the outcome + trace. */
function evaluateRule(def: RuleDefinition, sample: Record<string, Json>, filter: TraceAuthorityFilter): { outcome?: RuleOutcome; trace: RuleTraceEntry[] } {
  const compiled = compile([def])
  const result = new FormRuleGraph(compiled).evaluateInstance(RuleInstance.fromJson(sample))
  let outcome: RuleOutcome | undefined
  for (const o of result.byRule.values()) {
    if (o.ruleId === def.id) {
      outcome = o
      break
    }
  }
  const trace = buildFormTrace(compiled, result, filter)
  return { outcome, trace }
}

/**
 * Evaluates a draft against sample inputs through the REAL reactive engine (design §2.5). For a
 * decision table it also runs a PROBE skin (identical ordering/policy, outputs replaced by row ids)
 * so "which row fired" is derived from the engine's own semantics — never a surface re-implementation
 * that could diverge from the compiled rule.
 */
export function evaluatePreview(
  draft: RuleDraft,
  ruleId: string,
  sample: Record<string, Json>,
  filter: TraceAuthorityFilter = passThroughTraceFilter,
): PreviewResult {
  const def = compileDraft(draft, ruleId)
  const { outcome, trace } = evaluateRule(def, sample, filter)
  const value = outcome?.value?.state === 'Resolved' ? outcome.value.value : undefined

  let firedRowId: string | null | undefined
  if (draft.skin === 'table') {
    firedRowId = probeFiredRow(draft, sample)
  }
  return { value, outcome, firedRowId, trace }
}

const OTHERWISE = '__otherwise__'

/** Determines which row fired by compiling a PROBE table (outputs = row ids) and evaluating it — the
 * engine's own ordering + AND semantics, zero divergence from the real compiled rule. */
function probeFiredRow(draft: DecisionTableDraft, sample: Record<string, Json>): string | null {
  const probeRows: TableRow[] = draft.rows.map((r) => ({ ...r, output: r.id }))
  const probe: DecisionTableDraft = { ...draft, rows: probeRows, noMatch: { kind: 'default', value: OTHERWISE } }
  let def: RuleDefinition
  try {
    def = compileDraft(probe, `${'probe'}`)
  } catch (e) {
    if (e instanceof CompileError) return null
    throw e
  }
  const compiled = compile([def])
  const result = new FormRuleGraph(compiled).evaluateInstance(RuleInstance.fromJson(sample))
  for (const o of result.byRule.values()) {
    if (o.ruleId === def.id && o.value?.state === 'Resolved') {
      const v = o.value.value
      return v === OTHERWISE || typeof v !== 'string' ? null : v
    }
  }
  return null
}

/** True iff an error is the engine's compile rejection (so callers can read `.code`). */
export function isCompileError(e: unknown): e is CompileError {
  return e instanceof CompileError
}
