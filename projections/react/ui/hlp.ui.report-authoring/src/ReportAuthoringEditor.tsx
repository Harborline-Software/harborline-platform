import { useEffect, useRef, useState } from 'react'
export type ParameterType = 'boolean' | 'date_time' | 'integer' | 'float' | 'string'
export interface ReportParameter { name: string; type: ParameterType; default: { kind: 'boolean' | 'datetime' | 'integer' | 'float' | 'string'; value: string | number | boolean; type: ParameterType; is_valid: boolean }; effective_dated_type?: string }
export interface ReportDraft { tenant: string; key: string; version: string; measures: readonly { measure: string }[]; parameters?: readonly ReportParameter[] }
export type MeasureOption = string | { id: string; label: string }
export interface ReportRefusal { code: string; pointer: string; measure?: string; catalogue?: string }
export interface ReportAuthoringEditorProps { value: ReportDraft | null; resolveMeasures?: (query: string) => Promise<readonly MeasureOption[]>; onCreate: (draft: ReportDraft) => void; onChange: (value: ReportDraft) => void; refusals?: readonly ReportRefusal[]; readOnly?: boolean }
export function ReportAuthoringEditor({ value, onCreate, onChange, resolveMeasures, refusals = [], readOnly = false }: ReportAuthoringEditorProps) {
  const previous = useRef(value)
  const ignored = useRef<ReportDraft | null>(null)
  const [kept, setKept] = useState<ReportDraft | null>(null)
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
  useEffect(() => {
    const old = previous.current
    if (ignored.current && value && ignored.current.key === value.key && ignored.current.version === value.version && ignored.current.tenant === value.tenant) return
    if (old && value && (old.key !== value.key || old.version !== value.version || old.tenant !== value.tenant)) {
      if (parameterName || parameterDefault || effectiveType) setIncoming(true)
      else previous.current = value
    }
  }, [value, parameterName, parameterDefault, effectiveType])
  useEffect(() => {
    if (!open || !resolveMeasures) return
    let current = true
    setOptions([])
    setSelected('')
    setResolverError(false)
    void resolveMeasures(query).then(result => { if (current) setOptions(result) }).catch(() => { if (current) setResolverError(true) })
    return () => { current = false }
  }, [open, query, resolveMeasures])
  if (!value) return <button type="button" disabled={readOnly} onClick={() => onCreate({ tenant: '', key: '', version: '', measures: [], parameters: [] })}>Create report</button>
  const active = kept ?? (incoming ? previous.current : value) ?? value
  const change = (next: ReportDraft) => { if (kept) setKept(next); onChange(next) }
  const addParameter = () => {
    const text = parameterDefault.trim()
    const valid = parameterType === 'boolean' ? text === 'true' || text === 'false'
      : parameterType === 'integer' ? /^-?\d+$/.test(text) && Number.isSafeInteger(Number(text))
      : parameterType === 'float' ? text.length > 0 && Number.isFinite(Number(text))
      : parameterType === 'date_time' ? /(?:Z|[+-]\d{2}:\d{2})$/.test(text) && !Number.isNaN(Date.parse(text)) : true
    if (!valid) { setInvalidDefault(true); return }
    setInvalidDefault(false)
    change({ ...active, parameters: [...(active.parameters ?? []), { name: parameterName, type: parameterType, default: { kind: parameterType === 'date_time' ? 'datetime' : parameterType, type: parameterType, is_valid: true, value: parameterType === 'boolean' ? text === 'true' : parameterType === 'integer' || parameterType === 'float' ? Number(text) : parameterDefault }, ...(parameterName === 'as_at' ? { effective_dated_type: effectiveType } : {}) }] })
  }
  return <section aria-label="Report authoring"><fieldset disabled={readOnly || incoming}><legend>Report identity</legend><label>Tenant<input value={active.tenant} onChange={e => change({ ...active, tenant: e.currentTarget.value })} /></label><label>Report key<input value={active.key} onChange={e => change({ ...active, key: e.currentTarget.value })} /></label><label>Version<input value={active.version} onChange={e => change({ ...active, version: e.currentTarget.value })} /></label></fieldset>{invalidDefault && <p role="alert">Enter a valid default.</p>}{incoming && <div role="alert">A different revision is available. <button type="button" disabled={readOnly} onClick={() => { setParameterName(''); setParameterDefault(''); setEffectiveType(''); setParameterType('boolean'); setOpen(false); setQuery(''); setSelected(''); setOptions([]); setResolverError(false); previous.current = value; ignored.current = null; setKept(null); setIncoming(false) }}>Discard edits and load</button><button type="button" disabled={readOnly} onClick={() => { setKept(previous.current); ignored.current = value; setIncoming(false) }}>Keep editing</button></div>}{(active.parameters ?? []).map((item, i) => <div key={i}>{item.name}<button type="button" disabled={readOnly || incoming} onClick={() => change({ ...active, parameters: (active.parameters ?? []).filter((_, index) => index !== i) })}>Remove parameter {item.name}</button>{(incoming || kept ? [] : refusals).filter(r => r.pointer === `/parameters/${i}` || r.pointer.startsWith(`/parameters/${i}/`)).map((r, j) => <span key={j} aria-label={`Parameter ${i + 1} refusal`}>{r.code}</span>)}</div>)}<label>Parameter name<input disabled={readOnly || incoming} value={parameterName} onChange={e => setParameterName(e.currentTarget.value)} /></label><label>Parameter type<select disabled={readOnly || incoming} value={parameterType} onChange={e => setParameterType(e.currentTarget.value as ParameterType)}>{(['boolean', 'date_time', 'integer', 'float', 'string'] as const).map(t => <option key={t} value={t}>{t}</option>)}</select></label><label>Parameter default<input disabled={readOnly || incoming} value={parameterDefault} onChange={e => setParameterDefault(e.currentTarget.value)} /></label>{parameterName === "as_at" && <label>Effective-dated type<input disabled={readOnly || incoming} value={effectiveType} onChange={e => setEffectiveType(e.currentTarget.value)} /></label>}<button type="button" disabled={readOnly || incoming} onClick={addParameter}>Add parameter</button>{active.measures.map((item, i) => <div key={i}>{item.measure}<button type="button" disabled={readOnly || incoming} onClick={() => change({ ...active, measures: active.measures.filter((_, index) => index !== i) })}>Remove measure {item.measure}</button>{(incoming || kept ? [] : refusals).filter(r => r.pointer === `/measures/${i}/measure`).map((r, j) => <span key={j} aria-label={`Measure ${i + 1} refusal`}>{[r.code, r.measure, r.catalogue].filter(Boolean).join(" ")}</span>)}</div>)}<button type="button" onClick={() => setOpen(!open)}>{open ? 'Close measure picker' : 'Choose measure'}</button>{open && <>{resolverError && <p role="alert">Measures could not be loaded. Close and reopen to retry.</p>}<label>Search measures<input value={query} onChange={e => setQuery(e.currentTarget.value)} /></label><select aria-label="Measures" size={5} value={selected} onChange={e => setSelected(e.currentTarget.value)}>{options.map(option => { const id = typeof option === 'string' ? option : option.id; return <option key={id} value={id}>{typeof option === 'string' ? option : option.label}</option> })}</select><button type="button" disabled={readOnly || incoming || !options.some(option => (typeof option === 'string' ? option : option.id) === selected)} onClick={() => change({ ...active, measures: [...active.measures, { measure: selected }] })}>Add measure</button></>}</section>
}
