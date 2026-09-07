import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

export interface FixtureCase {
  readonly id: string
  readonly input?: Record<string, unknown>
  readonly expected?: Record<string, unknown> | string
}

interface FixtureDocument {
  readonly cases?: FixtureCase[]
  readonly accessibilityCases?: FixtureCase[]
  readonly internationalizationCases?: FixtureCase[]
  readonly themingCases?: FixtureCase[]
  readonly interactionCases?: FixtureCase[]
  readonly performanceCases?: FixtureCase[]
}

const root = resolve(import.meta.dirname, '../../../../../../')

function readFixture(relativePath: string): FixtureDocument {
  return JSON.parse(readFileSync(resolve(root, relativePath), 'utf8')) as FixtureDocument
}

function withInjectedCase(cases: FixtureCase[]): FixtureCase[] {
  const injected = process.env.HARBORLINE_CONFORMANCE_FIXTURE
  if (!injected) return cases
  const value = JSON.parse(injected) as FixtureCase
  return cases.map(candidate => candidate.id === value.id ? value : candidate)
}

export const sharedCases = withInjectedCase(readFixture('conformance/hlp.ui.chat/fixtures.yaml').cases ?? [])
export const contractCases = readFixture('specs/modules/ui/hlp.ui.chat/interface.yaml').cases ?? []
const quality = readFixture('conformance/hlp.ui.chat/quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]
export const performanceCases = quality.performanceCases ?? []

export function fixture(cases: readonly FixtureCase[], id: string): FixtureCase {
  const value = cases.find(candidate => candidate.id === id)
  if (!value) throw new Error(`missing-neutral-fixture: ${id}`)
  return value
}
