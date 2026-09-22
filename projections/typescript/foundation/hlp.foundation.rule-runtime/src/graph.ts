/**
 * The form orchestrator (SPINE-1 §2, §5.2) — TS port of `FormRuleGraph`. The reactive
 * client tier: builds the instance dependency graph, topo-sorts (Kahn), evaluates value
 * cells with Error/Pending propagation, and reactively re-evaluates the transitive
 * dependents on a single-cell change or a child-table row add/remove.
 */
import { Codes } from './codes.js'
import { cell } from './model.js'
import type {
  ComputedValue, Json, OptionsOutcome, RuleActionKind, RuleError, RuleOutcome, VisibilityState,
} from './model.js'
import { DEFAULT_LIMITS, type RuleEngineLimits } from './limits.js'
import { ownedCompiledRulesOf, type CompiledGraph, type CompiledRule } from './compiler.js'
import type { RuleRef } from './grammar.js'
import { detachJson, ownedInstanceDataOf, ownedRowOf, ownedValueOf, RuleInstance, type RuleRow } from './instance.js'
import { assertBoundedMemberName, INPUT_MAX_DEPTH, INPUT_MAX_NODES, INPUT_MAX_UTF8_BYTES, JsonStringifyByteCounter } from './input-envelope.js'
import {
  EvalBudget, RuleBudget, RuleEvalError, RulePending,
  err, refError, refPending, refResolved, unavailableAggregate,
  type EvalContext, type RefValue, type ValueResolver,
} from './eval-support.js'
import type { ContextAdapter, RuleEvalScope } from './context-adapter.js'
import { evaluate, isTruthy, toNumber } from './jsonlogic.js'
import { MoneyDecimal } from './money-decimal.js'

const computedExports = new WeakMap<object, ComputedValue>()
const outcomeExports = new WeakMap<object, RuleOutcome>()

export interface RuleEvaluationResult {
  byRule: Map<string, RuleOutcome>
  values: Map<string, ComputedValue>
  visibility: Map<string, VisibilityState>
  validations: RuleOutcome[]
  /** Available-options outcome per choice-field cell (`set-options` rules; ADR 0146 D2). Empty when none ran. */
  options: Map<string, OptionsOutcome>
  hasPending: boolean
  /** Fail-closed save gate (SPINE-1 §1.3, §5.3): any failing validity, errored value, or pending value. */
  isSaveBlocked: boolean
}

const isPendingSentinel = (n: Json): boolean =>
  typeof n === 'object' && n !== null && !Array.isArray(n) &&
  Object.keys(n).length === 1 && (n as Record<string, Json>)['@pending'] === true

const usesClock = (ast: Json): boolean => {
  if (typeof ast !== 'object' || ast === null || Array.isArray(ast)) return false
  const entries = Object.entries(ast)
  if (entries.length !== 1) return false
  const [op, raw] = entries[0]
  if (op === 'date.today') return true
  return Array.isArray(raw) ? raw.some(usesClock) : usesClock(raw)
}

class CellResolver implements ValueResolver {
  constructor(
    private readonly computed: Map<string, ComputedValue>,
    private readonly instance: RuleInstance,
    private readonly rowSection: string | null = null,
    private readonly rowId: string | null = null,
  ) {}

  resolveVar(path: string): RefValue {
    if (path.startsWith('row.')) {
      if (this.rowSection === null || this.rowId === null) return refError(err(Codes.badReference, 'path', path))
      const field = path.slice('row.'.length)
      const key = cell.row(this.rowSection, this.rowId, field)
      const cv = this.computed.get(key)
      if (cv) return fromComputed(cv, key)
      return this.rawRowField(this.rowSection, this.rowId, field)
    }
    const name = path.startsWith('field.') ? path.slice('field.'.length) : path
    const key = cell.field(name)
    const cv = this.computed.get(key)
    if (cv) return fromComputed(cv, key)
    const fields = ownedInstanceDataOf(this.instance)!.fields
    if (name in fields) {
      const v = fields[name]
      return isPendingSentinel(v) ? refPending : refResolved(v)
    }
    return refResolved(null)
  }

  resolveAgg(fn: string, section: string, col: string): RefValue {
    const key = cell.agg(section, fn, col)
    const cv = this.computed.get(key)
    // An aggregate the graph never computed is unavailable data, not a value (ticket 162):
    // resolving it to null fabricated a result the preview could not compute. Refuse with the
    // one shared bad-reference shape. Since the compiler now refuses agg nodes it cannot
    // statically register, every compiled agg has a fold cell — this branch is defense-in-depth.
    return cv ? fromComputed(cv, key) : unavailableAggregate(fn, section, col)
  }

  private rawRowField(section: string, rowId: string, field: string): RefValue {
    const rows = ownedInstanceDataOf(this.instance)!.tables[section]
    const row = rows?.find((r) => r.id === rowId)
    if (row && field in row.fields) {
      const v = row.fields[field]
      return isPendingSentinel(v) ? refPending : refResolved(v)
    }
    return refResolved(null)
  }
}

function fromComputed(cv: ComputedValue, key: string): RefValue {
  if (cv.state === 'Resolved') return refResolved(cv.value)
  if (cv.state === 'Pending') return refPending
  return refError(err(Codes.upstreamError, 'cell', key))
}

/**
 * The forms context as the first {@link ContextAdapter} implementation (ADR 0146 D3). Maps a
 * bound `RuleInstance` plus the graph's already-evaluated computed cells into a `CellResolver`
 * for the requested scope. Behaviour-neutral: the reactive form graph obtains its per-cell /
 * per-row resolvers through this seam instead of constructing `CellResolver` inline. The
 * computed-cell map is held by reference — filled live as the graph evaluates in topo order —
 * so a resolver produced mid-evaluation reads the up-to-date upstream cells, as before.
 */
class FormContextAdapter implements ContextAdapter {
  constructor(
    private readonly computed: Map<string, ComputedValue>,
    private readonly instance: RuleInstance,
  ) {}

  createResolver(scope: RuleEvalScope): ValueResolver {
    return new CellResolver(this.computed, this.instance, scope.rowSection, scope.rowId)
  }
}

// ── per-rule outcome builder ─────────────────────────────────────────────────

function buildOutcome(rule: CompiledRule, target: string, ctx: EvalContext): { outcome: RuleOutcome; pending: boolean } {
  const ruleId = rule.source.id
  try {
    const v = evaluate(rule.ast as Json, ctx)
    switch (rule.outputType) {
      case 'Value':
        return { outcome: { ruleId, target, outputType: 'Value', value: { state: 'Resolved', value: v } }, pending: false }
      case 'Validity':
        return { outcome: { ruleId, target, outputType: 'Validity', validity: isTruthy(v) ? { ok: true } : { ok: false, error: err(ruleId) } }, pending: false }
      case 'Visibility':
        return { outcome: { ruleId, target, outputType: 'Visibility', visibility: visibilityFor(rule.source.action, isTruthy(v)) }, pending: false }
      case 'Options':
        return { outcome: { ruleId, target, outputType: 'Options', options: optionsFor(v) }, pending: false }
      default:
        return { outcome: { ruleId, target, outputType: 'Presentation', presentation: isTruthy(v) ? presentationFor(rule) : {} }, pending: false }
    }
  } catch (e) {
    if (e instanceof RulePending) {
      switch (rule.outputType) {
        case 'Value': return { outcome: { ruleId, target, outputType: 'Value', value: { state: 'Pending' } }, pending: true }
        case 'Validity': return { outcome: { ruleId, target, outputType: 'Validity', validity: { ok: false, error: err(Codes.pendingAtSave) } }, pending: true }
        case 'Visibility':
          return rule.source.action === 'Required'
            ? { outcome: { ruleId, target, outputType: 'Validity', validity: { ok: false, error: err(Codes.pendingAtSave, 'rule', ruleId) } }, pending: true }
            : { outcome: { ruleId, target, outputType: 'Visibility', visibility: failClosedVisibility(rule.source.action) }, pending: true }
        case 'Options': return { outcome: { ruleId, target, outputType: 'Options', options: { state: 'Pending' } }, pending: true }
        default: return { outcome: { ruleId, target, outputType: 'Presentation', presentation: {} }, pending: true }
      }
    }
    if (e instanceof RuleEvalError) {
      switch (rule.outputType) {
        case 'Value': return { outcome: { ruleId, target, outputType: 'Value', value: { state: 'Error', error: e.error } }, pending: false }
        case 'Validity': return { outcome: { ruleId, target, outputType: 'Validity', validity: { ok: false, error: e.error } }, pending: false }
        case 'Visibility':
          return rule.source.action === 'Required'
            ? { outcome: { ruleId, target, outputType: 'Validity', validity: { ok: false, error: withRule(e.error, ruleId) } }, pending: false }
            : { outcome: { ruleId, target, outputType: 'Visibility', visibility: failClosedVisibility(rule.source.action) }, pending: false }
        case 'Options': return { outcome: { ruleId, target, outputType: 'Options', options: { state: 'Error', error: e.error } }, pending: false }
        default: return { outcome: { ruleId, target, outputType: 'Presentation', presentation: { severity: 'error' } }, pending: false }
      }
    }
    throw e // budget / timeout propagate to abort the whole instance
  }
}

function withRule(error: RuleError, ruleId: string): RuleError {
  return 'rule' in error.params ? error : { code: error.code, params: { ...error.params, rule: ruleId } }
}

function visibilityFor(action: RuleActionKind, result: boolean): VisibilityState {
  if (action === 'Required') return { visible: true, required: result, readOnly: false }
  if (action === 'ReadOnly') return { visible: true, required: false, readOnly: result }
  return { visible: result, required: false, readOnly: false }
}

// A set-options rule's expression must evaluate to a JSON array; any other type fails closed (ADR 0146 D2).
function optionsFor(v: Json): OptionsOutcome {
  return Array.isArray(v) ? { state: 'Resolved', options: v } : { state: 'Error', error: err(Codes.optionsNotArray) }
}

function failClosedVisibility(action: RuleActionKind): VisibilityState {
  if (action === 'Required') return { visible: true, required: false, readOnly: false }
  if (action === 'ReadOnly') return { visible: true, required: false, readOnly: true }
  return { visible: false, required: false, readOnly: false }
}

function presentationFor(rule: CompiledRule): RuleOutcome['presentation'] {
  const hint = rule.source.presentation
  if (!hint) return {}
  return { severity: hint.severity ?? null, badge: hint.badge, styleToken: hint.styleToken }
}

// ── graph internals ──────────────────────────────────────────────────────────

interface ComputedCell {
  key: string
  rule: CompiledRule | null
  rowId: string | null
  aggFold: { fn: string; section: string; col: string } | null
  cyclic: boolean
}

interface OutcomePlan {
  key: string
  rule: CompiledRule
  target: string
  rowId: string | null
  reads: Set<string>
  isComputeValue: boolean
}

interface DemandState {
  active: Set<string>
  completed: Set<string>
  dirty: Set<string> | null
}

/**
 * Resolves through the graph's current evaluation generation before falling back to the
 * ordinary context resolver. This is deliberately an evaluator wrapper, rather than a
 * second interpreter: dynamic missing reads use the exact same JsonLogic evaluation path.
 */
class DemandResolver implements ValueResolver {
  constructor(
    private readonly fallback: ValueResolver,
    private readonly resolve: (key: string, fallback: () => RefValue) => RefValue,
    private readonly rowSection: string | null,
    private readonly rowId: string | null,
  ) {}

  resolveVar(path: string): RefValue {
    if (path.startsWith('row.')) {
      if (this.rowSection === null || this.rowId === null) return this.fallback.resolveVar(path)
      const key = cell.row(this.rowSection, this.rowId, path.slice('row.'.length))
      return this.resolve(key, () => this.fallback.resolveVar(path))
    }
    const name = path.startsWith('field.') ? path.slice('field.'.length) : path
    const key = cell.field(name)
    return this.resolve(key, () => this.fallback.resolveVar(path))
  }

  resolveAgg(fn: string, section: string, col: string): RefValue {
    const key = cell.agg(section, fn, col)
    return this.resolve(key, () => this.fallback.resolveAgg(fn, section, col))
  }
}

export class FormRuleGraph {
  private instance = RuleInstance.empty()
  private order: ComputedCell[] = []
  private cells = new Map<string, ComputedCell>()
  private deps = new Map<string, Set<string>>()
  private dependents = new Map<string, Set<string>>()
  // Broad reverse index: ANY referenced cell key (raw field OR computed) -> computed cells that read it.
  // Used by the reactive path so a change to a RAW field still dirties its dependents.
  private readers = new Map<string, Set<string>>()
  // Actual read reverse index, populated by the single evaluator for dynamic paths.
  // It intentionally remains separate from the finite static DAG.
  private dynamicReaders = new Map<string, Set<string>>()
  private actualCellReads = new Map<string, Set<string>>()
  private actualPlanReads = new Map<string, Set<string>>()
  private plans: OutcomePlan[] = []
  private values = new Map<string, ComputedValue>()
  private outcomes = new Map<string, RuleOutcome>()
  private evaluationInstant: Date | null = null

  readonly compiled: CompiledGraph
  private readonly compiledRules: readonly CompiledRule[]

  constructor(
    compiled: CompiledGraph,
    private readonly clock: () => Date,
    private readonly limits: RuleEngineLimits = DEFAULT_LIMITS,
  ) {
    const ownedRules = ownedCompiledRulesOf(compiled)
    if (!ownedRules) throw new Error(Codes.contextSnapshotRequired)
    this.compiled = compiled
    this.compiledRules = ownedRules
    if (typeof clock !== 'function') throw new TypeError('FormRuleGraph requires a caller-supplied clock')
  }

  evaluateInstance(instance: RuleInstance, signal?: AbortSignal): RuleEvaluationResult {
    if (!RuleInstance.isRuntimeOwned(instance)) throw new Error(Codes.contextSnapshotRequired)
    this.instance = RuleInstance.cloneOwned(instance)
    return this.buildAndEvaluate(signal)
  }

  reevaluate(fieldName: string, newValue: import('./instance.js').RuleValueSnapshot, signal?: AbortSignal): RuleEvaluationResult {
    try { assertBoundedMemberName(fieldName, 'rule field name') } catch { return this.failClosed(Codes.inputTooLarge) }
    const captured = ownedValueOf(newValue)
    if (captured === undefined) throw new Error(Codes.contextSnapshotRequired)
    const instanceData = ownedInstanceDataOf(this.instance)!
    const hadPrevious = Object.hasOwn(instanceData.fields, fieldName)
    const previous = instanceData.fields[fieldName]
    instanceData.fields[fieldName] = captured
    try { assertInstanceEnvelope(instanceData.fields, instanceData.tables) }
    catch (error) {
      if (hadPrevious) instanceData.fields[fieldName] = previous
      else delete instanceData.fields[fieldName]
      if (error instanceof RangeError) return this.failClosed(Codes.inputTooLarge)
      throw error
    }
    const changedKey = cell.field(fieldName)

    const dirty = new Set<string>()
    const queue = [changedKey]
    while (queue.length > 0) {
      const node = queue.shift()!
      for (const d of this.readersOf(node)) {
        if (!dirty.has(d)) { dirty.add(d); queue.push(d) }
      }
    }

    // The business clock is an explicit graph input. Seed just its readers and their transitive
    // dependents through the existing index, preserving referential identity elsewhere.
    for (const c of this.order) if (c.rule !== null && usesClock(c.rule.ast)) {
      if (!dirty.has(c.key)) { dirty.add(c.key); queue.push(c.key) }
    }
    while (queue.length > 0) {
      const node = queue.shift()!
      for (const d of this.readersOf(node)) {
        if (!dirty.has(d)) { dirty.add(d); queue.push(d) }
      }
    }

    this.evaluationInstant = this.clock()
    const budget = new EvalBudget(this.limits, signal)
    const adapter = new FormContextAdapter(this.values, this.instance) // ADR 0146 D3 context seam
    const demand: DemandState = { active: new Set(), completed: new Set(), dirty }
    try {
      for (const c of this.order) if (dirty.has(c.key)) this.values.set(c.key, this.evaluateDemand(c, demand, budget, adapter))
      const touched = new Set(dirty)
      touched.add(changedKey)
      for (const plan of this.plans) {
        if (usesClock(plan.rule.ast) || [...plan.reads].some((r) => touched.has(r)) || this.overlaps(this.actualPlanReads.get(plan.key), touched) || dirty.has(plan.target)) {
          this.outcomes.set(plan.key, this.buildPlanOutcome(plan, demand, budget, adapter))
        }
      }
    } catch (e) {
      if (e instanceof RuleBudget) return this.failClosed(Codes.budgetExceeded)
      // RuleTimeout is a non-authoritative liveness fault (D1 ratification): it PROPAGATES (via the
      // rethrow below) rather than becoming a divergent `rule.timeout` outcome.
      throw e
    }
    return this.project()
  }

  addRow(section: string, row: import('./instance.js').RuleRowSnapshot, signal?: AbortSignal): RuleEvaluationResult {
    try { assertBoundedMemberName(section, 'rule section name') } catch { return this.failClosed(Codes.inputTooLarge) }
    const captured = ownedRowOf(row)
    if (!captured) throw new Error(Codes.contextSnapshotRequired)
    const tables = ownedInstanceDataOf(this.instance)!.tables
    const rows = tables[section] ?? []
    try { assertInstanceEnvelope(ownedInstanceDataOf(this.instance)!.fields, tables, section, captured) }
    catch (error) {
      if (error instanceof RangeError) return this.failClosed(Codes.inputTooLarge)
      throw error
    }
    if (rows.length >= this.limits.maxTableRowsPerAggregate && this.hasAggregateForSection(section)) return this.refuseTableRow(section)
    if (!tables[section]) tables[section] = rows
    rows.push(captured)
    return this.buildAndEvaluate(signal)
  }

  removeRow(section: string, rowId: string, signal?: AbortSignal): RuleEvaluationResult {
    try {
      assertBoundedMemberName(section, 'rule section name')
      assertBoundedMemberName(rowId, 'rule row id')
    } catch { return this.failClosed(Codes.inputTooLarge) }
    const tables = ownedInstanceDataOf(this.instance)!.tables
    const rows = tables[section]
    if (rows) tables[section] = rows.filter((r) => r.id !== rowId)
    return this.buildAndEvaluate(signal)
  }

  private buildAndEvaluate(signal?: AbortSignal): RuleEvaluationResult {
    const preflight = this.preflightBounds()
    if (preflight !== null) return this.failClosed(preflight)
    this.build()
    if (this.cells.size > this.limits.maxGraphNodes) return this.failClosed(Codes.graphTooLarge)

    this.evaluationInstant = this.clock()
    const budget = new EvalBudget(this.limits, signal)
    this.values = new Map()
    this.outcomes = new Map()
    const adapter = new FormContextAdapter(this.values, this.instance) // ADR 0146 D3 context seam
    const demand: DemandState = { active: new Set(), completed: new Set(), dirty: null }
    try {
      for (const c of this.order) this.values.set(c.key, this.evaluateDemand(c, demand, budget, adapter))
      for (const plan of this.plans) this.outcomes.set(plan.key, this.buildPlanOutcome(plan, demand, budget, adapter))
    } catch (e) {
      if (e instanceof RuleBudget) return this.failClosed(Codes.budgetExceeded)
      // RuleTimeout is a non-authoritative liveness fault (D1 ratification 2026-07-01): it PROPAGATES
      // (rethrown below) so the wall-clock can never emit a divergent evaluation outcome. The op-budget
      // above is the sole authoritative fail-closed bound.
      throw e
    }
    return this.project()
  }

  private preflightBounds(): string | null {
    // F3: project cell count BEFORE build() expands Row rules over user-controlled rows + Kahn-sorts.
    // F8: a Compute rule scoped to Section/Schema addresses no form cell → fail closed (not a silent
    // no-op). Guards use the shared compiler directly (not this form graph), so they are unaffected.
    let projectedCells = 0
    const aggCells = new Set<string>()
    for (const rule of this.compiledRules) {
      if (rule.source.action === 'Compute') {
        if (rule.source.scope !== 'Field' && rule.source.scope !== 'Row' && rule.source.scope !== 'Table') {
          return Codes.computeScopeInvalid
        }
        projectedCells += rule.source.scope === 'Row' ? this.rowsOf(rule.rowSection!).length : 1
      }
      for (const ref of rule.references) {
        if (ref.kind === 'agg') aggCells.add(cell.agg(ref.section, ref.fn, ref.col))
      }
      if (projectedCells > this.limits.maxGraphNodes) return Codes.graphTooLarge
    }
    return projectedCells + aggCells.size > this.limits.maxGraphNodes ? Codes.graphTooLarge : null
  }

  private build(): void {
    this.cells = new Map()
    this.deps = new Map()
    this.dependents = new Map()
    this.readers = new Map()
    this.dynamicReaders = new Map()
    this.actualCellReads = new Map()
    this.actualPlanReads = new Map()
    this.order = []
    this.plans = []

    for (const rule of this.compiledRules) {
      if (rule.source.action === 'Compute') this.addComputeCellsAndPlans(rule)
      else this.addNonComputePlans(rule)

      for (const ref of rule.references) {
        if (ref.kind === 'agg') {
          const key = cell.agg(ref.section, ref.fn, ref.col)
          if (!this.cells.has(key)) {
            this.cells.set(key, { key, rule: null, rowId: null, aggFold: { fn: ref.fn, section: ref.section, col: ref.col }, cyclic: false })
          }
        }
      }
    }

    for (const c of this.cells.values()) { this.deps.set(c.key, new Set()); this.dependents.set(c.key, new Set()) }
    for (const c of this.cells.values()) {
      if (c.aggFold) {
        const rows = ownedInstanceDataOf(this.instance)!.tables[c.aggFold.section] ?? []
        // F3: bound the un-budgeted edge walk — an over-cap table fails closed in foldAggregate.
        if (rows.length <= this.limits.maxTableRowsPerAggregate) {
          for (const row of rows) {
            const rowKey = cell.row(c.aggFold.section, row.id, c.aggFold.col)
            this.addReader(rowKey, c.key)
            if (this.cells.has(rowKey)) this.edge(c.key, rowKey)
          }
        }
      } else if (c.rule) {
        for (const ref of c.rule.references) {
          const depKey = this.resolveRefKey(ref, c.rule, c.rowId)
          if (depKey !== null) {
            this.addReader(depKey, c.key)
            if (this.cells.has(depKey)) this.edge(c.key, depKey)
          }
        }

      }
    }

    this.topoSort()
  }

  private addComputeCellsAndPlans(rule: CompiledRule): void {
    switch (rule.source.scope) {
      case 'Field': {
        const key = cell.field(rule.source.scopeTarget)
        this.cells.set(key, { key, rule, rowId: null, aggFold: null, cyclic: false })
        this.plans.push(this.makePlan(rule, key, null))
        break
      }
      case 'Row':
        for (const row of this.rowsOf(rule.rowSection!)) {
          const key = cell.row(rule.rowSection!, row.id, rule.rowField!)
          this.cells.set(key, { key, rule, rowId: row.id, aggFold: null, cyclic: false })
          this.plans.push(this.makePlan(rule, key, row.id))
        }
        break
      case 'Table': {
        const key = rule.staticTarget!
        this.cells.set(key, { key, rule, rowId: null, aggFold: null, cyclic: false })
        this.plans.push(this.makePlan(rule, key, null))
        break
      }
    }
  }

  private addNonComputePlans(rule: CompiledRule): void {
    if (rule.source.scope === 'Row') {
      for (const row of this.rowsOf(rule.rowSection!)) {
        this.plans.push(this.makePlan(rule, cell.row(rule.rowSection!, row.id, rule.rowField!), row.id))
      }
    } else {
      this.plans.push(this.makePlan(rule, rule.staticTarget!, null))
    }
  }

  private makePlan(rule: CompiledRule, target: string, rowId: string | null): OutcomePlan {
    const key = rowId === null ? rule.source.id : `${rule.source.id}#${rowId}`
    const reads = new Set<string>()
    for (const ref of rule.references) {
      const k = this.resolveRefKey(ref, rule, rowId)
      if (k !== null) reads.add(k)
    }
    return { key, rule, target, rowId, reads, isComputeValue: rule.source.action === 'Compute' }
  }

  private resolveRefKey(ref: RuleRef, rule: CompiledRule, rowId: string | null): string | null {
    if (ref.kind === 'field') return cell.field(ref.name)
    if (ref.kind === 'row' && rule.rowSection !== null && rowId !== null) return cell.row(rule.rowSection, rowId, ref.field)
    if (ref.kind === 'agg') return cell.agg(ref.section, ref.fn, ref.col)
    return null
  }

  private rowsOf(section: string): RuleRow[] {
    return ownedInstanceDataOf(this.instance)!.tables[section] ?? []
  }

  private edge(from: string, dep: string): void {
    this.deps.get(from)!.add(dep)
    this.dependents.get(dep)!.add(from)
  }

  private addReader(key: string, reader: string): void {
    let set = this.readers.get(key)
    if (!set) { set = new Set(); this.readers.set(key, set) }
    set.add(reader)
  }

  private readersOf(key: string): Set<string> {
    return new Set([...(this.readers.get(key) ?? []), ...(this.dynamicReaders.get(key) ?? [])])
  }

  private overlaps(reads: Set<string> | undefined, touched: Set<string>): boolean {
    return reads !== undefined && [...reads].some((key) => touched.has(key))
  }

  private clearActualCellReads(owner: string): void {
    for (const key of this.actualCellReads.get(owner) ?? []) {
      const readers = this.dynamicReaders.get(key)
      readers?.delete(owner)
      if (readers?.size === 0) this.dynamicReaders.delete(key)
    }
    this.actualCellReads.delete(owner)
  }

  private recordActualCellRead(owner: string, key: string): void {
    let reads = this.actualCellReads.get(owner)
    if (!reads) { reads = new Set(); this.actualCellReads.set(owner, reads) }
    reads.add(key)
    let readers = this.dynamicReaders.get(key)
    if (!readers) { readers = new Set(); this.dynamicReaders.set(key, readers) }
    readers.add(owner)
  }

  private beginPlanReads(owner: string): void {
    this.actualPlanReads.set(owner, new Set())
  }

  private recordActualPlanRead(owner: string, key: string): void {
    const reads = this.actualPlanReads.get(owner) ?? new Set<string>()
    reads.add(key)
    this.actualPlanReads.set(owner, reads)
  }

  private topoSort(): void {
    const inDeg = new Map<string, number>()
    for (const k of this.cells.keys()) inDeg.set(k, this.deps.get(k)!.size)
    const ready: string[] = [...inDeg.entries()].filter(([, v]) => v === 0).map(([k]) => k)
    const ordered = new Set<string>()
    while (ready.length > 0) {
      const node = ready.shift()!
      this.order.push(this.cells.get(node)!)
      ordered.add(node)
      for (const dependent of this.dependents.get(node)!) {
        inDeg.set(dependent, inDeg.get(dependent)! - 1)
        if (inDeg.get(dependent) === 0) ready.push(dependent)
      }
    }
    for (const c of this.cells.values()) {
      if (!ordered.has(c.key)) this.order.push({ ...c, cyclic: true })
    }
  }

  private evaluateDemand(c: ComputedCell, demand: DemandState, budget: EvalBudget, adapter: ContextAdapter): ComputedValue {
    const stable = demand.dirty === null || !demand.dirty.has(c.key) ? this.values.get(c.key) : undefined
    if (stable) return stable
    const completed = this.values.get(c.key)
    if (demand.completed.has(c.key) && completed) return completed
    if (demand.active.has(c.key)) return { state: 'Error', error: err(Codes.cycle, 'cell', c.key) }
    // The active set is the parent chain, so before adding this cell its size is the
    // demand-edge depth. Match static compiler admission: depth zero permits a leaf.
    if (demand.active.size > this.limits.maxDependencyDepth) return { state: 'Error', error: err(Codes.budgetExceeded) }

    budget.charge()
    demand.active.add(c.key)
    this.clearActualCellReads(c.key)
    try {
      const value = this.evalComputedCell(c, demand, budget, adapter)
      this.values.set(c.key, value)
      demand.completed.add(c.key)
      return value
    } finally {
      demand.active.delete(c.key)
    }
  }

  private resolveDemand(owner: string, plan: boolean, key: string, fallback: () => RefValue, demand: DemandState, budget: EvalBudget, adapter: ContextAdapter): RefValue {
    if (plan) this.recordActualPlanRead(owner, key)
    else this.recordActualCellRead(owner, key)
    const target = this.cells.get(key)
    if (!target) return fallback()
    const value = this.evaluateDemand(target, demand, budget, adapter)
    if (value.state === 'Resolved') return refResolved(value.value ?? null)
    if (value.state === 'Pending') return refPending
    return value.error?.code === Codes.cycle ? refError(value.error) : refError(err(Codes.upstreamError, 'cell', key))
  }

  private evalComputedCell(c: ComputedCell, demand: DemandState, budget: EvalBudget, adapter: ContextAdapter): ComputedValue {
    if (c.cyclic) return { state: 'Error', error: err(Codes.cycle, 'cell', c.key) }
    if (c.aggFold) return this.foldAggregate(c.aggFold.fn, c.aggFold.section, c.aggFold.col, demand, budget, adapter)

    const scope = { rowSection: c.rule!.rowSection, rowId: c.rowId }
    const fallback = adapter.createResolver(scope)
    const resolver = new DemandResolver(
      fallback,
      (key, next) => this.resolveDemand(c.key, false, key, next, demand, budget, adapter),
      scope.rowSection,
      scope.rowId,
    )
    const ctx: EvalContext = { resolver, now: this.evaluationInstant!, budget }
    try {
      return { state: 'Resolved', value: evaluate(c.rule!.ast as Json, ctx) }
    } catch (e) {
      if (e instanceof RulePending) return { state: 'Pending' }
      if (e instanceof RuleEvalError) return { state: 'Error', error: e.error }
      throw e
    }
  }

  private buildPlanOutcome(plan: OutcomePlan, demand: DemandState, budget: EvalBudget, adapter: ContextAdapter): RuleOutcome {
    if (plan.isComputeValue) {
      const cv = this.values.get(plan.target) ?? { state: 'Resolved', value: null }
      return { ruleId: plan.rule.source.id, target: plan.target, outputType: 'Value', value: cv }
    }
    this.beginPlanReads(plan.key)
    const scope = { rowSection: plan.rule.rowSection, rowId: plan.rowId }
    const fallback = adapter.createResolver(scope)
    const resolver = new DemandResolver(
      fallback,
      (key, next) => this.resolveDemand(plan.key, true, key, next, demand, budget, adapter),
      scope.rowSection,
      scope.rowId,
    )
    const ctx: EvalContext = { resolver, now: this.evaluationInstant!, budget }
    return buildOutcome(plan.rule, plan.target, ctx).outcome
  }

  private foldAggregate(fn: string, section: string, col: string, demand: DemandState, budget: EvalBudget, adapter: ContextAdapter): ComputedValue {
    const rows = ownedInstanceDataOf(this.instance)!.tables[section] ?? []
    if (rows.length > this.limits.maxTableRowsPerAggregate) return { state: 'Error', error: err(Codes.tableTooLarge, 'section', section) }

    const values: Json[] = []
    for (const row of rows) {
      budget.charge()
      const rowKey = cell.row(section, row.id, col)
      let cv: ComputedValue
      const rowCell = this.cells.get(rowKey)
      // A demanded aggregate can run before ordinary topo evaluation reaches this
      // row producer. Demand the current-generation cell; only an absent producer
      // is allowed to fall back to the raw row value.
      if (rowCell) cv = this.evaluateDemand(rowCell, demand, budget, adapter)
      else {
        const raw = col in row.fields ? row.fields[col] : null
        cv = isPendingSentinel(raw) ? { state: 'Pending' } : { state: 'Resolved', value: raw }
      }
      if (cv.state === 'Pending') return { state: 'Pending' }
      if (cv.state === 'Error') return { state: 'Error', error: err(Codes.upstreamError, 'cell', rowKey) }
      values.push(cv.value ?? null)
    }

    // A column whose every value is a decimal string is money — sum/min/max/avg must be exact
    // decimal, never IEEE double (finding F7). Number columns keep the numeric (double) fold.
    const allDecimalStrings = values.length > 0 && values.every((v) => typeof v === 'string')
    try {
      if (allDecimalStrings && (fn === 'sum' || fn === 'min' || fn === 'max' || fn === 'avg')) {
        return this.moneyAggregate(fn, values as string[], section)
      }
      switch (fn) {
        case 'count': return { state: 'Resolved', value: values.length }
        case 'sum': return { state: 'Resolved', value: values.reduce((a: number, v) => a + toNumber(v), 0) }
        case 'avg': return { state: 'Resolved', value: values.length === 0 ? 0 : values.reduce((a: number, v) => a + toNumber(v), 0) / values.length }
        case 'min': return { state: 'Resolved', value: values.length === 0 ? null : Math.min(...values.map(toNumber)) }
        case 'max': return { state: 'Resolved', value: values.length === 0 ? null : Math.max(...values.map(toNumber)) }
        case 'any': return { state: 'Resolved', value: values.some(isTruthy) }
        case 'all': return { state: 'Resolved', value: values.every(isTruthy) }
        default: return { state: 'Error', error: err(Codes.unknownOperator, 'agg', fn) }
      }
    } catch (e) {
      if (e instanceof RuleEvalError) return { state: 'Error', error: e.error }
      throw e
    }
  }

  private moneyAggregate(fn: string, values: string[], section: string): ComputedValue {
    // Exact decimal sum/min/max over a money column (finding F7). avg needs exact decimal division
    // (undefined in v1) → fail closed rather than silently use double.
    if (fn === 'avg') return { state: 'Error', error: err(Codes.moneyAggUnsupported, 'section', section) }
    try {
      let acc = MoneyDecimal.parse(values[0])
      for (let i = 1; i < values.length; i++) {
        const m = MoneyDecimal.parse(values[i])
        acc = fn === 'sum' ? acc.add(m) : fn === 'min' ? (m.compare(acc) < 0 ? m : acc) : (m.compare(acc) > 0 ? m : acc)
      }
      return { state: 'Resolved', value: acc.toCanonicalString() }
    } catch {
      return { state: 'Error', error: err(Codes.typeError, 'op', 'agg') }
    }
  }

  private project(): RuleEvaluationResult {
    const visibility = new Map<string, VisibilityState>()
    let hasPending = [...this.values.values()].some((v) => v.state === 'Pending')

    for (const outcome of this.outcomes.values()) {
      if (outcome.outputType === 'Visibility') {
        const prev = visibility.get(outcome.target) ?? { visible: true, required: false, readOnly: false }
        visibility.set(outcome.target, merge(prev, outcome.visibility!))
      } else if (outcome.outputType === 'Value' && outcome.value?.state === 'Pending') {
        hasPending = true
      }
    }

    const hasErrorValue = [...this.values.values()].some((v) => v.state === 'Error')
    const values = new Map([...this.values].map(([key, value]) => [key, detachComputedValue(value)]))
    const byRule = new Map([...this.outcomes].map(([key, outcome]) => [key, detachOutcome(outcome)]))
    const exportedValidations = [...byRule.values()].filter((outcome) => outcome.outputType === 'Validity' && outcome.validity && !outcome.validity.ok)
    const exportedOptions = new Map<string, OptionsOutcome>()
    for (const outcome of byRule.values()) if (outcome.outputType === 'Options' && outcome.options) exportedOptions.set(outcome.target, outcome.options)
    return {
      byRule,
      values,
      visibility,
      validations: exportedValidations,
      options: exportedOptions,
      hasPending,
      isSaveBlocked: exportedValidations.length > 0 || hasPending || hasErrorValue,
    }
  }

  private failClosed(code: string): RuleEvaluationResult {
    const synthetic: RuleOutcome = { ruleId: 'rule.engine', target: cell.schema(), outputType: 'Validity', validity: { ok: false, error: err(code) } }
    return {
      byRule: new Map([['rule.engine', synthetic]]),
      values: new Map(),
      visibility: new Map(),
      validations: [synthetic],
      options: new Map(),
      hasPending: false,
      isSaveBlocked: true,
    }
  }

  private refuseTableRow(section: string): RuleEvaluationResult {
    const accepted = this.project()
    const values = new Map(accepted.values)
    for (const key of values.keys()) {
      if (key.startsWith(`agg:${section}/`)) {
        values.set(key, { state: 'Error', error: err(Codes.tableTooLarge, 'section', section) })
      }
    }
    return { ...accepted, values, isSaveBlocked: true }
  }

  private hasAggregateForSection(section: string): boolean {
    return this.compiledRules.some(rule => rule.references.some(reference => reference.kind === 'agg' && reference.section === section))
  }
}

function detachComputedValue(value: ComputedValue): ComputedValue {
  const cached = computedExports.get(value)
  if (cached) return cached
  const detached = value.state === 'Resolved'
    ? Object.freeze({ ...value, value: detachJson(value.value ?? null) })
    : Object.freeze({ ...value, error: value.error ? Object.freeze({ ...value.error, params: Object.freeze({ ...value.error.params }) }) : undefined })
  computedExports.set(value, detached)
  return detached
}

function detachOutcome(outcome: RuleOutcome): RuleOutcome {
  const cached = outcomeExports.get(outcome)
  if (cached) return cached
  const detached = Object.freeze({
    ...outcome,
    value: outcome.value ? detachComputedValue(outcome.value) : undefined,
    options: outcome.options?.state === 'Resolved'
      ? { ...outcome.options, options: outcome.options.options?.map(detachJson) }
      : outcome.options ? { ...outcome.options } : undefined,
    validity: outcome.validity ? { ...outcome.validity, error: outcome.validity.error ? { ...outcome.validity.error, params: { ...outcome.validity.error.params } } : undefined } : undefined,
    visibility: outcome.visibility ? { ...outcome.visibility } : undefined,
    presentation: outcome.presentation ? Object.freeze({ ...outcome.presentation }) : undefined,
  })
  outcomeExports.set(outcome, detached)
  return detached
}

function assertInstanceEnvelope(fields: Record<string, Json>, tables: Record<string, RuleRow[]>, addedSection?: string, addedRow?: RuleRow): void {
  assertInstanceStructure(fields, tables, addedSection, addedRow)
  // Every source value was already captured. Count the prospective logical envelope
  // directly, including JSON.stringify punctuation and escapes, before graph mutation.
  const counter = new JsonStringifyByteCounter(INPUT_MAX_UTF8_BYTES, 'rule instance')
  counter.addPunctuation(1)
  let firstRootMember = true
  const writeRootMember = (name: string, writeValue: () => void): void => {
    if (!firstRootMember) counter.addPunctuation(1)
    counter.countString(name)
    counter.addPunctuation(1)
    writeValue()
    firstRootMember = false
  }
  for (const [name, value] of Object.entries(fields)) writeRootMember(name, () => counter.countValue(value))
  const tableEntries = Object.entries(tables)
  for (const [section, existingRows] of tableEntries) {
    writeRootMember(section, () => countRows(existingRows, counter, section === addedSection ? addedRow : undefined))
  }
  if (addedSection && !Object.hasOwn(tables, addedSection) && addedRow) writeRootMember(addedSection, () => countRows([], counter, addedRow))
  counter.addPunctuation(1)
}

function assertInstanceStructure(fields: Record<string, Json>, tables: Record<string, RuleRow[]>, addedSection?: string, addedRow?: RuleRow): void {
  let nodes = 1 // root object
  const addNode = (): void => {
    if (++nodes > INPUT_MAX_NODES) throw new RangeError('rule instance exceeds node ceiling')
  }
  const visit = (value: Json, containerDepth: number): void => {
    addNode()
    if (value === null || typeof value !== 'object') return
    if (containerDepth + 1 > INPUT_MAX_DEPTH) throw new RangeError('rule instance exceeds depth ceiling')
    if (Array.isArray(value)) {
      for (const child of value) visit(child, containerDepth + 1)
      return
    }
    for (const [name, child] of Object.entries(value)) {
      assertBoundedMemberName(name, 'rule instance member name')
      visit(child, containerDepth + 1)
    }
  }
  for (const [name, value] of Object.entries(fields)) {
    assertBoundedMemberName(name, 'rule instance member name')
    visit(value, 1)
  }
  const visitRows = (section: string, rows: RuleRow[], appendedRow?: RuleRow): void => {
    assertBoundedMemberName(section, 'rule instance member name')
    addNode() // table array
    if (2 > INPUT_MAX_DEPTH) throw new RangeError('rule instance exceeds depth ceiling')
    const visitRow = (row: RuleRow): void => {
      addNode() // row object
      if (3 > INPUT_MAX_DEPTH) throw new RangeError('rule instance exceeds depth ceiling')
      assertBoundedMemberName(row.id, 'rule row id')
      if (row.hasExplicitId) addNode()
      for (const [name, value] of Object.entries(row.fields)) {
        assertBoundedMemberName(name, 'rule instance member name')
        visit(value, 3)
      }
    }
    for (const row of rows) visitRow(row)
    if (appendedRow) visitRow(appendedRow)
  }
  for (const [section, existingRows] of Object.entries(tables)) visitRows(section, existingRows, section === addedSection ? addedRow : undefined)
  if (addedSection && !Object.hasOwn(tables, addedSection) && addedRow) visitRows(addedSection, [], addedRow)
}

function countRows(rows: RuleRow[], counter: JsonStringifyByteCounter, appendedRow?: RuleRow): void {
  counter.addPunctuation(1)
  let firstRow = true
  const countRow = (row: RuleRow): void => {
    if (!firstRow) counter.addPunctuation(1)
    counter.addPunctuation(1)
    let firstMember = true
    const writeMember = (name: string, writeValue: () => void): void => {
      if (!firstMember) counter.addPunctuation(1)
      counter.countString(name)
      counter.addPunctuation(1)
      writeValue()
      firstMember = false
    }
    if (row.hasExplicitId) writeMember('_id', () => counter.countString(row.id))
    for (const [name, value] of Object.entries(row.fields)) writeMember(name, () => counter.countValue(value))
    counter.addPunctuation(1)
    firstRow = false
  }
  for (const row of rows) countRow(row)
  if (appendedRow) countRow(appendedRow)
  counter.addPunctuation(1)
}

function merge(a: VisibilityState, b: VisibilityState): VisibilityState {
  return { visible: a.visible && b.visible, required: a.required || b.required, readOnly: a.readOnly || b.readOnly }
}

// re-export RuleError for consumers that catch eval errors
export type { RuleError }
