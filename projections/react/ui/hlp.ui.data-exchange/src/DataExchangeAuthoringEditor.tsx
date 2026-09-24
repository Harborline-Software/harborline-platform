import type { ReactNode } from 'react'
import type { DataExchangeAuthoringDraft, DataExchangeAuthoringEditorProps, DataExchangeMappingRow, DataExchangeOption } from './DataExchangeAuthoringEditor.types'

export const DATA_EXCHANGE_MAPPING_PROFILE = 'hl:tabular-mapping/v1'
export const DATA_EXCHANGE_MAPPING_SCHEMA = 'https://schemas.harborline.software/mapping/tabular/v1'
export const DATA_EXCHANGE_MAPPING_VERSION = '1.0.0'

export function emptyDataExchangeDraft(): DataExchangeAuthoringDraft {
  return { name: '', sourceCapability: '', connectorVersion: '', formatCapability: 'csv', secretReference: '', discoveredColumns: [], mappings: [], externalKeyColumns: [], replayPolicy: 'append', scheduleReference: '', referenceDataset: '', packDistribution: '', feedDistribution: '' }
}

function Options({ values }: { readonly values: readonly DataExchangeOption[] }) {
  return <>{values.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</>
}

function Section({ name, children }: { readonly name: string; readonly children: ReactNode }) {
  return <fieldset><legend>{name}</legend>{children}</fieldset>
}

const emptyMapping = (sourceColumn = ''): DataExchangeMappingRow => ({ sourceColumn, canonicalTarget: '', targetPointer: '', datatype: 'string', required: false, nullValue: '', defaultValue: '', separator: '', transform: '' })

export function DataExchangeAuthoringEditor({ value, catalogue, run, canCommit, canPublish = false, authoringRefusals = [], onChange, onDiscoverSource, onDryRun, onCommit, onSaveDraft, onPublish }: DataExchangeAuthoringEditorProps) {
  const set = <K extends keyof DataExchangeAuthoringDraft>(key: K, next: DataExchangeAuthoringDraft[K]) => onChange({ ...value, [key]: next })
  const replaceMapping = (index: number, mapping: DataExchangeMappingRow) => set('mappings', value.mappings.map((current, position) => position === index ? mapping : current))
  const commitEnabled = canCommit && run?.status === 'Ready' && !run.stale
  const formats = catalogue.formats ?? [{ id: 'csv', label: 'CSV' }]
  return <form className="hl-data-exchange-authoring" onSubmit={event => event.preventDefault()}>
    <label>Definition name<input aria-label="Definition name" value={value.name} onChange={event => set('name', event.currentTarget.value)} /></label>
    <Section name="Source">
      <label>Source capability<select aria-label="Source capability" value={value.sourceCapability} onChange={event => set('sourceCapability', event.currentTarget.value)}><option value="">Choose a capability</option><Options values={catalogue.sourceCapabilities} /></select></label>
      <label>Connector version<input aria-label="Connector version" value={value.connectorVersion} onChange={event => set('connectorVersion', event.currentTarget.value)} /></label>
      <label>Format<select aria-label="Format" value={value.formatCapability} onChange={event => set('formatCapability', event.currentTarget.value)}><option value="">Choose a format</option><Options values={formats} /></select></label>
      <label>Secret reference<input aria-label="Secret reference" value={value.secretReference} onChange={event => set('secretReference', event.currentTarget.value)} /></label>
      <button type="button" onClick={onDiscoverSource}>Discover source</button>
    </Section>
    <Section name="Discovered source shape">
      {value.discoveredColumns.map((column, index) => <label key={column.name}><input type="checkbox" aria-label={`Include ${column.name}`} checked={column.selected} onChange={event => set('discoveredColumns', value.discoveredColumns.map((current, position) => position === index ? { ...current, selected: event.currentTarget.checked } : current))} />{column.name}</label>)}
    </Section>
    <Section name="Mapping profile"><dl><dt>Profile</dt><dd>{DATA_EXCHANGE_MAPPING_PROFILE}</dd><dt>Schema</dt><dd>{DATA_EXCHANGE_MAPPING_SCHEMA}</dd><dt>Document version</dt><dd>{DATA_EXCHANGE_MAPPING_VERSION}</dd></dl></Section>
    <Section name="Canonical mappings">
      {value.mappings.map((mapping, index) => <div key={`${mapping.sourceColumn}-${index}`}>
        <select aria-label={`Mapping ${index + 1} source column`} value={mapping.sourceColumn} onChange={event => replaceMapping(index, { ...mapping, sourceColumn: event.currentTarget.value })}><option value="">Choose source column</option>{value.discoveredColumns.filter(column => column.selected).map(column => <option key={column.name} value={column.name}>{column.name}</option>)}</select>
        <select aria-label={`Mapping ${index + 1} canonical target`} value={mapping.canonicalTarget} onChange={event => replaceMapping(index, { ...mapping, canonicalTarget: event.currentTarget.value })}><option value="">Choose canonical target</option><Options values={catalogue.canonicalTargets} /></select>
        <input aria-label={`Mapping ${index + 1} target pointer`} value={mapping.targetPointer} onChange={event => replaceMapping(index, { ...mapping, targetPointer: event.currentTarget.value })} />
        <select aria-label={`Mapping ${index + 1} datatype`} value={mapping.datatype} onChange={event => replaceMapping(index, { ...mapping, datatype: event.currentTarget.value })}><Options values={catalogue.datatypes} /></select>
        <label><input type="checkbox" aria-label={`Mapping ${index + 1} required`} checked={mapping.required} onChange={event => replaceMapping(index, { ...mapping, required: event.currentTarget.checked })} />Required</label>
        <input aria-label={`Mapping ${index + 1} null`} value={mapping.nullValue} onChange={event => replaceMapping(index, { ...mapping, nullValue: event.currentTarget.value })} />
        <input aria-label={`Mapping ${index + 1} default`} value={mapping.defaultValue} onChange={event => replaceMapping(index, { ...mapping, defaultValue: event.currentTarget.value })} />
        <input aria-label={`Mapping ${index + 1} separator`} value={mapping.separator} onChange={event => replaceMapping(index, { ...mapping, separator: event.currentTarget.value })} />
        <select aria-label={`Mapping ${index + 1} transform`} value={mapping.transform} onChange={event => replaceMapping(index, { ...mapping, transform: event.currentTarget.value })}><option value="">No transform</option><Options values={catalogue.transforms} /></select>
        <button type="button" aria-label={`Remove mapping ${index + 1}`} onClick={() => set('mappings', value.mappings.filter((_, position) => position !== index))}>Remove</button>
      </div>)}
      <button type="button" onClick={() => set('mappings', [...value.mappings, emptyMapping(value.discoveredColumns.find(column => column.selected)?.name)])}>Add mapping</button>
    </Section>
    <label>External key columns<input aria-label="External key columns" value={value.externalKeyColumns.join(', ')} onChange={event => set('externalKeyColumns', event.currentTarget.value.split(',').map(item => item.trim()).filter(Boolean))} /></label>
    <label>Replay policy<select aria-label="Replay policy" value={value.replayPolicy} onChange={event => set('replayPolicy', event.currentTarget.value as DataExchangeAuthoringDraft['replayPolicy'])}><option value="append">Append</option><option value="overwrite">Overwrite</option><option value="append_dedup">Append and deduplicate</option></select></label>
    <label>Schedule reference<select aria-label="Schedule reference" value={value.scheduleReference} onChange={event => set('scheduleReference', event.currentTarget.value)}><option value="">Manual only</option><Options values={catalogue.schedules} /></select></label>
    <Section name="Reference set deliveries">
      <input aria-label="Reference dataset" value={value.referenceDataset} onChange={event => set('referenceDataset', event.currentTarget.value)} />
      <input aria-label="Pack distribution" value={value.packDistribution} onChange={event => set('packDistribution', event.currentTarget.value)} />
      <input aria-label="Feed distribution" value={value.feedDistribution} onChange={event => set('feedDistribution', event.currentTarget.value)} />
    </Section>
    <div><button type="button" onClick={onSaveDraft}>Save draft</button><button type="button" disabled={!canPublish || authoringRefusals.length > 0} onClick={() => canPublish && authoringRefusals.length === 0 && onPublish?.()}>Publish definition</button></div>
    {authoringRefusals.length > 0 && <Section name="Authoring refusals"><ul>{authoringRefusals.map(refusal => <li key={`${refusal.stage}:${refusal.code}`}><span>{refusal.stage}</span>: <a href={refusal.targetHref}>{refusal.code}</a></li>)}</ul></Section>}
    <div><button type="button" onClick={onDryRun}>Create dry run</button><button type="button" aria-label="Commit reviewed run" disabled={!commitEnabled} onClick={() => commitEnabled && onCommit()}>Commit reviewed run</button></div>
    {run && <Section name="Persisted run evidence"><dl><dt>Dry run</dt><dd>{run.dryRunId}</dd>{run.batchIdentity && <><dt>Batch identity</dt><dd>{run.batchIdentity}</dd></>}<dt>Status</dt><dd>{run.status}</dd><dt>Staleness</dt><dd>{run.stale ? 'Stale' : 'Current'}</dd><dt>Candidate checkpoint</dt><dd>{run.candidateCheckpoint}</dd></dl><p>Applied {run.census.applied}; skipped {run.census.skipped}; conflicted {run.census.conflicted}; rejected {run.census.rejected}; failed {run.census.failed}; halted {run.census.halted}</p><ul>{run.refusals.map(refusal => <li key={refusal}>{refusal}</li>)}</ul></Section>}
  </form>
}
