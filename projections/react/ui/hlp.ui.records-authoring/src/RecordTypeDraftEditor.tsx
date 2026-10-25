import { Button } from '@harborline-platform/hlp.ui.button'
import { useEffect, useRef, useState } from 'react'
import { emptyRecordsDraft, hasAcceptableRecordsRevision, sameRecordsRevision, type RecordsDiagnostic, type RecordsDiagnosticSet, type RecordsDraft, type RecordsWarning, type RecordsDraftChange, type RecordsKindOption, type RecordTypeBody, type RecordTypeField, type RecordTypeFieldReference } from './RecordsDraftTypes'

export interface RecordTypeDraftEditorProps { readonly value: RecordsDraft; readonly kindOptions: readonly RecordsKindOption[]; readonly diagnostics?: RecordsDiagnosticSet; readonly readOnly?: boolean; readonly onDraftChange: (change: RecordsDraftChange) => void }

// Reference spellings of the current grammar (RecordTypeIdentity.cs:45-62). A value outside them is shown, never rewritten.
const cardinalities = ['one', 'many']
const onDeletes = ['block', 'orphan', 'cascade']
const noDiagnostics: readonly RecordsDiagnostic[] = []
const noWarnings: readonly RecordsWarning[] = []
// The revision is part of the key as its exact string. A canonical revision is never empty, so an absent one ('') cannot collide.
const lifecycleKey = (value: RecordsDraft) => `${value.identity}\u0000${value.versionId}\u0000${value.expectedRevision ?? ''}`
const rejectedRevision = 'A host draft was not loaded because its revision is not a canonical decimal string.'

/** Local Record Type draft editing. Reports local changes only: it cannot evaluate, save, create or publish. */
export function RecordTypeDraftEditor(props: RecordTypeDraftEditorProps) {
  // A host value whose revision is present but not canonical is rejected: never adopted, held, repaired or emitted. Until the
  // first acceptable value arrives there is no draft, and the placeholder below is never shown or reported.
  const [hasDraft, setHasDraft] = useState(() => hasAcceptableRecordsRevision(props.value))
  const [value, setValue] = useState(() => hasDraft ? props.value : emptyRecordsDraft()); const [generation, setGeneration] = useState(0); const [incoming, setIncoming] = useState<RecordsDraft>()
  const dirty = useRef(false); const latest = useRef(value); const counter = useRef(0); const held = useRef(''); const adopted = useRef(hasDraft)
  // A host echo of the same identity, version and revision is not a replacement; a different one replaces a clean draft and waits behind Keep/Discard on a dirty one.
  // The generation is local and separate: it never reads, derives or changes the revision.
  useEffect(() => { if (!hasAcceptableRecordsRevision(props.value)) return; if (!adopted.current) { adopted.current = true; latest.current = props.value; setValue(props.value); setHasDraft(true); return } const key = lifecycleKey(props.value); if (key === lifecycleKey(latest.current)) { held.current = ''; setIncoming(undefined); return } if (key === held.current) return; if (dirty.current) { held.current = key; setIncoming(props.value) } else { counter.current++; latest.current = props.value; setValue(props.value); setGeneration(counter.current) } }, [props.value])
  const change = (next: RecordsDraft) => { if (props.readOnly || !adopted.current) return; dirty.current = true; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); props.onDraftChange({ draft: next, generation: counter.current }) }
  const load = (next: RecordsDraft) => { if (props.readOnly || !adopted.current) return; dirty.current = false; held.current = ''; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); setIncoming(undefined); props.onDraftChange({ draft: next, generation: counter.current }) }
  const keep = () => { if (props.readOnly) return; setIncoming(undefined) }
  const setBody = (body: RecordTypeBody) => change({ ...latest.current, body })
  const currentFields = () => latest.current.body.fields ?? []
  const setField = (index: number, update: (field: RecordTypeField) => RecordTypeField) => setBody({ ...latest.current.body, fields: currentFields().map((field, current) => current === index ? update(field) : field) })
  const chooseKind = (kind: string) => { if (kind === 'Records') change({ ...latest.current, definitionKind: 'Records' }) }
  const chooseFieldKind = (index: number, choice: string) => { const option = props.kindOptions[Number(choice)]; if (!option || choice === '') return; setField(index, field => ({ ...field, binding: { ...field.binding, kind: { ...field.binding?.kind, kind_id: option.kindId, version: option.version } } })) }
  const setParameter = (index: number, name: string, raw: string) => { const numeric = typeof currentFields()[index]?.binding?.kind?.parameters?.[name] === 'number'; if (numeric && (raw.trim() === '' || !Number.isFinite(Number(raw)))) return; setField(index, field => ({ ...field, binding: { ...field.binding, kind: { ...field.binding?.kind, parameters: { ...field.binding?.kind?.parameters, [name]: numeric ? Number(raw) : raw } } } })) }
  const setReference = (index: number, member: keyof RecordTypeFieldReference & string, next: string) => setField(index, field => ({ ...field, reference: { ...field.reference, [member]: next } }))
  const chooseRetention = (key: string) => { const { retention_clock_field_id: _previous, ...rest } = latest.current.body; setBody(key ? { ...latest.current.body, retention_clock_field_id: key } : rest) }
  // Revisions match only as identical canonical strings, or both absent; a noncanonical or numeric diagnostic revision never matches.
  const matched = hasDraft && props.diagnostics && hasAcceptableRecordsRevision(props.diagnostics) && props.diagnostics.identity === value.identity && sameRecordsRevision(props.diagnostics.expectedRevision, value.expectedRevision) && props.diagnostics.generation === generation ? props.diagnostics : undefined
  // Warnings follow the same matching rule and are never shown as diagnostics.
  const diagnostics = matched?.diagnostics ?? noDiagnostics; const warnings = matched?.warnings ?? noWarnings
  const body = value.body; const fields = body.fields ?? []; const fieldKeys = fields.map(field => field.field_key ?? '').filter(key => key !== ''); const retention = body.retention_clock_field_id; const kindChosen = value.definitionKind === 'Records'
  const choice = (label: string, current: string | undefined, spellings: readonly string[], onChange: (next: string) => void) => <select aria-label={label} value={current ?? ''} onChange={event => onChange(event.currentTarget.value)}>{(current === undefined || !spellings.includes(current)) && <option value={current ?? ''}>{current === undefined ? 'Not set' : `${current} (not a current spelling)`}</option>}{spellings.map(spelling => <option key={spelling} value={spelling}>{spelling}</option>)}</select>
  const content = <>
    <header className="hl-records-authoring__header">
      <label>Definition kind<select aria-label="Definition kind" value={value.definitionKind ?? ''} onChange={event => chooseKind(event.currentTarget.value)}>{!kindChosen && <option value="">Choose a definition kind</option>}<option value="Records">Record type</option></select></label>
      <output aria-label="Record type identity">{value.identity || 'Not yet assigned'}</output>
      <output aria-label="Version">{value.versionId || 'Not yet assigned'}</output>
      <output aria-label="Expected revision">{value.expectedRevision ?? 'None'}</output>
    </header>
    {kindChosen && <>
      <div className="hl-records-authoring__grid"><label>Name<input aria-label="Record type name" value={body.name ?? ''} onChange={event => setBody({ ...latest.current.body, name: event.currentTarget.value })} /></label><label>Section<input aria-label="Section" value={body.envelope?.section ?? ''} onChange={event => setBody({ ...latest.current.body, envelope: { ...latest.current.body.envelope, section: event.currentTarget.value } })} /></label></div>
      {fields.map((field, index) => { const n = index + 1; const kind = field.binding?.kind; const offered = kind ? props.kindOptions.findIndex(option => option.kindId === kind.kind_id && option.version === kind.version) : -1; const parameters = Object.entries(kind?.parameters ?? {}).filter(([, parameter]) => typeof parameter === 'string' || typeof parameter === 'number')
        return <fieldset key={index} className="hl-records-authoring__field"><legend>Field {n}</legend>
          <div className="hl-records-authoring__grid">
            <label>Field key<input aria-label={`Field ${n} key`} value={field.field_key ?? ''} onChange={event => { const next = event.currentTarget.value; setField(index, current => ({ ...current, field_key: next })) }} /></label>
            <label>Display name<input aria-label={`Field ${n} display name`} value={field.display_name ?? ''} onChange={event => { const next = event.currentTarget.value; setField(index, current => ({ ...current, display_name: next })) }} /></label>
            {kind && <label>Kind and version<select aria-label={`Field ${n} kind`} value={offered >= 0 ? String(offered) : ''} onChange={event => chooseFieldKind(index, event.currentTarget.value)}>{offered < 0 && <option value="">{`${kind.kind_id ?? ''} ${kind.version ?? ''} (not offered)`}</option>}{props.kindOptions.map((option, current) => <option key={current} value={String(current)}>{option.kindId} {option.version}</option>)}</select></label>}
            {parameters.map(([name, parameter]) => <label key={name}>Parameter {name}<input aria-label={`Field ${n} parameter ${name}`} type={typeof parameter === 'number' ? 'number' : 'text'} value={String(parameter)} onChange={event => setParameter(index, name, event.currentTarget.value)} /></label>)}
          </div>
          {!field.binding && <p className="hl-records-authoring__note">Unbound field. Bindings are not authored here.</p>}
          {field.reference && <fieldset className="hl-records-authoring__grid"><legend>Field {n} reference</legend><label>Target type<input aria-label={`Field ${n} reference target type`} value={field.reference.target_type_id ?? ''} onChange={event => setReference(index, 'target_type_id', event.currentTarget.value)} /></label><label>Cardinality{choice(`Field ${n} reference cardinality`, field.reference.cardinality, cardinalities, next => setReference(index, 'cardinality', next))}</label><label>On delete{choice(`Field ${n} reference on delete`, field.reference.on_delete, onDeletes, next => setReference(index, 'on_delete', next))}</label></fieldset>}
          <Button type="button" intent="secondary" size="sm" onClick={() => setBody({ ...latest.current.body, fields: currentFields().filter((_, current) => current !== index) })}>Remove field {n}</Button>
        </fieldset> })}
      <Button type="button" intent="secondary" size="sm" onClick={() => setBody({ ...latest.current.body, fields: [...currentFields(), { field_key: '', display_name: '' }] })}>Add field</Button>
      <label>Retention clock field<select aria-label="Retention clock field" value={retention ?? ''} onChange={event => chooseRetention(event.currentTarget.value)}><option value="">None</option>{retention !== undefined && !fieldKeys.includes(retention) && <option value={retention}>{retention} (not a field)</option>}{fieldKeys.map((key, index) => <option key={index} value={key}>{key}</option>)}</select></label>
    </>}
  </>
  return <form className="hl-records-authoring" aria-label="Record type draft" data-read-only={props.readOnly ? '' : undefined} data-generation={generation} onSubmit={event => event.preventDefault()}>
    <p className="hl-records-authoring__scope">Local draft. Edits stay in this editor and are not saved.</p>
    {!hasAcceptableRecordsRevision(props.value) && <p role="status" className="hl-records-authoring__note">{rejectedRevision}</p>}
    {hasDraft && (props.readOnly ? <fieldset className="hl-records-authoring__read-only" disabled>{content}</fieldset> : content)}
    {diagnostics.length > 0 && <ul className="hl-records-authoring__diagnostics" aria-label="Diagnostics">{diagnostics.map((diagnostic, index) => { const target = diagnostic.target || undefined; return <li key={index} data-code={diagnostic.code} data-pointer={diagnostic.pointer} data-target={target}>{diagnostic.code} at {diagnostic.pointer}{target === undefined ? '' : ` for ${target}`}</li> })}</ul>}
    {warnings.length > 0 && <><p className="hl-records-authoring__note">Warnings do not block publication.</p><ul className="hl-records-authoring__warnings" aria-label="Warnings">{warnings.map((warning, index) => <li key={index} data-code={warning.code} data-pointer={warning.pointer}>{warning.code} at {warning.pointer}</li>)}</ul></>}
    {incoming && <aside role="alert" className="hl-records-authoring__incoming">A different draft or revision arrived while you have local edits.<Button type="button" disabled={props.readOnly} onClick={keep}>Keep edits</Button><Button type="button" disabled={props.readOnly} onClick={() => load(incoming)}>Discard edits</Button></aside>}
  </form>
}
