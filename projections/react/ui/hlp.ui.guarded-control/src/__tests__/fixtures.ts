import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
const root = resolve(import.meta.dirname, '../../../../../../')
export const sharedCases = (JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.guarded-control/fixtures.yaml'), 'utf8')) as { cases: Array<{ id: string }> }).cases
export const contractCases = (JSON.parse(readFileSync(resolve(root, 'specs/modules/ui/hlp.ui.guarded-control/interface.yaml'), 'utf8')) as { cases: Array<{ id: string }> }).cases

// The cross-lane duration fixture: the Blazor lane replays the same file (GuardedControlAnnouncementTests).
export type DurationStep = 'arm' | 'tick' | 'escape'
export interface DurationExpectation { status: string; timer: string }
export interface DurationCase {
  id: string
  locale: string
  label: string
  catalog: Record<string, string>
  hostNumberingSystem?: string
  steps: DurationStep[]
  expected: DurationExpectation
  divergence?: { ticket: string; lane: 'react' | 'blazor'; reason: string; expected: DurationExpectation }
}
export const durationCases = (JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.guarded-control/duration-v1.json'), 'utf8')) as { cases: DurationCase[] }).cases
