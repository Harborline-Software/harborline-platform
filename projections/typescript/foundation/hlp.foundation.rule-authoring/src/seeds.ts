/**
 * Blank authoring seeds for the Rules front-door "New" flow (design §1.2) — never a truly empty
 * editor (predefined-first): a blank table lands with one numeric column + one interval row + an
 * unresolved Otherwise (the author fills it — §2.3), a blank formula with no inputs + no expression.
 */

import type { DecisionTableDraft, FormulaDraft, RuleSkinType } from './model.js'
import { makeLocalId } from './model.js'

/** A minimal, authorable decision table: one numeric `amount` column, one interval row, an
 * unresolved default (author must fill before publish — the F1 gate is honest from the start). */
export function blankTableDraft(): DecisionTableDraft {
  const colId = makeLocalId('col')
  return {
    skin: 'table',
    scope: 'Field',
    scopeTarget: 'outcome',
    outputType: 'Compute',
    hitPolicy: 'first-match',
    columns: [{ id: colId, input: 'amount', valueType: 'number' }],
    rows: [{ id: makeLocalId('row'), cells: { [colId]: { kind: 'range', lo: '0', hi: '' } }, output: '', priority: 0 }],
    noMatch: { kind: 'default', value: '' },
  }
}

/** A minimal formula: no declared inputs, no expression yet. */
export function blankFormulaDraft(): FormulaDraft {
  return {
    skin: 'formula',
    scope: 'Field',
    scopeTarget: 'outcome',
    outputType: 'Compute',
    inputs: [],
    expression: null,
  }
}

/** The blank seed for a given skin type. */
export function blankDraftFor(skin: Extract<RuleSkinType, 'table' | 'formula'>): DecisionTableDraft | FormulaDraft {
  return skin === 'table' ? blankTableDraft() : blankFormulaDraft()
}
