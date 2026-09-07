import { describe, expect, it } from 'vitest'

import { blankDraftFor, blankFormulaDraft, blankTableDraft } from '../seeds.js'

describe('blankTableDraft', () => {
  it('creates one authorable numeric amount column and an unresolved default row', () => {
    const draft = blankTableDraft()
    const column = draft.columns[0]!

    expect(draft).toMatchObject({
      skin: 'table',
      scope: 'Field',
      scopeTarget: 'outcome',
      outputType: 'Compute',
      hitPolicy: 'first-match',
      noMatch: { kind: 'default', value: '' },
    })
    expect(column).toMatchObject({ input: 'amount', valueType: 'number' })
    expect(draft.rows).toEqual([{ id: expect.any(String), cells: { [column.id]: { kind: 'range', lo: '0', hi: '' } }, output: '', priority: 0 }])
  })
})

describe('blankFormulaDraft', () => {
  it('creates an authorable formula with no inputs or expression', () => {
    expect(blankFormulaDraft()).toEqual({
      skin: 'formula',
      scope: 'Field',
      scopeTarget: 'outcome',
      outputType: 'Compute',
      inputs: [],
      expression: null,
    })
  })
})

describe('blankDraftFor', () => {
  it('dispatches each typed skin to its corresponding blank seed', () => {
    expect(blankDraftFor('table').skin).toBe('table')
    expect(blankDraftFor('formula').skin).toBe('formula')
  })

  it('characterizes current behavior for a runtime skin outside the typed union', () => {
    // characterizes current behavior — undocumented
    const parsedSkin: unknown = JSON.parse('"other"')

    expect(blankDraftFor(parsedSkin as 'table' | 'formula').skin).toBe('formula')
  })
})
