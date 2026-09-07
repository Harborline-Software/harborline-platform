import { readFileSync } from 'node:fs'

export interface NeutralCase {
  id: string
  input?: unknown
  expected: unknown
}

interface NeutralFixture {
  cases?: NeutralCase[]
  accessibilityCases?: NeutralCase[]
  internationalizationCases?: NeutralCase[]
  themingCases?: NeutralCase[]
}

function readFixture(name: string): NeutralFixture {
  const url = new URL(`../../../../../../conformance/hlp.ui.tone-style/${name}`, import.meta.url)
  return JSON.parse(readFileSync(url, 'utf8')) as NeutralFixture
}

export const sharedCases = readFixture('fixtures.yaml').cases ?? []

const quality = readFixture('quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
]

export function fixture(cases: readonly NeutralCase[], id: string): NeutralCase {
  const value = cases.find(candidate => candidate.id === id)
  if (!value) throw new Error(`Missing neutral fixture: ${id}`)
  return value
}
