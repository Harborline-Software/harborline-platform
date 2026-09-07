import { readFileSync } from 'node:fs'

export interface NeutralCase {
  id: string
  input?: unknown
  expected: unknown
}

interface NeutralFixture {
  schemaVersion: number
  moduleId: string
  cases?: NeutralCase[]
  accessibilityCases?: NeutralCase[]
  internationalizationCases?: NeutralCase[]
  themingCases?: NeutralCase[]
}

function readFixture(relativePath: string): NeutralFixture {
  const path = new URL(`../../../../../../conformance/hlp.ui.aspect-lens/${relativePath}`, import.meta.url)
  return JSON.parse(readFileSync(path, 'utf8')) as NeutralFixture
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
