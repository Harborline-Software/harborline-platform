import type { Meta, StoryObj } from '@storybook/react-vite'
import { ReportAuthoringEditor, type ReportDraft } from '@harborline-software/ui-react'
const draft: ReportDraft = { tenant: 'tenant-r', key: 'report-a', version: '1.0.0', measures: [{ measure: 'finance.rent' }], parameters: [{ name: 'count', type: 'integer', default: { kind: 'integer', value: 42, type: 'integer', is_valid: true } }] }
const names: Record<string, string> = { draft: 'Report draft', refusals: 'Admission refusals', 'read-only': 'Read only', empty: 'From empty' }
const scenarioIds: Record<string, string> = { draft: 'report-authoring.draft', refusals: 'report-authoring.refusals', 'read-only': 'report-authoring.read-only', empty: 'report-authoring.empty' }
function ReportAuthoringScenario({ kind }: { kind: string }) {
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioIds[kind]}><header className="hl-gallery-heading"><h2>{names[kind]}</h2><p>A report draft with typed parameters and catalogue measure references.</p></header><div className="hl-gallery-stage"><ReportAuthoringEditor value={kind === 'empty' ? null : draft} readOnly={kind === 'read-only'} refusals={kind === 'refusals' ? [{ code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.rent', catalogue: 'Main catalogue' }] : []} resolveMeasures={async () => ['finance.rent']} onCreate={() => undefined} onChange={() => undefined} /></div></section>
}
const meta = { title: 'Platform/Report authoring', component: ReportAuthoringScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof ReportAuthoringScenario>
export default meta
type Story = StoryObj<typeof meta>
export const ReportDraftStory: Story = { name: 'Report draft', args: { kind: 'draft' } }
export const AdmissionRefusals: Story = { name: 'Admission refusals', args: { kind: 'refusals' } }
export const ReadOnly: Story = { name: 'Read only', args: { kind: 'read-only' } }
export const FromEmpty: Story = { name: 'From empty', args: { kind: 'empty' } }
