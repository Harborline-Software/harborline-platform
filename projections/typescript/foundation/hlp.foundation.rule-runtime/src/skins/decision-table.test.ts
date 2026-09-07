import { describe, expect, it } from 'vitest'
import { CompileError } from '../grammar.js'
import type { DecisionTableSkin } from './decision-table.js'
import { compileDecisionTable } from './decision-table.js'
import { SkinCodes } from './codes.js'

function skin(overrides: Partial<DecisionTableSkin> = {}): DecisionTableSkin {
  return {
    ruleId: 'discount',
    scope: 'Field',
    scopeTarget: 'total',
    action: 'Compute',
    hitPolicy: 'first-match',
    inputs: ['subtotal'],
    rows: [{ when: [{ kind: 'compare', op: '>=', value: 100 }], output: 10 }],
    noMatch: { kind: 'default', value: 0 },
    ...overrides,
  }
}

describe('compileDecisionTable', () => {
  it('lowers compare and bounded range cells to one multi-branch expression', () => {
    const result = compileDecisionTable(skin({
      inputs: ['subtotal', 'quantity'],
      rows: [
        {
          when: [
            { kind: 'compare', op: '>=', value: 100 },
            { kind: 'range', loInclusive: 2, hiExclusive: 5 },
          ],
          output: 'discount',
        },
      ],
      noMatch: { kind: 'default', value: 'none' },
    }))

    expect(result).toEqual({
      id: 'discount',
      tier: 'JsonLogic',
      scope: 'Field',
      scopeTarget: 'total',
      action: 'Compute',
      expression: {
        if: [
          {
            and: [
              { '>=': [{ var: 'subtotal' }, 100] },
              {
                and: [
                  { '>=': [{ var: 'quantity' }, 2] },
                  { '<': [{ var: 'quantity' }, 5] },
                ],
              },
            ],
          },
          'discount',
          'none',
        ],
      },
    })
  })

  it('orders priority rows descending while retaining declaration order for equal priorities', () => {
    const result = compileDecisionTable(skin({
      hitPolicy: 'priority',
      rows: [
        { when: [{ kind: 'any' }], output: 'first', priority: 2 },
        { when: [{ kind: 'any' }], output: 'highest', priority: 4 },
        { when: [{ kind: 'any' }], output: 'second', priority: 2 },
      ],
    }))

    expect(result.expression).toEqual({ if: [true, 'highest', true, 'first', true, 'second', 0] })
  })

  it('uses the final catch-all row as the explicit terminal else', () => {
    const result = compileDecisionTable(skin({
      rows: [
        { when: [{ kind: 'compare', op: '==', value: 'member' }], output: 'member-rate' },
        { when: [{ kind: 'any' }], output: 'standard-rate' },
      ],
      noMatch: { kind: 'catch-all' },
    }))

    expect(result.expression).toEqual({
      if: [{ '==': [{ var: 'subtotal' }, 'member'] }, 'member-rate', 'standard-rate'],
    })
  })

  it.each([
    [skin({ inputs: [] }), SkinCodes.decisionTableNoInputs],
    [skin({ rows: [] }), SkinCodes.decisionTableEmpty],
    [skin({ rows: [{ when: [], output: 1 }] }), SkinCodes.decisionTableBadRow],
    [skin({ rows: [{ when: [{ kind: 'compare', op: 'contains', value: 1 }], output: 1 }] }), SkinCodes.decisionTableBadCell],
    [skin({ hitPolicy: 'last-match' as DecisionTableSkin['hitPolicy'] }), SkinCodes.decisionTableInvalidHitPolicy],
    [skin({ noMatch: { kind: 'catch-all' } }), SkinCodes.noMatchUnresolved],
  ])('rejects invalid table input with %s', (input, code) => {
    try {
      compileDecisionTable(input)
      throw new Error('expected compilation to fail')
    } catch (error) {
      expect(error).toBeInstanceOf(CompileError)
      expect(error).toMatchObject({ code, ruleId: 'discount' })
    }
  })
})
