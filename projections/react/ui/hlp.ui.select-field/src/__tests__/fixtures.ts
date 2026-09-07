import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

interface FixtureDocument { cases: Array<{ id: string }> }
interface QualityDocument {
  accessibilityCases: Array<{ id: string }>
  internationalizationCases: Array<{ id: string }>
  themingCases: Array<{ id: string }>
}

const root = resolve(import.meta.dirname, '../../../../../../')
const read = <T>(path: string): T => JSON.parse(readFileSync(resolve(root, path), 'utf8')) as T

export const sharedCases = read<FixtureDocument>('conformance/hlp.ui.select-field/fixtures.yaml').cases
export const contractCases = read<FixtureDocument>('specs/modules/ui/hlp.ui.select-field/interface.yaml').cases
export const qualityCases = read<QualityDocument>('conformance/hlp.ui.select-field/quality-fixtures.yaml')
