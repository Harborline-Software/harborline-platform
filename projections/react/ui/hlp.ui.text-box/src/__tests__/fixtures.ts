import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

interface FixtureCase { id: string }
interface FixtureDocument {
  cases?: FixtureCase[]
  accessibilityCases?: FixtureCase[]
  internationalizationCases?: FixtureCase[]
  themingCases?: FixtureCase[]
  interactionCases?: FixtureCase[]
}

function readFixture(name: string): FixtureDocument {
  return JSON.parse(readFileSync(resolve(process.cwd(), `../../../../conformance/hlp.ui.text-box/${name}`), 'utf8')) as FixtureDocument
}

export const sharedCases = readFixture('fixtures.yaml').cases ?? []
const quality = readFixture('quality-fixtures.yaml')
export const qualityCases = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]
