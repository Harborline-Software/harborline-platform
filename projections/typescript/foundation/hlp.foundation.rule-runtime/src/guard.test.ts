import { describe, expect, it } from 'vitest'

import { Codes } from './codes.js'
import { GuardEvaluator, RuleContextSnapshot } from './guard.js'
import type { Json, RuleDefinition } from './model.js'
import { admitEnvironment as admitTestEnvironment, fieldReadEffect as testFieldRead, lentGrammar as testGrammar } from './environment.js'
import { builtInFunctions as testBuiltIns } from './functions.js'
// The suite's own borrower: the whole register, every scope token, every phase (T-590 rules-eng-26).
const testAdmission = admitTestEnvironment({ borrower: 'rule-engine-tests', grammar: testGrammar, variables: { field: 'test', row: 'test', wf: 'test', timer: 'test' }, operations: testBuiltIns.map((f) => f.key), effects: [testFieldRead], missingValues: 'missing-field-reads-null', timeSource: 'injected-test-clock', timeZone: 'utc', phases: { AuthoringValidation: true, PublishValidation: true, Render: true, Submission: true, Run: true, SignOff: true }, replay: 'deterministic' }).forPhase('Run')

const fixedClock = () => new Date('2026-06-30T00:00:00.000Z')
const snapshot = (value: Record<string, Json>) => RuleContextSnapshot.fromJsonText(JSON.stringify(value))

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
  const evaluator = new GuardEvaluator(fixedClock)
  const minimumAmount = rule('guard.minimum-amount', { '>=': [{ var: 'amount' }, 100] })

  it('accepts a boundary value and rejects a lower value with the rule id', () => {
    expect(evaluator.evaluateGuard(minimumAmount, snapshot({ amount: 100 }), testAdmission)).toEqual({ ok: true })
    expect(evaluator.evaluateGuard(minimumAmount, snapshot({ amount: 99 }), testAdmission)).toEqual({
      ok: false,
      error: { code: 'guard.minimum-amount', params: {} },
    })
  })

  it('fails closed with typed results for an empty context and a pending dependency', () => {
    expect(evaluator.evaluateGuard(minimumAmount, snapshot({}), testAdmission)).toEqual({
      ok: false,
      error: { code: Codes.typeError, params: { reason: 'null-as-number' } },
    })
    expect(evaluator.evaluateGuard(minimumAmount, snapshot({ amount: { '@pending': true } }), testAdmission)).toEqual({
      ok: false,
      error: { code: Codes.pendingAtSave, params: {} },
    })
  })

  it('returns a stable error result for an invalid division expression', () => {
    expect(evaluator.evaluateGuard(rule('guard.invalid', { '/': [1, 0] }), snapshot({}), testAdmission)).toEqual({
      ok: false,
      error: { code: Codes.divByZero, params: {} },
    })
  })

  it('refuses an accessor-backed host context without invoking its getter', () => {
    let invoked = false
    const context = {} as Record<string, Json>
    Object.defineProperty(context, 'amount', { enumerable: true, get: () => {
      invoked = true
      throw new Error('host callback ran')
    } })

    expect(evaluator.evaluateGuard(minimumAmount, context as unknown as RuleContextSnapshot, testAdmission)).toEqual({
      ok: false,
      error: { code: 'rule.context_snapshot_required', params: {} },
    })
    expect(invoked).toBe(false)
  })

  it('refuses a proxied host context before reflective traps can run', () => {
    let invoked = false
    const context = new Proxy({ amount: 100 } as Record<string, Json>, {
      getPrototypeOf: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
      ownKeys: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
      getOwnPropertyDescriptor: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
    })

    expect(evaluator.evaluateGuard(minimumAmount, context as unknown as RuleContextSnapshot, testAdmission)).toEqual({
      ok: false,
      error: { code: Codes.contextSnapshotRequired, params: {} },
    })
    expect(invoked).toBe(false)
  })

  it('refuses a JavaScript-forged context snapshot without reading its supplied values', () => {
    let invoked = false
    const UnsafeConstructor = RuleContextSnapshot as unknown as new (values: Record<string, Json>) => RuleContextSnapshot
    const forged = new UnsafeConstructor(new Proxy({ amount: 100 } as Record<string, Json>, {
      get: () => {
        invoked = true
        throw new Error('forged values ran')
      },
    }))

    expect(evaluator.evaluateGuard(minimumAmount, forged, testAdmission)).toEqual({
      ok: false,
      error: { code: Codes.contextSnapshotRequired, params: {} },
    })
    expect(invoked).toBe(false)
  })

  it('does not read a replacement values getter on a branded snapshot', () => {
    let invoked = false
    const context = RuleContextSnapshot.fromJsonText('{"amount":100}')
    Object.defineProperty(context, 'values', { get: () => {
      invoked = true
      throw new Error('replacement values ran')
    } })

    expect(evaluator.evaluateGuard(minimumAmount, context, testAdmission)).toEqual({ ok: true })
    expect(invoked).toBe(false)
  })

  it('refuses a JSON context over the finite node envelope before evaluation', () => {
    const overLimit = JSON.stringify({ values: Array.from({ length: 4_999 }, () => 0) })

    expect(() => RuleContextSnapshot.fromJsonText(overLimit)).toThrow(RangeError)
  })

  it('uses UTF-8 bytes rather than JavaScript character count at the input boundary', () => {
    const prefix = '{"payload":"'
    const suffix = '"}'
    const asciiAtLimit = `${prefix}${'a'.repeat(262_144 - prefix.length - suffix.length)}${suffix}`
    const nonAsciiAtLimit = `${prefix}${'é'.repeat((262_144 - prefix.length - suffix.length) / 2)}${suffix}`

    expect(() => RuleContextSnapshot.fromJsonText(asciiAtLimit)).not.toThrow()
    expect(() => RuleContextSnapshot.fromJsonText(`${asciiAtLimit.slice(0, -2)}a${suffix}`)).toThrow(RangeError)
    expect(() => RuleContextSnapshot.fromJsonText(nonAsciiAtLimit)).not.toThrow()
    expect(() => RuleContextSnapshot.fromJsonText(`${nonAsciiAtLimit.slice(0, -2)}é${suffix}`)).toThrow(RangeError)
  })
})

describe('GuardEvaluator.evaluateValue', () => {
  const evaluator = new GuardEvaluator(fixedClock)

  it('resolves a deterministic expression', () => {
    expect(evaluator.evaluateValue(rule('value.total', { '+': [{ var: 'amount' }, 1] }, 'Compute'), snapshot({
      amount: 41,
    }), testAdmission)).toEqual({ state: 'Resolved', value: 42 })
  })

  it('resolves a missing value as null and preserves a pending value as Pending', () => {
    const reference = rule('value.reference', { var: 'amount' }, 'Compute')

    expect(evaluator.evaluateValue(reference, snapshot({}), testAdmission)).toEqual({ state: 'Resolved', value: null })
    expect(evaluator.evaluateValue(reference, snapshot({ amount: { '@pending': true } }), testAdmission)).toEqual({
      state: 'Pending',
    })
  })

  it('returns the stable error result for an invalid division expression', () => {
    expect(evaluator.evaluateValue(rule('value.invalid', { '/': [1, 0] }, 'Compute'), snapshot({}), testAdmission)).toEqual({
      state: 'Error',
      error: { code: Codes.divByZero, params: {} },
    })
  })
})
