import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

export interface FixtureCase {
  readonly id: string
  readonly input: Record<string, unknown>
  readonly expected: Record<string, unknown> | string
}

interface FixtureDocument {
  readonly cases?: FixtureCase[]
  readonly accessibilityCases?: FixtureCase[]
  readonly internationalizationCases?: FixtureCase[]
  readonly themingCases?: FixtureCase[]
  readonly interactionCases?: FixtureCase[]
  readonly performanceCases?: FixtureCase[]
}

function readFixture(name: string): FixtureDocument {
  return JSON.parse(readFileSync(resolve(process.cwd(), `../../../../conformance/hlp.ui.scheduler/${name}`), 'utf8')) as FixtureDocument
}

function inject(cases: FixtureCase[]): FixtureCase[] {
  const raw = process.env.HARBORLINE_CONFORMANCE_FIXTURE
  if (!raw) return cases
  const value = JSON.parse(raw) as FixtureCase
  return cases.map(candidate => candidate.id === value.id ? value : candidate)
}

export const sharedCases = inject(readFixture('fixtures.yaml').cases ?? [])
const quality = readFixture('quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]
export const performanceCases = quality.performanceCases ?? []

export function fixture(cases: readonly FixtureCase[], id: string): FixtureCase {
  const result = cases.find(candidate => candidate.id === id)
  if (!result) throw new Error(`missing-neutral-fixture: ${id}`)
  return result
}
