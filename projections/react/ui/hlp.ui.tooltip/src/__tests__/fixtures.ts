import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

export interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument {
  cases?: FixtureCase[]
  accessibilityCases?: FixtureCase[]
  internationalizationCases?: FixtureCase[]
  themingCases?: FixtureCase[]
  interactionCases?: FixtureCase[]
}

function readFixture(name: string): FixtureDocument {
  const path = resolve(process.cwd(), `../../../../conformance/hlp.ui.tooltip/${name}`)
  return JSON.parse(readFileSync(path, 'utf8')) as FixtureDocument
}

function withInjectedCase(cases: FixtureCase[]): FixtureCase[] {
  const injected = process.env.HARBORLINE_CONFORMANCE_FIXTURE
  if (!injected) return cases
  const value = JSON.parse(injected) as FixtureCase
  return cases.map(candidate => candidate.id === value.id ? value : candidate)
}

export const sharedCases = withInjectedCase(readFixture('fixtures.yaml').cases ?? [])

const quality = readFixture('quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]

export function fixture(cases: readonly FixtureCase[], id: string): FixtureCase {
  const value = cases.find(candidate => candidate.id === id)
  if (!value) throw new Error(`missing-neutral-fixture: ${id}`)
  return value
}
