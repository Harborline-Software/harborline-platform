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
import type { CompiledGraph, CompiledRule } from './compiler.js'
import type { RuleRef } from './grammar.js'
import { RuleInstance, type RuleRow } from './instance.js'
import {
  EvalBudget, RuleBudget, RuleEvalError, RulePending,
  err, refError, refPending, refResolved, unavailableAggregate,
  type EvalContext, type RefValue, type ValueResolver,
} from './eval-support.js'
import type { ContextAdapter, RuleEvalScope } from './context-adapter.js'
import { evaluate, isTruthy, toNumber } from './jsonlogic.js'
import { MoneyDecimal } from './money-decimal.js'

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
    if (name in this.instance.fields) {
      const v = this.instance.fields[name]
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
    const rows = this.instance.tables[section]
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
        case 'Visibility': return { outcome: { ruleId, target, outputType: 'Visibility', visibility: failClosedVisibility(rule.source.action) }, pending: true }
        case 'Options': return { outcome: { ruleId, target, outputType: 'Options', options: { state: 'Pending' } }, pending: true }
        default: return { outcome: { ruleId, target, outputType: 'Presentation', presentation: {} }, pending: true }
      }
    }
    if (e instanceof RuleEvalError) {
      switch (rule.outputType) {
        case 'Value': return { outcome: { ruleId, target, outputType: 'Value', value: { state: 'Error', error: e.error } }, pending: false }
        case 'Validity': return { outcome: { ruleId, target, outputType: 'Validity', validity: { ok: false, error: e.error } }, pending: false }
        case 'Visibility': return { outcome: { ruleId, target, outputType: 'Visibility', visibility: failClosedVisibility(rule.source.action) }, pending: false }
        case 'Options': return { outcome: { ruleId, target, outputType: 'Options', options: { state: 'Error', error: e.error } }, pending: false }
        default: return { outcome: { ruleId, target, outputType: 'Presentation', presentation: { severity: 'error' } }, pending: false }
      }
    }
    throw e // budget / timeout propagate to abort the whole instance
  }
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

export class FormRuleGraph {
  private instance = new RuleInstance()
  private order: ComputedCell[] = []
  private cells = new Map<string, ComputedCell>()
  private deps = new Map<string, Set<string>>()
  private dependents = new Map<string, Set<string>>()
  // Broad reverse index: ANY referenced cell key (raw field OR computed) -> computed cells that read it.
  // Used by the reactive path so a change to a RAW field still dirties its dependents.
  private readers = new Map<string, Set<string>>()
  private plans: OutcomePlan[] = []
  private values = new Map<string, ComputedValue>()
  private outcomes = new Map<string, RuleOutcome>()

  constructor(
    readonly compiled: CompiledGraph,
    private readonly limits: RuleEngineLimits = DEFAULT_LIMITS,
    private readonly clock: () => Date = () => new Date(),
  ) {}

  evaluateInstance(instance: RuleInstance, signal?: AbortSignal): RuleEvaluationResult {
    this.instance = instance
    return this.buildAndEvaluate(signal)
  }

  reevaluate(fieldName: string, newValue: Json, signal?: AbortSignal): RuleEvaluationResult {
    this.instance.fields[fieldName] = newValue
    const changedKey = cell.field(fieldName)

    const dirty = new Set<string>()
    const queue = [changedKey]
    while (queue.length > 0) {
      const node = queue.shift()!
      for (const d of this.readers.get(node) ?? []) {
        if (!dirty.has(d)) { dirty.add(d); queue.push(d) }
      }
    }

    const budget = new EvalBudget(this.limits, signal)
    const adapter = new FormContextAdapter(this.values, this.instance) // ADR 0146 D3 context seam
    try {
      for (const c of this.order) if (dirty.has(c.key)) this.values.set(c.key, this.evalComputedCell(c, budget, adapter))
      const touched = new Set(dirty)
      touched.add(changedKey)
      for (const plan of this.plans) {
        if ([...plan.reads].some((r) => touched.has(r)) || dirty.has(plan.target)) {
          this.outcomes.set(plan.key, this.buildPlanOutcome(plan, budget, adapter))
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

  addRow(section: string, row: RuleRow, signal?: AbortSignal): RuleEvaluationResult {
    if (!this.instance.tables[section]) this.instance.tables[section] = []
    this.instance.tables[section].push(row)
    return this.buildAndEvaluate(signal)
  }

  removeRow(section: string, rowId: string, signal?: AbortSignal): RuleEvaluationResult {
    const rows = this.instance.tables[section]
    if (rows) this.instance.tables[section] = rows.filter((r) => r.id !== rowId)
    return this.buildAndEvaluate(signal)
  }

  private buildAndEvaluate(signal?: AbortSignal): RuleEvaluationResult {
    const preflight = this.preflightBounds()
    if (preflight !== null) return this.failClosed(preflight)
    this.build()
    if (this.cells.size > this.limits.maxGraphNodes) return this.failClosed(Codes.graphTooLarge)

    const budget = new EvalBudget(this.limits, signal)
    this.values = new Map()
    this.outcomes = new Map()
    const adapter = new FormContextAdapter(this.values, this.instance) // ADR 0146 D3 context seam
    try {
      for (const c of this.order) this.values.set(c.key, this.evalComputedCell(c, budget, adapter))
      for (const plan of this.plans) this.outcomes.set(plan.key, this.buildPlanOutcome(plan, budget, adapter))
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
    for (const rule of this.compiled.rules) {
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
    this.order = []
    this.plans = []

    for (const rule of this.compiled.rules) {
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
        const rows = this.instance.tables[c.aggFold.section] ?? []
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
    return this.instance.tables[section] ?? []
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

  private evalComputedCell(c: ComputedCell, budget: EvalBudget, adapter: ContextAdapter): ComputedValue {
    if (c.cyclic) return { state: 'Error', error: err(Codes.cycle, 'cell', c.key) }
    if (c.aggFold) return this.foldAggregate(c.aggFold.fn, c.aggFold.section, c.aggFold.col, budget)

    const resolver = adapter.createResolver({ rowSection: c.rule!.rowSection, rowId: c.rowId })
    const ctx: EvalContext = { resolver, now: this.clock(), budget }
    try {
      return { state: 'Resolved', value: evaluate(c.rule!.ast as Json, ctx) }
    } catch (e) {
      if (e instanceof RulePending) return { state: 'Pending' }
      if (e instanceof RuleEvalError) return { state: 'Error', error: e.error }
      throw e
    }
  }

  private buildPlanOutcome(plan: OutcomePlan, budget: EvalBudget, adapter: ContextAdapter): RuleOutcome {
    if (plan.isComputeValue) {
      const cv = this.values.get(plan.target) ?? { state: 'Resolved', value: null }
      return { ruleId: plan.rule.source.id, target: plan.target, outputType: 'Value', value: cv }
    }
    const resolver = adapter.createResolver({ rowSection: plan.rule.rowSection, rowId: plan.rowId })
    const ctx: EvalContext = { resolver, now: this.clock(), budget }
    return buildOutcome(plan.rule, plan.target, ctx).outcome
  }

  private foldAggregate(fn: string, section: string, col: string, budget: EvalBudget): ComputedValue {
    const rows = this.instance.tables[section] ?? []
    if (rows.length > this.limits.maxTableRowsPerAggregate) return { state: 'Error', error: err(Codes.tableTooLarge, 'section', section) }

    const values: Json[] = []
    for (const row of rows) {
      budget.charge()
      const rowKey = cell.row(section, row.id, col)
      let cv: ComputedValue
      const computed = this.values.get(rowKey)
      if (computed) cv = computed
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
    const validations: RuleOutcome[] = []
    const options = new Map<string, OptionsOutcome>()
    let hasPending = [...this.values.values()].some((v) => v.state === 'Pending')

    for (const outcome of this.outcomes.values()) {
      if (outcome.outputType === 'Visibility') {
        const prev = visibility.get(outcome.target) ?? { visible: true, required: false, readOnly: false }
        visibility.set(outcome.target, merge(prev, outcome.visibility!))
      } else if (outcome.outputType === 'Validity' && outcome.validity && !outcome.validity.ok) {
        validations.push(outcome)
      } else if (outcome.outputType === 'Value' && outcome.value?.state === 'Pending') {
        hasPending = true
      } else if (outcome.outputType === 'Options') {
        // Last-writer-wins per cell (multiple set-options on one field is an authoring smell).
        options.set(outcome.target, outcome.options!)
      }
    }

    const hasErrorValue = [...this.values.values()].some((v) => v.state === 'Error')
    return {
      byRule: new Map(this.outcomes),
      values: new Map(this.values),
      visibility,
      validations,
      options,
      hasPending,
      isSaveBlocked: validations.length > 0 || hasPending || hasErrorValue,
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
}

function merge(a: VisibilityState, b: VisibilityState): VisibilityState {
  return { visible: a.visible && b.visible, required: a.required || b.required, readOnly: a.readOnly || b.readOnly }
}

// re-export RuleError for consumers that catch eval errors
export type { RuleError }
