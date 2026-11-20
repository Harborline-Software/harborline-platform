import { expect, it } from 'vitest'

import { admitEnvironment, compile, DEFAULT_LIMITS, GuardEvaluator, RuleContextSnapshot,
  type Json, type RuleDefinition } from '../index.js'
import { deriveCoreTypes } from '../core-types.js'

const rule = (expression: Json): RuleDefinition => ({ id: 'quantifier', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'result', action: 'Compute', expression })
const declaration = {
  borrower: 'array-string-tests', grammar: 'harborline-jsonlogic/v1', variables: { clock: 'test' },
  operations: ['all', 'var', 'startsWith'], effects: ['field-read'], missingValues: 'missing-field-reads-null',
  timeSource: 'injected', timeZone: 'utc', replay: 'deterministic',
  phases: { AuthoringValidation: true, PublishValidation: true, Render: true, Submission: true, Run: true, SignOff: true },
}

it.each<[string, boolean]>([['some', true], ['all', true], ['none', false]])('%s local paths need no outer variable but every operation requires admission', (op, expected) => {
  const guard = new GuardEvaluator(() => new Date(0))
  const context = RuleContextSnapshot.fromJsonText('{}')
  const expression = rule({ [op]: [[{ name: 'pump' }], { startsWith: [{ var: 'name' }, 'p'] }] })
  const admitted = admitEnvironment({ ...declaration, operations: [op, 'var', 'startsWith'] }).forPhase('Run')
  expect(guard.evaluateValue(expression, context, admitted)).toEqual({ state: 'Resolved', value: expected })
  const withoutText = admitEnvironment({ ...declaration, operations: [op, 'var'] }).forPhase('Run')
  const refused = { state: 'Error', error: { code: 'rule.environment.operation_not_admitted', params: {} } }
  expect(guard.evaluateValue(expression, context, withoutText)).toEqual(refused)
  expect(guard.evaluateValue(rule({ [op]: [[], { startsWith: ['pump', 'p'] }] }), context, withoutText)).toEqual(refused)
  expect(guard.evaluateValue(rule({ [op]: [{ var: 'values' }, true] }), context, admitted)).toEqual({
    state: 'Error', error: { code: 'rule.environment.variable_not_admitted', params: {} },
  })
})

it.each(['some', 'all', 'none', 'startsWith', 'endsWith'])('%s requires exactly two arguments', op => {
  for (const args of [[], [[]], [[], true, false]]) {
    expect(() => compile([rule({ [op]: args })])).toThrow(expect.objectContaining({ code: 'rule.compile.invalid_expression' }))
  }
})

it.each<[Json, string]>([
  [{ all: [[1], true] }, '60082'],
  [{ all: [[true], { '==': [{ var: '' }, true] }] }, '5243060026'],
  [{ all: [[[1]], { all: [[1], true] }] }, '300370086'],
  [{ startsWith: ['abc', 'a'] }, '130'],
  [{ endsWith: ['abc', 'a'] }, '130'],
])('accounts for iteration and string work in %j', (expression, work) => {
  // Same independent transfer arithmetic as ArrayStringPrimitiveTests in the native tier.
  const rules = [rule(expression)]
  const proof = compile(rules).workProof
  expect(proof.maximumEvaluationWork.toString()).toBe(work)
  expect(proof.maximumResultBytes.toString()).toBe('16394000')
  expect(() => compile(rules, { ...DEFAULT_LIMITS, maxStaticWork: BigInt(work) })).not.toThrow()
  expect(() => compile(rules, { ...DEFAULT_LIMITS, maxStaticWork: BigInt(work) - 1n })).toThrow(expect.objectContaining({ code: 'rule.compile.work_exceeded' }))
})

it('does not use a graph producers type for an element-local path', () => {
  const result = deriveCoreTypes({ all: [[[1]], { '+': [{ var: 'field.result' }, 1] }] }, 'local', () => new Set(['object']))
  expect([...result.types]).toEqual(['boolean'])
  expect(result.canError).toBe(true)
})
