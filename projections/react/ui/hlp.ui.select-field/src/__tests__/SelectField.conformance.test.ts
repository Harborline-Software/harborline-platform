import { describe, expect, it } from 'vitest'

import { contractCases, qualityCases, sharedCases } from './fixtures'

describe('SelectField frozen conformance coverage', () => {
  it('consumes every revision-1 behavior case in contract order', () => {
    expect(sharedCases.map(value => value.id)).toEqual(contractCases.map(value => value.id))
  })

  it('loads every required quality fixture family', () => {
    expect(qualityCases.accessibilityCases.length).toBeGreaterThan(0)
    expect(qualityCases.internationalizationCases.length).toBeGreaterThan(0)
    expect(qualityCases.themingCases.length).toBeGreaterThan(0)
  })
})
