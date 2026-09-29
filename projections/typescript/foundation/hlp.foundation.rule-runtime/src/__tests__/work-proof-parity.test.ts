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
