import { afterEach, describe, expect, it, vi } from 'vitest'

import { makeLocalId, outputActionKind } from '../model.js'

afterEach(() => {
  vi.restoreAllMocks()
})

describe('outputActionKind', () => {
  it('preserves every typed authoring output', () => {
    expect(outputActionKind('Compute')).toBe('Compute')
    expect(outputActionKind('Options')).toBe('Options')
    expect(outputActionKind('Validate')).toBe('Validate')
    expect(outputActionKind('Visibility')).toBe('Visibility')
  })
})

describe('makeLocalId', () => {
  it('uses the supplied prefix and the generated base-36 suffix', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.5)

    expect(makeLocalId('column')).toBe('column-i')
  })

  it('retains an empty, typed prefix', () => {
    vi.spyOn(Math, 'random').mockReturnValue(0.5)

    expect(makeLocalId('')).toBe('-i')
  })
})
