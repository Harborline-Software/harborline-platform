/**
 * ADR 0146 D10 — interactive explainability traces (TS mirror of
 * `Harborline.Foundation.RuleEngine.Explain.RuleTrace`). The Pilot "why is this field
 * hidden? / why did this route?" substrate — localizable CODE + PARAM entries (ADR 0055
 * validation-codes doctrine, never embedded English).
 *
 * Board F2 (BINDING) — the value-leak seam is closed BY CONSTRUCTION: a trace is a pure
 * projection over the compiled rules' STATIC field references + the built RuleOutcome (a
 * code). It never touches a resolved RefValue (the now-public resolver seam from slice 2),
 * so a field VALUE can never enter a trace param. The authority filter then gates field
 * REFERENCES (shredded → reference+hash; unreadable → redacted). Explainability is never a
 * PBAC or crypto-shred bypass. Byte-identical to the .NET tier.
 */
import { compile, type CompiledGraph, type CompiledRule } from './compiler.js'
import { CompileError, type RuleRef } from './grammar.js'
import type { RuleEvaluationResult } from './graph.js'
import type { RuleActionKind, RuleDefinition, RuleOutcome, Validity, VisibilityState } from './model.js'

/** Stable trace codes — byte-identical to the .NET `RuleTraceCodes`. */
export const RuleTraceCodes = {
  valueComputed: 'rule.trace.value_computed',
  valueError: 'rule.trace.value_error',
  pending: 'rule.trace.pending',
  validationPassed: 'rule.trace.validation_passed',
  validationFailed: 'rule.trace.validation_failed',
  shown: 'rule.trace.shown',
  hidden: 'rule.trace.hidden',
  required: 'rule.trace.required',
  notRequired: 'rule.trace.not_required',
  readonly: 'rule.trace.readonly',
  editable: 'rule.trace.editable',
  optionsSet: 'rule.trace.options_set',
  optionsError: 'rule.trace.options_error',
  presented: 'rule.trace.presented',
  notPresented: 'rule.trace.not_presented',
  guardPassed: 'rule.trace.guard_passed',
  guardFailed: 'rule.trace.guard_failed',
} as const

/** How a field reference appears in a trace (board F2). */
export type TraceFieldDisclosure = 'show' | 'redact' | 'hash'

/** The authority filter — sees only field NAMES, never values; decides disclosure per field. */
export interface TraceAuthorityFilter {
  disclose(fieldName: string): TraceFieldDisclosure
}

/** The no-op filter — every field reference is shown (the default when no authority context is bound). */
export const passThroughTraceFilter: TraceAuthorityFilter = { disclose: () => 'show' }

/** One trace entry — a localizable code + scalar params. A field VALUE never appears in `params`. */
export interface RuleTraceEntry {
  ruleId: string
  target: string
  code: string
  params: Record<string, string>
}

/**
 * Builds the trace for a whole form evaluation — one entry per rule outcome. Call on the interactive /
 * CP-gated path; the batch path simply does not call it (traceless). `filter` gates each field reference.
 */
export function buildFormTrace(
  compiled: CompiledGraph,
  result: RuleEvaluationResult,
  filter: TraceAuthorityFilter = passThroughTraceFilter,
): RuleTraceEntry[] {
  const rulesById = new Map<string, CompiledRule>()
  for (const r of compiled.rules) rulesById.set(r.source.id, r)

  const entries: RuleTraceEntry[] = []
  // Deterministic order (Map insertion order is not guaranteed identical cross-tier): sort by rule key.
  for (const key of [...result.byRule.keys()].sort()) {
    const outcome = result.byRule.get(key)!
    entries.push(describeOutcome(outcome, rulesById.get(outcome.ruleId), filter))
  }
  return entries
}

/**
 * Builds a single trace entry for a workflow guard outcome (the guard-evaluator arm). Re-derives the guard
 * rule's field references from its definition (deterministic). A compile failure degrades to an entry with
 * no reads (the trace path never throws).
 */
export function buildGuardTrace(
  rule: RuleDefinition,
  outcome: Validity,
  filter: TraceAuthorityFilter = passThroughTraceFilter,
): RuleTraceEntry {
  let reads: string[] = []
  try {
    const compiled = compile([rule])
    if (compiled.rules.length > 0) reads = fieldRefsOf(compiled.rules[0])
  } catch (e) {
    if (!(e instanceof CompileError)) throw e // trace is best-effort only for compile rejections
  }
  const params = baseParams(rule.id, reads, filter)
  if (!outcome.ok && outcome.error) params.cause = outcome.error.code
  return {
    ruleId: rule.id,
    target: `guard:${rule.id}`,
    code: outcome.ok ? RuleTraceCodes.guardPassed : RuleTraceCodes.guardFailed,
    params,
  }
}

function describeOutcome(outcome: RuleOutcome, rule: CompiledRule | undefined, filter: TraceAuthorityFilter): RuleTraceEntry {
  const reads = rule ? fieldRefsOf(rule) : []
  const params = baseParams(outcome.ruleId, reads, filter)

  let code: string
  switch (outcome.outputType) {
    case 'Value':
      code = outcome.value!.state === 'Resolved' ? RuleTraceCodes.valueComputed
        : outcome.value!.state === 'Pending' ? RuleTraceCodes.pending
        : cause(params, outcome.value!.error?.code, RuleTraceCodes.valueError)
      break
    case 'Validity':
      code = outcome.validity!.ok ? RuleTraceCodes.validationPassed
        : cause(params, outcome.validity!.error?.code, RuleTraceCodes.validationFailed)
      break
    case 'Visibility':
      code = visibilityCode(rule?.source.action ?? 'Visibility', outcome.visibility!)
      break
    case 'Options':
      code = outcome.options!.state === 'Resolved' ? RuleTraceCodes.optionsSet
        : outcome.options!.state === 'Pending' ? RuleTraceCodes.pending
        : cause(params, outcome.options!.error?.code, RuleTraceCodes.optionsError)
      break
    default: // Presentation
      code = outcome.presentation?.severity ? RuleTraceCodes.presented : RuleTraceCodes.notPresented
      break
  }
  return { ruleId: outcome.ruleId, target: outcome.target, code, params }
}

function visibilityCode(action: RuleActionKind, v: VisibilityState): string {
  if (action === 'Required') return v.required ? RuleTraceCodes.required : RuleTraceCodes.notRequired
  if (action === 'ReadOnly') return v.readOnly ? RuleTraceCodes.readonly : RuleTraceCodes.editable
  return v.visible ? RuleTraceCodes.shown : RuleTraceCodes.hidden
}

function cause(params: Record<string, string>, code: string | undefined, traceCode: string): string {
  if (code) params.cause = code
  return traceCode
}

function baseParams(ruleId: string, reads: string[], filter: TraceAuthorityFilter): Record<string, string> {
  const params: Record<string, string> = { rule: ruleId }
  if (reads.length > 0) {
    params.reads = reads.map((name) => render(name, filter.disclose(name))).join(',')
  }
  return params
}

function render(fieldName: string, disclosure: TraceFieldDisclosure): string {
  if (disclosure === 'redact') return '[redacted]'
  if (disclosure === 'hash') return '#' + fnv1a8(fieldName)
  return fieldName
}

function fieldRefsOf(rule: CompiledRule): string[] {
  const seen: string[] = []
  const set = new Set<string>()
  for (const rf of rule.references as RuleRef[]) {
    const name = rf.kind === 'field' ? rf.name : rf.kind === 'row' ? rf.field : rf.kind === 'agg' ? rf.col : null
    if (name !== null && !set.has(name)) { set.add(name); seen.push(name) }
  }
  return seen
}

/** FNV-1a 32-bit over UTF-16 code units (low byte then high byte) — byte-identical to the .NET `Fnv1a`. */
function fnv1a8(s: string): string {
  const prime = 16777619
  let h = 2166136261 >>> 0
  for (let i = 0; i < s.length; i++) {
    const cc = s.charCodeAt(i)
    h = (h ^ (cc & 0xff)) >>> 0
    h = Math.imul(h, prime) >>> 0
    h = (h ^ ((cc >> 8) & 0xff)) >>> 0
    h = Math.imul(h, prime) >>> 0
  }
  return (h >>> 0).toString(16).padStart(8, '0')
}
