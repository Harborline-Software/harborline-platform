import { describe, expect, it } from 'vitest'

import { Codes } from './codes.js'
import { GuardEvaluator } from './guard.js'
import type { Json, RuleDefinition } from './model.js'

function rule(id: string, expression: Json, action: RuleDefinition['action'] = 'Validate'): RuleDefinition {
  return {
    id,
    tier: 'JsonLogic',
    scope: 'Schema',
    scopeTarget: '',
    action,
    expression,
  }
}

describe('GuardEvaluator.evaluateGuard', () => {
  const evaluator = new GuardEvaluator()
  const minimumAmount = rule('guard.minimum-amount', { '>=': [{ var: 'amount' }, 100] })

  it('accepts a boundary value and rejects a lower value with the rule id', () => {
    expect(evaluator.evaluateGuard(minimumAmount, { amount: 100 })).toEqual({ ok: true })
    expect(evaluator.evaluateGuard(minimumAmount, { amount: 99 })).toEqual({
      ok: false,
      error: { code: 'guard.minimum-amount', params: {} },
    })
  })

  it('fails closed with typed results for an empty context and a pending dependency', () => {
    expect(evaluator.evaluateGuard(minimumAmount, {})).toEqual({
      ok: false,
      error: { code: Codes.typeError, params: { reason: 'null-as-number' } },
    })
    expect(evaluator.evaluateGuard(minimumAmount, { amount: { '@pending': true } })).toEqual({
      ok: false,
      error: { code: Codes.pendingAtSave, params: {} },
    })
  })

  it('returns a stable error result for an invalid division expression', () => {
    expect(evaluator.evaluateGuard(rule('guard.invalid', { '/': [1, 0] }), {})).toEqual({
      ok: false,
      error: { code: Codes.divByZero, params: {} },
    })
  })
})

describe('GuardEvaluator.evaluateValue', () => {
  const evaluator = new GuardEvaluator()

  it('resolves a deterministic expression', () => {
    expect(evaluator.evaluateValue(rule('value.total', { '+': [{ var: 'amount' }, 1] }, 'Compute'), {
      amount: 41,
    })).toEqual({ state: 'Resolved', value: 42 })
  })

  it('resolves a missing value as null and preserves a pending value as Pending', () => {
    const reference = rule('value.reference', { var: 'amount' }, 'Compute')

    expect(evaluator.evaluateValue(reference, {})).toEqual({ state: 'Resolved', value: null })
    expect(evaluator.evaluateValue(reference, { amount: { '@pending': true } })).toEqual({
      state: 'Pending',
    })
  })

  it('returns the stable error result for an invalid division expression', () => {
    expect(evaluator.evaluateValue(rule('value.invalid', { '/': [1, 0] }, 'Compute'), {})).toEqual({
      state: 'Error',
      error: { code: Codes.divByZero, params: {} },
    })
  })
})
