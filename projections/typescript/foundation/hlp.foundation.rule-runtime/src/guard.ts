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
import { ROOT_SCOPE, type ContextAdapter, type RuleEvalScope } from './context-adapter.js'
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
class ContextBagAdapter implements ContextAdapter {
  constructor(private readonly bag: Record<string, Json>) {}

  createResolver(_scope: RuleEvalScope): ValueResolver {
    return new ContextBagResolver(this.bag)
  }
}

export class GuardEvaluator {
  constructor(
    private readonly limits: RuleEngineLimits = DEFAULT_LIMITS,
    private readonly clock: () => Date = () => new Date(),
  ) {}

  evaluateGuard(rule: RuleDefinition, context: Record<string, Json>, signal?: AbortSignal): Validity {
    const compiled = compile([rule], this.limits)
    if (compiled.rules.length === 0) return { ok: true } // Tier-1 guard: nothing for this engine.
    return this.run<Validity>(
      compiled.rules[0].ast as Json,
      context,
      (v) => (isTruthy(v) ? { ok: true } : { ok: false, error: err(rule.id) }),
      (e) => ({ ok: false, error: e }),
      () => ({ ok: false, error: err(Codes.pendingAtSave) }),
      signal,
    )
  }

  evaluateValue(rule: RuleDefinition, context: Record<string, Json>, signal?: AbortSignal): ComputedValue {
    const compiled = compile([rule], this.limits)
    if (compiled.rules.length === 0) return { state: 'Resolved', value: null }
    return this.run<ComputedValue>(
      compiled.rules[0].ast as Json,
      context,
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
    // The workflow-guard context flows through the ADR 0146 D3 seam (behaviour-neutral).
    const ctx: EvalContext = { resolver: new ContextBagAdapter(context).createResolver(ROOT_SCOPE), now: this.clock(), budget: new EvalBudget(this.limits, signal) }
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
