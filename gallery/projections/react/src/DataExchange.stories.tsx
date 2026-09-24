import type { Meta, StoryObj } from '@storybook/react-vite'
import { DataExchangeAuthoringEditor, type DataExchangeAuthoringCatalogue, type DataExchangeAuthoringDraft, type DataExchangeRunSummary } from '@harborline-software/ui-react'

type ScenarioId = 'data-exchange.authoring' | 'data-exchange.stale-review'
const catalogue: DataExchangeAuthoringCatalogue = {
  sourceCapabilities: [{ id: 'files.csv', label: 'CSV file' }],
  formats: [{ id: 'csv', label: 'CSV' }],
  canonicalTargets: [{ id: 'records.customer/v1', label: 'Customer record v1' }],
  datatypes: [{ id: 'string', label: 'String' }],
  transforms: [{ id: 'trimToNull', label: 'Trim to null' }],
  schedules: [{ id: 'schedule.daily', label: 'Daily' }],
}
const draft: DataExchangeAuthoringDraft = {
  name: 'Customer intake', sourceCapability: 'files.csv', connectorVersion: '2.4.0', formatCapability: 'csv', secretReference: 'secretref:customer-intake',
  discoveredColumns: [{ name: 'CustomerNumber', selected: true }, { name: 'Email', selected: true }],
  mappings: [{ sourceColumn: 'CustomerNumber', canonicalTarget: 'records.customer/v1', targetPointer: '/customerNumber', datatype: 'string', required: true, nullValue: '', defaultValue: '', separator: '', transform: 'trimToNull' }],
  externalKeyColumns: ['CustomerNumber'], replayPolicy: 'append_dedup', scheduleReference: 'schedule.daily',
  referenceDataset: 'dataset.customers', packDistribution: 'pack://customers', feedDistribution: 'feed://customers',
}
const currentRun: DataExchangeRunSummary = { dryRunId: 'dry-42', status: 'Ready', stale: false, candidateCheckpoint: 'row:125', census: { applied: 0, skipped: 0, conflicted: 0, rejected: 0, failed: 0, halted: 0 }, refusals: [] }
const staleRun: DataExchangeRunSummary = { ...currentRun, stale: true, status: 'Stale', refusals: ['mapping.changed'] }
const copy: Record<ScenarioId, readonly [string, string]> = {
  'data-exchange.authoring': ['Current review', 'A bounded inbound definition maps selected source columns to a canonical Records contract and exposes an approved current review.'],
  'data-exchange.stale-review': ['Stale review', 'A proposal-affecting mapping change leaves the persisted review visible while commit fails closed.'],
}
function DataExchangeScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><DataExchangeAuthoringEditor value={draft} catalogue={catalogue} run={scenarioId === 'data-exchange.stale-review' ? staleRun : currentRun} canCommit onChange={() => undefined} onDiscoverSource={() => undefined} onDryRun={() => undefined} onCommit={() => undefined} /></div></section>
}
const meta = { title: 'Platform/Data Exchange', component: DataExchangeScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof DataExchangeScenario>
export default meta
type Story = StoryObj<typeof meta>
export const CurrentReview: Story = { name: 'Current review', args: { scenarioId: 'data-exchange.authoring' } }
export const StaleReview: Story = { name: 'Stale review', args: { scenarioId: 'data-exchange.stale-review' } }
