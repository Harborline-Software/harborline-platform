import { expect, it } from 'vitest'

import { compile } from '../compiler.js'
import { DEFAULT_LIMITS } from '../limits.js'
import type { Json, RuleDefinition } from '../model.js'

function compute(id: string, target: string, expression: Json): RuleDefinition {
  return { id, tier: 'JsonLogic', scope: 'Field', scopeTarget: target, action: 'Compute', expression }
}

const dynamic = compute('dynamic', 'result', { missing: [{ var: 'keys' }] })

it.each([
  ['empty', [], undefined, '16394000', '0'],
  ['literal', [compute('literal', 'x', 1)], undefined, '16394000', '130'],
  ['dynamic', [dynamic], undefined, '1310725002', '398373634780430518'],
  ['two-compute', [dynamic, compute('second', 'second', 1)], undefined, '1310725002', '796747264317950648'],
  ['aggregate-validate', [dynamic, { ...compute('aggregate', 'check', { '==': [{ var: 'table.sum(items.amount)' }, 0] }), action: 'Validate' as const }], undefined, '1310725002', '796829528653110775'],
  ['row-two', [{ ...compute('row', 'items/calculated', { missing: [{ var: 'rowKeys' }] }), scope: 'Row' as const }], { ...DEFAULT_LIMITS, maxGraphNodes: 2, maxTableRowsPerAggregate: 2 }, '1310725002', '1573833540185021324'],
  // T-818: the smallest dynamic Row program over the default static work ceiling, compiled under a permissive ceiling.
  ['row-26-over-default', Array.from({ length: 26 }, (_, i) => ({ ...compute(`row${i}`, `items/c${i}`, { missing: [{ var: 'rowKeys' }] }), scope: 'Row' as const })), { ...DEFAULT_LIMITS, maxStaticWork: 10n ** 27n }, '1310725002', '103577143680027831354424000'],
] as const)('pins the public %s proof for cross-tier calibration', (_, rules, limits, resultBytes, work) => {
  const proof = compile(rules, limits).workProof

  expect(proof.maximumResultBytes.toString()).toBe(resultBytes)
  expect(proof.maximumEvaluationWork.toString()).toBe(work)
})

// T-772: one program per transfer family, so a changed literal size, operator transfer, static
// reference or fixed-point pass moves an exact public proof. The .NET twin is
// Compiler_pins_public_transfer_proofs_for_cross_tier_calibration with the same programs and values.
const rule = (id: string, target: string, expression: Json, scope: RuleDefinition['scope'] = 'Field',
  action: RuleDefinition['action'] = 'Compute'): RuleDefinition => ({ id, tier: 'JsonLogic', scope, scopeTarget: target, action, expression })
const noRows = { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 0 }
// A 1400-character JSON string literal (8402 proof bytes) exceeds one fold result (8197).
const longText = JSON.stringify('x'.repeat(1400))

it.each([
  // Declared out of dependency order, so static results need several fixed-point passes.
  ['field-chain-reversed', [rule('c', 'c', { var: 'b' }), rule('b', 'b', { var: 'a' }), rule('a', 'a', 1)], undefined, '16394000', '1766'],
  // A Validate rule's result never stands in for the field it validates.
  ['validate-then-read', [rule('v', 'amount', { '<': [{ var: 'amount' }, 10] }, 'Field', 'Validate'), rule('y', 'y', { var: 'amount' })], undefined, '16394000', '1574042'],
  ['row-chain-reversed', [rule('rb', 'items/b', { var: 'row.a' }, 'Row'), rule('ra', 'items/a', 1, 'Row')], undefined, '16394000', '1340660000'],
  // Two sections computing the same row column: a row.c reader takes the larger result.
  ['row-collision', [rule('r1', 'items/c', ['long', 'text'], 'Row'), rule('r2', 'lines/c', 1, 'Row'), rule('r3', 'items/d', { var: 'row.c' }, 'Row')], undefined, '16394000', '1341196000'],
  // A table admitting no rows still folds to one value (an empty sum is 0), so the aggregate keeps one fold result.
  ['empty-table-aggregate', [rule('x', 'x', { var: 'table.sum(items.amount)' })], noRows, '262144', '17506'],
  // Two Table rules computing one aggregate cell: a reader takes the larger result (above one fold result).
  ['table-collision', [rule('t1', 'items/sum/amount', longText, 'Table'), rule('t2', 'items/sum/amount', 1, 'Table'), rule('x', 'x', { var: 'table.sum(items.amount)' })], noRows, '262144', '118140'],
  // A computed Row, Table or Field target is only its own kind of static result.
  ['row-target-not-a-field', [rule('rc', 'items/c', 1, 'Row'), rule('y', 'y', { var: 'items/c' })], undefined, '16394000', '2414616'],
  ['table-target-not-a-row', [rule('t', 'items/sum/amount', 1, 'Table'), rule('rc', 'items/c', { var: 'row.sum/amount' }, 'Row')], undefined, '16394000', '4549280130'],
  ['row-target-not-a-table-cell', [rule('rs', 'items/sum', longText, 'Row'), rule('x', 'x', { agg: ['sum', 'items', 'undefined'] })], noRows, '262144', '84046838'],
  ['literals', [rule('l', 'l', { in: [{ var: 'k' }, [true, false, null, 'ab', 1.5, [], {}, { p: 1, qr: [true] }]] }), rule('o', 'o', { '==': [{ var: 'k' }, { p: 1, qr: [false] }] })], undefined, '16394000', '13114627062'],
  ['cat', [rule('c0', 'c0', { cat: [] }), rule('c2', 'c2', { cat: ['a', { var: 'k' }] })], undefined, '16394000', '22021438'],
  ['missing-keys', [rule('m', 'm', { missing: ['a', 'b'] }), rule('s', 's', { missing_some: [1, ['a', 'b']] })], undefined, '16394000', '2003311172'],
  // A computed first key of a multi-key missing is evaluated once.
  ['missing-computed-keys', [rule('m', 'm', { missing: [{ var: 'table.sum(items.amount)' }, 'b'] })], undefined, '32788004', '17931176452408'],
  ['missing-aggregate', [rule('m', 'm', { missing: { if: [{ var: 'table.sum(items.amount)' }, 'a', 'b'] } })], undefined, '81970005002', '49212329574195094826'],
  ['money', [rule('a', 'a', { 'money.add': ['1.00', '2.00'] }), rule('s', 's', { 'money.sub': ['1.00', '2.00', '3'] }), rule('m', 'm', { 'money.mul': ['1.5', '2'] })], undefined, '16394000', '235020984'],
  ['date-coding', [rule('a', 'a', { 'date.add': [{ 'date.today': [] }, 1, 'day'] }), rule('d', 'd', { 'date.diff': [{ var: 'x' }, { var: 'y' }] }), rule('c', 'c', { 'coding.is': [{ var: 'x' }, 'sys', 'code'] })], undefined, '16394000', '26220152086'],
  ['logic', [rule('l', 'l', { and: [{ '==': [1, 1] }, { '!': [{ var: 'a' }] }, { '<=': [1, 2] }, { '!==': [1, 2] }, { if: [true, 1, 2] }, { max: [1, 2] }, { in: ['a', 'abc'] }] })], undefined, '16394000', '1631522'],
] as const)('pins the public %s transfer proof for cross-tier calibration', (_, rules, limits, resultBytes, work) => {
  const proof = compile(rules as readonly RuleDefinition[], limits).workProof

  expect(proof.maximumResultBytes.toString()).toBe(resultBytes)
  expect(proof.maximumEvaluationWork.toString()).toBe(work)
})
