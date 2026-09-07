/**
 * SPINE-1 neutral contract shapes (TS mirror of Harborline.Foundation.RuleEngine.Model).
 * Structurally identical to the .NET records; the shared conformance corpus proves the
 * two tiers emit byte-identical {@link RuleOutcome} values.
 */

/** A JSON value flowing through the engine (instance values, computed values, AST literals). */
export type Json = null | boolean | number | string | Json[] | { [key: string]: Json }

/** Locale-keyed text — mirrors the .NET `InternationalizedText`. */
export interface InternationalizedText {
  defaultLocale: string
  values: Record<string, string>
}

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

// ── authoring shape (mirror of foundation-forms RuleDefinition + enums) ──────────

export type RuleTier = 'JsonSchema' | 'JsonLogic' | 'PowerFx'
export type RuleScope = 'Field' | 'Section' | 'Schema' | 'Row' | 'Table'
export type RuleActionKind = 'Visibility' | 'Required' | 'ReadOnly' | 'Validate' | 'Compute' | 'Presentation' | 'Options'

/** Presentation payload carried by a `Presentation` rule (SPINE-1 Decision DA). */
export interface PresentationHint {
  severity?: 'info' | 'warn' | 'error'
  badge?: InternationalizedText
  styleToken?: string
}

/** One cross-field rule (mirror of the .NET `RuleDefinition`). `expression` is the JsonLogic AST. */
export interface RuleDefinition {
  id: string
  tier: RuleTier
  scope: RuleScope
  scopeTarget: string
  /** The opaque JsonLogic expression — a JSON string OR a parsed object. */
  expression: string | Json
  action: RuleActionKind
  errorMessage?: InternationalizedText
  presentation?: PresentationHint
}

// ── cell address helpers (string keys, cross-tier identical with the .NET CellAddress.Key) ──

export const cell = {
  field: (name: string): string => `field:${name}`,
  row: (section: string, rowId: string, field: string): string => `row:${section}/${rowId}/${field}`,
  agg: (section: string, fn: string, col: string): string => `agg:${section}/${fn}/${col}`,
  section: (id: string): string => `section:${id}`,
  schema: (): string => 'schema:',
}
