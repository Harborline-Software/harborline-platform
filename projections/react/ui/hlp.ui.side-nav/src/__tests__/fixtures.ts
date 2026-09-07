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
  const path = resolve(process.cwd(), `../../../../conformance/hlp.ui.side-nav/${name}`)
  return JSON.parse(readFileSync(path, 'utf8')) as FixtureDocument
}

export const sharedCases = readFixture('fixtures.yaml').cases ?? []
const quality = readFixture('quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]
