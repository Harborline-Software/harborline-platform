import { describe, expect, it } from 'vitest'
import { lintTable, noMatchResolved, RuleLintCodes } from '../lint.js'
import type { DecisionTableDraft } from '../model.js'

function table(over: Partial<DecisionTableDraft> = {}): DecisionTableDraft {
  return {
    skin: 'table', scope: 'Field', scopeTarget: 'field:amount', outputType: 'Compute', hitPolicy: 'first-match',
    columns: [{ id: 'amount', input: 'amount', valueType: 'number' }],
    rows: [{ id: 'low', cells: { amount: { kind: 'range', lo: '0', hi: '10' } }, output: 'low', priority: 0 }],
    noMatch: { kind: 'default', value: 'other' },
    ...over,
  }
}

describe('noMatchResolved', () => {
  it('accepts a non-empty default and a terminal catch-all', () => {
    expect(noMatchResolved(table())).toBe(true)
    expect(noMatchResolved(table({ noMatch: { kind: 'catch-all' }, rows: [{ id: 'all', cells: {}, output: 'all', priority: 0 }] }))).toBe(true)
  })

  it('rejects empty defaults and missing terminal catch-alls', () => {
    expect(noMatchResolved(table({ noMatch: { kind: 'default', value: '  ' } }))).toBe(false)
    expect(noMatchResolved(table({ noMatch: { kind: 'catch-all' } }))).toBe(false)
  })
})

describe('lintTable', () => {
  it('reports structural and interval findings in stable order', () => {
    const findings = lintTable(table({
      noMatch: { kind: 'default', value: '' },
      rows: [
        { id: 'all', cells: {}, output: '', priority: 0 },
        { id: 'later', cells: { amount: { kind: 'range', lo: '20', hi: '30' } }, output: 'later', priority: 0 },
      ],
    }))
    expect(findings.map((f) => f.code)).toEqual([
      RuleLintCodes.noMatchUnresolved, RuleLintCodes.nonTerminalCatchAll, RuleLintCodes.emptyOutput,
    ])
  })

  it('reports numeric gaps and overlaps while ignoring invalid intervals', () => {
    const findings = lintTable(table({ rows: [
      { id: 'a', cells: { amount: { kind: 'range', lo: '0', hi: '10' } }, output: 'a', priority: 0 },
      { id: 'b', cells: { amount: { kind: 'range', lo: '5', hi: '8' } }, output: 'b', priority: 0 },
      { id: 'c', cells: { amount: { kind: 'range', lo: '20', hi: 'x' } }, output: 'c', priority: 0 },
    ] }))
    expect(findings.map((f) => f.code)).toContain(RuleLintCodes.overlap)
  })
})
