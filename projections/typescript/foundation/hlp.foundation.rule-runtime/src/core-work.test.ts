import { describe, expect, it } from 'vitest'

import { deriveCoreWork, deriveGraphWork } from './core-work.js'
import { DEFAULT_LIMITS } from './limits.js'
import type { Json, RuleDefinition } from './model.js'

const proofOf = (node: Json) => deriveCoreWork(node, 'work-proof-boundary')

describe('deriveCoreWork', () => {
  it('sizes boolean, number, array, and object literals conservatively', () => {
    expect(proofOf(true)).toMatchObject({ result: 4n, work: 5n })
    expect(proofOf(false)).toMatchObject({ result: 5n, work: 6n })
    expect(proofOf(1)).toMatchObject({ result: 32n, work: 33n })
    expect(proofOf([true, 1])).toMatchObject({ result: 39n, work: 40n })
    expect(proofOf({ x: true, y: 1 })).toMatchObject({ result: 57n, work: 58n })
  })

  it('charges repeated missing-key reads and missing_some row-key probes', () => {
    expect(proofOf({ missing: 'name' })).toMatchObject({
      result: 135002n, work: 135055n, reads: 5000n, aggregateReads: 0n,
    })
    expect(proofOf({ missing_some: [{ var: 'threshold' }, ['row.amount', 'row.other']] })).toMatchObject({
      result: 610002n, work: 872503n, reads: 5001n,
    })
    expect(proofOf({ missing: { agg: ['sum', 'items', 'amount'] } })).toMatchObject({
      result: 1310725002n, work: 1310725371n, reads: 5002n, aggregateReads: 2n,
    })
  })

  it('assigns the documented transfer bounds to every closed operator family', () => {
    expect(proofOf({ '!==': [1, 2] })).toMatchObject({ result: 5n, work: 131n })
    expect(proofOf({ '<=': [1, 2] })).toMatchObject({ result: 5n, work: 131n })
    expect(proofOf({ if: [true, false, true] })).toMatchObject({ result: 5n, work: 17n })
    expect(proofOf({ in: [1, [2, 3]] })).toMatchObject({ result: 5n, work: 500102n })
    expect(proofOf({ cat: [] })).toMatchObject({ result: 2n, work: 1n })
    expect(proofOf({ 'money.add': ['1', '2'] })).toMatchObject({ result: 8198n, work: 33570863n })
    expect(proofOf({ 'money.mul': ['1', '2'] })).toMatchObject({ result: 4100n, work: 33562667n })
    expect(proofOf({ 'date.diff': [] })).toMatchObject({ result: 32n, work: 1n })
    expect(proofOf({ 'date.today': [] })).toMatchObject({ result: 12n, work: 1n })
    expect(proofOf({ 'coding.is': [1, 'system', 'code'] })).toMatchObject({ result: 5n, work: 485100n })
  })

  it('uses the most expensive cell when bounding resolver demand', () => {
    const source: RuleDefinition = {
      id: 'work.read', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'output', action: 'Compute', expression: { var: 'input' },
    }
    const proof = deriveGraphWork([{ source, ast: source.expression as Json }], { ...DEFAULT_LIMITS, maxGraphNodes: 1 })

    expect(proof).toEqual({ maximumResultBytes: 16394000n, maximumEvaluationWork: 524558n })
  })

  it('keeps a dynamic demand seed at zero cells and composes it for one cell', () => {
    const source: RuleDefinition = {
      id: 'work.dynamic', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'output', action: 'Compute', expression: { var: 'input' },
    }
    const limits = { ...DEFAULT_LIMITS, maxAstNodes: 0, maxTableRowsPerAggregate: 0 }
    const zeroCells = deriveGraphWork([{ source, ast: source.expression as Json, references: [{ kind: 'dynamic-read' }] }], { ...limits, maxGraphNodes: 0 })
    const oneCell = deriveGraphWork([{ source, ast: source.expression as Json, references: [{ kind: 'dynamic-read' }] }], { ...limits, maxGraphNodes: 1 })

    expect(zeroCells).toEqual({ maximumResultBytes: 262144n, maximumEvaluationWork: 524424n })
    expect(oneCell).toEqual({ maximumResultBytes: 7864320000n, maximumEvaluationWork: 471874929164424n })
  })
})
