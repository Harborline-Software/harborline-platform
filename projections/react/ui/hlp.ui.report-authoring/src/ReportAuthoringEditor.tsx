import { useEffect, useRef, useState } from 'react'
export type ParameterType = 'boolean' | 'date_time' | 'integer' | 'float' | 'string'
export interface ReportParameter { name: string; type: ParameterType; default: { kind: 'boolean' | 'datetime' | 'integer' | 'float' | 'string'; value: string | number | boolean; type: ParameterType; is_valid: boolean }; effective_dated_type?: string }
export interface ReportDraft { tenant: string; key: string; version: string; measures: readonly { measure: string }[]; parameters?: readonly ReportParameter[] }
export type MeasureOption = string | { id: string; label: string }
export interface ReportRefusal { code: string; pointer: string; measure?: string; catalogue?: string }
export interface ReportAuthoringEditorProps { value: ReportDraft | null; resolveMeasures?: (query: string) => Promise<readonly MeasureOption[]>; onCreate: (draft: ReportDraft) => void; onChange: (value: ReportDraft) => void; refusals?: readonly ReportRefusal[]; readOnly?: boolean; revision?: string | null }
const realDateTime = (text: string) => {
  const [year, month, day] = text.slice(0, 10).split('-').map(Number)
  const date = new Date(0)
  date.setUTCFullYear(year, month - 1, day)
  const offset = /([+-])(\d{2}):(\d{2})$/.exec(text)
  return year >= 1 && date.getUTCMonth() === month - 1 && date.getUTCDate() === day && (!offset || Number(offset[2]) * 60 + Number(offset[3]) <= 840) && !Number.isNaN(Date.parse(text))
}
export function ReportAuthoringEditor({ value, onCreate, onChange, resolveMeasures, refusals = [], readOnly = false, revision = null }: ReportAuthoringEditorProps) {
  const current = useRef(value)
  const currentRevision = useRef(revision)
  const dirty = useRef(false)
  const held = useRef<{ draft: ReportDraft; revision: string | null } | null>(null)
  const generation = useRef(0)
  const lookupSerial = useRef(0)
  const staleRefusals = useRef<readonly ReportRefusal[] | null>(null)
  const [draft, setDraft] = useState(value)
  const [hasHeld, setHasHeld] = useState(false)
  const [invalidDefault, setInvalidDefault] = useState(false)
  const [incoming, setIncoming] = useState(false)
  const [effectiveType, setEffectiveType] = useState('')
  const [parameterName, setParameterName] = useState('')
  const [parameterType, setParameterType] = useState<ParameterType>('boolean')
  const [parameterDefault, setParameterDefault] = useState('')
  const [selected, setSelected] = useState('')
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [resolverError, setResolverError] = useState(false)
  const [options, setOptions] = useState<readonly MeasureOption[]>([])
  const resetPicker = () => { lookupSerial.current++; setOpen(false); setQuery(''); setSelected(''); setOptions([]); setResolverError(false) }
  const adopt = (next: ReportDraft | null) => { current.current = next; staleRefusals.current = null; currentRevision.current = revision; dirty.current = false; held.current = null; setHasHeld(false); generation.current++; setDraft(next); setIncoming(false); setParameterName(''); setParameterDefault(''); setEffectiveType(''); setParameterType('boolean'); setInvalidDefault(false); resetPicker() }
  const same = (left: ReportDraft, right: ReportDraft) => left.tenant === right.tenant && left.key === right.key && left.version === right.version
  useEffect(() => {
    if (!value) { adopt(null); return }
    if (!current.current) { adopt(value); return }
    if (same(value, current.current) && revision === currentRevision.current) { held.current = null; setHasHeld(false); setIncoming(false); return }
    if (held.current && same(value, held.current.draft) && revision === held.current.revision) return
    if (dirty.current) { held.current = { draft: value, revision }; setHasHeld(true); setIncoming(true) } else adopt(value)
  }, [value, revision])
  useEffect(() => {
    if (!open || !resolveMeasures) return
    let live = true
    const ticket = ++lookupSerial.current
    setOptions([])
    setSelected('')
    setResolverError(false)
    void resolveMeasures(query).then(result => { if (live && ticket === lookupSerial.current) setOptions(result) }).catch(() => { if (live && ticket === lookupSerial.current) setResolverError(true) })
    return () => { live = false }
  }, [open, query, resolveMeasures])
  if (!value) return <button type="button" disabled={readOnly} onClick={() => onCreate({ tenant: '', key: '', version: '', measures: [], parameters: [] })}>Create report</button>
  const active = draft ?? value
  const located = incoming || hasHeld || refusals === staleRefusals.current ? [] : refusals
  const change = (next: ReportDraft) => { current.current = next; staleRefusals.current = refusals; currentRevision.current = revision; dirty.current = true; generation.current++; setDraft(next); resetPicker(); onChange(next) }
  const edit = (update: () => void) => { dirty.current = true; generation.current++; update(); resetPicker() }
  const addParameter = () => {
    const text = parameterDefault.trim()
    const valid = parameterType === 'boolean' ? text === 'true' || text === 'false'
      : parameterType === 'integer' ? /^-?\d+$/.test(text) && Number.isSafeInteger(Number(text))
      : parameterType === 'float' ? text.length > 0 && Number.isFinite(Number(text))
      : parameterType === 'date_time' ? /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|[+-]\d{2}:\d{2})$/.test(text) && realDateTime(text) : true
    if (!valid) { setInvalidDefault(true); return }
    setInvalidDefault(false)
    change({ ...active, parameters: [...(active.parameters ?? []), { name: parameterName, type: parameterType, default: { kind: parameterType === 'date_time' ? 'datetime' : parameterType, type: parameterType, is_valid: true, value: parameterType === 'boolean' ? text === 'true' : parameterType === 'integer' || parameterType === 'float' ? Number(text) : parameterType === 'date_time' ? text : parameterDefault }, ...(parameterName === 'as_at' ? { effective_dated_type: effectiveType } : {}) }] })
  }
  return <section aria-label="Report authoring" style={{ overflowWrap: 'anywhere' }}><fieldset disabled={readOnly || incoming}><legend>Report identity</legend><label>Tenant<input value={active.tenant} onChange={e => change({ ...active, tenant: e.currentTarget.value })} /></label><label>Report key<input value={active.key} onChange={e => change({ ...active, key: e.currentTarget.value })} /></label><label>Version<input value={active.version} onChange={e => change({ ...active, version: e.currentTarget.value })} /></label></fieldset>{invalidDefault && <p role="alert">Enter a valid default.</p>}{incoming && <div role="alert">A different revision is available. <button type="button" onClick={() => adopt(value)}>Discard edits and load</button><button type="button" disabled={readOnly} onClick={() => setIncoming(false)}>Keep editing</button></div>}{(active.parameters ?? []).map((item, i) => <div key={i}>{item.name}<button type="button" disabled={readOnly || incoming} onClick={() => change({ ...active, parameters: (active.parameters ?? []).filter((_, index) => index !== i) })}>Remove parameter {item.name}</button>{located.filter(r => r.pointer === `/parameters/${i}` || r.pointer.startsWith(`/parameters/${i}/`)).map((r, j) => <span key={j} aria-label={`Parameter ${i + 1} refusal`}>{r.code}</span>)}</div>)}<label>Parameter name<input disabled={readOnly || incoming} value={parameterName} onChange={e => { const v = e.currentTarget.value; edit(() => setParameterName(v)) }} /></label><label>Parameter type<select disabled={readOnly || incoming} value={parameterType} onChange={e => { const v = e.currentTarget.value as ParameterType; edit(() => setParameterType(v)) }}>{(['boolean', 'date_time', 'integer', 'float', 'string'] as const).map(t => <option key={t} value={t}>{t}</option>)}</select></label><label>Parameter default<input disabled={readOnly || incoming} value={parameterDefault} onChange={e => { const v = e.currentTarget.value; edit(() => setParameterDefault(v)) }} /></label>{parameterName === "as_at" && <label>Effective-dated type<input disabled={readOnly || incoming} value={effectiveType} onChange={e => { const v = e.currentTarget.value; edit(() => setEffectiveType(v)) }} /></label>}<button type="button" disabled={readOnly || incoming} onClick={addParameter}>Add parameter</button>{active.measures.map((item, i) => <div key={i}>{item.measure}<button type="button" disabled={readOnly || incoming} onClick={() => change({ ...active, measures: active.measures.filter((_, index) => index !== i) })}>Remove measure {item.measure}</button>{located.filter(r => r.pointer === `/measures/${i}/measure`).map((r, j) => <span key={j} aria-label={`Measure ${i + 1} refusal`}>{[r.code, r.measure, r.catalogue].filter(Boolean).join(" ")}</span>)}</div>)}<button type="button" onClick={() => setOpen(!open)}>{open ? 'Close measure picker' : 'Choose measure'}</button>{open && <>{resolverError && <p role="alert">Measures could not be loaded. Close and reopen to retry.</p>}<label>Search measures<input value={query} onChange={e => setQuery(e.currentTarget.value)} /></label><select aria-label="Measures" size={5} value={selected} onChange={e => { setSelected(e.currentTarget.value); if (!readOnly) dirty.current = true }}>{options.map(option => { const id = typeof option === 'string' ? option : option.id; return <option key={id} value={id}>{typeof option === 'string' ? option : option.label}</option> })}</select><button type="button" disabled={readOnly || incoming || !options.some(option => (typeof option === 'string' ? option : option.id) === selected)} onClick={() => change({ ...active, measures: [...active.measures, { measure: selected }] })}>Add measure</button></>}</section>
}
