import { Button } from '@harborline-platform/hlp.ui.button'
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { emptySchedulingDraft, hasAcceptableSchedulingRevision, sameSchedulingRevision, schedulingOption, type SchedulingCatalogueOption, type SchedulingDiagnostic, type SchedulingDiagnosticSet, type SchedulingDraft, type SchedulingDraftChange, type SchedulingCatalogue, type SchedulingJsonObject } from './SchedulingDraftTypes'

export interface SchedulingAuthoringEditorProps { readonly value: SchedulingDraft; readonly catalogue?: SchedulingCatalogue; readonly diagnostics?: SchedulingDiagnosticSet; readonly readOnly?: boolean; readonly onDraftChange: (change: SchedulingDraftChange) => void }

type Json = SchedulingJsonObject
type Choices = readonly (readonly [value: string, label: string])[]
// Spellings of the ScheduleDefinition grammar (ScheduleDefinitionAdmission, ScheduleProblemClass, the admitted module catalogue). A
// value outside them is shown, never rewritten.
const startKinds: Choices = [['form', 'A form'], ['workflow', 'A workflow'], ['work_item', 'A work item']]
const basisKinds: Choices = [['fixed_calendar', 'Fixed calendar'], ['completion_offset', 'Completion offset'], ['threshold', 'Threshold'], ['event_anchored_chain', 'Event-anchored chain']]
const crossings: Choices = [['above', 'Above'], ['below', 'Below']]
const missedBehaviours: Choices = [['record_and_replan', 'Record and replan'], ['window_closed', 'Window closed']]
const problemClasses: Choices = ['Workforce', 'Timetabling', 'ProtocolAdmission', 'Project', 'JobShop', 'Mobility', 'CompositeRecovery'].map(name => [name, name] as const)
const admittedModules: Choices = [['time_windows', 'Time windows'], ['resource_requirements', 'Resource requirements'], ['no_overlap', 'No overlap'], ['precedence', 'Precedence']]
// The field a refusal pointer belongs to: the first prefix that matches and whose field is on screen. Anything else is listed unplaced.
const pointerFields: Choices = [['/name', 'Name'], ['/on_type_id', 'Runs on record type'], ['/starts/target_id', 'Starts target'], ['/starts', 'Starts'], ['/basis/rrule', 'Recurrence rule'], ['/basis/offset', 'Completion offset'], ['/basis/reading', 'Threshold reading'], ['/basis/crossing', 'Threshold crossing'], ['/basis/value', 'Threshold value'], ['/basis/anchor', 'Chain anchor'], ['/basis/nodes', 'Chain nodes'], ['/basis', 'Basis'], ['/due_date', 'Due date'], ['/window', 'Window'], ['/tolerance', 'Tolerance'], ['/missed_behaviour', 'Missed behaviour'], ['/problem_class', 'Problem class'], ['/solver/backend_id', 'Solver backend'], ['/solver/guarantee', 'Solver guarantee'], ['/solver/work_budget', 'Work budget'], ['/solver/parameters', 'Backend parameters'], ['/solver', 'Solver backend'], ['/modules', 'Constraint module'], ['/legality_ref', 'Legality rule set']]
const supply = 'Supply is read from the shared availability runtime, from each Resource\'s own availability source, set on the Resource in the Booking editor. It cannot be changed here.'
const rejectedRevision = 'A host draft was not loaded because its revision is not a canonical decimal string.'
const noDiagnostics: readonly SchedulingDiagnostic[] = []
const lifecycleKey = (value: SchedulingDraft) => `${value.identity}\u0000${value.versionId}\u0000${value.expectedRevision ?? ''}`
const obj = (node: unknown): Json | undefined => typeof node === 'object' && node !== null && !Array.isArray(node) ? node as Json : undefined
const text = (node: unknown) => typeof node === 'string' ? node : undefined
const list = (node: unknown): readonly unknown[] => Array.isArray(node) ? node : []
// Setting a member writes it; undefined removes it and writes nothing in its place. An object left with no members is removed.
const put = (target: Json, member: string, next: unknown): Json => { const { [member]: _previous, ...rest } = target; return next === undefined ? rest : { ...rest, [member]: next } }
const orNone = (target: Json) => Object.keys(target).length > 0 ? target : undefined
// Numbers are read from the text by the JSON number grammar; anything else leaves the draft unchanged.
const decimal = (raw: string) => /^-?(0|[1-9]\d*)(\.\d+)?([eE][-+]?\d+)?$/.test(raw.trim()) && Number.isFinite(Number(raw.trim())) ? Number(raw.trim()) : undefined
const whole = (raw: string) => /^-?(0|[1-9]\d*)$/.test(raw.trim()) && Number.isSafeInteger(Number(raw.trim())) ? Number(raw.trim()) : undefined
const jsonObject = (raw: string) => { try { return obj(JSON.parse(raw)) } catch { return undefined } }
const at = (pointer: string) => pointer || 'the whole definition'
const choices = (entries: readonly SchedulingCatalogueOption[] | undefined): Choices => (entries ?? []).map(schedulingOption).map(option => [option.id, option.label] as const)

/** Local Schedule draft editing (DES-0022 scheduling-eng-19). Reports local changes only: it cannot save, admit or publish. */
export function SchedulingAuthoringEditor(props: SchedulingAuthoringEditorProps) {
  const catalogue = props.catalogue ?? {}
  const [hasDraft, setHasDraft] = useState(() => hasAcceptableSchedulingRevision(props.value))
  const [value, setValue] = useState(() => hasDraft ? props.value : emptySchedulingDraft()); const [generation, setGeneration] = useState(0); const [incoming, setIncoming] = useState<SchedulingDraft>()
  const dirty = useRef(false); const latest = useRef(value); const counter = useRef(0); const held = useRef(''); const adopted = useRef(hasDraft)
  // Text a field cannot commit yet (a half-typed number or JSON object) stays in its input until the draft value under it changes, or a replacement draft is adopted.
  const [typed, setTyped] = useState<Readonly<Record<string, { readonly text: string; readonly under: string }>>>({})
  // A host echo of the same identity, version and revision is not a replacement; a different one replaces a clean draft and waits
  // behind Keep or Discard on a dirty one, so a new identity or revision never silently overwrites local edits.
  useEffect(() => { if (!hasAcceptableSchedulingRevision(props.value)) return; if (!adopted.current) { adopted.current = true; latest.current = props.value; setValue(props.value); setHasDraft(true); return } const key = lifecycleKey(props.value); if (key === lifecycleKey(latest.current)) { held.current = ''; setIncoming(undefined); return } if (key === held.current) return; if (dirty.current) { held.current = key; setIncoming(props.value) } else { counter.current++; latest.current = props.value; setValue(props.value); setGeneration(counter.current); setTyped({}) } }, [props.value])
  const change = (next: SchedulingDraft) => { if (props.readOnly || !adopted.current) return; dirty.current = true; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); props.onDraftChange({ draft: next, generation: counter.current }) }
  const discard = (next: SchedulingDraft) => { if (props.readOnly) return; dirty.current = false; held.current = ''; counter.current++; latest.current = next; setValue(next); setGeneration(counter.current); setIncoming(undefined); setTyped({}); props.onDraftChange({ draft: next, generation: counter.current }) }
  const keep = () => { if (props.readOnly) return; setIncoming(undefined) }

  const remember = (field: string, raw: string, under: unknown) => { if (!props.readOnly && adopted.current) setTyped(previous => ({ ...previous, [field]: { text: raw, under: JSON.stringify(under ?? null) } })) }
  const shown = (field: string, under: unknown, fallback: string) => { const entry = typed[field]; return entry !== undefined && entry.under === JSON.stringify(under ?? null) ? entry.text : fallback }

  // Every edit starts from the latest local body, so unedited members (envelope, grace, escalation, unknown ones) survive.
  const current = () => latest.current.body
  const set = (member: string, next: unknown) => change({ ...latest.current, body: put(current(), member, next) })
  const setIn = (member: string, key: string, next: unknown) => set(member, orNone(put(obj(current()[member]) ?? {}, key, next)))
  // A basis replaces the single due date; a chain carries its windows on its nodes, so it also removes the top-level window.
  const chooseBasis = (kind: string) => { if (!kind) { set('basis', undefined); return } const rest = put(put(current(), 'due_date', undefined), 'basis', undefined); change({ ...latest.current, body: { ...(kind === 'event_anchored_chain' ? put(rest, 'window', undefined) : rest), basis: { kind } } }) }
  const setDecimal = (field: string, raw: string, under: unknown, write: (next: number | undefined) => void) => { const number = decimal(raw); if (raw.trim() === '') write(undefined); else if (number !== undefined) write(number); remember(field, raw, raw.trim() === '' ? undefined : number ?? under) }
  const nodes = () => list(obj(current().basis)?.nodes)
  const setNodes = (next: readonly unknown[]) => setIn('basis', 'nodes', next.length > 0 ? next : undefined)
  const setNode = (index: number, edit: (node: Json) => Json) => setNodes(nodes().map((node, position) => position === index ? edit(obj(node) ?? {}) : node))
  // Choosing a backend keeps the rest of the solver; the producer requires a parameters object, so a new solver starts with an empty one.
  const chooseBackend = (id: string) => { if (!id) { set('solver', undefined); return } const solver = obj(current().solver) ?? {}; set('solver', { ...solver, backend_id: id, parameters: solver.parameters ?? {} }) }
  const modules = () => list(current().modules)
  const chooseModule = (name: string) => { const next = name ? [{ module: name }, ...modules().slice(1)] : modules().slice(1); set('modules', next.length > 0 ? next : undefined) }
  const constraints = () => list(obj(obj(modules()[0])?.parameters)?.constraints)
  const setConstraints = (next: readonly unknown[]) => { const head = obj(modules()[0]) ?? {}; set('modules', [put(head, 'parameters', orNone(put(obj(head.parameters) ?? {}, 'constraints', next.length > 0 ? next : undefined))), ...modules().slice(1)]) }
  const setConstraint = (index: number, key: string, next: unknown) => setConstraints(constraints().map((constraint, position) => position === index ? put(obj(constraint) ?? {}, key, next) : constraint))

  // Refusals match only the exact identity, canonical revision and local generation; anything else is stale and never shown.
  const matched = hasDraft && props.diagnostics && hasAcceptableSchedulingRevision(props.diagnostics) && props.diagnostics.identity === value.identity && sameSchedulingRevision(props.diagnostics.expectedRevision, value.expectedRevision) && props.diagnostics.generation === generation ? props.diagnostics.diagnostics : noDiagnostics
  const body = value.body; const basis = obj(body.basis); const basisKind = text(basis?.kind); const hasBasis = body.basis !== undefined; const chain = basisKind === 'event_anchored_chain'
  const starts = obj(body.starts); const startsKind = text(starts?.kind); const problemClass = text(body.problem_class) === 'None' ? '' : text(body.problem_class); const solver = obj(body.solver)
  const solverShown = !!problemClass || body.solver !== undefined; const modulesShown = !!problemClass || body.modules !== undefined; const dueShown = !hasBasis || body.due_date !== undefined; const windowShown = !chain || body.window !== undefined
  const onScreen = new Set(['Name', 'Runs on record type', 'Starts', 'Basis', 'Tolerance', 'Missed behaviour', 'Problem class', 'Legality rule set'])
  if (startsKind) onScreen.add('Starts target')
  for (const [kind, fields] of [['fixed_calendar', ['Recurrence rule']], ['completion_offset', ['Completion offset']], ['threshold', ['Threshold reading', 'Threshold crossing', 'Threshold value']], ['event_anchored_chain', ['Chain anchor', 'Chain nodes']]] as const) if (basisKind === kind) fields.forEach(field => onScreen.add(field))
  if (dueShown) onScreen.add('Due date'); if (windowShown) onScreen.add('Window'); if (solverShown) onScreen.add('Solver backend'); if (modulesShown) onScreen.add('Constraint module')
  if (solver) ['Solver guarantee', 'Work budget', 'Backend parameters'].forEach(field => onScreen.add(field))
  const fieldOf = (pointer: string) => pointerFields.find(([prefix, field]) => (pointer === prefix || pointer.startsWith(`${prefix}/`)) && onScreen.has(field))?.[1]
  const invalid = (field: string) => matched.some(diagnostic => fieldOf(diagnostic.pointer) === field) ? true : undefined

  // The live region announces only refusals that were not in the previous set, so a screen reader hears each new one once.
  const [announcement, setAnnouncement] = useState(''); const announced = useRef<ReadonlySet<string>>(new Set())
  const refusalKeys = matched.map(diagnostic => `${diagnostic.code} at ${at(diagnostic.pointer)}`); const joined = refusalKeys.join('\n')
  useEffect(() => { const fresh = refusalKeys.filter(key => !announced.current.has(key)); announced.current = new Set(refusalKeys); if (fresh.length > 0) setAnnouncement(`${fresh.length} new refusal${fresh.length === 1 ? '' : 's'}: ${fresh.join('; ')}`); else if (refusalKeys.length === 0) setAnnouncement('') }, [joined])

  const select = (field: string, selected: string | undefined, options: Choices, onChange: (next: string) => void, none = 'Not set', unknown = 'not offered') => <label>{field}<select aria-label={field} aria-invalid={invalid(field)} value={selected ?? ''} onChange={event => onChange(event.currentTarget.value)}><option value="">{none}</option>{selected !== undefined && selected !== '' && !options.some(([id]) => id === selected) && <option value={selected}>{`${selected} (${unknown})`}</option>}{options.map(([id, label]) => <option key={id} value={id}>{label}</option>)}</select></label>
  const input = (field: string, shownText: string, onChange: (raw: string) => void, inputMode?: 'decimal' | 'numeric') => <label>{field}<input aria-label={field} aria-invalid={invalid(field)} inputMode={inputMode} value={shownText} onChange={event => onChange(event.currentTarget.value)} /></label>
  const pair = (legend: string, member: string, keys: readonly [string, string], labels: readonly [string, string], hint: string) => { const pairValue = obj(body[member]); return <fieldset className="hl-scheduling-authoring__group" aria-invalid={invalid(legend)}><legend>{legend}</legend><p className="hl-scheduling-authoring__note">{hint}</p><div className="hl-scheduling-authoring__row">{keys.map((key, index) => <span key={key}>{input(labels[index], text(pairValue?.[key]) ?? '', raw => setIn(member, key, raw || undefined))}</span>)}</div></fieldset> }

  const basisFields = (): ReactNode => {
    if (basisKind === 'fixed_calendar') return input('Recurrence rule', text(basis?.rrule) ?? '', raw => setIn('basis', 'rrule', raw || undefined))
    if (basisKind === 'completion_offset') return input('Completion offset', text(basis?.offset) ?? '', raw => setIn('basis', 'offset', raw || undefined))
    if (basisKind === 'threshold') return <>
      {input('Threshold reading', text(basis?.reading) ?? '', raw => setIn('basis', 'reading', raw || undefined))}
      {select('Threshold crossing', text(basis?.crossing), crossings, next => setIn('basis', 'crossing', next || undefined), 'Not set', 'not a current spelling')}
      {input('Threshold value', shown('Threshold value', basis?.value, typeof basis?.value === 'number' ? String(basis.value) : ''), raw => setDecimal('Threshold value', raw, obj(current().basis)?.value, next => setIn('basis', 'value', next)), 'decimal')}
    </>
    return null
  }

  const chainSection = (): ReactNode => { const anchor = text(basis?.anchor); const chainNodes = list(basis?.nodes); return <fieldset className="hl-scheduling-authoring__group"><legend>Event-anchored chain</legend>
    {input('Chain anchor', anchor ?? '', raw => setIn('basis', 'anchor', raw || undefined))}
    <fieldset className="hl-scheduling-authoring__group" aria-invalid={invalid('Chain nodes')}><legend>Chain nodes</legend>
      {chainNodes.length === 0 && <p className="hl-scheduling-authoring__note">No nodes authored.</p>}
      {chainNodes.map((entry, index) => { const node = obj(entry) ?? {}; const n = index + 1; const after = text(node.after_node_id); const tolerance = obj(node.tolerance); const earlier = [...new Set(chainNodes.slice(0, index).map(other => text(obj(other)?.node_id)).filter((id): id is string => !!id))]
        return <fieldset key={index} className="hl-scheduling-authoring__group"><legend>{`Node ${n}`}</legend>
          <p className="hl-scheduling-authoring__note">{`Node ${n} scope: ${after ? `follows node ${after}` : `follows the chain anchor${anchor ? ` ${anchor}` : ''}`}`}</p>
          <div className="hl-scheduling-authoring__grid">
            {input(`Node ${n} id`, text(node.node_id) ?? '', raw => setNode(index, target => put(target, 'node_id', raw)))}
            {select(`Node ${n} follows`, after, earlier.map(id => [id, id] as const), next => setNode(index, target => put(target, 'after_node_id', next || undefined)), 'The chain anchor', 'not an earlier node')}
            {input(`Node ${n} offset`, text(node.offset) ?? '', raw => setNode(index, target => put(target, 'offset', raw)))}
            {input(`Node ${n} tolerance early`, text(tolerance?.early) ?? '', raw => setNode(index, target => put(target, 'tolerance', { ...(obj(target.tolerance) ?? {}), early: raw })))}
            {input(`Node ${n} tolerance late`, text(tolerance?.late) ?? '', raw => setNode(index, target => put(target, 'tolerance', { ...(obj(target.tolerance) ?? {}), late: raw })))}
          </div>
          <Button type="button" intent="secondary" size="sm" onClick={() => setNodes(nodes().filter((_, position) => position !== index))}>{`Remove node ${n}`}</Button>
        </fieldset> })}
      <Button type="button" intent="secondary" size="sm" onClick={() => setNodes([...nodes(), { node_id: '', offset: '', tolerance: { early: '', late: '' } }])}>Add node</Button>
    </fieldset>
  </fieldset> }

  const solverSection = (): ReactNode => { const backendId = text(solver?.backend_id); const backend = catalogue.backends?.find(entry => entry.id === backendId); return <fieldset className="hl-scheduling-authoring__group"><legend>Solver</legend>
    <div className="hl-scheduling-authoring__grid">
      {select('Solver backend', backendId, (catalogue.backends ?? []).map(entry => [entry.id, entry.label] as const), chooseBackend)}
      {solver && select('Solver guarantee', text(solver.guarantee), (backend?.guarantees ?? []).map(guarantee => [guarantee, guarantee] as const), next => setIn('solver', 'guarantee', next || undefined))}
      {solver && input('Work budget', shown('Work budget', solver.work_budget, typeof solver.work_budget === 'number' ? String(solver.work_budget) : ''), raw => { const number = whole(raw); if (raw.trim() === '') setIn('solver', 'work_budget', undefined); else if (number !== undefined) setIn('solver', 'work_budget', number); remember('Work budget', raw, raw.trim() === '' ? undefined : number ?? obj(current().solver)?.work_budget) }, 'numeric')}
    </div>
    {solver && <label>Backend parameters<textarea aria-label="Backend parameters" aria-invalid={invalid('Backend parameters')} rows={3} value={shown('Backend parameters', solver.parameters, obj(solver.parameters) ? JSON.stringify(solver.parameters) : '')} onChange={event => { const raw = event.currentTarget.value; const parameters = jsonObject(raw); if (parameters) setIn('solver', 'parameters', parameters); remember('Backend parameters', raw, parameters ?? obj(current().solver)?.parameters) }} /><span className="hl-scheduling-authoring__note">A JSON object; other text is not applied.</span></label>}
  </fieldset> }

  const moduleSection = (): ReactNode => { const all = list(body.modules); const first = obj(all[0]); const name = text(first?.module); const rows = list(obj(first?.parameters)?.constraints); const others = all.slice(1).map(entry => text(obj(entry)?.module) ?? JSON.stringify(entry)); return <fieldset className="hl-scheduling-authoring__group"><legend>Constraint modules</legend>
    {select('Constraint module', name, admittedModules, chooseModule, 'Not set', 'not admitted')}
    {others.length > 0 && <p className="hl-scheduling-authoring__note">{`Also carried unchanged: ${others.join(', ')}.`}</p>}
    {name === 'precedence' && <>
      {rows.map((entry, index) => { const row = obj(entry) ?? {}; const n = index + 1; const gapField = `Precedence ${n} minimum gap slots`
        return <div key={index} className="hl-scheduling-authoring__row">
          {input(`Precedence ${n} id`, text(row.id) ?? '', raw => setConstraint(index, 'id', raw))}
          {input(`Precedence ${n} before activity`, text(row.beforeActivityId) ?? '', raw => setConstraint(index, 'beforeActivityId', raw))}
          {input(`Precedence ${n} after activity`, text(row.afterActivityId) ?? '', raw => setConstraint(index, 'afterActivityId', raw))}
          {input(gapField, shown(gapField, row.minimumGapSlots, typeof row.minimumGapSlots === 'number' ? String(row.minimumGapSlots) : ''), raw => { const number = whole(raw); if (number !== undefined) setConstraint(index, 'minimumGapSlots', number); remember(gapField, raw, number ?? obj(constraints()[index])?.minimumGapSlots) }, 'numeric')}
          <Button type="button" intent="secondary" size="sm" onClick={() => setConstraints(constraints().filter((_, position) => position !== index))}>{`Remove precedence constraint ${n}`}</Button>
        </div> })}
      <Button type="button" intent="secondary" size="sm" onClick={() => setConstraints([...constraints(), { id: '', beforeActivityId: '', afterActivityId: '', minimumGapSlots: 0 }])}>Add precedence constraint</Button>
    </>}
  </fieldset> }

  const content = <>
    <header className="hl-scheduling-authoring__grid">
      <output aria-label="Identity">{value.identity || 'Not yet assigned'}</output>
      <output aria-label="Version">{value.versionId || 'Not yet assigned'}</output>
      <output aria-label="Expected revision">{value.expectedRevision ?? 'None'}</output>
    </header>
    <div className="hl-scheduling-authoring__grid">
      {input('Name', text(body.name) ?? '', raw => set('name', raw))}
      {select('Runs on record type', text(body.on_type_id), choices(catalogue.types), next => set('on_type_id', next || undefined))}
      {select('Starts', startsKind, startKinds, next => set('starts', next ? { kind: next } : undefined), 'Not set', 'not a current spelling')}
      {startsKind && select('Starts target', text(starts?.target_id), choices(startsKind === 'form' ? catalogue.forms : startsKind === 'workflow' ? catalogue.workflows : startsKind === 'work_item' ? catalogue.workItems : []), next => setIn('starts', 'target_id', next || undefined))}
      {select('Basis', basisKind, basisKinds, chooseBasis, 'Single due date', 'not a current spelling')}
      {basisFields()}
      {dueShown && input('Due date', text(body.due_date) ?? '', raw => set('due_date', raw || undefined))}
      {select('Missed behaviour', text(body.missed_behaviour), missedBehaviours, next => set('missed_behaviour', next || undefined), 'Not set', 'not a current spelling')}
      {select('Problem class', problemClass, problemClasses, next => set('problem_class', next || undefined), 'None', 'not a current spelling')}
      {select('Legality rule set', text(body.legality_ref), choices(catalogue.legalityRuleSets), next => set('legality_ref', next || undefined))}
    </div>
    {windowShown && pair('Window', 'window', ['earliest', 'latest'], ['Window earliest', 'Window latest'], hasBasis ? 'Signed ISO 8601 durations around each occurrence, such as -P7D and P7D.' : 'ISO 8601 instants, such as 2027-01-01T00:00:00Z.')}
    {pair('Tolerance', 'tolerance', ['early', 'late'], ['Tolerance early', 'Tolerance late'], 'ISO 8601 durations, such as PT30M.')}
    {chain && chainSection()}
    {solverShown && solverSection()}
    {modulesShown && moduleSection()}
    <p role="note" aria-label="Supply" className="hl-scheduling-authoring__note">{supply}</p>
  </>
  return <form className="hl-scheduling-authoring" aria-label="Schedule definition draft" data-read-only={props.readOnly ? '' : undefined} data-generation={generation} onSubmit={event => event.preventDefault()}>
    <p className="hl-scheduling-authoring__scope">Local draft. Edits stay in this editor and are not saved.</p>
    {!hasAcceptableSchedulingRevision(props.value) && <p role="status" className="hl-scheduling-authoring__note">{rejectedRevision}</p>}
    {hasDraft && (matched.length > 0
      ? <ul className="hl-scheduling-authoring__refusals" aria-label="Refusal list">{matched.map((diagnostic, index) => { const field = fieldOf(diagnostic.pointer); return <li key={index} data-code={diagnostic.code} data-pointer={diagnostic.pointer}>{`${diagnostic.code} at ${at(diagnostic.pointer)}${field ? ` (${field})` : ''}`}</li> })}</ul>
      : <p className="hl-scheduling-authoring__note">No refusals.</p>)}
    {hasDraft && (props.readOnly ? <fieldset className="hl-scheduling-authoring__read-only" disabled>{content}</fieldset> : content)}
    <div role="status" aria-live="polite" aria-label="Refusal announcements" className="hl-scheduling-authoring__announcer">{announcement}</div>
    {incoming && <aside role="alert" className="hl-scheduling-authoring__incoming">A different draft or revision arrived while you have local edits.<Button type="button" disabled={props.readOnly} onClick={keep}>Keep edits</Button><Button type="button" disabled={props.readOnly} onClick={() => discard(incoming)}>Discard edits</Button></aside>}
  </form>
}
