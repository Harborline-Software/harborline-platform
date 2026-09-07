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
}

// Resolved from process.cwd() rather than import.meta.dirname, matching hlp.ui.dialog: vitest runs
// with the module directory as cwd, so this is four levels to the repository root. Counting from
// the test file instead would need five and silently resolves to projections/conformance, which
// does not exist — the read fails with a path nobody wrote.
function read(name: string): FixtureDocument {
  return JSON.parse(
    readFileSync(resolve(process.cwd(), `../../../../conformance/hlp.ui.confirm-dialog/${name}`), 'utf8'),
  ) as FixtureDocument
}

// Read the frozen fixtures rather than restating them here. A test file that redeclares its own
// cases cannot detect a fixture change, which is the point of freezing them.
export const fixtureCases: FixtureCase[] = read('fixtures.yaml').cases ?? []
export const fixtureIds = fixtureCases.map(value => value.id)

const quality = read('quality-fixtures.yaml')
export const qualityCases: FixtureCase[] = [
  ...(quality.accessibilityCases ?? []),
  ...(quality.internationalizationCases ?? []),
  ...(quality.themingCases ?? []),
  ...(quality.interactionCases ?? []),
]
export const qualityIds = qualityCases.map(value => value.id)
