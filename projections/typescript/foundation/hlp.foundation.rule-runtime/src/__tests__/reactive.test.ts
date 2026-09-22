/**
 * TS-tier unit coverage (SPINE-1 §6.3): the reactive re-eval front (transitive
 * dependents only + memoization soundness), incremental child-table row edits, the
 * async Pending path, identical static-cap rejection to .NET, and the guard evaluator.
 */
import { describe, it, expect } from 'vitest'

import type { RuleDefinition } from '../model.js'
import { compile, CompileError, RuleTimeout } from '../index.js'
import { FormRuleGraph } from '../graph.js'
import { GuardEvaluator } from '../guard.js'
import { RuleInstance } from '../instance.js'
import { Codes } from '../codes.js'
import type { Json } from '../model.js'

const fixedClock = () => new Date('2026-06-30T00:00:00.000Z')

function rule(id: string, scopeTarget: string, action: RuleDefinition['action'], expression: Json, scope: RuleDefinition['scope'] = 'Field'): RuleDefinition {
  return { id, tier: 'JsonLogic', scope, scopeTarget, action, expression }
}

function graphOf(rules: RuleDefinition[], instance: Record<string, Json>) {
  const g = new FormRuleGraph(compile(rules), fixedClock)
  return { g, first: g.evaluateInstance(RuleInstance.fromJson(instance)) }
}

describe('business-clock pinning', () => {
  it('refuses graph and guard construction when an untyped consumer omits the required clock', () => {
    const ClocklessGraph = FormRuleGraph as unknown as new (compiled: ReturnType<typeof compile>) => FormRuleGraph
    const ClocklessGuard = GuardEvaluator as unknown as new () => GuardEvaluator

    expect(() => new ClocklessGraph(compile([]))).toThrow(/clock/i)
    expect(() => new ClocklessGuard()).toThrow(/clock/i)
  })

  it('samples an advancing clock once for an evaluation and once again for reactive re-evaluation', () => {
    const instants = [
      new Date('2026-06-30T23:59:59.000Z'),
      new Date('2026-07-01T00:00:01.000Z'),
    ]
    let reads = 0
    const clock = () => instants[reads++]
    const rules = [
      rule('c.first', 'first', 'Compute', { if: [{ var: 'toggle' }, { 'date.today': [] }, { 'date.today': [] }] }),
      rule('c.second', 'second', 'Compute', { if: [{ var: 'toggle' }, { 'date.today': [] }, { 'date.today': [] }] }),
    ]
    const graph = new FormRuleGraph(compile(rules), clock)

    const first = graph.evaluateInstance(RuleInstance.fromJson({ toggle: true }))
    expect(reads).toBe(1)
    expect(first.values.get('field:first')).toEqual({ state: 'Resolved', value: '2026-06-30' })
    expect(first.values.get('field:second')).toEqual({ state: 'Resolved', value: '2026-06-30' })

    const next = graph.reevaluate('toggle', false)
    expect(reads).toBe(2)
    expect(next.values.get('field:first')).toEqual({ state: 'Resolved', value: '2026-07-01' })
    expect(next.values.get('field:second')).toEqual({ state: 'Resolved', value: '2026-07-01' })
  })

  it('treats date.today as an input without replacing genuinely unaffected outcomes', () => {
    const before = new Date('2026-06-30T23:59:59.000Z')
    const after = new Date('2026-07-01T00:00:01.000Z')
    let reads = 0
    const graph = new FormRuleGraph(compile([
      rule('c.today', 'today', 'Compute', { 'date.today': [] }),
      rule('c.label', 'label', 'Compute', { cat: [{ var: 'today' }, '/', { var: 'other' }] }),
      rule('c.stable', 'stable-output', 'Compute', { var: 'stable' }),
      rule('v.today', 'today-valid', 'Validate', { '==': [{ 'date.today': [] }, '2026-07-01'] }),
    ]), () => [before, after][reads++])
    const first = graph.evaluateInstance(RuleInstance.fromJson({ other: 'before', stable: 'unchanged' }))
    const stableOutcome = first.byRule.get('c.stable')

    const incremental = graph.reevaluate('other', 'after')
    const full = new FormRuleGraph(compile([
      rule('c.today', 'today', 'Compute', { 'date.today': [] }),
      rule('c.label', 'label', 'Compute', { cat: [{ var: 'today' }, '/', { var: 'other' }] }),
      rule('c.stable', 'stable-output', 'Compute', { var: 'stable' }),
      rule('v.today', 'today-valid', 'Validate', { '==': [{ 'date.today': [] }, '2026-07-01'] }),
    ]), () => after).evaluateInstance(RuleInstance.fromJson({ other: 'after', stable: 'unchanged' }))

    expect(reads).toBe(2)
    expect(incremental.values.get('field:today')).toEqual(full.values.get('field:today'))
    expect(incremental.values.get('field:label')).toEqual(full.values.get('field:label'))
    expect(incremental.byRule.get('v.today')?.validity).toEqual(full.byRule.get('v.today')?.validity)
    expect(incremental.byRule.get('c.stable')).toBe(stableOutcome)
  })
})

describe('reactive re-evaluation — transitive dependents only', () => {
  it('re-evaluates only the dependent front; non-dependent outcomes are referentially unchanged', () => {
    const rules = [
      rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] }),
      rule('c.d', 'd', 'Compute', { '+': [{ var: 'x' }, 1] }),
    ]
    const { g, first } = graphOf(rules, { a: 10, x: 100 })
    expect(first.values.get('field:b')).toEqual({ state: 'Resolved', value: 11 })
    expect(first.values.get('field:d')).toEqual({ state: 'Resolved', value: 101 })

    const dOutcomeBefore = first.byRule.get('c.d')
    const next = g.reevaluate('a', 20)

    expect(next.values.get('field:b')).toEqual({ state: 'Resolved', value: 21 })
    // c.d does not depend on a — its outcome object is the SAME reference (not re-evaluated).
    expect(next.byRule.get('c.d')).toBe(dOutcomeBefore)
  })
})

describe('numeric / collation determinism (D1 ratification fix 2)', () => {
  it('money avg aggregate fails closed — no inexact decimal division (exact-or-fail-closed, no rounding)', () => {
    // Symmetric to the .NET tier: avg over a money (decimal-string) column would need inexact decimal
    // division (undefined in v1), so it fails closed rather than silently using IEEE double. (The corpus
    // proves the exact money add/mul/sum byte-identity; this pins the deliberate avg refusal.)
    const { first } = graphOf([rule('c.avg', 'avg', 'Compute', { var: 'table.avg(items.price)' })], { items: [{ price: '0.1' }, { price: '0.2' }] })
    const agg = first.values.get('agg:items/avg/price')
    expect(agg?.state).toBe('Error')
    expect(agg?.error?.code).toBe(Codes.moneyAggUnsupported)
  })
})

describe('incremental child-table edit', () => {
  const rules = [rule('c.total', 'total', 'Compute', { var: 'table.sum(items.amount)' })]

  it('add a row re-links the aggregate', () => {
    const { g, first } = graphOf(rules, { items: [{ amount: 10 }, { amount: 20 }] })
    expect(first.values.get('field:total')).toEqual({ state: 'Resolved', value: 30 })
    const after = g.addRow('items', { id: 'r3', fields: { amount: 5 } })
    expect(after.values.get('field:total')).toEqual({ state: 'Resolved', value: 35 })
  })

  it('remove a row re-links the aggregate', () => {
    const { g } = graphOf(rules, { items: [{ _id: 'a', amount: 10 }, { _id: 'b', amount: 20 }] })
    const after = g.removeRow('items', 'a')
    expect(after.values.get('field:total')).toEqual({ state: 'Resolved', value: 20 })
  })
})

describe('async Pending path', () => {
  it('a pending dependency yields Pending, then Resolved on settle', () => {
    const rules = [rule('c.ship', 'ship', 'Compute', { cat: [{ var: 'city' }, '!'] })]
    const { g, first } = graphOf(rules, { city: { '@pending': true } })
    expect(first.values.get('field:ship')).toEqual({ state: 'Pending' })
    expect(first.hasPending).toBe(true)
    expect(first.isSaveBlocked).toBe(true)

    const settled = g.reevaluate('city', 'Paris')
    expect(settled.values.get('field:ship')).toEqual({ state: 'Resolved', value: 'Paris!' })
    expect(settled.hasPending).toBe(false)
  })
})

describe('static-cap rejection (identical to the .NET integrity tier)', () => {
  it('rejects a cyclic definition at publish with the cycle path', () => {
    const rules = [
      rule('c.x', 'x', 'Compute', { '+': [{ var: 'y' }, 1] }),
      rule('c.y', 'y', 'Compute', { '+': [{ var: 'x' }, 1] }),
    ]
    const e = (() => { try { compile(rules); return null } catch (err) { return err } })()
    expect(e).toBeInstanceOf(CompileError)
    expect((e as CompileError).code).toBe(Codes.compileCycle)
    expect((e as CompileError).cyclePath?.length).toBeGreaterThan(0)
  })

  it('rejects a Power-Fx (demoted) rule', () => {
    const r: RuleDefinition = { id: 'c.p', tier: 'PowerFx', scope: 'Field', scopeTarget: 'p', action: 'Compute', expression: { var: 'a' } }
    expect(() => compile([r])).toThrow(CompileError)
  })

  it('rejects a too-deep dependency chain', () => {
    const rules: RuleDefinition[] = []
    rules.push(rule('c.f0', 'f0', 'Compute', { '+': [{ var: 'seed' }, 1] }))
    for (let i = 1; i <= 70; i++) rules.push(rule(`c.f${i}`, `f${i}`, 'Compute', { '+': [{ var: `f${i - 1}` }, 1] }))
    const e = (() => { try { compile(rules); return null } catch (err) { return err } })()
    expect((e as CompileError).code).toBe(Codes.compileDepthExceeded)
  })

  it('rejects a rule exceeding the max-references bound', () => {
    const refs = Array.from({ length: 70 }, (_, i) => ({ var: `r${i}` }))
    const r = rule('c.many', 'many', 'Compute', { '+': refs })
    expect(() => compile([r])).toThrow(CompileError)
  })

  it('fails closed when the per-instance step budget is exhausted', () => {
    const g = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock, { ...{
      maxGraphNodes: 5000, maxTableRowsPerAggregate: 2000, maxDependencyDepth: 64, maxReferencesPerRule: 64,
      maxAstNodes: 256, maxLiteralLength: 4096, stepBudget: 0, wallClockMs: 250,
    } })
    const res = g.evaluateInstance(RuleInstance.fromJson({ a: 1 }))
    expect(res.isSaveBlocked).toBe(true)
    expect(res.validations[0].validity?.error?.code).toBe(Codes.budgetExceeded)
  })
})

describe('wall-clock is a non-authoritative liveness fault, not a divergent outcome (D1 fix 1)', () => {
  // The wall-clock / AbortSignal is time- and hardware-dependent: if it produced a `rule.timeout`
  // OUTCOME, a slow tier would disagree with a fast tier, breaking replay-determinism + the byte-identical
  // corpus. So it PROPAGATES as an infrastructure fault (RuleTimeout) rather than returning a result.
  // (An AbortSignal is the deterministic, hardware-independent way to trip the guard — the TS analog of
  // the .NET pre-cancelled CancellationToken.)
  const one = () => new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)

  it('evaluateInstance PROPAGATES RuleTimeout on an aborted signal (never a rule.timeout result)', () => {
    const ac = new AbortController()
    ac.abort()
    expect(() => one().evaluateInstance(RuleInstance.fromJson({ a: 1 }), ac.signal)).toThrow(RuleTimeout)
  })

  it('reevaluate PROPAGATES RuleTimeout on an aborted signal', () => {
    const g = one()
    g.evaluateInstance(RuleInstance.fromJson({ a: 1 }))
    const ac = new AbortController()
    ac.abort()
    expect(() => g.reevaluate('a', 2, ac.signal)).toThrow(RuleTimeout)
  })

  it('a guard PROPAGATES RuleTimeout on an aborted signal (not a Validity verdict)', () => {
    const ac = new AbortController()
    ac.abort()
    const gd = new GuardEvaluator(fixedClock)
    const r: RuleDefinition = { id: 'g.min', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Validate', expression: { '>': [{ var: 'amount' }, 50] } }
    expect(() => gd.evaluateGuard(r, { amount: 100 }, ac.signal)).toThrow(RuleTimeout)
  })

  it('the op-budget, by contrast, STAYS an authoritative fail-closed OUTCOME (deterministic across tiers)', () => {
    // Distinct from the wall-clock: the op-budget is deterministic (same op count on both tiers), so it
    // remains an outcome-affecting fail-closed result — the two tiers reach it identically.
    const g = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock, {
      maxGraphNodes: 5000, maxTableRowsPerAggregate: 2000, maxDependencyDepth: 64, maxReferencesPerRule: 64,
      maxAstNodes: 256, maxLiteralLength: 4096, stepBudget: 0, wallClockMs: 250,
    })
    const res = g.evaluateInstance(RuleInstance.fromJson({ a: 1 }))
    expect(res.isSaveBlocked).toBe(true)
    expect(res.validations[0].validity?.error?.code).toBe(Codes.budgetExceeded)
  })
})

describe('unavailable aggregate refuses — never a fabricated null (ticket 162)', () => {
  it('an agg node the compiler cannot statically register is a publish-time refusal, not runtime data', () => {
    // Arity-4 (and expression-valued/non-string-arg) agg nodes used to skip reference
    // extraction, register no fold cell, and then refuse per-keystroke at runtime with no
    // authoring-time signal — permanently un-saveable data. The compiler now refuses them
    // (150-family direction), identically in both tiers.
    const bad = (expression: Json) => (() => { try { compile([rule('c.dyn', 'dyn', 'Compute', expression)]); return null } catch (e) { return e } })()
    for (const expression of [
      { agg: ['sum', 'items', 'amount', 'extra'] } as Json, // arity 4
      { agg: ['sum', { var: 'field.section' }, 'amount'] } as Json, // expression-valued arg (phantom String() cell before)
    ]) {
      const e = bad(expression)
      expect(e).toBeInstanceOf(CompileError)
      expect((e as CompileError).code).toBe(Codes.compileBadGrammar)
    }
  })

  it('a compiled aggregate the evaluation context cannot provide refuses with the ONE shared shape', () => {
    // The guard tier's flat context bag carries no tables, so a well-formed agg reference is
    // unavailable data there: rule.bad_reference with params {agg: "section/fn/col"} — the
    // exact shape (and param order) the shared corpus pins byte-identically across tiers.
    const guard = new GuardEvaluator(fixedClock)
    const v: RuleDefinition = { id: 'g.total', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Compute', expression: { var: 'table.sum(items.amount)' } }
    expect(guard.evaluateValue(v, {})).toEqual({
      state: 'Error',
      error: { code: Codes.badReference, params: { agg: 'items/sum/amount' } },
    })
  })
})

describe('guard evaluator (workflow transition guards)', () => {
  const guard = new GuardEvaluator(fixedClock)
  const g: RuleDefinition = { id: 'g.minAmount', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Validate', expression: { '>': [{ var: 'amount' }, 50] } }

  it('passes when the guard holds', () => {
    expect(guard.evaluateGuard(g, { amount: 100 })).toEqual({ ok: true })
  })

  it('fails closed with a stable code when the guard does not hold', () => {
    expect(guard.evaluateGuard(g, { amount: 10 })).toEqual({ ok: false, error: { code: 'g.minAmount', params: {} } })
  })

  it('evaluates a value expression', () => {
    const v: RuleDefinition = { id: 'g.fee', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Compute', expression: { 'money.mul': ['10', '3'] } }
    expect(guard.evaluateValue(v, {})).toEqual({ state: 'Resolved', value: '30' })
  })

  it('fails closed on a pending dependency (server tier)', () => {
    expect(guard.evaluateGuard(g, { amount: { '@pending': true } })).toEqual({ ok: false, error: { code: Codes.pendingAtSave, params: {} } })
  })
})
