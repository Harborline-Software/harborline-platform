/**
 * ADR 0146 D2 — decision-table + formula authoring-skin COMPILE rejections + happy-path lowering (TS tier).
 * Byte-identical routing outcomes live in the shared conformance corpus; this asserts the fail-closed
 * rejections (which throw at compile) and the exact lowered shape. Mirror of the .NET `SkinTests`.
 */
import { describe, it, expect } from 'vitest'

import { CompileError } from '../grammar.js'
import { write } from '../canonical.js'
import type { Json } from '../model.js'
import {
  compileDecisionTable,
  compileFormula,
  SkinCodes,
  type DecisionRow,
  type DecisionTableSkin,
  type NoMatch,
} from '../skins/index.js'

function minimalTable(over: Partial<DecisionTableSkin> = {}): DecisionTableSkin {
  return {
    ruleId: 't',
    scope: 'Field',
    scopeTarget: 'out',
    action: 'Compute',
    hitPolicy: 'first-match',
    inputs: ['x'],
    rows: [{ when: [{ kind: 'compare', op: '>=', value: 1 }], output: 'hi' }],
    noMatch: { kind: 'default', value: 'lo' },
    ...over,
  }
}

function codeOf(fn: () => unknown): string {
  try {
    fn()
  } catch (e) {
    if (e instanceof CompileError) return e.code
    throw e
  }
  throw new Error('expected a CompileError, but compile succeeded')
}

describe('ADR 0146 D2 skins — decision-table rejections', () => {
  it('rejects no inputs', () => {
    expect(codeOf(() => compileDecisionTable(minimalTable({ inputs: [] })))).toBe(SkinCodes.decisionTableNoInputs)
  })
  it('rejects no rows', () => {
    expect(codeOf(() => compileDecisionTable(minimalTable({ rows: [] })))).toBe(SkinCodes.decisionTableEmpty)
  })
  it('rejects a ragged row', () => {
    const rows: DecisionRow[] = [{ when: [{ kind: 'any' }, { kind: 'any' }], output: 'x' }]
    expect(codeOf(() => compileDecisionTable(minimalTable({ rows })))).toBe(SkinCodes.decisionTableBadRow)
  })
  it('rejects a bad compare operator', () => {
    const rows: DecisionRow[] = [{ when: [{ kind: 'compare', op: '~=', value: 1 }], output: 'x' }]
    expect(codeOf(() => compileDecisionTable(minimalTable({ rows })))).toBe(SkinCodes.decisionTableBadCell)
  })
  it('rejects no explicit no-match (neither default nor catch-all)', () => {
    // an unsound NoMatch object (neither branch) — the compiler must reject, never emit a silent null.
    const noMatch = {} as unknown as NoMatch
    expect(codeOf(() => compileDecisionTable(minimalTable({ noMatch })))).toBe(SkinCodes.noMatchUnresolved)
  })
  it('rejects catch-all required but absent', () => {
    const rows: DecisionRow[] = [{ when: [{ kind: 'compare', op: '>=', value: 1 }], output: 'hi' }]
    expect(codeOf(() => compileDecisionTable(minimalTable({ rows, noMatch: { kind: 'catch-all' } })))).toBe(SkinCodes.noMatchUnresolved)
  })
})

describe('ADR 0146 D2 skins — decision-table happy path (exact lowered shape)', () => {
  it('lowers a priority table to a priority-ordered if cascade', () => {
    const skin: DecisionTableSkin = {
      ruleId: 'tier',
      scope: 'Field',
      scopeTarget: 'tier',
      action: 'Compute',
      hitPolicy: 'priority',
      inputs: ['score'],
      rows: [
        { when: [{ kind: 'compare', op: '>=', value: 80 }], output: 'B', priority: 1 },
        { when: [{ kind: 'compare', op: '>=', value: 90 }], output: 'A', priority: 2 },
        { when: [{ kind: 'any' }], output: 'F', priority: 0 },
      ],
      noMatch: { kind: 'catch-all' },
    }
    const rule = compileDecisionTable(skin)
    const expected: Json = {
      if: [
        { '>=': [{ var: 'score' }, 90] }, 'A',
        { '>=': [{ var: 'score' }, 80] }, 'B',
        'F',
      ],
    }
    expect(write(rule.expression as Json)).toBe(write(expected))
  })

  it('reifies a range upper bound to and(>=lo, <hi)', () => {
    const skin = minimalTable({
      rows: [{ when: [{ kind: 'range', loInclusive: 0, hiExclusive: 100 }], output: 'in' }],
      noMatch: { kind: 'default', value: 'out' },
    })
    const rule = compileDecisionTable(skin)
    const expected: Json = {
      if: [
        { and: [{ '>=': [{ var: 'x' }, 0] }, { '<': [{ var: 'x' }, 100] }] }, 'in',
        'out',
      ],
    }
    expect(write(rule.expression as Json)).toBe(write(expected))
  })
})

describe('ADR 0146 D2 skins — formula', () => {
  it('rejects an empty expression', () => {
    expect(codeOf(() => compileFormula({ ruleId: 'f', scope: 'Field', scopeTarget: 'out', action: 'Compute', inputs: [], expression: null })))
      .toBe(SkinCodes.formulaEmpty)
  })
  it('rejects an undeclared reference', () => {
    const expression: Json = { '*': [{ var: 'qty' }, { var: 'price' }] }
    const code = codeOf(() => compileFormula({
      ruleId: 'f', scope: 'Field', scopeTarget: 'total', action: 'Compute',
      inputs: [{ ref: 'qty', type: 'number' }], expression,
    }))
    expect(code).toBe(SkinCodes.formulaUndeclaredRef)
  })
  it('compiles when all refs are declared', () => {
    const expression: Json = { '*': [{ var: 'qty' }, { var: 'price' }] }
    const rule = compileFormula({
      ruleId: 'f', scope: 'Field', scopeTarget: 'total', action: 'Compute',
      inputs: [{ ref: 'qty', type: 'number' }, { ref: 'price', type: 'number' }], expression,
    })
    expect(rule.action).toBe('Compute')
    expect(write(rule.expression as Json)).toBe(write(expression))
  })
})
