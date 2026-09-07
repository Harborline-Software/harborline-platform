/**
 * ADR 0146 D2 — the DECISION-TABLE authoring skin (compile layer), TS mirror of
 * `Harborline.Foundation.RuleEngine.Skins.DecisionTableSkin`.
 *
 * "One expression language" (binding constraint #3): the decision table is an
 * authoring SKIN over the ratified D1 core, never a parallel evaluator. It lowers to a
 * plain `RuleDefinition` whose `expression` is a single multi-branch `if` the existing
 * engine evaluates — no new operator, no new OutputType (the 5-switches-per-tier rule
 * does NOT apply; a skin is a compile-layer representation over the existing AST).
 *
 * Board F1 (BINDING): the skin declares a HIT POLICY (`priority | first-match`) resolved
 * INSIDE the compiled rule (the row ORDER of the emitted `if`), an EXPLICIT no-match
 * default (declared value OR mandatory catch-all — a silent null is a compile rejection),
 * and REIFIED interval upper bounds (`and(>=lo, <hi)`).
 *
 * Byte-identical to the .NET compiler — proven by the shared conformance corpus.
 */
import { CompileError } from '../grammar.js'
import type { Json, RuleActionKind, RuleDefinition, RuleScope } from '../model.js'
import { SkinCodes } from './codes.js'

export type HitPolicy = 'priority' | 'first-match'

/** The closed comparison-operator set a `compare` cell may use. */
const COMPARE_OPS = new Set(['==', '!=', '<', '<=', '>', '>='])

/** One condition cell — the test applied to ONE input column, per row. */
export type DecisionCell =
  | { kind: 'any' }
  | { kind: 'compare'; op: string; value: Json }
  | { kind: 'range'; loInclusive?: Json | null; hiExclusive?: Json | null }

/** One row: a condition per input column + an output + a priority (used only under `priority`). */
export interface DecisionRow {
  when: DecisionCell[]
  output: Json
  priority?: number
}

/** The explicit no-match posture — exactly one form (board F1; a silent null is forbidden). */
export type NoMatch = { kind: 'default'; value: Json } | { kind: 'catch-all' }

/** The decision-table authoring skin. Compile with {@link compileDecisionTable}. */
export interface DecisionTableSkin {
  ruleId: string
  scope: RuleScope
  scopeTarget: string
  action: RuleActionKind
  hitPolicy: HitPolicy
  inputs: string[]
  rows: DecisionRow[]
  noMatch: NoMatch
}

/** Lowers a decision-table skin to a `RuleDefinition` (a single multi-branch `if`). */
export function compileDecisionTable(skin: DecisionTableSkin): RuleDefinition {
  const id = skin.ruleId

  if (skin.inputs.length === 0) throw reject(SkinCodes.decisionTableNoInputs, id, 'a decision table must declare at least one input column')
  if (skin.rows.length === 0) throw reject(SkinCodes.decisionTableEmpty, id, 'a decision table must declare at least one row')

  for (let r = 0; r < skin.rows.length; r++) {
    const row = skin.rows[r]
    if (row.when.length !== skin.inputs.length) {
      throw reject(SkinCodes.decisionTableBadRow, id,
        `row ${r} has ${row.when.length} cell(s) but the table declares ${skin.inputs.length} input column(s)`)
    }
    for (const cell of row.when) {
      if (cell.kind === 'compare' && !COMPARE_OPS.has(cell.op)) {
        throw reject(SkinCodes.decisionTableBadCell, id,
          `row ${r} has a compare cell with an unsupported operator '${cell.op}' (allowed: ${[...COMPARE_OPS].join(', ')})`)
      }
    }
  }

  // Order rows per the hit policy — resolve overlap INSIDE the compiled rule. Both tiers use a stable
  // sort so ties (equal priority) keep declared order, matching .NET's stable OrderByDescending.
  let ordered: DecisionRow[]
  if (skin.hitPolicy === 'priority') {
    ordered = skin.rows.slice().sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0))
  } else if (skin.hitPolicy === 'first-match') {
    ordered = skin.rows.slice()
  } else {
    throw reject(SkinCodes.decisionTableInvalidHitPolicy, id, `unknown hit policy '${skin.hitPolicy as string}'`)
  }

  // Resolve the explicit no-match terminal (board F1 — never a silent null).
  let terminalElse: Json
  let cascadeRows: DecisionRow[]
  if (skin.noMatch.kind === 'default') {
    terminalElse = skin.noMatch.value
    cascadeRows = ordered
  } else {
    // catch-all: the LAST catch-all row (evaluation order) is the terminal else; earlier rows still cascade.
    let catchAllIdx = -1
    for (let i = ordered.length - 1; i >= 0; i--) {
      if (isCatchAll(ordered[i])) { catchAllIdx = i; break }
    }
    if (catchAllIdx < 0) {
      throw reject(SkinCodes.noMatchUnresolved, id,
        'hit policy requires a catch-all row (all-Any cells) but none is present — a silent null on no-match is forbidden (ADR 0146 D2, board F1)')
    }
    terminalElse = ordered[catchAllIdx].output
    cascadeRows = ordered.filter((_, i) => i !== catchAllIdx)
  }

  // Emit the single multi-branch if: [cond0, out0, …, terminalElse].
  const ifArgs: Json[] = []
  for (const row of cascadeRows) {
    ifArgs.push(rowCondition(row, skin.inputs))
    ifArgs.push(row.output)
  }
  ifArgs.push(terminalElse)

  return {
    id,
    tier: 'JsonLogic',
    scope: skin.scope,
    scopeTarget: skin.scopeTarget,
    expression: { if: ifArgs },
    action: skin.action,
  }
}

function isCatchAll(row: DecisionRow): boolean {
  return row.when.every((c) => c.kind === 'any')
}

function rowCondition(row: DecisionRow, inputs: string[]): Json {
  const terms: Json[] = []
  for (let c = 0; c < row.when.length; c++) {
    const term = cellCondition(row.when[c], inputs[c])
    if (term !== null) terms.push(term)
  }
  if (terms.length === 0) return true
  if (terms.length === 1) return terms[0]
  return { and: terms }
}

function cellCondition(cell: DecisionCell, input: string): Json | null {
  switch (cell.kind) {
    case 'any':
      return null
    case 'compare':
      return { [cell.op]: [{ var: input }, cell.value] }
    case 'range': {
      const lo = cell.loInclusive === undefined || cell.loInclusive === null ? null : { '>=': [{ var: input }, cell.loInclusive] }
      const hi = cell.hiExclusive === undefined || cell.hiExclusive === null ? null : { '<': [{ var: input }, cell.hiExclusive] }
      if (lo !== null && hi !== null) return { and: [lo, hi] }
      return lo ?? hi // an open-ended range with neither bound is a wildcard (null)
    }
  }
}

function reject(code: string, ruleId: string, message: string): CompileError {
  return new CompileError(code, `decision-table skin '${ruleId}': ${message}`, ruleId)
}
