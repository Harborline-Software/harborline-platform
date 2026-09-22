/**
 * The workflow orchestrator (SPINE-1 §5.4) — TS port of `GuardEvaluator`. Evaluates a
 * transition guard / action condition (a one-node graph) over a flat context bag.
 * Fail-closed: a pending/errored guard does not let a transition fire.
 */
import { Codes } from './codes.js'
import type { ComputedValue, Json, RuleDefinition, Validity } from './model.js'
import { DEFAULT_LIMITS, type RuleEngineLimits } from './limits.js'
import { compile } from './compiler.js'
import {
  EvalBudget, RuleBudget, RuleEvalError, RulePending,
  err, refError, refPending, refResolved, unavailableAggregate,
  type EvalContext, type RefValue, type ValueResolver,
} from './eval-support.js'
import { evaluate, isTruthy } from './jsonlogic.js'

const isPendingSentinel = (n: Json): boolean =>
  typeof n === 'object' && n !== null && !Array.isArray(n) &&
  Object.keys(n).length === 1 && (n as Record<string, Json>)['@pending'] === true

class ContextBagResolver implements ValueResolver {
  constructor(private readonly bag: Record<string, Json>) {}

  resolveVar(path: string): RefValue {
    if (path.startsWith('row.')) return refError(err(Codes.badReference, 'path', path))
    const name = path.startsWith('field.') ? path.slice('field.'.length) : path
    if (name in this.bag) {
      const v = this.bag[name]
      return isPendingSentinel(v) ? refPending : refResolved(v)
    }
    return refResolved(null)
  }

  resolveAgg(fn: string, section: string, col: string): RefValue {
    // A flat context bag carries no tables: every aggregate is unavailable data (ticket 162's
    // one shared refusal shape — the corpus pins its params across tiers).
    return unavailableAggregate(fn, section, col)
  }
}

/**
 * The workflow-guard context as a {@link ContextAdapter} (ADR 0146 D3): maps a flat process /
 * context bag into a `ContextBagResolver`. The bag has no repeating sub-collection, so the
 * scope's `rowSection` is ignored (a `row.` reference resolves to a bad-reference, as before).
 */
class ContextSnapshotError extends Error {}

/** Copies own data descriptors to an inert JSON snapshot without reading accessor properties. */
function captureJson(value: Json): Json {
  if (value === null || typeof value !== 'object') return value
  if (Array.isArray(value)) {
    const descriptors = Object.getOwnPropertyDescriptors(value)
    const out: Json[] = []
    for (let i = 0; i < value.length; i++) {
      const descriptor = descriptors[String(i)]
      if (!descriptor || !('value' in descriptor)) throw new ContextSnapshotError()
      out.push(captureJson(descriptor.value as Json))
    }
    return out
  }
  if (Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null) throw new ContextSnapshotError()
  const out: Record<string, Json> = {}
  for (const [key, descriptor] of Object.entries(Object.getOwnPropertyDescriptors(value))) {
    if (!descriptor.enumerable) continue
    if (!('value' in descriptor)) throw new ContextSnapshotError()
    out[key] = captureJson(descriptor.value as Json)
  }
  return out
}

function captureContext(context: Record<string, Json>): Record<string, Json> {
  const snapshot = captureJson(context)
  if (snapshot === null || typeof snapshot !== 'object' || Array.isArray(snapshot)) throw new ContextSnapshotError()
  return snapshot as Record<string, Json>
}

export class GuardEvaluator {
  constructor(
    private readonly clock: () => Date,
    private readonly limits: RuleEngineLimits = DEFAULT_LIMITS,
  ) {
    if (typeof clock !== 'function') throw new TypeError('GuardEvaluator requires a caller-supplied clock')
  }

  evaluateGuard(rule: RuleDefinition, context: Record<string, Json>, signal?: AbortSignal): Validity {
    const compiled = compile([rule], this.limits)
    if (compiled.rules.length === 0) return { ok: true } // Tier-1 guard: nothing for this engine.
    let snapshot: Record<string, Json>
    try { snapshot = captureContext(context) } catch (e) {
      if (e instanceof ContextSnapshotError) return { ok: false, error: err(Codes.contextSnapshotRequired) }
      throw e
    }
    return this.run<Validity>(
      compiled.rules[0].ast as Json,
      snapshot,
      (v) => (isTruthy(v) ? { ok: true } : { ok: false, error: err(rule.id) }),
      (e) => ({ ok: false, error: e }),
      () => ({ ok: false, error: err(Codes.pendingAtSave) }),
      signal,
    )
  }

  evaluateValue(rule: RuleDefinition, context: Record<string, Json>, signal?: AbortSignal): ComputedValue {
    const compiled = compile([rule], this.limits)
    if (compiled.rules.length === 0) return { state: 'Resolved', value: null }
    let snapshot: Record<string, Json>
    try { snapshot = captureContext(context) } catch (e) {
      if (e instanceof ContextSnapshotError) return { state: 'Error', error: err(Codes.contextSnapshotRequired) }
      throw e
    }
    return this.run<ComputedValue>(
      compiled.rules[0].ast as Json,
      snapshot,
      (v) => ({ state: 'Resolved', value: v }),
      (e) => ({ state: 'Error', error: e }),
      () => ({ state: 'Pending' }),
      signal,
    )
  }

  private run<T>(
    ast: Json,
    context: Record<string, Json>,
    onValue: (v: Json) => T,
    onError: (e: ReturnType<typeof err>) => T,
    onPending: () => T,
    signal?: AbortSignal,
  ): T {
    const ctx: EvalContext = { resolver: new ContextBagResolver(context), now: this.clock(), budget: new EvalBudget(this.limits, signal) }
    try {
      return onValue(evaluate(ast, ctx))
    } catch (e) {
      if (e instanceof RuleEvalError) return onError(e.error)
      if (e instanceof RulePending) return onPending()
      if (e instanceof RuleBudget) return onError(err(Codes.budgetExceeded))
      // RuleTimeout is a non-authoritative liveness fault (D1 ratification 2026-07-01): a guard that hits
      // the wall-clock is an infrastructure fault, not a transition verdict — it PROPAGATES (rethrown).
      throw e
    }
  }
}
