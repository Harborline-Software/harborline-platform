import { useEffect, useRef, useState, type ReactNode } from 'react'
import type { DataExchangeAuthoringDraft, DataExchangeAuthoringEditorProps, DataExchangeCatalogueOption, DataExchangeMappingRow } from './DataExchangeAuthoringEditor.types'

export const DATA_EXCHANGE_MAPPING_PROFILE = 'hl:tabular-mapping/v1'
export const DATA_EXCHANGE_MAPPING_SCHEMA = 'https://schemas.harborline.software/mapping/tabular/v1'
export const DATA_EXCHANGE_MAPPING_VERSION = '1.0.0'

export function emptyDataExchangeDraft(): DataExchangeAuthoringDraft {
  return { name: '', sourceCapability: '', connectorVersion: '', formatCapability: 'csv', secretReference: '', discoveredColumns: [], mappings: [], externalKeyColumns: [], replayPolicy: 'append', scheduleReference: '', referenceDataset: '', packDistribution: '', feedDistribution: '' }
}

function Options({ values }: { readonly values: readonly DataExchangeCatalogueOption[] }) {
  return <>{values.map(entry => typeof entry === 'string' ? { id: entry, label: entry } : entry).map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</>
}

function Section({ name, children }: { readonly name: string; readonly children: ReactNode }) {
  return <fieldset><legend>{name}</legend>{children}</fieldset>
}

const revisionKey = (value: DataExchangeAuthoringDraft) => `${value.identity ?? ''}\u0000${value.expectedRevision ?? ''}`
const authoredContent = ({ identity: _identity, expectedRevision: _revision, ...content }: DataExchangeAuthoringDraft) => JSON.stringify(content)

const emptyMapping = (sourceColumn = ''): DataExchangeMappingRow => ({ sourceColumn, canonicalTarget: '', targetPointer: '', datatype: 'string', required: false, nullValue: '', defaultValue: '', separator: '', transform: '' })

export function DataExchangeAuthoringEditor({ value: host, catalogue, run, canCommit, canPublish = false, readOnly = false, authoringRefusals = [], onChange, onDiscoverSource, onDryRun, onCommit, onSaveDraft, onPublish }: DataExchangeAuthoringEditorProps) {
  const [value, setValue] = useState(host)
  const [incoming, setIncoming] = useState<DataExchangeAuthoringDraft>()
  const loaded = useRef(host)
  const dirty = useRef(false)
  // ADR 0096 pending edits: a new identity or revision never silently overwrites local edits.
  useEffect(() => {
    const revisionChanged = revisionKey(host) !== revisionKey(loaded.current)
    loaded.current = host
    if (revisionChanged && dirty.current && authoredContent(host) !== authoredContent(value)) { setIncoming(host); return }
    if (revisionChanged) dirty.current = false
    setIncoming(undefined)
    setValue(host)
  }, [host])
  const pending = incoming !== undefined
  const intentsEnabled = !readOnly && !pending
  const set = <K extends keyof DataExchangeAuthoringDraft>(key: K, next: DataExchangeAuthoringDraft[K]) => {
    if (readOnly) return
    const draft = { ...value, [key]: next }
    dirty.current = true
    setValue(draft)
    onChange(draft)
  }
  const keepEdits = () => setIncoming(undefined)
  const discardEdits = () => { if (!incoming) return; dirty.current = false; setValue(incoming); setIncoming(undefined); onChange(incoming) }
  const replaceMapping = (index: number, mapping: DataExchangeMappingRow) => set('mappings', value.mappings.map((current, position) => position === index ? mapping : current))
  const commitEnabled = intentsEnabled && canCommit && run?.status === 'Ready' && !run.stale
  const publishEnabled = intentsEnabled && canPublish && authoringRefusals.length === 0
  const formats = catalogue.formats ?? [{ id: 'csv', label: 'CSV' }]
  return <form className="hl-data-exchange-authoring" onSubmit={event => event.preventDefault()}>
    {incoming && <aside role="alert">Revision changed.<button type="button" onClick={keepEdits}>Keep edits</button><button type="button" onClick={discardEdits}>Discard edits</button></aside>}
    <label>Definition name<input disabled={readOnly} aria-label="Definition name" value={value.name} onChange={event => set('name', event.currentTarget.value)} /></label>
    <Section name="Source">
      <label>Source capability<select disabled={readOnly} aria-label="Source capability" value={value.sourceCapability} onChange={event => set('sourceCapability', event.currentTarget.value)}><option value="">Choose a capability</option><Options values={catalogue.sourceCapabilities} /></select></label>
      <label>Connector version<input disabled={readOnly} aria-label="Connector version" value={value.connectorVersion} onChange={event => set('connectorVersion', event.currentTarget.value)} /></label>
      <label>Format<select disabled={readOnly} aria-label="Format" value={value.formatCapability} onChange={event => set('formatCapability', event.currentTarget.value)}><option value="">Choose a format</option><Options values={formats} /></select></label>
      <label>Secret reference<input disabled={readOnly} aria-label="Secret reference" value={value.secretReference} onChange={event => set('secretReference', event.currentTarget.value)} /></label>
      <button type="button" disabled={!intentsEnabled} onClick={() => intentsEnabled && onDiscoverSource()}>Discover source</button>
    </Section>
    <Section name="Discovered source shape">
      {value.discoveredColumns.length === 0 && <p>No source columns discovered.</p>}
      {value.discoveredColumns.map((column, index) => <label key={column.name}><input type="checkbox" disabled={readOnly} aria-label={`Include ${column.name}`} checked={column.selected} onChange={event => set('discoveredColumns', value.discoveredColumns.map((current, position) => position === index ? { ...current, selected: event.currentTarget.checked } : current))} />{column.name}</label>)}
    </Section>
    <Section name="Mapping profile"><dl><dt>Profile</dt><dd>{DATA_EXCHANGE_MAPPING_PROFILE}</dd><dt>Schema</dt><dd>{DATA_EXCHANGE_MAPPING_SCHEMA}</dd><dt>Document version</dt><dd>{DATA_EXCHANGE_MAPPING_VERSION}</dd></dl></Section>
    <Section name="Canonical mappings">
      {value.mappings.length === 0 && <p>No mappings authored.</p>}
      {value.mappings.map((mapping, index) => <div key={`${mapping.sourceColumn}-${index}`}>
        <select disabled={readOnly} aria-label={`Mapping ${index + 1} source column`} value={mapping.sourceColumn} onChange={event => replaceMapping(index, { ...mapping, sourceColumn: event.currentTarget.value })}><option value="">Choose source column</option>{value.discoveredColumns.filter(column => column.selected).map(column => <option key={column.name} value={column.name}>{column.name}</option>)}</select>
        <select disabled={readOnly} aria-label={`Mapping ${index + 1} canonical target`} value={mapping.canonicalTarget} onChange={event => replaceMapping(index, { ...mapping, canonicalTarget: event.currentTarget.value })}><option value="">Choose canonical target</option><Options values={catalogue.canonicalTargets} /></select>
        <input disabled={readOnly} aria-label={`Mapping ${index + 1} target pointer`} value={mapping.targetPointer} onChange={event => replaceMapping(index, { ...mapping, targetPointer: event.currentTarget.value })} />
        <select disabled={readOnly} aria-label={`Mapping ${index + 1} datatype`} value={mapping.datatype} onChange={event => replaceMapping(index, { ...mapping, datatype: event.currentTarget.value })}><Options values={catalogue.datatypes} /></select>
        <label><input type="checkbox" disabled={readOnly} aria-label={`Mapping ${index + 1} required`} checked={mapping.required} onChange={event => replaceMapping(index, { ...mapping, required: event.currentTarget.checked })} />Required</label>
        <input disabled={readOnly} aria-label={`Mapping ${index + 1} null`} value={mapping.nullValue} onChange={event => replaceMapping(index, { ...mapping, nullValue: event.currentTarget.value })} />
        <input disabled={readOnly} aria-label={`Mapping ${index + 1} default`} value={mapping.defaultValue} onChange={event => replaceMapping(index, { ...mapping, defaultValue: event.currentTarget.value })} />
        <input disabled={readOnly} aria-label={`Mapping ${index + 1} separator`} value={mapping.separator} onChange={event => replaceMapping(index, { ...mapping, separator: event.currentTarget.value })} />
        <select disabled={readOnly} aria-label={`Mapping ${index + 1} transform`} value={mapping.transform} onChange={event => replaceMapping(index, { ...mapping, transform: event.currentTarget.value })}><option value="">No transform</option><Options values={catalogue.transforms} /></select>
        <button type="button" disabled={readOnly} aria-label={`Remove mapping ${index + 1}`} onClick={() => set('mappings', value.mappings.filter((_, position) => position !== index))}>Remove</button>
      </div>)}
      <button type="button" disabled={readOnly} onClick={() => set('mappings', [...value.mappings, emptyMapping(value.discoveredColumns.find(column => column.selected)?.name)])}>Add mapping</button>
    </Section>
    <label>External key columns<input disabled={readOnly} aria-label="External key columns" value={value.externalKeyColumns.join(', ')} onChange={event => set('externalKeyColumns', event.currentTarget.value.split(',').map(item => item.trim()).filter(Boolean))} /></label>
    <label>Replay policy<select disabled={readOnly} aria-label="Replay policy" value={value.replayPolicy} onChange={event => set('replayPolicy', event.currentTarget.value as DataExchangeAuthoringDraft['replayPolicy'])}><option value="append">Append</option><option value="overwrite">Overwrite</option><option value="append_dedup">Append and deduplicate</option></select></label>
    <label>Schedule reference<select disabled={readOnly} aria-label="Schedule reference" value={value.scheduleReference} onChange={event => set('scheduleReference', event.currentTarget.value)}><option value="">Manual only</option><Options values={catalogue.schedules} /></select></label>
    <Section name="Reference set deliveries">
      <input disabled={readOnly} aria-label="Reference dataset" value={value.referenceDataset} onChange={event => set('referenceDataset', event.currentTarget.value)} />
      <input disabled={readOnly} aria-label="Pack distribution" value={value.packDistribution} onChange={event => set('packDistribution', event.currentTarget.value)} />
      <input disabled={readOnly} aria-label="Feed distribution" value={value.feedDistribution} onChange={event => set('feedDistribution', event.currentTarget.value)} />
    </Section>
    <div><button type="button" disabled={!intentsEnabled} onClick={() => intentsEnabled && onSaveDraft?.()}>Save draft</button><button type="button" disabled={!publishEnabled} onClick={() => publishEnabled && onPublish?.()}>Publish definition</button></div>
    {authoringRefusals.length > 0 && <Section name="Authoring refusals"><ul>{authoringRefusals.map(refusal => <li key={`${refusal.stage}:${refusal.code}`}><span>{refusal.stage}</span>: <a href={refusal.targetHref}>{refusal.code}</a></li>)}</ul></Section>}
    <div><button type="button" disabled={!intentsEnabled} onClick={() => intentsEnabled && onDryRun()}>Create dry run</button><button type="button" aria-label="Commit reviewed run" disabled={!commitEnabled} onClick={() => commitEnabled && onCommit()}>Commit reviewed run</button></div>
    {run && <Section name="Persisted run evidence"><dl><dt>Dry run</dt><dd>{run.dryRunId}</dd>{run.batchIdentity && <><dt>Batch identity</dt><dd>{run.batchIdentity}</dd></>}<dt>Status</dt><dd>{run.status}</dd><dt>Staleness</dt><dd>{run.stale ? 'Stale' : 'Current'}</dd><dt>Candidate checkpoint</dt><dd>{run.candidateCheckpoint}</dd></dl><p>Applied {run.census.applied}; skipped {run.census.skipped}; conflicted {run.census.conflicted}; rejected {run.census.rejected}; failed {run.census.failed}; halted {run.census.halted}</p><ul>{run.refusals.map(refusal => <li key={refusal}>{refusal}</li>)}</ul></Section>}
    {!run && <Section name="Persisted run evidence"><p>No dry run recorded.</p></Section>}
  </form>
}
