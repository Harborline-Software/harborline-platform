import { Button } from '@harborline-platform/hlp.ui.button'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { bookingOption, emptyBookingDraft, hasAcceptableBookingRevision, sameBookingRevision, type BookingCatalogue, type BookingCatalogueOption, type BookingDiagnostic, type BookingDiagnosticSet, type BookingDraft, type BookingDraftChange} from './BookingDraftTypes'

export interface BookingAuthoringEditorProps { readonly value: BookingDraft; readonly catalogue?: BookingCatalogue; readonly diagnostics?: BookingDiagnosticSet; readonly readOnly?: boolean; readonly onDraftChange: (change: BookingDraftChange) => void }

// Spellings of the Booking grammar (BookingDefinitionAdmission.cs). A value outside them is shown, never rewritten.
const capacityKinds = ['exclusive', 'pool']
// The field a refusal pointer belongs to, by its first reference token. Any other pointer is listed under "Other refusals".
const resourceFields: Readonly<Record<string, string>> = { name: 'Name', from_type_id: 'Resource record type', capacity_kind: 'Capacity', pool_size: 'Pool size', setup_minutes: 'Setup minutes', cleanup_minutes: 'Cleanup minutes', maintenance_windows: 'Maintenance', availability_from: 'Availability source' }
const bookableFields: Readonly<Record<string, string>> = { name: 'Name', on_type_id: 'Offered against record type', duration_intervals_minutes: 'Durations', requires: 'Required resources', waitlist: 'Waitlist' }
const noDiagnostics: readonly BookingDiagnostic[] = []
const noOptions: readonly BookingCatalogueOption[] = []
const lifecycleKey = (value: BookingDraft) => `${value.identity}\u0000${value.versionId}\u0000${value.expectedRevision ?? ''}`
const text = (node: unknown) => typeof node === 'string' ? node : undefined
const numbers = (node: unknown): readonly unknown[] => Array.isArray(node) ? node : []
const strings = (node: unknown): readonly string[] => Array.isArray(node) ? node.filter((entry): entry is string => typeof entry === 'string') : []
// A number is read invariantly from the input text. Empty or non-finite text is not a number, and leaves the draft unchanged.
const parsed = (raw: string) => { const trimmed = raw.trim(); const value = Number(trimmed); return trimmed !== '' && Number.isFinite(value) ? value : undefined }
const firstToken = (pointer: string) => pointer.split('/')[1] ?? ''
const rejectedRevision = 'A host draft was not loaded because its revision is not a canonical decimal string.'

/** Local Resource and Bookable draft editing (DES-0025 booking-eng-20). Reports local changes only: it cannot save, admit or publish. */
export function BookingAuthoringEditor(props: BookingAuthoringEditorProps) {
  const catalogue = props.catalogue ?? {}
  const [hasDraft, setHasDraft] = useState(() => hasAcceptableBookingRevision(props.value))
  const [value, setValue] = useState(() => hasDraft ? props.value : emptyBookingDraft()); const [generation, setGeneration] = useState(0); const [incoming, setIncoming] = useState<BookingDraft>(); const [newDuration, setNewDuration] = useState('')
  const dirty = useRef(false); const latest = useRef(value); const counter = useRef(0); const held = useRef(''); const adopted = useRef(hasDraft)
  // A host echo of the same identity, version and revision is not a replacement; a different one replaces a clean draft and waits
  // behind Keep or Discard on a dirty one, so a new identity or revision never silently overwrites local edits.
  useEffect(() => { if (!hasAcceptableBookingRevision(props.value)) return; if (!adopted.current) { adopted.current = true; latest.current = props.value; setValue(props.value); setHasDraft(true); return } const key = lifecycleKey(props.value); if (key === lifecycleKey(latest.current)) { held.current = ''; setIncoming(undefined); return } if (key === held.current) return; if (dirty.current) { held.current = key; setIncoming(props.value) } else { counter.current++; latest.current = props.value; setValue(props.value); setGeneration(counter.current) } }, [props.value])
  const change = (next: BookingDraft) => { if (props.readOnly || !adopted.current) return; dirty.current = true; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); props.onDraftChange({ draft: next, generation: counter.current }) }
  const discard = (next: BookingDraft) => { if (props.readOnly) return; dirty.current = false; held.current = ''; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); setIncoming(undefined); props.onDraftChange({ draft: next, generation: counter.current }) }
  const keep = () => { if (props.readOnly) return; setIncoming(undefined) }
  // Setting a member writes it; undefined removes it and writes nothing in its place.
  const set = (member: string, next: unknown) => { const { [member]: _previous, ...rest } = latest.current.body; change({ ...latest.current, body: next === undefined ? rest : { ...latest.current.body, [member]: next } }) }
  const setNumber = (member: string, raw: string) => { if (raw.trim() === '') { set(member, undefined); return } const number = parsed(raw); if (number !== undefined) set(member, number) }
  // Exclusive capacity carries no pool size (the producer refuses one), so choosing it removes pool_size in the same edit.
  const chooseCapacity = (kind: string) => { const { capacity_kind: _kind, pool_size: size, ...rest } = latest.current.body; change({ ...latest.current, body: { ...rest, ...(kind ? { capacity_kind: kind } : {}), ...(kind === 'pool' && size !== undefined ? { pool_size: size } : {}) } }) }
  const toggle = (member: string, id: string, on: boolean) => { const current = strings(latest.current.body[member]); const next = on ? (current.includes(id) ? current : [...current, id]) : current.filter(entry => entry !== id); set(member, next.length > 0 ? next : undefined) }
  const setDuration = (index: number, raw: string) => { const number = parsed(raw); if (number === undefined) return; set('duration_intervals_minutes', numbers(latest.current.body.duration_intervals_minutes).map((entry, current) => current === index ? number : entry)) }
  const addDuration = () => { const number = parsed(newDuration); if (number === undefined) return; set('duration_intervals_minutes', [...numbers(latest.current.body.duration_intervals_minutes), number]); setNewDuration('') }
  const removeDuration = (index: number) => { const next = numbers(latest.current.body.duration_intervals_minutes).filter((_, current) => current !== index); set('duration_intervals_minutes', next.length > 0 ? next : undefined) }

  // Refusals match only the exact identity, canonical revision and local generation; anything else is stale and never shown.
  const matched = hasDraft && props.diagnostics && hasAcceptableBookingRevision(props.diagnostics) && props.diagnostics.identity === value.identity && sameBookingRevision(props.diagnostics.expectedRevision, value.expectedRevision) && props.diagnostics.generation === generation ? props.diagnostics.diagnostics : noDiagnostics
  const body = value.body; const kind = text(body.kind); const fields = kind === 'resource' ? resourceFields : kind === 'bookable' ? bookableFields : {}
  const placed = (field: string) => matched.filter(diagnostic => fields[firstToken(diagnostic.pointer)] === field)
  const unplaced = matched.filter(diagnostic => fields[firstToken(diagnostic.pointer)] === undefined)
  const refusals = (field: string) => { const own = placed(field); return own.length > 0 ? <ul className="hl-booking-authoring__refusals" aria-label={`${field} refusals`}>{own.map((diagnostic, index) => <li key={index} data-code={diagnostic.code} data-pointer={diagnostic.pointer}>{diagnostic.code}</li>)}</ul> : null }
  const invalid = (field: string) => placed(field).length > 0 ? true : undefined
  const select = (field: string, current: string | undefined, entries: readonly BookingCatalogueOption[], onChange: (next: string) => void, fallback: string) => { const options = entries.map(bookingOption); return <label>{field}{options.length === 0 && <span className="hl-booking-authoring__note">{fallback}</span>}<select aria-label={field} aria-invalid={invalid(field)} value={current ?? ''} onChange={event => onChange(event.currentTarget.value)}><option value="">Not set</option>{current !== undefined && current !== '' && !options.some(option => option.id === current) && <option value={current}>{`${current} (not offered)`}</option>}{options.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select>{refusals(field)}</label> }
  const numberInput = (field: string, member: string) => <label>{field}<input aria-label={field} aria-invalid={invalid(field)} type="number" inputMode="numeric" value={typeof body[member] === 'number' ? String(body[member]) : ''} onChange={event => setNumber(member, event.currentTarget.value)} />{refusals(field)}</label>
  const checks = (legend: string, member: string, entries: readonly BookingCatalogueOption[], label: (option: string) => string, fallback: string) => { const options = entries.map(bookingOption); const chosen = strings(body[member]); return <fieldset className="hl-booking-authoring__group" aria-invalid={invalid(legend)}><legend>{legend}</legend>{options.length === 0 && <p className="hl-booking-authoring__note">{fallback}</p>}{chosen.filter(id => !options.some(option => option.id === id)).map(id => <p key={id} className="hl-booking-authoring__note">{`${id} (not offered)`}</p>)}{options.map(option => <label key={option.id}><input type="checkbox" aria-label={label(option.label)} checked={chosen.includes(option.id)} onChange={event => toggle(member, option.id, event.currentTarget.checked)} />{option.label}</label>)}{refusals(legend)}</fieldset> }

  const resource = (): ReactNode => { const capacity = text(body.capacity_kind); return <div className="hl-booking-authoring__grid">
    <label>Name<input aria-label="Name" aria-invalid={invalid('Name')} value={text(body.name) ?? ''} onChange={event => set('name', event.currentTarget.value)} />{refusals('Name')}</label>
    {select('Resource record type', text(body.from_type_id), catalogue.resourceTypes ?? noOptions, next => set('from_type_id', next || undefined), 'No resource record types offered.')}
    <label>Capacity<select aria-label="Capacity" aria-invalid={invalid('Capacity')} value={capacity ?? ''} onChange={event => chooseCapacity(event.currentTarget.value)}><option value="">Not set (exclusive)</option>{capacity !== undefined && !capacityKinds.includes(capacity) && <option value={capacity}>{`${capacity} (not a current spelling)`}</option>}{capacityKinds.map(kind => <option key={kind} value={kind}>{kind}</option>)}</select>{refusals('Capacity')}</label>
    {(capacity === 'pool' || body.pool_size !== undefined) && numberInput('Pool size', 'pool_size')}
    {numberInput('Setup minutes', 'setup_minutes')}
    {numberInput('Cleanup minutes', 'cleanup_minutes')}
    {checks('Maintenance', 'maintenance_windows', catalogue.maintenanceSources ?? noOptions, label => `Maintenance from ${label}`, 'No maintenance sources offered. Maintenance is read from the resource record, not authored here.')}
    <label>Availability source<input aria-label="Availability source" aria-invalid={invalid('Availability source')} value={text(body.availability_from) ?? ''} onChange={event => set('availability_from', event.currentTarget.value || undefined)} />{refusals('Availability source')}</label>
  </div> }

  const bookable = (): ReactNode => { const durations = numbers(body.duration_intervals_minutes); return <div className="hl-booking-authoring__grid">
    <label>Name<input aria-label="Name" aria-invalid={invalid('Name')} value={text(body.name) ?? ''} onChange={event => set('name', event.currentTarget.value)} />{refusals('Name')}</label>
    {select('Offered against record type', text(body.on_type_id), catalogue.bookableTypes ?? noOptions, next => set('on_type_id', next || undefined), 'No record types offered.')}
    <fieldset className="hl-booking-authoring__group" aria-invalid={invalid('Durations')}><legend>Durations</legend>
      {durations.length === 0 && <p className="hl-booking-authoring__note">No durations authored.</p>}
      {durations.map((duration, index) => <div key={index} className="hl-booking-authoring__row"><label>{`Duration ${index + 1} minutes`}<input aria-label={`Duration ${index + 1} minutes`} type="number" inputMode="numeric" value={typeof duration === 'number' ? String(duration) : ''} onChange={event => setDuration(index, event.currentTarget.value)} /></label><Button type="button" intent="secondary" size="sm" onClick={() => removeDuration(index)}>{`Remove duration ${index + 1}`}</Button></div>)}
      <div className="hl-booking-authoring__row"><label>New duration minutes<input aria-label="New duration minutes" type="number" inputMode="numeric" value={newDuration} onChange={event => setNewDuration(event.currentTarget.value)} /></label><Button type="button" intent="secondary" size="sm" onClick={addDuration}>Add duration</Button></div>
      {refusals('Durations')}
    </fieldset>
    {checks('Required resources', 'requires', catalogue.resources ?? noOptions, label => `Required resource ${label}`, 'No resources offered.')}
    <label><input type="checkbox" aria-label="Waitlist" aria-invalid={invalid('Waitlist')} checked={body.waitlist === true} onChange={event => set('waitlist', event.currentTarget.checked ? true : (body.waitlist === undefined ? undefined : false))} />Waitlist: a queue exists. Offers and their expiry are a Workflow.{refusals('Waitlist')}</label>
  </div> }

  const content = <>
    <header className="hl-booking-authoring__grid">
      <label>Definition kind<select aria-label="Definition kind" value={kind ?? ''} onChange={event => { const next = event.currentTarget.value; if (next === 'resource' || next === 'bookable') set('kind', next) }}>{kind === undefined && <option value="">Choose a definition kind</option>}{kind !== undefined && kind !== 'resource' && kind !== 'bookable' && <option value={kind}>{`${kind} (not a Booking kind)`}</option>}<option value="resource">Resource</option><option value="bookable">Bookable</option></select></label>
      <output aria-label="Identity">{value.identity || 'Not yet assigned'}</output>
      <output aria-label="Version">{value.versionId || 'Not yet assigned'}</output>
      <output aria-label="Expected revision">{value.expectedRevision ?? 'None'}</output>
    </header>
    {kind === 'resource' && resource()}
    {kind === 'bookable' && bookable()}
  </>
  return <form className="hl-booking-authoring" aria-label="Booking definition draft" data-read-only={props.readOnly ? '' : undefined} data-generation={generation} onSubmit={event => event.preventDefault()}>
    <p className="hl-booking-authoring__scope">Local draft. Edits stay in this editor and are not saved.</p>
    {!hasAcceptableBookingRevision(props.value) && <p role="status" className="hl-booking-authoring__note">{rejectedRevision}</p>}
    {hasDraft && (props.readOnly ? <fieldset className="hl-booking-authoring__read-only" disabled>{content}</fieldset> : content)}
    {unplaced.length > 0 && <ul className="hl-booking-authoring__refusals" aria-label="Other refusals">{unplaced.map((diagnostic, index) => <li key={index} data-code={diagnostic.code} data-pointer={diagnostic.pointer}>{diagnostic.code} at {diagnostic.pointer}</li>)}</ul>}
    {incoming && <aside role="alert" className="hl-booking-authoring__incoming">A different draft or revision arrived while you have local edits.<Button type="button" disabled={props.readOnly} onClick={keep}>Keep edits</Button><Button type="button" disabled={props.readOnly} onClick={() => discard(incoming)}>Discard edits</Button></aside>}
  </form>
}
