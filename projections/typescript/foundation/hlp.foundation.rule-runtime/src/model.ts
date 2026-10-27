import type { RuleDefinition as WireRuleDefinition } from '@harborline-software/contracts/rules'
import type { InternationalizedText } from '@harborline-software/contracts/text'

export type { RuleActionKind, RuleScope, RuleTier } from '@harborline-software/contracts/rules'
export type { InternationalizedText } from '@harborline-software/contracts/text'
export type { PresentationHint } from '@harborline-software/contracts/presentation'

/**
 * SPINE-1 outcome shapes (TS mirror of Harborline.Foundation.RuleEngine.Model).
 * Structurally identical to the .NET records; the shared conformance corpus proves the
 * two tiers emit byte-identical {@link RuleOutcome} values.
 */

/** A JSON value flowing through the engine (instance values, computed values, AST literals). */
export type Json = null | boolean | number | string | Json[] | { [key: string]: Json }

/** The output-type taxonomy of a {@link RuleOutcome}. `Options` is the additive fifth (ADR 0146 D2). */
export type OutputType = 'Value' | 'Validity' | 'Visibility' | 'Presentation' | 'Options'

/** State of a {@link ComputedValue}. `Pending` is the client tier's async-dependency state. */
export type ValueState = 'Resolved' | 'Error' | 'Pending'

/** Severity of a {@link PresentationOutcome}. */
export type Severity = 'info' | 'warn' | 'error'

/** A localizable rule error — stable code + stringified params, never English prose. */
export interface RuleError {
  code: string
  params: Record<string, string>
}

/** A computed value outcome. Exactly one of `value` / `error` per `state`. */
export interface ComputedValue {
  state: ValueState
  value?: Json
  error?: RuleError
}

/** A validation verdict. `error` is present iff `ok` is false. */
export interface Validity {
  ok: boolean
  error?: RuleError
}

/** The merged show/hide/required/readonly state for a cell. Defaults: visible, not-required, not-readonly. */
export interface VisibilityState {
  visible: boolean
  required: boolean
  readOnly: boolean
}

/** A presentation hint outcome. */
export interface PresentationOutcome {
  severity?: Severity | null
  badge?: InternationalizedText
  styleToken?: string
}

/**
 * The available-options outcome of a `set-options` rule (ADR 0146 D2). Mirrors {@link ComputedValue}'s
 * state machine: `options` present iff `state` is `Resolved`, `error` present iff `Error`. Option
 * labelling (i18n) is a renderer concern — this carries raw JSON option values.
 */
export interface OptionsOutcome {
  state: ValueState
  options?: Json[]
  error?: RuleError
}

/** The single neutral evaluation result both tiers emit. Exactly one payload per `outputType`. */
export interface RuleOutcome {
  ruleId: string
  /** Canonical cell key (cross-tier identical): `field:x` / `row:s/r/f` / `agg:s/fn/col` / `section:id` / `schema:`. */
  target: string
  outputType: OutputType
  value?: ComputedValue
  validity?: Validity
  visibility?: VisibilityState
  presentation?: PresentationOutcome
  options?: OptionsOutcome
}

/** The canonical Rules wire shape also accepts a parsed expression inside the runtime. */
export interface RuleDefinition extends Omit<WireRuleDefinition, 'expression'> {
  /** The opaque JsonLogic expression — a JSON string OR a parsed object. */
  expression: string | Json
}

// ── cell address helpers (string keys, cross-tier identical with the .NET CellAddress.Key) ──

export const cell = {
  field: (name: string): string => `field:${name}`,
  row: (section: string, rowId: string, field: string): string => `row:${section}/${rowId}/${field}`,
  agg: (section: string, fn: string, col: string): string => `agg:${section}/${fn}/${col}`,
  section: (id: string): string => `section:${id}`,
  schema: (): string => 'schema:',
}
