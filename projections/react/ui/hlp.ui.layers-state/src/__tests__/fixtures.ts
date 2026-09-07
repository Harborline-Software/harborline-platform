import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

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
  const path = resolve(import.meta.dirname, `../../../../../../conformance/hlp.ui.layers-state/${name}`)
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
