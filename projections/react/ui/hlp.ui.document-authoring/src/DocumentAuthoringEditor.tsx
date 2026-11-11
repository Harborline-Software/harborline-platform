import { useEffect, useRef, useState } from 'react'
import { Button } from '@harborline-platform/hlp.ui.button'
import { documentOption, type DocumentBlock, type DocumentCatalogue, type DocumentCatalogueOption, type DocumentDenial, type DocumentDraft, type DocumentLine, type DocumentMasterVariant, type DocumentPageFlow, type DocumentPreviewRequest, type DocumentPreviewResult, type DocumentRun } from './DocumentDraft.types'

export interface DocumentAuthoringEditorProps {
  readonly value: DocumentDraft
  readonly catalogue?: DocumentCatalogue
  readonly pageFlow: DocumentPageFlow
  readonly readOnly?: boolean
  readonly canPreview?: boolean
  readonly denial?: DocumentDenial
  readonly onDraftChange: (draft: DocumentDraft, generation: number) => void
  readonly onPreview: (request: DocumentPreviewRequest) => Promise<DocumentPreviewResult>
}

const same = (left: DocumentDraft, right: DocumentDraft) => left.identity === right.identity && left.revision === right.revision
export function DocumentAuthoringEditor(props: DocumentAuthoringEditorProps) {
  const [draft, setDraft] = useState(props.value)
  const [incoming, setIncoming] = useState<DocumentDraft>()
  const [generation, setGeneration] = useState(0)
  const [preview, setPreview] = useState<DocumentPreviewResult>()
  const [status, setStatus] = useState('')
  const [targetKind, setTargetKind] = useState<'record' | 'sample'>('sample')
  const [targetId, setTargetId] = useState('')
  const current = useRef(draft)
  const dirty = useRef(false)
  const held = useRef<DocumentDraft | undefined>(undefined)
  const serial = useRef(0)
  const permission = useRef(false)
  permission.current = !props.readOnly && props.canPreview === true
  const resetPreview = () => { serial.current++; setPreview(undefined); setStatus('') }
  useEffect(() => { if (props.readOnly || !props.canPreview) resetPreview() }, [props.readOnly, props.canPreview])
  useEffect(() => { resetPreview() }, [props.pageFlow])
  const adopt = (next: DocumentDraft) => { current.current = next; setDraft(next); dirty.current = false; held.current = undefined; setIncoming(undefined); setGeneration(value => value + 1); resetPreview() }
  useEffect(() => {
    if (same(props.value, current.current)) { held.current = undefined; setIncoming(undefined); return }
    if (held.current && same(props.value, held.current)) return
    if (dirty.current) { held.current = props.value; setIncoming(props.value); resetPreview() } else adopt(props.value)
  }, [props.value])
  const change = (next: DocumentDraft) => { if (props.readOnly) return; current.current = next; dirty.current = true; setDraft(next); const nextGeneration = generation + 1; setGeneration(nextGeneration); resetPreview(); props.onDraftChange(next, nextGeneration) }
  const block = (index: number, next: DocumentBlock) => change({ ...draft, blocks: draft.blocks.map((value, position) => position === index ? next : value) })
  const line = (index: number, lineIndex: number, next: DocumentLine) => block(index, { ...draft.blocks[index], lines: draft.blocks[index].lines.map((value, position) => position === lineIndex ? next : value) })
  const run = (index: number, lineIndex: number, runIndex: number, next: DocumentRun) => line(index, lineIndex, { runs: draft.blocks[index].lines[lineIndex].runs.map((value, position) => position === runIndex ? next : value) })
  const choose = (label: string, value: string | undefined, options: readonly DocumentCatalogueOption[] | undefined, update: (value: string) => void) => {
    const entries = (options ?? []).map(documentOption)
    return <label>{label}{entries.length === 0 && <span>No options offered.</span>}<select aria-label={label} value={value ?? ''} onChange={event => update(event.currentTarget.value)}><option value="">Not set</option>{value && !entries.some(entry => entry.id === value) && <option value={value}>{value} (not offered)</option>}{entries.map(entry => <option key={entry.id} value={entry.id}>{entry.label}</option>)}</select></label>
  }
  const requestPreview = async () => {
    if (!permission.current || !targetId || incoming) return
    const ticket = ++serial.current
    setPreview(undefined); setStatus('Rendering preview…')
    try {
      const result = await props.onPreview({ mode: 'preview', draft, pageFlow: props.pageFlow, target: { kind: targetKind, id: targetId } })
      if (ticket !== serial.current || !permission.current) return
      if (result.minted !== false) { setStatus('Preview refused.'); return }
      setPreview(result); setStatus('Preview only. No document issued.')
    } catch { if (ticket === serial.current && permission.current) setStatus('Preview refused.') }
  }
  const denial = props.denial
  if (new Set(draft.blocks.map(value => value.id)).size !== draft.blocks.length) return <p role="alert">Duplicate block ids. The host must supply unique block ids.</p>
  return <form className="hl-document-authoring" aria-label="Document template draft" onSubmit={event => event.preventDefault()}>
    <p>Local draft. Preview does not issue a document.</p>
    <fieldset disabled={props.readOnly}><legend>Template</legend>
      <label>Template name<input aria-label="Template name" value={draft.name} onChange={event => change({ ...draft, name: event.currentTarget.value })} /></label>
      <label>Document type<input aria-label="Document type" value={draft.documentType} onChange={event => change({ ...draft, documentType: event.currentTarget.value })} /></label>
      <label>Locale policy<select aria-label="Locale policy" value={draft.locale.kind} onChange={event => change({ ...draft, locale: { kind: event.currentTarget.value as DocumentDraft['locale']['kind'] } })}><option value="fixed">Fixed</option><option value="from_record">From record</option><option value="from_instance">From instance</option></select></label>
      {draft.locale.kind === 'fixed' && <label>Document locale<input aria-label="Document locale" value={draft.locale.tag ?? ''} onChange={event => change({ ...draft, locale: { ...draft.locale, tag: event.currentTarget.value } })} /></label>}
      <label>Brand name<input aria-label="Brand name" value={draft.style.brandName ?? ''} onChange={event => change({ ...draft, style: { ...draft.style, brandName: event.currentTarget.value } })} /></label>
      <label>Logo asset reference<input aria-label="Logo asset reference" value={draft.style.logoAssetRef ?? ''} onChange={event => change({ ...draft, style: { ...draft.style, logoAssetRef: event.currentTarget.value || undefined } })} /></label>
      <fieldset><legend>Page masters</legend>{(['first', 'left', 'right'] as const).map(variant => <fieldset key={variant}><legend>{variant}</legend>{(['leftRegion', 'centerRegion', 'rightRegion'] as const).map(region => <div key={region}>{choose(`${variant} ${region}`, draft.masters[variant][region], props.catalogue?.regions, value => change({ ...draft, masters: { ...draft.masters, [variant]: { ...draft.masters[variant], [region]: value || undefined } satisfies DocumentMasterVariant } }))}</div>)}</fieldset>)}</fieldset>
      {draft.blocks.length === 0 && <p>No blocks authored.</p>}
      {draft.blocks.map((value, index) => <fieldset key={value.id}><legend>Block {index + 1}</legend>
        {choose(`Block ${index + 1} container token`, value.collapseBelow, ['sm', 'md', 'lg'], next => block(index, { ...value, collapseBelow: next as DocumentBlock['collapseBelow'] || undefined }))}
        <label><input type="checkbox" aria-label={`Block ${index + 1} break before`} checked={value.breakBefore} onChange={event => block(index, { ...value, breakBefore: event.currentTarget.checked })} />Break before</label>
        <label><input type="checkbox" aria-label={`Block ${index + 1} break after`} checked={value.breakAfter} onChange={event => block(index, { ...value, breakAfter: event.currentTarget.checked })} />Break after</label>
        <label>Break inside<select aria-label={`Block ${index + 1} break inside`} value={value.breakInside} onChange={event => block(index, { ...value, breakInside: event.currentTarget.value as DocumentBlock['breakInside'] })}><option value="auto">Auto</option><option value="avoid_page">Avoid page</option></select></label>
        {value.lines.map((textLine, lineIndex) => <fieldset key={lineIndex}><legend>Line {lineIndex + 1}</legend>{textLine.runs.map((textRun, runIndex) => <fieldset key={runIndex}><legend>Run {runIndex + 1}</legend>{textRun.text !== undefined ? <label>Literal<input aria-label={`Block ${index + 1} line ${lineIndex + 1} run ${runIndex + 1} literal`} value={textRun.text} onChange={event => run(index, lineIndex, runIndex, { text: event.currentTarget.value })} /></label> : <>{choose(`Block ${index + 1} line ${lineIndex + 1} run ${runIndex + 1} field`, textRun.fieldPath, props.catalogue?.fields, next => run(index, lineIndex, runIndex, { ...textRun, fieldPath: next }))}<label>Fallback<input aria-label={`Block ${index + 1} line ${lineIndex + 1} run ${runIndex + 1} fallback`} value={textRun.fallback ?? ''} onChange={event => run(index, lineIndex, runIndex, { ...textRun, fallback: event.currentTarget.value })} /></label></>}<Button type="button" onClick={() => line(index, lineIndex, { runs: textLine.runs.filter((_, position) => position !== runIndex) })}>Remove run {runIndex + 1} from line {lineIndex + 1} in block {index + 1}</Button></fieldset>)}<Button type="button" onClick={() => line(index, lineIndex, { runs: [...textLine.runs, { text: '' }] })}>Add literal to line {lineIndex + 1} in block {index + 1}</Button><Button type="button" onClick={() => line(index, lineIndex, { runs: [...textLine.runs, { fieldPath: '' }] })}>Add merge to line {lineIndex + 1} in block {index + 1}</Button><Button type="button" onClick={() => block(index, { ...value, lines: value.lines.filter((_, position) => position !== lineIndex) })}>Remove line {lineIndex + 1} from block {index + 1}</Button></fieldset>)}
        <Button type="button" onClick={() => block(index, { ...value, lines: [...value.lines, { runs: [] }] })}>Add line to block {index + 1}</Button>
        <Button type="button" disabled={index === 0} onClick={() => { const blocks = [...draft.blocks]; [blocks[index - 1], blocks[index]] = [blocks[index], blocks[index - 1]]; change({ ...draft, blocks }) }}>Move block {index + 1} up</Button>
        <Button type="button" onClick={() => change({ ...draft, blocks: draft.blocks.filter((_, position) => position !== index) })}>Remove block {index + 1}</Button>
      </fieldset>)}
      <Button type="button" onClick={() => { let identity = 1; while (draft.blocks.some(value => value.id === `block-${identity}`)) identity++; change({ ...draft, blocks: [...draft.blocks, { id: `block-${identity}`, lines: [], breakBefore: false, breakAfter: false, breakInside: 'auto' }] }) }}>Add block</Button>
      <fieldset><legend>Server preview</legend><label>Preview source<select aria-label="Preview source" value={targetKind} onChange={event => { setTargetKind(event.currentTarget.value as 'record' | 'sample'); setTargetId(''); resetPreview() }}><option value="record">Record</option><option value="sample">Labelled sample</option></select></label>{choose('Preview target', targetId, targetKind === 'record' ? props.catalogue?.records : props.catalogue?.samples, next => { setTargetId(next); resetPreview() })}<Button type="button" disabled={!props.canPreview || !targetId || !!incoming || status === 'Rendering preview…'} onClick={requestPreview}>Render preview</Button></fieldset>
    </fieldset>
    {incoming && <aside role="alert">A different template or revision arrived while you have local edits.<Button type="button" disabled={props.readOnly} onClick={() => setIncoming(undefined)}>Keep edits</Button><Button type="button" disabled={props.readOnly} onClick={() => { adopt(incoming); props.onDraftChange(incoming, generation + 1) }}>Discard edits</Button></aside>}
    {denial && denial.identity === draft.identity && denial.revision === draft.revision && denial.generation === generation && <ul aria-label="Admission refusals">{denial.diagnostics.map((diagnostic, index) => <li key={index}>{diagnostic.code} at {diagnostic.pointer}</li>)}</ul>}
    <p role="status">{status}</p>
    {preview && <section aria-label="Document preview" lang={preview.localeTag}>{preview.blocks.map(value => <p key={value.id}>{value.content}</p>)}</section>}
  </form>
}
