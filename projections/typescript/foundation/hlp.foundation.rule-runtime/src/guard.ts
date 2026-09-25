/**
 * The workflow orchestrator (SPINE-1 §5.4) — TS port of `GuardEvaluator`. Evaluates a
 * transition guard / action condition (a one-node graph) over a flat context bag.
 * Fail-closed: a pending/errored guard does not let a transition fire.
 */
import { admissionRefusal, type EvaluationAdmission } from './environment.js'
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
import { parseBoundedJsonText } from './input-envelope.js'
import { detachJson } from './instance.js'

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
const snapshotBrand = new WeakSet<object>()
const snapshotData = new WeakMap<object, Record<string, Json>>()
/**
 * Runtime-owned data admitted from JSON text before crossing into pure evaluation.
 * Parsing belongs to the host boundary; evaluator entry never reflects over a caller object.
 */
export class RuleContextSnapshot {
  // No constructor arguments or instance data: JavaScript callers can invoke a TS-private
  // constructor, but only this module's JSON-text factory can add owned data to the WeakMap.
  private constructor() {}

  static fromJsonText(jsonText: string): RuleContextSnapshot {
    const parsed: unknown = parseBoundedJsonText(jsonText, 'rule context')
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) throw new TypeError('rule context must be a JSON object')
    const snapshot = new RuleContextSnapshot()
    snapshotBrand.add(snapshot)
    snapshotData.set(snapshot, parsed as Record<string, Json>)
    return snapshot
  }
}

function contextValuesOf(value: unknown): Record<string, Json> | undefined {
  return typeof value === 'object' && value !== null && snapshotBrand.has(value)
    ? snapshotData.get(value)
    : undefined
}

export class GuardEvaluator {
  constructor(
    private readonly clock: () => Date,
    private readonly limits: RuleEngineLimits = DEFAULT_LIMITS,
  ) {
    if (typeof clock !== 'function') throw new TypeError('GuardEvaluator requires a caller-supplied clock')
  }

  evaluateGuard(rule: RuleDefinition, context: RuleContextSnapshot, admission: EvaluationAdmission | null, signal?: AbortSignal): Validity {
    const compiled = compile([rule], this.limits)
    // rules-eng-26: admission evidence is checked before any value is read.
    const refusal = admissionRefusal(admission, compiled.rules.map((r) => r.ast))
    if (refusal !== null) return { ok: false, error: err(refusal) }
    if (compiled.rules.length === 0) return { ok: true } // Tier-1 guard: nothing for this engine.
    const snapshot = contextValuesOf(context)
    if (!snapshot) return { ok: false, error: err(Codes.contextSnapshotRequired) }
    return this.run<Validity>(
      compiled.rules[0].ast as Json,
      snapshot,
      (v) => (isTruthy(v) ? { ok: true } : { ok: false, error: err(rule.id) }),
      (e) => ({ ok: false, error: e }),
      () => ({ ok: false, error: err(Codes.pendingAtSave) }),
      signal,
    )
  }

  evaluateValue(rule: RuleDefinition, context: RuleContextSnapshot, admission: EvaluationAdmission | null, signal?: AbortSignal): ComputedValue {
    const compiled = compile([rule], this.limits)
    const refusal = admissionRefusal(admission, compiled.rules.map((r) => r.ast))
    if (refusal !== null) return { state: 'Error', error: err(refusal) }
    if (compiled.rules.length === 0) return { state: 'Resolved', value: null }
    const snapshot = contextValuesOf(context)
    if (!snapshot) return { state: 'Error', error: err(Codes.contextSnapshotRequired) }
    return this.run<ComputedValue>(
      compiled.rules[0].ast as Json,
      snapshot,
      (v) => ({ state: 'Resolved', value: detachJson(v) }),
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
