import type { Meta, StoryObj } from '@storybook/react-vite'
import { useState } from 'react'
import { DataExchangeAuthoringEditor, emptyDataExchangeDraft, type DataExchangeAuthoringCatalogue, type DataExchangeAuthoringDraft, type DataExchangeRunSummary } from '@harborline-software/ui-react'

type ScenarioId = 'data-exchange.authoring' | 'data-exchange.stale-review' | 'data-exchange.partial-failure' | 'data-exchange.from-empty'
const catalogue: DataExchangeAuthoringCatalogue = {
  sourceCapabilities: [{ id: 'files.csv', label: 'CSV file' }],
  formats: [{ id: 'csv', label: 'CSV' }],
  canonicalTargets: [{ id: 'records.customer/v1', label: 'Customer record v1' }],
  datatypes: [{ id: 'string', label: 'String' }],
  transforms: [{ id: 'trimToNull', label: 'Trim to null' }],
  schedules: [{ id: 'schedule.daily', label: 'Daily' }],
}
const draft: DataExchangeAuthoringDraft = {
  name: 'Customer intake', recordType: 'records.customer/v1', sourceCapability: 'files.csv', connectorVersion: '2.4.0', formatCapability: 'csv', secretReference: 'secretref:customer-intake',
  discoveredColumns: [{ name: 'CustomerNumber', selected: true }, { name: 'Email', selected: true }],
  mappings: [{ sourceColumn: 'CustomerNumber', canonicalTarget: 'records.customer/v1', targetPointer: '/customerNumber', datatype: 'string', required: true, nullValue: '', defaultValue: '', separator: '', transform: 'trimToNull' }],
  externalKeyColumns: ['CustomerNumber'], replayPolicy: 'append_dedup', scheduleReference: 'schedule.daily',
  referenceDataset: 'dataset.customers', packDistribution: 'pack://customers', feedDistribution: 'feed://customers',
}
const currentRun: DataExchangeRunSummary = { dryRunId: 'dry-42', status: 'Completed', stale: false, retentionClass: 'standard-7y', candidateCheckpoint: 'row:125', census: { applied: 0, skipped: 0, conflicted: 0, rejected: 0, failed: 0, halted: 0 }, refusals: [] }
const staleRun: DataExchangeRunSummary = { ...currentRun, stale: true, driftReason: 'proposal.changed', refusals: ['mapping.changed'] }
const partialRun: DataExchangeRunSummary = { dryRunId: 'review-81', commitRunId: 'commit-12', batchIdentity: 'batch-12', effectIdentity: 'effect-3', attemptId: 'attempt-2', status: 'CompletedWithRefusals', stale: true, driftReason: 'source.changed', retentionClass: 'standard-7y', candidateCheckpoint: 'row:19', census: { applied: 11, skipped: 2, conflicted: 3, rejected: 5, failed: 7, halted: 0 }, refusals: [] }
const copy: Record<ScenarioId, readonly [string, string]> = {
  'data-exchange.authoring': ['Current review', 'A bounded inbound definition maps selected source columns to a canonical Records contract and exposes an approved current review.'],
  'data-exchange.stale-review': ['Stale review', 'A proposal-affecting mapping change leaves the persisted review visible while commit fails closed.'],
  'data-exchange.partial-failure': ['Partial failure', 'Server review evidence retains its CompletedWithRefusals result, census, staleness, retention class and separate identity scopes as read-only text.'],
  'data-exchange.from-empty': ['From empty', 'An empty exchange authors its record type and all mapping column extensions after host-owned source discovery.'],
}
function DataExchangeScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const [value, setValue] = useState(() => scenarioId === 'data-exchange.from-empty' ? emptyDataExchangeDraft() : draft)
  const run = scenarioId === 'data-exchange.from-empty' ? undefined : scenarioId === 'data-exchange.partial-failure' ? partialRun : scenarioId === 'data-exchange.stale-review' ? staleRun : currentRun
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><DataExchangeAuthoringEditor value={value} catalogue={catalogue} run={run} canCommit onChange={setValue} onDiscoverSource={() => setValue(current => ({ ...current, discoveredColumns: draft.discoveredColumns }))} onDryRun={() => undefined} onCommit={() => undefined} /></div></section>
}
const meta = { title: 'Platform/Data Exchange', component: DataExchangeScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof DataExchangeScenario>
export default meta
type Story = StoryObj<typeof meta>
export const CurrentReview: Story = { name: 'Current review', args: { scenarioId: 'data-exchange.authoring' } }
export const StaleReview: Story = { name: 'Stale review', args: { scenarioId: 'data-exchange.stale-review' } }
export const PartialFailure: Story = { name: 'Partial failure', args: { scenarioId: 'data-exchange.partial-failure' } }
export const FromEmpty: Story = { name: 'From empty', args: { scenarioId: 'data-exchange.from-empty' } }
