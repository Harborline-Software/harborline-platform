import { Codes } from './codes.js'
import type { Json, RuleError, ValueState } from './model.js'
import type { RuleEngineLimits } from './limits.js'

/** The resolution of a referenced cell handed to the evaluator. */
export interface RefValue {
  state: ValueState
  value?: Json
  error?: RuleError
}

export const refResolved = (value: Json | undefined): RefValue => ({ state: 'Resolved', value })
export const refError = (error: RuleError): RefValue => ({ state: 'Error', error })
export const refPending: RefValue = { state: 'Pending' }

/**
 * The ONE bad-aggregate refusal shape in this tier (ticket 162): an aggregate the evaluation
 * context cannot provide refuses with `rule.bad_reference` carrying the address params
 * `{agg: "section/fn/col"}`. Both resolvers (the form graph's CellResolver and the guard
 * evaluator's ContextBagResolver) construct it HERE, so the param order cannot drift within
 * the tier; the shared corpus pins the shape across tiers.
 */
export const unavailableAggregate = (fn: string, section: string, col: string): RefValue =>
  refError(err(Codes.badReference, 'agg', `${section}/${fn}/${col}`))

/** Resolves `var` / `agg` references for one rule evaluation (SPINE-1 §2.2). */
export interface ValueResolver {
  resolveVar(path: string): RefValue
  resolveAgg(fn: string, section: string, col: string): RefValue
}

/** The shared step budget + liveness guard for one whole-instance evaluation (SPINE-1 §4). */
export class EvalBudget {
  steps = 0
  cancelled = false
  private readonly deadline: number
  constructor(readonly limits: RuleEngineLimits, private readonly signal?: AbortSignal) {
    // Advisory client-tier wall-clock (the .NET integrity tier holds the authoritative one): wire
    // the previously-vestigial `cancelled`/`wallClockMs` to a real deadline (finding F4). The optional
    // `signal` is the TS analog of the .NET `CancellationToken` — an explicit liveness/cancel seam.
    this.deadline = limits.wallClockMs > 0 ? Date.now() + limits.wallClockMs : Number.POSITIVE_INFINITY
  }

  private checkClock(): void {
    // The wall-clock / abort signal is a NON-authoritative liveness guard (D1 ratification 2026-07-01):
    // it throws RuleTimeout, which PROPAGATES as an infrastructure fault and never becomes a divergent
    // evaluation outcome. The op-budget (`steps > stepBudget`) is the sole authoritative bound.
    if (this.cancelled || this.signal?.aborted || Date.now() >= this.deadline) { this.cancelled = true; throw new RuleTimeout() }
  }

  charge(): void {
    this.checkClock()
    if (++this.steps > this.limits.stepBudget) throw new RuleBudget()
  }

  /** Charges `size` steps proportional to a large operand (string concat / money arithmetic), so a
   *  single big operand cannot run uncharged (finding F4); fails closed identically to `charge`. */
  chargeSize(size: number): void {
    this.checkClock()
    this.steps += size > this.limits.stepBudget ? this.limits.stepBudget + 1 : (size < 1 ? 1 : size)
    if (this.steps > this.limits.stepBudget) throw new RuleBudget()
  }
}

/** Per-cell evaluation state. */
export interface EvalContext {
  resolver: ValueResolver
  now: Date
  budget: EvalBudget
}

/** A recoverable error → becomes a ComputedValue.Error / Validity failure. */
export class RuleEvalError extends Error {
  constructor(readonly error: RuleError) {
    super(error.code)
  }
}

/** A referenced cell is Pending → the whole outcome becomes Pending. */
export class RulePending extends Error {}

/** The per-instance step budget was exhausted — the authoritative, deterministic, outcome-affecting
 *  bound (becomes a fail-closed `rule.budget_exceeded` result). */
export class RuleBudget extends Error {}

/**
 * The wall-clock liveness guard / abort signal tripped. This is a NON-authoritative infrastructure
 * fault (D1 ratification 2026-07-01): it PROPAGATES out of the evaluator — it is NEVER converted into
 * an evaluation outcome, so the (time/hardware-dependent) wall-clock cannot make this tier disagree
 * with the .NET tier. The .NET analog is `RuleEngineTimeoutException`. Consumers catch it as an
 * infrastructure fault (retry / degrade), never as a rule verdict.
 */
export class RuleTimeout extends Error {}

export const err = (code: string, key?: string, value?: string): RuleError => ({
  code,
  params: key !== undefined && value !== undefined ? { [key]: value } : {},
})
