/**
 * ADR 0146 D10 — interactive explainability traces (board F2), TS tier. Mirror of the .NET `RuleTraceTests`:
 * a trace is emitted on the interactive path (opt-in projection) and traceless otherwise; the authority
 * filter darkens field references (redact / shredded-subject hash); and a field VALUE is NEVER in a trace param.
 */
import { describe, it, expect } from 'vitest'

import { compile } from '../compiler.js'
import { FormRuleGraph } from '../graph.js'
import { GuardEvaluator } from '../guard.js'
import { RuleInstance } from '../instance.js'
import type { Json, RuleDefinition } from '../model.js'
import {
  buildFormTrace, buildGuardTrace, RuleTraceCodes, passThroughTraceFilter,
  type TraceAuthorityFilter, type TraceFieldDisclosure,
} from '../trace.js'

const hideHighEarnerComp: RuleDefinition = {
  id: 'vis.comp', tier: 'JsonLogic', scope: 'Section', scopeTarget: 'comp',
  expression: { '<=': [{ var: 'salary' }, 100000] }, action: 'Visibility',
}

function evaluate(rule: RuleDefinition, instance: Record<string, Json>) {
  const compiled = compile([rule])
  const graph = new FormRuleGraph(compiled)
  const result = graph.evaluateInstance(RuleInstance.fromJson(instance))
  return { compiled, result }
}

function denyFilter(field: string): TraceAuthorityFilter {
  return { disclose: (name): TraceFieldDisclosure => (name === field ? 'redact' : 'show') }
}
function shredFilter(field: string): TraceAuthorityFilter {
  return { disclose: (name): TraceFieldDisclosure => (name === field ? 'hash' : 'show') }
}

describe('ADR 0146 D10 traces', () => {
  it('emits a trace on the interactive path (opt-in projection); the eval itself is traceless', () => {
    const { compiled, result } = evaluate(hideHighEarnerComp, { salary: 150000 })
    const trace = buildFormTrace(compiled, result)
    expect(trace).toHaveLength(1)
    expect(trace[0].ruleId).toBe('vis.comp')
  })

  it('high earner is hidden; the trace carries the code + field ref, NEVER the value', () => {
    const { compiled, result } = evaluate(hideHighEarnerComp, { salary: 150000 })
    const entry = buildFormTrace(compiled, result, passThroughTraceFilter)[0]
    expect(entry.code).toBe(RuleTraceCodes.hidden)
    expect(entry.params.reads).toBe('salary')
    expect(entry.target).toBe('section:comp')
    for (const v of Object.values(entry.params)) {
      expect(v).not.toContain('150000')
      expect(v).not.toContain('100000')
    }
  })

  it('the authority filter redacts an unreadable field reference', () => {
    const { compiled, result } = evaluate(hideHighEarnerComp, { salary: 150000 })
    const entry = buildFormTrace(compiled, result, denyFilter('salary'))[0]
    expect(entry.code).toBe(RuleTraceCodes.hidden)
    expect(entry.params.reads).toBe('[redacted]')
  })

  it('a shredded subject darkens the reference to a reference+hash form', () => {
    const { compiled, result } = evaluate(hideHighEarnerComp, { salary: 150000 })
    const entry = buildFormTrace(compiled, result, shredFilter('salary'))[0]
    expect(entry.params.reads.startsWith('#')).toBe(true)
    // Cross-tier anchor: the SAME literal is pinned in the .NET tier's trace test — byte-identical hashing.
    expect(entry.params.reads).toBe('#8561b279')
    expect(entry.params.reads).not.toContain('salary')
  })

  it('validation failure carries the cause code, never the value', () => {
    const rule: RuleDefinition = { id: 'v.qty', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'qty', expression: { '>': [{ var: 'qty' }, 0] }, action: 'Validate' }
    const { compiled, result } = evaluate(rule, { qty: -3 })
    const entry = buildFormTrace(compiled, result)[0]
    expect(entry.code).toBe(RuleTraceCodes.validationFailed)
    expect(entry.params.cause).toBe('v.qty')
    expect(Object.values(entry.params).join('|')).not.toContain('-3')
  })

  it('compute value carries the computed code but not the computed value', () => {
    const rule: RuleDefinition = { id: 'c.total', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'total', expression: { '+': [{ var: 'qty' }, { var: 'extra' }] }, action: 'Compute' }
    const { compiled, result } = evaluate(rule, { qty: 3, extra: 4 })
    const entry = buildFormTrace(compiled, result)[0]
    expect(entry.code).toBe(RuleTraceCodes.valueComputed)
    expect(entry.params.reads.split(',').sort().join(',')).toBe('extra,qty')
    expect(Object.values(entry.params).join('|')).not.toContain('7')
  })

  it('guard traces carry the code + field ref (passed / failed)', () => {
    const guard: RuleDefinition = { id: 'g.amount', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', expression: { '>': [{ var: 'amount' }, 5000] }, action: 'Validate' }
    const evaluator = new GuardEvaluator()

    const pass = evaluator.evaluateGuard(guard, { amount: 7000 })
    const passTrace = buildGuardTrace(guard, pass)
    expect(passTrace.code).toBe(RuleTraceCodes.guardPassed)
    expect(passTrace.params.reads).toBe('amount')
    expect(passTrace.target).toBe('guard:g.amount')

    const fail = evaluator.evaluateGuard(guard, { amount: 3000 })
    const failTrace = buildGuardTrace(guard, fail)
    expect(failTrace.code).toBe(RuleTraceCodes.guardFailed)
    expect(failTrace.params.cause).toBe('g.amount')
    expect(Object.values(failTrace.params).join('|')).not.toContain('3000')
  })
})
