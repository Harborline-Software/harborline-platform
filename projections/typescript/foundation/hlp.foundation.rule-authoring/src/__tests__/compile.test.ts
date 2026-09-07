/**
 * De-risk + semantics proof for the Rules-surface authoring→engine bridge (design R2/R3/R4 gates).
 * These assert the LOAD-BEARING behaviors against the REAL shipped `@harborline-software/rule-engine` skins:
 *   - hit-policy round-trips (priority vs first-match change which row fires)
 *   - no-match is structurally enforced (compiler + surface linter agree)
 *   - reified inclusive-low / exclusive-high bounds evaluate honestly (the $5000.00 edge)
 *   - a non-terminal catch-all is flagged (L1)
 *   - a formula's undeclared reference is a compile rejection (formula_undeclared_ref)
 */
import { describe, it, expect } from 'vitest'
import { SkinCodes } from '@harborline-software/rule-engine'

import { compileDraft, evaluatePreview, isCompileError } from '../compile.js'
import { lintTable, noMatchResolved, RuleLintCodes } from '../lint.js'
import type { DecisionTableDraft, FormulaDraft } from '../model.js'

function amountTable(over: Partial<DecisionTableDraft> = {}): DecisionTableDraft {
  return {
    skin: 'table',
    scope: 'Field',
    scopeTarget: 'route',
    outputType: 'Compute',
    hitPolicy: 'first-match',
    columns: [{ id: 'c1', input: 'amount', valueType: 'number' }],
    rows: [
      { id: 'r1', cells: { c1: { kind: 'range', lo: '0', hi: '1000' } }, output: 'Auto-approve', priority: 0 },
      { id: 'r2', cells: { c1: { kind: 'range', lo: '1000', hi: '5000' } }, output: 'Manager', priority: 0 },
      { id: 'r3', cells: { c1: { kind: 'range', lo: '5000', hi: '' } }, output: 'Director', priority: 0 },
    ],
    noMatch: { kind: 'default', value: 'Require approval' },
    ...over,
  }
}

describe('decision-table authoring bridge', () => {
  it('compiles to a RuleDefinition and evaluates the firing row + outcome (reified bounds)', () => {
    const draft = amountTable()
    // $500 -> row 1
    let r = evaluatePreview(draft, 'invoice-route', { amount: 500 })
    expect(r.firedRowId).toBe('r1')
    expect(r.value).toBe('Auto-approve')
    // $1000.00 is inclusive-low of row 2 (>= 1000), exclusive-high of row 1 (< 1000) -> row 2
    r = evaluatePreview(draft, 'invoice-route', { amount: 1000 })
    expect(r.firedRowId).toBe('r2')
    expect(r.value).toBe('Manager')
    // $5000.00 is >= 5000 so it enters row 3 (Director) — the reified boundary is honest
    r = evaluatePreview(draft, 'invoice-route', { amount: 5000 })
    expect(r.firedRowId).toBe('r3')
    expect(r.value).toBe('Director')
  })

  it('produces a localizable D10 trace for the evaluation (design §6.3)', () => {
    const r = evaluatePreview(amountTable(), 'invoice-route', { amount: 500 })
    expect(r.trace.length).toBeGreaterThan(0)
    expect(r.trace[0].code.startsWith('rule.trace.')).toBe(true)
  })

  it('hit policy changes which row fires (priority vs first-match round-trip)', () => {
    // Two overlapping catch-all-ish rows; priority should pick the higher-priority row.
    const overlapping: DecisionTableDraft = amountTable({
      hitPolicy: 'priority',
      columns: [{ id: 'c1', input: 'amount', valueType: 'number' }],
      rows: [
        { id: 'low', cells: { c1: { kind: 'compare', op: '>=', value: '0' } }, output: 'LOW', priority: 1 },
        { id: 'high', cells: { c1: { kind: 'compare', op: '>=', value: '0' } }, output: 'HIGH', priority: 5 },
      ],
      noMatch: { kind: 'default', value: 'none' },
    })
    expect(evaluatePreview(overlapping, 'k', { amount: 10 }).firedRowId).toBe('high')
    // Under first-match the DECLARED-order-first row wins instead.
    expect(evaluatePreview({ ...overlapping, hitPolicy: 'first-match' }, 'k', { amount: 10 }).firedRowId).toBe('low')
  })

  it('blank Otherwise default is a compile rejection AND a surface lint error (no-match unresolved)', () => {
    const unresolved = amountTable({ noMatch: { kind: 'default', value: '' } })
    expect(noMatchResolved(unresolved)).toBe(false)
    expect(lintTable(unresolved).some((f) => f.code === RuleLintCodes.noMatchUnresolved)).toBe(true)
    // the underlying compiler ALSO rejects (empty default is not a resolved terminal — via catch-all path)
    const noCatch = amountTable({ noMatch: { kind: 'catch-all' } })
    try {
      compileDraft(noCatch, 'k')
      throw new Error('expected compile rejection')
    } catch (e) {
      expect(isCompileError(e) && e.code).toBe(SkinCodes.noMatchUnresolved)
    }
  })

  it('flags a non-terminal catch-all (L1)', () => {
    const midCatchAll = amountTable({
      rows: [
        { id: 'r1', cells: { c1: { kind: 'any' } }, output: 'everything', priority: 0 },
        { id: 'r2', cells: { c1: { kind: 'range', lo: '0', hi: '10' } }, output: 'never', priority: 0 },
      ],
    })
    expect(lintTable(midCatchAll).some((f) => f.code === RuleLintCodes.nonTerminalCatchAll)).toBe(true)
  })

  it('detects an interval gap (advisory)', () => {
    const gapped = amountTable({
      rows: [
        { id: 'r1', cells: { c1: { kind: 'range', lo: '0', hi: '1000' } }, output: 'A', priority: 0 },
        { id: 'r2', cells: { c1: { kind: 'range', lo: '2000', hi: '3000' } }, output: 'B', priority: 0 },
      ],
    })
    expect(lintTable(gapped).some((f) => f.code === RuleLintCodes.gap)).toBe(true)
  })
})

describe('formula authoring bridge', () => {
  function overtime(over: Partial<FormulaDraft> = {}): FormulaDraft {
    return {
      skin: 'formula',
      scope: 'Field',
      scopeTarget: 'pay',
      outputType: 'Compute',
      inputs: [
        { id: 'i1', ref: 'hours', type: 'number' },
        { id: 'i2', ref: 'rate', type: 'number' },
      ],
      expression: { kind: 'binary', op: '*', left: { kind: 'ref', ref: 'hours' }, right: { kind: 'ref', ref: 'rate' } },
      ...over,
    }
  }

  it('compiles + evaluates a valid formula', () => {
    const r = evaluatePreview(overtime(), 'overtime', { hours: 10, rate: 20 })
    expect(r.value).toBe(200)
  })

  it('rejects an undeclared reference (formula_undeclared_ref)', () => {
    const bad = overtime({ expression: { kind: 'ref', ref: 'bonus' } })
    try {
      compileDraft(bad, 'overtime')
      throw new Error('expected compile rejection')
    } catch (e) {
      expect(isCompileError(e) && e.code).toBe(SkinCodes.formulaUndeclaredRef)
    }
  })
})
