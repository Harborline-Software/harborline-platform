import { Fragment, useEffect, useId, useRef, useState } from 'react'

// This adapter edits the producer's JSON body. Admission remains server-owned;
// unknown members are preserved, including host-owned envelope metadata.
export interface ScheduleNumberToken { readonly rawJSON: string }
export type ScheduleJson = null | boolean | number | string | ScheduleNumberToken | ScheduleBody | readonly ScheduleJson[]
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
  /** Pass producer JSON text before JSON.parse can round its numeric tokens; serialize emitted bodies with JSON.stringify. */
  readonly value: ScheduleBody | string
  readonly catalogue?: ScheduleCatalogue
  readonly refusals?: readonly ScheduleRefusal[]
  readonly readOnly?: boolean
  readonly onChange: (value: ScheduleBody) => void
  readonly onCreate?: (value: ScheduleBody) => void
}
export const emptyScheduleDraft = (): ScheduleBody => ({})
// Native JSON source access/raw JSON keeps numeric tokens atomic across immutable body edits.
const tokenJson = JSON as JSON & {
  rawJSON: (source: string) => ScheduleNumberToken
  isRawJSON: (value: unknown) => value is ScheduleNumberToken
}
const isNumberToken = (value: unknown): value is ScheduleNumberToken => tokenJson.isRawJSON?.(value) ?? false
const parseBody = (source: string): ScheduleBody => {
  if (typeof tokenJson.rawJSON !== 'function' || typeof tokenJson.isRawJSON !== 'function') throw new Error('Exact Schedule JSON requires native JSON source access and rawJSON support.')
  const body: ScheduleJson = JSON.parse(source, (_key: string, value: ScheduleJson, context?: { source?: string }) => {
    if (typeof value !== 'number') return value
    if (context?.source === undefined) throw new Error('Exact Schedule JSON requires native JSON source access and rawJSON support.')
    return tokenJson.rawJSON(context.source)
  })
  if (!isObject(body)) throw new TypeError('Schedule JSON must be an object.')
  return body
}
const isObject = (value: ScheduleJson | undefined): value is ScheduleBody => value !== null && typeof value === 'object' && !Array.isArray(value) && !isNumberToken(value)
const object = (value: ScheduleJson | undefined): ScheduleBody => isObject(value) ? value : {}
const text = (value: ScheduleJson | undefined): string => isNumberToken(value) ? value.rawJSON : typeof value === 'string' || typeof value === 'number' ? String(value) : ''
const numberText = (value: ScheduleJson | undefined): string => {
  const raw = text(value)
  if (typeof value !== 'number' && !isNumberToken(value)) return raw
  const [mantissa, exponent = '0'] = raw.split(/[eE]/)
  const sign = mantissa.startsWith('-') ? '-' : ''
  const [whole, fraction = ''] = mantissa.replace('-', '').split('.')
  const digits = whole + fraction
  const point = whole.length + Number(exponent)
  const plain = point <= 0 ? '0.' + '0'.repeat(-point) + digits
    : point >= digits.length ? digits + '0'.repeat(point - digits.length)
    : digits.slice(0, point) + '.' + digits.slice(point)
  const [integer, decimal = ''] = plain.split('.')
  const normalizedWhole = integer.replace(/^0+/, '')
  const normalizedFraction = decimal.replace(/0+$/, '')
  if (!normalizedWhole && !normalizedFraction) return '0'
  return sign + (normalizedWhole || '0') + (normalizedFraction ? '.' + normalizedFraction : '')
}
const list = (value: ScheduleJson | undefined): readonly ScheduleJson[] => Array.isArray(value) ? value : []
const classes = ['None', 'Workforce', 'Timetabling', 'ProtocolAdmission', 'Project', 'JobShop', 'Mobility', 'CompositeRecovery']
const exactDecimalWire = (whole: string, fraction: string, parsed: number): boolean => {
  const [mantissa, exponent = '0'] = Math.abs(parsed).toString().split('e')
  const places = (mantissa.split('.')[1] ?? '').length
  const actual = BigInt(mantissa.replace('.', ''))
  const expected = BigInt(whole + fraction || '0')
  const shift = fraction.length + Number(exponent) - places
  return shift >= 0 ? actual * 10n ** BigInt(shift) === expected : actual === expected * 10n ** BigInt(-shift)
}
const parseNumber = (value: string, integer: boolean): number | undefined => {
  if (/^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)$/.exec(value)?.[0] !== value) return undefined
  const [digits, fraction = ''] = value.replace(/^[+-]/, '').split('.')
  const whole = digits.replace(/^0+/, '')
  // Compare decimal text before Number can round a value just beyond System.Decimal's bound.
  const maximum = '79228162514264337593543950335'
  if (whole.length > maximum.length || (whole.length === maximum.length && (whole > maximum || (whole === maximum && /[1-9]/.test(fraction))))) return undefined
  if (integer && /[1-9]/.test(fraction)) return undefined
  const normalizedFraction = fraction.replace(/0+$/, '')
  const significant = (whole + normalizedFraction).replace(/^0+/, '').replace(/0+$/, '')
  if (!integer && (significant.length > 15 || normalizedFraction.length > 28)) return undefined
  const parsed = Number(value)
  if (integer && (parsed < -2147483648 || parsed > 2147483647)) return undefined
  // Compare integer coefficients, so the JSON token must retain the entered decimal value exactly.
  if (!integer && !exactDecimalWire(whole, normalizedFraction, parsed)) return undefined
  return parsed === 0 ? 0 : parsed
}

/** Controlled Schedule body editor. The host supplies authoritative refusals for this value before publishing. */
export function SchedulingAuthoringEditor({ value: suppliedValue, catalogue = {}, refusals = [], readOnly, onChange, onCreate }: SchedulingAuthoringEditorProps) {
  const value = typeof suppliedValue === 'string' ? parseBody(suppliedValue) : suppliedValue
  const id = useId()
  const fieldId = (key: string) => `${id}-${encodeURIComponent(key)}`
  const latest = useRef(value)
  latest.current = value
  const [errors, setErrors] = useState<Readonly<Record<string, string>>>({})
  const setError = (controlKey: string, message: string) => setErrors(previous => ({ ...previous, [controlKey]: message }))
  const change = (member: string, next: ScheduleJson | undefined) => {
    const body = { ...latest.current }
    if (next === undefined) delete body[member]
    else body[member] = next
    onChange(body)
  }
  const nested = (member: string, key: string, next: ScheduleJson) => {
    if (member === 'solver' && key === 'backend_id' && next === '') return change(member, undefined)
    const group: ScheduleBody = { ...(member === 'solver' ? { parameters: {} } : {}), ...object(latest.current[member]), [key]: next }
    change(member, member === 'escalation' && !text(group.after) && !text(group.to) ? undefined : group)
  }
  const input = (label: string, current: ScheduleJson | undefined, update: (next: string) => void) => <label htmlFor={fieldId(label)}>{label}<input id={fieldId(label)} aria-label={label} value={text(current)} onChange={event => update(event.currentTarget.value)} /></label>
  const numberInput = (label: string, current: ScheduleJson | undefined, update: (next: number | null) => void, integer = false) => <label htmlFor={fieldId(label)}>{label}<input id={fieldId(label)} key={numberText(current)} aria-label={label} inputMode="decimal" defaultValue={numberText(current)} onBlur={event => {
    const raw = event.currentTarget.value
    if (raw === numberText(current)) { setError(label, ''); return }
    const trimmed = raw.replace(/^[ \t\r\n\f\v]+|[ \t\r\n\f\v]+$/g, '')
    const parsed = trimmed === '' ? null : parseNumber(trimmed, integer)
    if (parsed === undefined) { setError(label, integer ? `${label} must be a 32-bit integer in plain decimal notation.` : `${label} must be an exact decimal with at most 15 significant digits and scale 28.`); return }
    event.currentTarget.value = numberText(parsed)
    setError(label, ''); update(parsed)
  }} /></label>
  const pick = (label: string, current: string, options: readonly ScheduleOption[], update: (next: string) => void) => <label htmlFor={fieldId(label)}>{label}<select id={fieldId(label)} aria-label={label} value={current} onChange={event => update(event.currentTarget.value)}><option value="">Choose…</option>{current && !options.some(option => option.id === current) && <option value={current}>{current} (not offered)</option>}{options.map(option => <option key={option.id} value={option.id}>{option.label}</option>)}</select></label>
  const words = (values: readonly string[]): ScheduleOption[] => values.map(word => ({ id: word, label: word }))
  const basis = object(value.basis)
  const starts = object(value.starts)
  const solver = object(value.solver)
  const nodes = list(basis.nodes)
  const modules = list(value.modules)
  const errorControls = new Set(['Backend parameters', 'Work budget',
    ...(basis.kind === 'threshold' ? ['Threshold'] : []),
    ...(catalogue.modules ?? []).filter(module => modules.some(entry => object(entry).module === module.id)).map(module => `module.parameters.${module.id}`)])
  useEffect(() => {
    if (Object.keys(errors).some(key => !errorControls.has(key)))
      setErrors(previous => Object.fromEntries(Object.entries(previous).filter(([key]) => errorControls.has(key))))
  })
  const updateNode = (index: number, key: string, next: ScheduleJson | undefined) => nested('basis', 'nodes', nodes.map((node, position) => {
    if (position !== index) return node
    const updated = { ...object(node) }
    if (next === undefined) delete updated[key]
    else updated[key] = next
    return updated
  }))
  const parameters = (label: string, current: ScheduleJson | undefined, update: (next: ScheduleBody) => void, controlKey = label) => <label htmlFor={fieldId(controlKey)}>{label}<textarea id={fieldId(controlKey)} aria-label={label} key={JSON.stringify(current)} defaultValue={JSON.stringify(current ?? {}, null, 2)} onBlur={event => {
    if (event.currentTarget.value === event.currentTarget.defaultValue) { setError(controlKey, ''); return }
    try {
      const parsed: ScheduleJson = JSON.parse(event.currentTarget.value)
      if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) { setError(controlKey, `${label} must be a JSON object.`); return }
      setError(controlKey, ''); update(object(parsed))
    } catch { setError(controlKey, `${label} must be a JSON object.`) }
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
          {['early', 'late'].map(side => <Fragment key={side}>{input(`Node ${index + 1} tolerance ${side}`, object(node.tolerance)[side], next => updateNode(index, 'tolerance', { ...object(node.tolerance), [side]: next }))}</Fragment>)}
          {['earliest', 'latest'].map(side => <Fragment key={side}>{input(`Node ${index + 1} ${side}`, object(node.window)[side], next => updateNode(index, 'window', { ...object(node.window), [side]: next }))}</Fragment>)}
          {Object.hasOwn(node, 'window') && <button type="button" onClick={() => updateNode(index, 'window', undefined)}>Remove node {index + 1} window</button>}
          <button type="button" onClick={() => nested('basis', 'nodes', nodes.filter((_, position) => position !== index))}>Remove node {index + 1}</button>
        </fieldset> })}
        <button type="button" onClick={() => nested('basis', 'nodes', [...nodes, { node_id: '', after_node_id: null, offset: '', tolerance: { early: '', late: '' } }])}>Add node</button>
      </fieldset>}
      <fieldset><legend>Validity window</legend>{['earliest', 'latest'].map(side => <Fragment key={side}>{input(`Window ${side}`, object(value.window)[side], next => nested('window', side, next))}</Fragment>)}{Object.hasOwn(value, 'window') && <button type="button" onClick={() => change('window', undefined)}>Remove validity window</button>}</fieldset>
      <fieldset><legend>On-time tolerance</legend>{['early', 'late'].map(side => <Fragment key={side}>{input(`Tolerance ${side}`, object(value.tolerance)[side], next => nested('tolerance', side, next))}</Fragment>)}{Object.hasOwn(value, 'tolerance') && <button type="button" onClick={() => change('tolerance', undefined)}>Remove tolerance</button>}</fieldset>
      {input('Grace', value.grace, next => change('grace', next || undefined))}
      {pick('Missed behaviour', text(value.missed_behaviour), words(['record_and_replan', 'window_closed']), next => change('missed_behaviour', next || undefined))}
      {input('Escalation after', object(value.escalation).after, next => nested('escalation', 'after', next))}
      {input('Escalation to', object(value.escalation).to, next => nested('escalation', 'to', next))}
      {pick('Problem class', text(value.problem_class) || 'None', words(classes), next => change('problem_class', next))}
      {pick('Legality citation', text(value.legality_ref), catalogue.legality ?? [], next => change('legality_ref', next || undefined))}
      <fieldset><legend>Solver binding</legend>
        {pick('Backend', text(solver.backend_id), catalogue.backends ?? [], next => nested('solver', 'backend_id', next))}
        {pick('Guarantee', text(solver.guarantee), words(catalogue.backends?.find(backend => backend.id === solver.backend_id)?.guarantees ?? []), next => nested('solver', 'guarantee', next))}
        {numberInput('Work budget', solver.work_budget, next => nested('solver', 'work_budget', next), true)}
        {parameters('Backend parameters', solver.parameters, next => nested('solver', 'parameters', next))}
      </fieldset>
      <fieldset><legend>Admitted modules</legend><p>Naming a module does not certify it or confer backend support.</p>
        {(catalogue.modules ?? []).map(module => { const entry = modules.find(candidate => object(candidate).module === module.id); return <div key={module.id} data-scope={module.scope}>
          <label htmlFor={fieldId(`module.selected.${module.id}`)}><input id={fieldId(`module.selected.${module.id}`)} type="checkbox" checked={entry !== undefined} onChange={event => change('modules', event.currentTarget.checked ? [...modules, { module: module.id, parameters: {} }] : modules.filter(candidate => object(candidate).module !== module.id))} />{module.label} — {module.scope} scope{module.scope === 'chain' ? ' (explicit opt-in)' : ''}</label>
          {entry && parameters(`${module.label} parameters`, object(entry).parameters, next => change('modules', modules.map(candidate => object(candidate).module === module.id ? { ...object(candidate), parameters: next } : candidate)), `module.parameters.${module.id}`)}
        </div> })}
      </fieldset>
    </fieldset>
    <p>Supply is read from the shared availability runtime, from each Resource's own availability source. Configure that source in Booking, per Resource.</p>
    {Object.entries(errors).map(([key, message]) => errorControls.has(key) && message && <p key={key} role="alert">{message}</p>)}
    <section aria-labelledby={`${id}-refusals`} aria-live="polite" aria-atomic="true"><h2 id={`${id}-refusals`}>Refusal list</h2>
      {refusals.length === 0 ? <p>No refusals supplied. The server must validate before publish.</p> : <ul>{refusals.map((refusal, index) => <li key={index}><code>{refusal.code}</code> at <code>{refusal.pointer}</code>{refusal.target && <span> {refusal.target}</span>}{refusal.detail !== undefined && <pre>{JSON.stringify(refusal.detail, null, 2)}</pre>}</li>)}</ul>}
    </section>
  </form>
}
