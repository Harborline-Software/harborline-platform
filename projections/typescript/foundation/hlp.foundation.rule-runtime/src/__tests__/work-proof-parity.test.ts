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
  ['dynamic', [dynamic], undefined, '39321766454000', '11801253547021660190518'],
  ['two-compute', [dynamic, compute('second', 'second', 1)], undefined, '1179652993636394000', '708074912910306823677470648'],
  ['aggregate-validate', [dynamic, { ...compute('aggregate', 'check', { '==': [{ var: 'table.sum(items.amount)' }, 0] }), action: 'Validate' as const }], undefined, '1179652993636394000', '708148051395913279490506775'],
  ['row-two', [{ ...compute('row', 'items/calculated', { missing: [{ var: 'rowKeys' }] }), scope: 'Row' as const }], { ...DEFAULT_LIMITS, maxGraphNodes: 2, maxTableRowsPerAggregate: 2 }, '1179652502291836394', '1416149235951638144053181324'],
] as const)('pins the public %s proof for cross-tier calibration', (_, rules, limits, resultBytes, work) => {
  const proof = compile(rules, limits).workProof

  expect(proof.maximumResultBytes.toString()).toBe(resultBytes)
  expect(proof.maximumEvaluationWork.toString()).toBe(work)
})
