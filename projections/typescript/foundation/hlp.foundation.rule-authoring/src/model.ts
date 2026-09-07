/**
 * application-side AUTHORING model for the Rules pillar surface (`/rules`) — ADR 0146 D2/D5, the
 * `rules-surface-design-2026-07-06` direction. These are the shapes the decision-table and formula
 * EDITORS hold and persist; they lower to the engine's authoring SKINS
 * (`@harborline-software/rule-engine` `DecisionTableSkin` / `FormulaSkin`), which in turn compile to a plain
 * `RuleDefinition` the ratified D1 core evaluates. The surface builds no evaluator — it authors the
 * skins and consumes the shipped compile layer (design §0).
 *
 * Nothing here re-implements engine semantics: the F1 hit-policy/no-match/reified-bounds rules are
 * ENFORCED by the skin compiler (`compileDecisionTable`) — this model just captures the author's
 * intent legibly and hands it to the compiler, which is the single source of truth for rejection.
 */

import type { HitPolicy, RuleActionKind, RuleScope } from '@harborline-software/rule-engine'

/** The three authoring skins, one per type badge (design §0). `condition` reuses the form builder's
 * `RuleBuilder` and is documented (design §4) but not authored here yet — table + formula are the
 * new UI value. */
export type RuleSkinType = 'condition' | 'table' | 'formula'

/** The output a table/formula produces — the author-facing name for the engine `RuleActionKind`
 * (design §2.1 outcome column / §3.1 output-type selector). `Compute` is the default. */
export type RuleOutputType = 'Compute' | 'Options' | 'Validate' | 'Visibility'

/** Maps the author-facing output type to the engine action kind the skin declares. */
export function outputActionKind(t: RuleOutputType): RuleActionKind {
  return t
}

// -- Decision-table authoring state (design §2) -------------------------------

/** The declared type of a condition column's input — drives the cell control (design §2.1). A
 * `number` column renders interval cells (§2.3); everything else renders a compare cell. */
export type ColumnValueType = 'number' | 'text' | 'boolean'

/** One condition column: watches one named input, typed. */
export interface ConditionColumn {
  /** Stable per-table column id (never shown; the cell control keys off it). */
  id: string
  /** The input variable this column tests — the `var` the compiled rule reads. */
  input: string
  /** Column type — drives the cell control + preview input coercion. */
  valueType: ColumnValueType
}

/** The closed comparison-operator set the decision-table skin's `compare` cell accepts. */
export type CompareOp = '==' | '!=' | '<' | '<=' | '>' | '>='

/** One condition cell in a row, addressed by column. Mirrors the engine `DecisionCell` shape but
 * carries author-friendly string inputs the compiler step coerces. */
export type TableCell =
  | { kind: 'any' }
  /** Numeric interval: inclusive-low / exclusive-high (design §2.4). Either bound may be blank
   * (open-ended). Stored as strings the compile step parses to numbers. */
  | { kind: 'range'; lo: string; hi: string }
  /** A comparison for enum/text/boolean columns (reuses the closed compare-op set). */
  | { kind: 'compare'; op: CompareOp; value: string }

/** One conditional row: a cell per column + the outcome value + an optional priority. */
export interface TableRow {
  id: string
  /** One cell per column, addressed by column id (missing ⇒ treated as `any`). */
  cells: Record<string, TableCell>
  /** The outcome this row produces when it matches (the `THEN` cell). */
  output: string
  /** Precedence weight under the `priority` hit policy (higher wins). Ignored under `first-match`. */
  priority: number
}

/** The no-match posture the author resolves (design §2.3 — silent null is structurally impossible).
 * `default` fills the Otherwise outcome; `catch-all` marks the last conditional row unconditional. */
export type NoMatchPosture =
  | { kind: 'default'; value: string }
  | { kind: 'catch-all' }

/** The full decision-table editor state, persisted per rule. */
export interface DecisionTableDraft {
  skin: 'table'
  scope: RuleScope
  scopeTarget: string
  outputType: RuleOutputType
  hitPolicy: HitPolicy
  columns: ConditionColumn[]
  rows: TableRow[]
  noMatch: NoMatchPosture
}

// -- Formula authoring state (design §3) --------------------------------------

/** A declared, typed formula input (design §3.1). The closed set of names the expression may read. */
export interface FormulaInputDecl {
  id: string
  ref: string
  type: ColumnValueType
}

/** The arithmetic operator set (closed — the engine ships `+ - * /`). */
export type ArithOp = '+' | '-' | '*' | '/'

/** One guided expression term — the constrained composition the formula editor exposes (design §3.1:
 * "guided composition, not a code box"). v1 supports a conditional shape and a binary-arithmetic
 * shape over declared inputs + literals; the closed operator set keeps `formula_undeclared_ref`
 * catchable and preserves the no-codegen/ReDoS invariant. */
export type FormulaExpr =
  | { kind: 'ref'; ref: string }
  | { kind: 'literal'; value: string; valueType: ColumnValueType }
  | { kind: 'binary'; op: ArithOp; left: FormulaExpr; right: FormulaExpr }
  | { kind: 'if'; when: FormulaCondition; then: FormulaExpr; else: FormulaExpr }

/** A guided boolean condition for the `if` shape — one comparison over declared inputs/literals. */
export interface FormulaCondition {
  left: FormulaExpr
  op: CompareOp
  right: FormulaExpr
}

/** The full formula editor state, persisted per rule. */
export interface FormulaDraft {
  skin: 'formula'
  scope: RuleScope
  scopeTarget: string
  outputType: RuleOutputType
  inputs: FormulaInputDecl[]
  expression: FormulaExpr | null
}

/** The persisted authoring blob for a named rule — one skin's editor state. */
export type RuleDraft = DecisionTableDraft | FormulaDraft

/** A tiny, dependency-free id minter for columns/rows/inputs (not a security surface). */
export function makeLocalId(prefix: string): string {
  return `${prefix}-${Math.random().toString(36).slice(2, 9)}`
}
