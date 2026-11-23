import { useId, useRef, useState } from 'react'

// This adapter edits the producer's JSON body. Admission remains server-owned;
// unknown members are preserved, including host-owned envelope metadata.
export type ScheduleJson = null | boolean | number | string | ScheduleBody | readonly ScheduleJson[]
export interface ScheduleBody { readonly [member: string]: ScheduleJson | undefined }
export interface ScheduleOption { readonly id: string; readonly label: string }
export interface ScheduleBackend extends ScheduleOption { readonly guarantees: readonly string[] }
export interface ScheduleModule extends ScheduleOption { readonly scope: 'resource' | 'chain' | 'occurrence' }
export interface ScheduleCatalogue {
  readonly types?: readonly ScheduleOption[]
  readonly starts?: readonly ScheduleOption[]
  readonly legality?: readonly ScheduleOption[]
  readonly backends?: readonly ScheduleBackend[]
  /** Only admitted modules; naming one confers no backend support or certification. */
  readonly modules?: readonly ScheduleModule[]
}
export interface ScheduleRefusal { readonly code: string; readonly pointer: string; readonly detail?: ScheduleJson; readonly target?: string }
export interface SchedulingAuthoringEditorProps {
  readonly value: ScheduleBody
  readonly catalogue?: ScheduleCatalogue
  readonly refusals?: readonly ScheduleRefusal[]
  readonly readOnly?: boolean
  readonly onChange: (value: ScheduleBody) => void
  readonly onCreate?: (value: ScheduleBody) => void
}
export const emptyScheduleDraft = (): ScheduleBody => ({})
const isObject = (value: ScheduleJson | undefined): value is ScheduleBody => value !== null && typeof value === 'object' && !Array.isArray(value)
const object = (value: ScheduleJson | undefined): ScheduleBody => isObject(value) ? value : {}
const text = (value: ScheduleJson | undefined): string => typeof value === 'string' || typeof value === 'number' ? String(value) : ''
const list = (value: ScheduleJson | undefined): readonly ScheduleJson[] => Array.isArray(value) ? value : []
const classes = ['None', 'Workforce', 'Timetabling', 'ProtocolAdmission', 'Project', 'JobShop', 'Mobility', 'CompositeRecovery']

/** Controlled Schedule body editor. The host supplies authoritative refusals for this value before publishing. */
export function SchedulingAuthoringEditor({ value, catalogue = {}, refusals = [], readOnly, onChange, onCreate }: SchedulingAuthoringEditorProps) {
  const id = useId()
  const latest = useRef(value)
  latest.current = value
  const [parameterError, setParameterError] = useState('')
  const [numberError, setNumberError] = useState('')
  const change = (member: string, next: ScheduleJson | undefined) => {
    const body = { ...latest.current }
    if (next === undefined) delete body[member]
    else body[member] = next
    onChange(body)
  }
  const nested = (member: string, key: string, next: ScheduleJson) => change(member, { ...object(latest.current[member]), [key]: next })
  const input = (label: string, current: ScheduleJson | undefined, update: (next: string) => void) => <label>{label}<input aria-label={label} value={text(current)} onChange={event => update(event.currentTarget.value)} /></label>
  const numberInput = (label: string, current: ScheduleJson | undefined, update: (next: number | null) => void) => <label>{label}<input key={text(current)} aria-label={label} inputMode="decimal" defaultValue={text(current)} onBlur={event => {
    const raw = event.currentTarget.value
    if (raw === text(current)) { setNumberError(''); return }
    const parsed = raw.trim() === '' ? null : Number(raw)
    if (parsed !== null && !Number.isFinite(parsed)) { setNumberError(`${label} must be a finite number.`); return }
    setNumberError(''); update(parsed)
  }} /></label>
  const pick = (label: string, current: string, options: readonly ScheduleOption[], update: (next: string) => void) => <label>{label}<select aria-label={label} value={current} onChange={event => update(event.currentTarget.value)}><option value="">Choose…</option>{current && !options.some(option => option.id === current) && <option value={current}>{current} (not offered)</option>}{options.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label>
  const words = (values: readonly string[]): ScheduleOption[] => values.map(word => ({ id: word, label: word }))
  const basis = object(value.basis)
  const starts = object(value.starts)
  const solver = object(value.solver)
  const nodes = list(basis.nodes)
  const modules = list(value.modules)
  const updateNode = (index: number, key: string, next: ScheduleJson) => nested('basis', 'nodes', nodes.map((node, position) => position === index ? { ...object(node), [key]: next } : node))
  const parameters = (label: string, current: ScheduleJson | undefined, update: (next: ScheduleBody) => void) => <label>{label}<textarea aria-label={label} key={JSON.stringify(current)} defaultValue={JSON.stringify(current ?? {}, null, 2)} onBlur={event => {
    if (event.currentTarget.value === event.currentTarget.defaultValue) { setParameterError(''); return }
    try {
      const parsed: ScheduleJson = JSON.parse(event.currentTarget.value)
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) { setParameterError(`${label} must be a JSON object.`); return }
      setParameterError(''); update(object(parsed))
    } catch { setParameterError(`${label} must be a JSON object.`) }
  }} /></label>
  return <form className="hl-scheduling-authoring" aria-label="Schedule editor" onSubmit={event => event.preventDefault()}>
    <fieldset disabled={readOnly}><legend>Schedule</legend>
      {onCreate && <button type="button" onClick={() => onCreate(emptyScheduleDraft())}>Create Schedule</button>}
      {input('Schedule name', value.name, next => change('name', next))}
      {pick('Record type', text(value.on_type_id), catalogue.types ?? [], next => change('on_type_id', next || undefined))}
      {pick('Starts kind', text(starts.kind), words(['form', 'workflow', 'work_item']), next => nested('starts', 'kind', next))}
      {catalogue.starts ? pick('Starts target', text(starts.target_id), catalogue.starts, next => nested('starts', 'target_id', next)) : input('Starts target', starts.target_id, next => nested('starts', 'target_id', next))}
      {pick('Basis', text(basis.kind), words(['fixed_calendar', 'completion_offset', 'threshold', 'event_anchored_chain']), next => {
        const body = { ...latest.current }
        if (next) { body.basis = { kind: next }; delete body.due_date }
        else delete body.basis
        onChange(body)
      })}
      {!basis.kind && input('Due date', value.due_date, next => change('due_date', next))}
      {basis.kind === 'fixed_calendar' && input('Recurrence rule', basis.rrule, next => nested('basis', 'rrule', next))}
      {basis.kind === 'completion_offset' && input('Completion offset', basis.offset, next => nested('basis', 'offset', next))}
      {basis.kind === 'threshold' && <>{input('Reading', basis.reading, next => nested('basis', 'reading', next))}{pick('Crossing', text(basis.crossing), words(['above', 'below']), next => nested('basis', 'crossing', next))}{numberInput('Threshold', basis.value, next => nested('basis', 'value', next))}</>}
      {basis.kind === 'event_anchored_chain' && <fieldset><legend>Event-anchored chain</legend>
        {input('Chain anchor', basis.anchor, next => nested('basis', 'anchor', next))}
        {nodes.map((entry, index) => { const node = object(entry); return <fieldset key={index}><legend>Node {index + 1}</legend>
          {input(`Node ${index + 1} id`, node.node_id, next => updateNode(index, 'node_id', next))}
          {input(`Node ${index + 1} follows`, node.after_node_id, next => updateNode(index, 'after_node_id', next || null))}
          {input(`Node ${index + 1} offset`, node.offset, next => updateNode(index, 'offset', next))}
          {['early', 'late'].map(side => <span key={side}>{input(`Node ${index + 1} tolerance ${side}`, object(node.tolerance)[side], next => updateNode(index, 'tolerance', { ...object(node.tolerance), [side]: next }))}</span>)}
          {['earliest', 'latest'].map(side => <span key={side}>{input(`Node ${index + 1} ${side}`, object(node.window)[side], next => updateNode(index, 'window', { ...object(node.window), [side]: next }))}</span>)}
          <button type="button" onClick={() => nested('basis', 'nodes', nodes.filter((_, position) => position !== index))}>Remove node {index + 1}</button>
        </fieldset> })}
        <button type="button" onClick={() => nested('basis', 'nodes', [...nodes, { node_id: '', after_node_id: null, offset: '', tolerance: { early: '', late: '' } }])}>Add node</button>
      </fieldset>}
      <fieldset><legend>Validity window</legend>{['earliest', 'latest'].map(side => <span key={side}>{input(`Window ${side}`, object(value.window)[side], next => nested('window', side, next))}</span>)}{Object.hasOwn(value, 'window') && <button type="button" onClick={() => change('window', undefined)}>Remove validity window</button>}</fieldset>
      <fieldset><legend>On-time tolerance</legend>{['early', 'late'].map(side => <span key={side}>{input(`Tolerance ${side}`, object(value.tolerance)[side], next => nested('tolerance', side, next))}</span>)}</fieldset>
      {input('Grace', value.grace, next => change('grace', next || undefined))}
      {pick('Missed behaviour', text(value.missed_behaviour), words(['record_and_replan', 'window_closed']), next => change('missed_behaviour', next || undefined))}
      {input('Escalation after', object(value.escalation).after, next => nested('escalation', 'after', next))}
      {input('Escalation to', object(value.escalation).to, next => nested('escalation', 'to', next))}
      {pick('Problem class', text(value.problem_class) || 'None', words(classes), next => change('problem_class', next))}
      {pick('Legality citation', text(value.legality_ref), catalogue.legality ?? [], next => change('legality_ref', next || undefined))}
      <fieldset><legend>Solver binding</legend>
        {pick('Backend', text(solver.backend_id), catalogue.backends ?? [], next => nested('solver', 'backend_id', next))}
        {pick('Guarantee', text(solver.guarantee), words(catalogue.backends?.find(backend => backend.id === solver.backend_id)?.guarantees ?? []), next => nested('solver', 'guarantee', next))}
        {input('Work budget', solver.work_budget, next => nested('solver', 'work_budget', next === '' ? null : Number(next)))}
        {parameters('Backend parameters', solver.parameters, next => nested('solver', 'parameters', next))}
      </fieldset>
      <fieldset><legend>Admitted modules</legend><p>Naming a module does not certify it or confer backend support.</p>
        {(catalogue.modules ?? []).map(module => { const entry = modules.find(candidate => object(candidate).module === module.id); return <div key={module.id} data-scope={module.scope}>
          <label><input type="checkbox" checked={entry !== undefined} onChange={event => change('modules', event.currentTarget.checked ? [...modules, { module: module.id, parameters: {} }] : modules.filter(candidate => object(candidate).module !== module.id))} />{module.label} — {module.scope} scope{module.scope === 'chain' ? ' (explicit opt-in)' : ''}</label>
          {entry && parameters(`${module.label} parameters`, object(entry).parameters, next => change('modules', modules.map(candidate => object(candidate).module === module.id ? { ...object(candidate), parameters: next } : candidate)))}
        </div> })}
      </fieldset>
    </fieldset>
    <p>Supply is read from the shared availability runtime, from each Resource's own availability source. Configure that source in Booking, per Resource.</p>
    {parameterError && <p role="alert">{parameterError}</p>}
    {numberError && <p role="alert">{numberError}</p>}
    <section aria-labelledby={`${id}-refusals`} aria-live="polite" aria-atomic="true"><h2 id={`${id}-refusals`}>Refusal list</h2>
      {refusals.length === 0 ? <p>No refusals supplied. The server must validate before publish.</p> : <ul>{refusals.map((refusal, index) => <li key={index}><code>{refusal.code}</code> at <code>{refusal.pointer}</code>{refusal.target && <span> {refusal.target}</span>}{refusal.detail !== undefined && <pre>{JSON.stringify(refusal.detail, null, 2)}</pre>}</li>)}</ul>}
    </section>
  </form>
}
