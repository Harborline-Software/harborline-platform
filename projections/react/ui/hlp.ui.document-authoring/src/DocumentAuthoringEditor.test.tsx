import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen, waitFor, act } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DocumentAuthoringEditor, type DocumentAuthoringEditorProps } from './DocumentAuthoringEditor'
import { DocumentAuthoringScreen } from './DocumentAuthoringScreen'
import { documentOption, emptyDocumentDraft, type DocumentDraft, type DocumentPreviewResult } from './DocumentDraft.types'

const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../conformance/hlp.ui.document-authoring/fixtures.yaml'), 'utf8')).cases[0]
const catalogue = { fields: ['record.total'], regions: [{ id: 'masthead', label: 'Masthead' }, 'folio'], samples: [{ id: 'example', label: 'Example invoice (sample)' }], records: ['record.1'] }
const flow = { blocks: [{ id: 'block-1', pageVariant: 'first' as const, content: 'Supplied page flow' }] }
const result: DocumentPreviewResult = { minted: false, localeTag: 'fr-FR', blocks: [{ id: 'block-1', content: 'Total: Not supplied' }] }
const button = (name: string) => fireEvent.click(screen.getByRole('button', { name, exact: true }))
const type = (name: string, value: string) => fireEvent.change(screen.getByLabelText(name), { target: { value } })
function editor(overrides: Partial<DocumentAuthoringEditorProps> = {}) {
  const props = { value: emptyDocumentDraft(), catalogue, pageFlow: flow, canPreview: true, onDraftChange: vi.fn(), onPreview: vi.fn().mockResolvedValue(result), ...overrides }
  return { props, ...render(<DocumentAuthoringEditor {...props} />) }
}
function replay() { for (const step of fixture.input.steps) { if (step.set) type(step.set, step.value); else if (step.check) fireEvent.click(screen.getByLabelText(step.check)); else button(step.click) } }
const last = (change: ReturnType<typeof vi.fn>) => change.mock.calls.at(-1)![0] as DocumentDraft

describe('Document authoring', () => {
  it('documents-eng-21: gallery component styles precede import maps and host overrides', () => {
    for (const file of ['index.html', 'iframe.html']) {
      const markup = readFileSync(resolve(import.meta.dirname, `../../../../../gallery/projections/blazor/wwwroot/${file}`), 'utf8')
      const position = markup.indexOf('href="_content/Harborline.UIAdapters.Blazor/document-authoring.css"')
      expect(position).toBeGreaterThan(0); expect(position).toBeLessThan(markup.indexOf('<script type="importmap"'))
      if (file === 'iframe.html') expect(position).toBeLessThan(markup.indexOf('href="gallery.css"'))
    }
  })

  it('documents-auth-16: clearing a pinned logo omits the optional asset reference', () => {
    const { props } = editor(); type('Logo asset reference', 'sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef')
    type('Logo asset reference', '')
    expect(last(props.onDraftChange).style.logoAssetRef).toBeUndefined()
    expect(JSON.parse(JSON.stringify(last(props.onDraftChange))).style).not.toHaveProperty('logoAssetRef')
  })

  it('documents-auth-5 layout-ck-43: action names identify their block line and run, and edits stay isolated', () => {
    const value = { ...emptyDocumentDraft(), blocks: ['first', 'second'].map(id => ({ id, breakBefore: false, breakAfter: false, breakInside: 'auto' as const, lines: [{ runs: [{ text: id }] }] })) }
    const { props } = editor({ value })
    const names = screen.getAllByRole('button').map(element => element.textContent)
    expect(new Set(names).size).toBe(names.length)
    type('Block 2 line 1 run 1 literal', 'Second edited')
    expect(last(props.onDraftChange).blocks[0].lines[0].runs).toEqual([{ text: 'first' }])
    expect(last(props.onDraftChange).blocks[1].lines[0].runs).toEqual([{ text: 'Second edited' }])
    button('Remove run 1 from line 1 in block 2')
    expect(last(props.onDraftChange).blocks[0].lines[0].runs).toEqual([{ text: 'first' }])
    expect(last(props.onDraftChange).blocks[1].lines[0].runs).toEqual([])
  })

  it('documents-auth-5 T-594 item 4: duplicate block ids are refused without discarding data and a valid revision recovers', () => {
    const value = { ...emptyDocumentDraft(), blocks: [1, 2].map(() => ({ id: 'duplicate', breakBefore: false, breakAfter: false, breakInside: 'auto' as const, lines: [] })) }
    const { props, rerender } = editor({ value })
    expect(screen.getByRole('alert').textContent).toBe('Duplicate block ids. The host must supply unique block ids.')
    expect(screen.queryByRole('form')).toBeNull()
    expect(props.onDraftChange).not.toHaveBeenCalled(); expect(props.onPreview).not.toHaveBeenCalled()
    rerender(<DocumentAuthoringEditor {...props} value={{ ...emptyDocumentDraft(), revision: '2' }} />)
    expect(screen.queryByRole('alert')).toBeNull(); expect(screen.getByRole('form')).toBeDefined()
  })

  it('documents-eng-21 documents-auth-1 documents-auth-3 documents-auth-4 documents-auth-5 documents-auth-7 documents-auth-10 documents-auth-15 documents-auth-16 documents-ck-5 documents-ck-6 documents-ck-23 layout-ck-43: creates the exact literal contract oracle from nothing', () => {
    const { props } = editor(); replay()
    expect(last(props.onDraftChange)).toEqual(fixture.expected)
    expect(screen.queryByLabelText(/format|Block label/i)).not.toBeInTheDocument()
  })
  it('documents-auth-5 layout-ck-43: orders blocks, lines and runs and removes only the selected item', () => {
    const { props } = editor(); replay(); button('Add block'); button('Move block 2 up')
    expect(last(props.onDraftChange).blocks.map(value => value.id)).toEqual(['block-2', 'block-1'])
    button('Remove block 1'); button('Add block'); expect(last(props.onDraftChange).blocks.map(value => value.id)).toEqual(['block-1', 'block-2'])
    button('Add line to block 1'); button('Add literal to line 2 in block 1'); type('Block 1 line 2 run 1 literal', 'Second line')
    button('Remove line 1 from block 1'); expect(last(props.onDraftChange).blocks[0].lines).toEqual([{ runs: [{ text: 'Second line' }] }])
    button('Add merge to line 1 in block 1'); button('Remove run 1 from line 1 in block 1'); expect(last(props.onDraftChange).blocks[0].lines[0].runs).toEqual([{ fieldPath: '' }])
    button('Remove block 2'); expect(last(props.onDraftChange).blocks.map(value => value.id)).toEqual(['block-1'])
  })
  it('T-594 item 4 documents-ck-23: only container sm/md/lg, no chrome window class, and booleans can be cleared', () => {
    const { props } = editor(); replay()
    expect([...screen.getByLabelText('Block 1 container token').querySelectorAll('option')].map(option => option.value)).toEqual(['', 'sm', 'md', 'lg'])
    for (const token of ['sm', 'md', 'lg', '']) { type('Block 1 container token', token); expect(last(props.onDraftChange).blocks[0].collapseBelow).toBe(token || undefined) }
    fireEvent.click(screen.getByLabelText('Block 1 break before')); fireEvent.click(screen.getByLabelText('Block 1 break after')); type('Block 1 break inside', 'auto')
    expect(last(props.onDraftChange).blocks[0]).toMatchObject({ breakBefore: false, breakAfter: false, breakInside: 'auto' })
  })
  it('documents-ck-5 documents-auth-3: locale policy clears the old fixed tag without touching style or runs', () => {
    const { props } = editor(); replay(); type('Locale policy', 'from_record'); expect(last(props.onDraftChange).locale).toEqual({ kind: 'from_record' }); expect(screen.queryByLabelText('Document locale')).toBeNull()
    type('Locale policy', 'from_instance'); expect(last(props.onDraftChange).locale).toEqual({ kind: 'from_instance' }); expect(last(props.onDraftChange).blocks).toEqual(fixture.expected.blocks)
  })
  it('documents-auth-15: all nine page master slots preserve the other assignments and clear only the chosen slot', () => {
    const { props } = editor()
    for (const variant of ['first', 'left', 'right'] as const) for (const region of ['leftRegion', 'centerRegion', 'rightRegion'] as const) type(`${variant} ${region}`, 'folio')
    expect(last(props.onDraftChange).masters).toEqual({ first: { leftRegion: 'folio', centerRegion: 'folio', rightRegion: 'folio' }, left: { leftRegion: 'folio', centerRegion: 'folio', rightRegion: 'folio' }, right: { leftRegion: 'folio', centerRegion: 'folio', rightRegion: 'folio' } })
    type('first centerRegion', ''); expect(last(props.onDraftChange).masters.first).toEqual({ leftRegion: 'folio', rightRegion: 'folio', centerRegion: undefined })
  })
  it('T-594 item 4: string/object options, absent catalogue fallback and unoffered current options remain visible', () => {
    expect(documentOption('field')).toEqual({ id: 'field', label: 'field' }); expect(documentOption({ id: 'field', label: 'Field' })).toEqual({ id: 'field', label: 'Field' })
    editor({ value: fixture.expected, catalogue: undefined })
    expect(screen.getAllByText('No options offered.')).toHaveLength(11)
    expect(screen.getByRole('option', { name: 'record.total (not offered)' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'masthead (not offered)' })).toBeInTheDocument()
  })
  it('T-594 item 4: readOnly disables all controls and emits no draft or preview', () => {
    const { props } = editor({ value: fixture.expected, readOnly: true }); button('Add block'); type('Template name', 'Changed')
    expect(props.onDraftChange).not.toHaveBeenCalled(); expect(props.onPreview).not.toHaveBeenCalled()
    for (const input of screen.getAllByRole('button')) expect(input).toBeDisabled()
    expect(screen.getByLabelText('Template name')).toHaveValue('template.invoice')
  })
  it('T-594 item 4: a clean identity/revision reset adopts the host but a same-revision echo does not overwrite', async () => {
    const { props, rerender } = editor(); rerender(<DocumentAuthoringEditor {...props} value={{ ...props.value, identity: 'other', revision: '9007199254740993', name: 'Incoming' }} />)
    await waitFor(() => expect(screen.getByLabelText('Template name')).toHaveValue('Incoming'))
    rerender(<DocumentAuthoringEditor {...props} value={{ ...props.value, identity: 'other', revision: '9007199254740993', name: 'Echo' }} />)
    expect(screen.getByLabelText('Template name')).toHaveValue('Incoming'); expect(props.onDraftChange).not.toHaveBeenCalled()
  })
  it('T-594 item 4: dirty identity and revision replacements require Keep or Discard, including readOnly pending handling', () => {
    const { props, rerender } = editor(); type('Template name', 'Local')
    const incoming = { ...props.value, identity: 'new', revision: '2', name: 'Remote' }
    rerender(<DocumentAuthoringEditor {...props} value={incoming} />); expect(screen.getByRole('alert')).toBeInTheDocument(); expect(screen.getByLabelText('Template name')).toHaveValue('Local')
    button('Keep edits'); rerender(<DocumentAuthoringEditor {...props} value={{ ...incoming }} />); expect(screen.queryByRole('alert')).toBeNull()
    const revision = { ...incoming, revision: '3' }; rerender(<DocumentAuthoringEditor {...props} value={revision} readOnly />)
    expect(screen.getByRole('button', { name: 'Discard edits' })).toBeDisabled(); button('Discard edits'); expect(screen.getByLabelText('Template name')).toHaveValue('Local')
    rerender(<DocumentAuthoringEditor {...props} value={revision} />); button('Discard edits'); expect(screen.getByLabelText('Template name')).toHaveValue('Remote'); expect(last(props.onDraftChange)).toEqual(revision)
  })
  it('documents-eng-21: denials are scoped to identity, revision and local generation, not echoed onto later edits', () => {
    const denial = { identity: '', revision: null, generation: 0, diagnostics: [{ code: 'documents.denied', pointer: '/style' }] }
    const { props, rerender } = editor({ denial }); expect(screen.getByText('documents.denied at /style')).toBeInTheDocument()
    for (const stale of [{ ...denial, identity: 'other' }, { ...denial, revision: '2' }, { ...denial, generation: 1 }]) { rerender(<DocumentAuthoringEditor {...props} denial={stale} />); expect(screen.queryByLabelText('Admission refusals')).toBeNull() }
    rerender(<DocumentAuthoringEditor {...props} />); type('Brand name', 'Edited'); expect(screen.queryByLabelText('Admission refusals')).toBeNull()
  })
  it('documents-eng-21 documents-auth-17 documents-run-2: requests only a non-minting server preview with the supplied flow and a labelled sample', async () => {
    const { props } = editor(); replay(); type('Preview target', 'example'); button('Render preview')
    await screen.findByText('Preview only. No document issued.')
    expect(props.onPreview).toHaveBeenCalledExactlyOnceWith({ mode: 'preview', draft: fixture.expected, pageFlow: flow, target: { kind: 'sample', id: 'example' } })
    expect(screen.getByRole('region', { name: 'Document preview' })).toHaveAttribute('lang', 'fr-FR'); expect(screen.getByText('Total: Not supplied')).toBeInTheDocument()
  })
  it('documents-auth-17: record preview clears a previous sample and never chooses a target automatically', async () => {
    const { props } = editor(); type('Preview target', 'example'); type('Preview source', 'record'); expect(screen.getByRole('button', { name: 'Render preview' })).toBeDisabled()
    type('Preview target', 'record.1'); button('Render preview'); await screen.findByText('Preview only. No document issued.')
    expect(props.onPreview.mock.calls[0][0].target).toEqual({ kind: 'record', id: 'record.1' })
  })
  it.each(['en-US', 'fr-FR'])('documents-auth-3 documents-auth-17 layout-ck-43: displays host-rendered literal and fallback runs under document locale %s', async localeTag => {
    const onPreview = vi.fn().mockResolvedValue({ minted: false, localeTag, blocks: [{ id: 'block-1', content: 'Total: Not supplied' }] })
    editor({ onPreview }); replay(); type('Document locale', localeTag); type('Preview target', 'example'); button('Render preview')
    await screen.findByText('Preview only. No document issued.')
    expect(onPreview.mock.calls[0][0].draft.locale).toEqual({ kind: 'fixed', tag: localeTag })
    expect(onPreview.mock.calls[0][0].draft.blocks[0].lines[0].runs).toEqual([{ text: 'Total: ' }, { fieldPath: 'record.total', fallback: 'Not supplied' }])
    expect(screen.getByRole('region', { name: 'Document preview' })).toHaveAttribute('lang', localeTag)
    expect(screen.getByText('Total: Not supplied')).toBeInTheDocument()
  })
  it('documents-run-2: absent preview authority refuses, failure is retryable, and a minted response is never displayed', async () => {
    const { props, rerender } = editor({ canPreview: undefined }); type('Preview target', 'example'); button('Render preview'); expect(props.onPreview).not.toHaveBeenCalled()
    props.onPreview.mockRejectedValueOnce(new Error('secret detail')); rerender(<DocumentAuthoringEditor {...props} canPreview />); button('Render preview'); await screen.findByText('Preview refused.'); expect(screen.queryByText('secret detail')).toBeNull()
    props.onPreview.mockResolvedValueOnce({ ...result, minted: true }); button('Render preview'); await waitFor(() => expect(props.onPreview).toHaveBeenCalledTimes(2)); await screen.findByText('Preview refused.'); expect(screen.queryByLabelText('Document preview')).toBeNull()
    button('Render preview'); await screen.findByText('Preview only. No document issued.')
  })
  it('documents-run-2 T-594 item 4: in-flight previews cannot overwrite an edited draft, replacement or revoked authority', async () => {
    let finish!: (value: DocumentPreviewResult) => void
    const { props, rerender } = editor({ onPreview: vi.fn(() => new Promise(resolve => { finish = resolve })) }); type('Preview target', 'example'); button('Render preview'); expect(screen.getByRole('button', { name: 'Render preview' })).toBeDisabled()
    type('Template name', 'Later'); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
    button('Render preview'); rerender(<DocumentAuthoringEditor {...props} canPreview={false} />); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
  })
  it('documents-run-2 T-594 item 4: target flow and pending replacement invalidate outstanding preview requests', async () => {
    let finish!: (value: DocumentPreviewResult) => void
    const { props, rerender } = editor({ onPreview: vi.fn(() => new Promise(resolve => { finish = resolve })) })
    type('Preview target', 'example'); button('Render preview'); type('Preview source', 'record'); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
    type('Preview target', 'record.1'); button('Render preview'); const nextFlow = { blocks: [] }; rerender(<DocumentAuthoringEditor {...props} pageFlow={nextFlow} />); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
    button('Render preview'); type('Template name', 'Unsaved'); rerender(<DocumentAuthoringEditor {...props} pageFlow={nextFlow} value={{ ...props.value, revision: '2' }} />); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
    expect(screen.getByRole('button', { name: 'Render preview' })).toBeDisabled(); button('Render preview'); expect(props.onPreview).toHaveBeenCalledTimes(3)
    button('Keep edits'); button('Render preview'); rerender(<DocumentAuthoringEditor {...props} pageFlow={nextFlow} readOnly />); await act(async () => finish(result)); expect(screen.queryByLabelText('Document preview')).toBeNull()
  })
  it('documents-eng-21: the screen renders the editor through the shared host shell', () => {
    const props = { value: emptyDocumentDraft(), pageFlow: flow, onDraftChange: vi.fn(), onPreview: vi.fn() }
    render(<DocumentAuthoringScreen {...props} shell={{ render: body => <main aria-label="Host shell">{body}</main> }} />)
    expect(screen.getByRole('main', { name: 'Host shell' })).toContainElement(screen.getByRole('form', { name: 'Document template draft' }))
  })
})
