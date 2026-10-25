import { fireEvent, render, screen } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { RecordTypeDraftEditor } from './RecordTypeDraftEditor'
import { emptyRecordsDraft, type RecordsDiagnostic, type RecordsDiagnosticSet, type RecordsDraft, type RecordsDraftChange, type RecordsWarning, type RecordTypeBody, type RecordTypeField } from './RecordsDraftTypes'

type NewRecordType = { tenant: string; section: string; name: string; version: string; fields: RecordTypeField[] }
type RefusalCase = { id: string; replace?: Partial<NewRecordType>; createRequest?: NewRecordType; rawSave?: { key: { definitionId: string }; versionId: string; expectedRevision: number; bodyJson: string }; expected: { outcome: string; refusals?: RecordsDiagnostic[]; mintedRecordTypeId?: string } }
const fixture = <T,>(name: string) => JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.ui.records-authoring/source-fixtures', name), 'utf8')) as T
const manifest = fixture<{ caseCount: number; files: readonly { path: string; cases?: readonly string[] }[] }>('manifest.json')
const grammar = fixture<{ grammarFacts: { testHostKindRegistry: { kinds: readonly { kind_id: string; version: string }[] } }; cases: readonly { id: string; catalogue: { definitionKind: 'Records'; definitionId: string; definitionVersion: string; expectedCreateRevision: number }; createRequest: { newRecordType: NewRecordType }; expectedStoredBody: RecordTypeBody & { fields: RecordTypeField[] } }[] }>('records-current-grammar.json')
const refusalCases = fixture<{ cases: readonly RefusalCase[] }>('records-refusal-cases.json').cases
const parity = fixture<{ cases: readonly { id: string; inputs?: unknown; inputSequence?: readonly { identity: string; versionId: string; expectedRevision: number }[]; producerFact?: { staleSave: RecordsDiagnostic[] }; expected?: { changed: { refusals: RecordsDiagnostic[] } } }[] }>('records-parity-inputs.json').cases
const g1 = grammar.cases[0]
// The frozen producer fixtures carry small JSON-number revisions (1 and 2). The editor takes the host's canonical decimal string, so each
// fixture number maps to its string through this literal table, never through String() or arithmetic.
const fixtureRevisions: Readonly<Record<number, string>> = { 1: '1', 2: '2' }
const fixtureRevision = (revision: number): string => { const text = fixtureRevisions[revision]; if (text === undefined) throw new Error(`unmapped fixture revision ${revision}`); return text }
const kindOptions = grammar.grammarFacts.testHostKindRegistry.kinds.map(kind => ({ kindId: kind.kind_id, version: kind.version }))
const parityCase = (id: string) => parity.find(item => item.id === id)!
const refusalCase = (id: string) => refusalCases.find(item => item.id === id)!
const g1Draft = (): RecordsDraft => ({ identity: g1.catalogue.definitionId, versionId: g1.catalogue.definitionVersion, expectedRevision: fixtureRevision(g1.catalogue.expectedCreateRevision), definitionKind: g1.catalogue.definitionKind, body: structuredClone(g1.expectedStoredBody) })
// Fixture expansion for display only: the case's members in a not-yet-identified draft. It is not a producer request and proves no admission.
const caseDraft = (item: RefusalCase): RecordsDraft => {
  if (item.rawSave) return { identity: item.rawSave.key.definitionId, versionId: item.rawSave.versionId, expectedRevision: fixtureRevision(item.rawSave.expectedRevision), definitionKind: 'Records', body: JSON.parse(item.rawSave.bodyJson) as RecordTypeBody }
  const request = item.createRequest ?? { ...g1.createRequest.newRecordType, ...item.replace }
  return { identity: '', versionId: request.version, definitionKind: 'Records', body: { name: request.name, envelope: { tenant: request.tenant, section: request.section }, fields: request.fields } }
}
const supplied = (draft: RecordsDraft, generation: number, diagnostics: readonly RecordsDiagnostic[]): RecordsDiagnosticSet => ({ identity: draft.identity, expectedRevision: draft.expectedRevision, generation, diagnostics })
const rendered = () => Array.from(screen.queryByRole('list', { name: 'Diagnostics' })?.querySelectorAll('li') ?? []).map(item => ({ code: item.getAttribute('data-code'), pointer: item.getAttribute('data-pointer'), text: item.textContent }))
const expectedRendering = (diagnostics: readonly RecordsDiagnostic[]) => diagnostics.map(item => ({ code: item.code, pointer: item.pointer, text: `${item.code} at ${item.pointer}` }))
const generationOf = () => screen.getByRole('form', { name: 'Record type draft' }).getAttribute('data-generation')
const revisionShown = () => screen.getByLabelText('Expected revision').textContent

describe('Records local draft editor, React lane', () => {
  it('maps every source fixture case to a local mechanism or a producer-only deferral', () => {
    const coverage: Record<string, string> = {
      'G1-asset-class': 'local edits preserve the stored body; canonical serialization and admission stay producer-only',
      'R1-punctuation-name': 'supplied diagnostics rendered verbatim', 'R2-zero-revision-collision': 'supplied diagnostics rendered verbatim; the collision and history stay producer-only', 'R3-duplicate-field-key': 'supplied diagnostics rendered verbatim', 'R4-duplicate-field-key-raw-complete-body': 'supplied diagnostics rendered over the raw body; the raw catalogue save stays producer-only', 'R5-reference-binding-conflict': 'supplied diagnostics rendered verbatim; reference edits preserve other members', 'R6-unknown-kind': 'supplied diagnostics rendered verbatim',
      'R7-same-name-other-section-positive': 'local section edit; the minted identity is host-supplied, never computed',
      'P1-refusal-rendering-parity': 'ordered supplied lists rendered', 'P2-identity-revision-reset': 'clean replace, dirty Keep/Discard, stale generation ignored', 'P3-kind-version-change': 'local version change with no clearing; supplied refusal rendered', 'P4-read-only-and-ordering': 'read-only emits nothing; supplied order kept',
    }
    const cases = manifest.files.flatMap(file => file.cases ?? [])
    expect(cases).toHaveLength(manifest.caseCount)
    expect(Object.keys(coverage).sort()).toEqual([...cases].sort())
  })

  it('G1: local name and field edits preserve every untouched member of the stored body', () => {
    const changes: RecordsDraftChange[] = []
    render(<RecordTypeDraftEditor value={g1Draft()} kindOptions={kindOptions} onDraftChange={change => changes.push(change)} />)
    expect(screen.getByLabelText('Record type identity')).toHaveTextContent(g1.catalogue.definitionId)
    expect(screen.getByLabelText('Version')).toHaveTextContent(g1.catalogue.definitionVersion)
    expect(revisionShown()).toBe('1')
    expect(screen.getByLabelText('Record type name')).toHaveValue(g1.expectedStoredBody.name)
    expect(changes).toHaveLength(0)
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'Asset Class local' } })
    fireEvent.change(screen.getByLabelText('Field 3 display name'), { target: { value: 'Remarks' } })
    const stored = structuredClone(g1.expectedStoredBody)
    const expected = { ...stored, name: 'Asset Class local', fields: stored.fields.map((field, index) => index === 2 ? { ...field, display_name: 'Remarks' } : field) }
    expect(changes.at(-1)!.draft).toStrictEqual({ ...g1Draft(), body: expected })
    // The existing Boolean floor is carried, never offered as a control.
    expect(screen.queryByRole('checkbox')).toBeNull()
  })

  it('preserves unknown members and other reference members while editing reference inputs', () => {
    const r5Field = refusalCase('R5-reference-binding-conflict').createRequest!.fields[0]
    const body: RecordTypeBody = { ...structuredClone(g1.expectedStoredBody), future_member: { kept: [1, 'two'] }, envelope: { ...g1.expectedStoredBody.envelope, future_envelope_member: true }, fields: [{ ...structuredClone(r5Field), future_field_member: 'kept', reference: { ...r5Field.reference, required_trait_id: 'trait.kept', pin: 'kept' } }] }
    const changes: RecordsDraftChange[] = []
    render(<RecordTypeDraftEditor value={{ ...g1Draft(), body }} kindOptions={kindOptions} onDraftChange={change => changes.push(change)} />)
    fireEvent.change(screen.getByLabelText('Field 1 reference target type'), { target: { value: 'eam.space' } })
    fireEvent.change(screen.getByLabelText('Field 1 reference on delete'), { target: { value: 'orphan' } })
    const original = structuredClone(body).fields![0]
    const expected = { ...structuredClone(body), fields: [{ ...original, reference: { ...original.reference, target_type_id: 'eam.space', on_delete: 'orphan' } }] }
    expect(changes.at(-1)!.draft.body).toStrictEqual(expected)
  })

  it('asks for the definition kind before the name and adds a field without invented members', () => {
    const changes: RecordsDraftChange[] = []
    render(<RecordTypeDraftEditor value={emptyRecordsDraft()} kindOptions={kindOptions} onDraftChange={change => changes.push(change)} />)
    expect(screen.queryByLabelText('Record type name')).toBeNull()
    expect(Array.from((screen.getByLabelText('Definition kind') as HTMLSelectElement).options).map(option => option.value)).toEqual(['', 'Records'])
    fireEvent.change(screen.getByLabelText('Definition kind'), { target: { value: 'Records' } })
    expect(changes.at(-1)!.draft).toStrictEqual({ identity: '', versionId: '', definitionKind: 'Records', body: {} })
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'Asset Class' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add field' }))
    fireEvent.change(screen.getByLabelText('Field 1 key'), { target: { value: 'notes' } })
    fireEvent.change(screen.getByLabelText('Field 1 display name'), { target: { value: 'Notes' } })
    expect(changes.at(-1)!.draft.body).toStrictEqual({ name: 'Asset Class', fields: [{ field_key: 'notes', display_name: 'Notes' }] })
    // An unbound field gets no kind selection, so nothing is chosen automatically.
    expect(screen.queryByLabelText('Field 1 kind')).toBeNull()
    expect(screen.getByLabelText('Record type identity')).toHaveTextContent('Not yet assigned')
  })

  it('R1-R6 and P1: renders each supplied ordered refusal list exactly as given', () => {
    const refused = refusalCases.filter(item => item.expected.outcome === 'refused')
    expect(refused.map(item => item.id)).toEqual(expect.arrayContaining(parityCase('P1-refusal-rendering-parity').inputs as string[]))
    for (const item of refused) {
      const draft = caseDraft(item); const onDraftChange = vi.fn()
      const { unmount } = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={supplied(draft, 0, item.expected.refusals!)} onDraftChange={onDraftChange} />)
      expect(rendered(), item.id).toEqual(expectedRendering(item.expected.refusals!))
      expect(onDraftChange, item.id).not.toHaveBeenCalled()
      unmount()
    }
  })

  it('R7: a local section edit never mints an identity; the host-supplied identity is shown as given', () => {
    const item = refusalCase('R7-same-name-other-section-positive'); const draft = caseDraft({ ...item, replace: {} }); const changes: RecordsDraftChange[] = []
    const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.change(screen.getByLabelText('Section'), { target: { value: item.replace!.section! } })
    expect(changes.at(-1)!.draft).toStrictEqual({ ...draft, body: { ...draft.body, envelope: { ...draft.body.envelope, section: item.replace!.section! } } })
    expect(screen.getByLabelText('Record type identity')).toHaveTextContent('Not yet assigned')
    view.rerender(<RecordTypeDraftEditor value={{ ...changes.at(-1)!.draft, identity: item.expected.mintedRecordTypeId!, expectedRevision: '1' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(screen.getByLabelText('Record type identity')).toHaveTextContent(item.expected.mintedRecordTypeId!)
  })

  it('P2: clean incoming replaces, a parent echo is not a reset, dirty incoming waits, and stale generations stay ignored', () => {
    const step = parityCase('P2-identity-revision-reset'); const [loadedStep, externalStep, identityStep] = step.inputSequence!; const staleSave = step.producerFact!.staleSave
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const loaded: RecordsDraft = { ...g1Draft(), identity: loadedStep.identity, versionId: loadedStep.versionId, expectedRevision: fixtureRevision(loadedStep.expectedRevision) }
    const view = render(<RecordTypeDraftEditor value={loaded} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    const show = (value: RecordsDraft, diagnostics?: RecordsDiagnosticSet) => view.rerender(<RecordTypeDraftEditor value={value} kindOptions={kindOptions} diagnostics={diagnostics} onDraftChange={onDraftChange} />)
    expect(generationOf()).toBe('0')

    const external: RecordsDraft = { ...loaded, expectedRevision: fixtureRevision(externalStep.expectedRevision), body: { ...loaded.body, name: 'Asset Class external' } }
    show(external)
    expect(screen.queryByRole('alert')).toBeNull(); expect(screen.getByLabelText('Record type name')).toHaveValue('Asset Class external'); expect(revisionShown()).toBe('2'); expect(generationOf()).toBe('1')

    show(external, supplied(loaded, 0, staleSave))
    expect(rendered()).toEqual([])
    show(external, supplied(external, 1, staleSave))
    expect(rendered()).toEqual(expectedRendering(staleSave))

    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'local' } })
    expect(changes.at(-1)!.generation).toBe(2)
    expect(rendered()).toEqual([])
    show(changes.at(-1)!.draft, supplied(external, 1, staleSave))
    expect(screen.queryByRole('alert')).toBeNull(); expect(screen.getByLabelText('Record type name')).toHaveValue('local'); expect(generationOf()).toBe('2'); expect(rendered()).toEqual([])

    const other: RecordsDraft = { identity: identityStep.identity, versionId: identityStep.versionId, expectedRevision: fixtureRevision(identityStep.expectedRevision), definitionKind: 'Records', body: { ...g1Draft().body, record_type_id: identityStep.identity, envelope: { ...g1Draft().body.envelope, section: 'finance' } } }
    show(other)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.queryByRole('alert')).toBeNull(); expect(screen.getByLabelText('Record type name')).toHaveValue('local'); expect(screen.getByLabelText('Record type identity')).toHaveTextContent(loadedStep.identity)

    // The next host revision is supplied as a literal; the editor never increments a revision.
    const otherRevised = { ...other, expectedRevision: '2' }
    show(otherRevised)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes.at(-1)).toStrictEqual({ draft: otherRevised, generation: 3 })
    expect(screen.getByLabelText('Record type identity')).toHaveTextContent(identityStep.identity); expect(screen.getByLabelText('Section')).toHaveValue('finance')
    show(otherRevised, supplied(otherRevised, 2, staleSave))
    expect(rendered()).toEqual([])
    show(otherRevised, supplied(otherRevised, 3, staleSave))
    expect(rendered()).toEqual(expectedRendering(staleSave))
  })

  it('P3: a kind version change is a local edit with no clearing; the supplied retention refusal is rendered as given', () => {
    const item = parityCase('P3-kind-version-change'); const draft = g1Draft()
    // The P3 base input, from its fixture description: G1 fields plus acquired_on bound to date 1.0.0 and named as the retention clock.
    const acquiredOn: RecordTypeField = { field_key: 'acquired_on', display_name: 'Acquired on', binding: { kind: { kind_id: 'date', version: '1.0.0', parameters: {} }, constraints: { required: true, minimum_count: 0, maximum_count: 1, read_role_ids: [] } } }
    const base: RecordTypeBody = { ...draft.body, fields: [...g1.expectedStoredBody.fields, acquiredOn], retention_clock_field_id: 'acquired_on' }
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const view = render(<RecordTypeDraftEditor value={{ ...draft, body: base }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.getByLabelText('Retention clock field')).toHaveValue('acquired_on')
    const select = screen.getByLabelText('Field 4 kind') as HTMLSelectElement
    expect(select.selectedOptions[0].textContent).toBe('date 1.0.0')
    fireEvent.change(select, { target: { value: Array.from(select.options).find(option => option.textContent === 'date 2.0.0')!.value } })
    const change = changes.at(-1)!
    expect(change.draft.body).toStrictEqual({ ...base, fields: [...g1.expectedStoredBody.fields, { ...acquiredOn, binding: { ...acquiredOn.binding, kind: { kind_id: 'date', version: '2.0.0', parameters: {} } } }] })
    expect(rendered()).toEqual([])
    view.rerender(<RecordTypeDraftEditor value={{ ...draft, body: base }} kindOptions={kindOptions} diagnostics={supplied(change.draft, change.generation, item.expected!.changed.refusals)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(item.expected!.changed.refusals))
  })

  it('read-only also blocks pending Keep and Discard after an editable draft becomes dirty', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'local' } })
    expect(onDraftChange).toHaveBeenCalledTimes(1)
    view.rerender(<RecordTypeDraftEditor value={{ ...draft, expectedRevision: '2' }} kindOptions={kindOptions} readOnly onDraftChange={onDraftChange} />)
    const keep = screen.getByRole('button', { name: 'Keep edits' }); const discard = screen.getByRole('button', { name: 'Discard edits' })
    expect(keep).toBeDisabled(); expect(discard).toBeDisabled()
    fireEvent.click(keep); fireEvent.click(discard)
    expect(onDraftChange).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByLabelText('Record type name')).toHaveValue('local')
    expect(revisionShown()).toBe('1')
    expect(generationOf()).toBe('1')
  })

  it('withdraws a pending draft when the host returns to the current lifecycle value', () => {
    const draft = g1Draft(); const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'local' } })
    view.rerender(<RecordTypeDraftEditor value={{ ...draft, expectedRevision: '2' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Discard edits' })).toBeNull()
    expect(screen.getByLabelText('Record type name')).toHaveValue('local')
    expect(changes).toHaveLength(1)
  })

  it('does not replace a numeric JSON parameter with text when its input is cleared', () => {
    const draft = g1Draft(); const field = draft.body.fields![0]
    // Synthetic local JSON value; this asserts editing type preservation, not producer parameter admission.
    const body: RecordTypeBody = { ...draft.body, fields: [{ ...field, binding: { ...field.binding, kind: { ...field.binding!.kind, parameters: { probe: 2 } } } }] }
    const onDraftChange = vi.fn()
    render(<RecordTypeDraftEditor value={{ ...draft, body }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.change(screen.getByLabelText('Field 1 parameter probe'), { target: { value: '' } })
    expect(onDraftChange).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Field 1 parameter probe'), { target: { value: '3' } })
    expect(onDraftChange).toHaveBeenCalledTimes(1)
    expect(onDraftChange.mock.calls[0][0].draft.body.fields[0].binding.kind.parameters).toEqual({ probe: 3 })
    expect(body.fields![0].binding!.kind!.parameters).toEqual({ probe: 2 })
  })

  it('P4: read-only disables every control, emits nothing, and keeps the supplied list exactly', () => {
    const onDraftChange = vi.fn(); const draft = g1Draft(); const list = refusalCase('R1-punctuation-name').expected.refusals!
    const { container } = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} readOnly diagnostics={supplied(draft, 0, list)} onDraftChange={onDraftChange} />)
    const controls = Array.from(container.querySelectorAll('input, select, button'))
    expect(controls.length).toBeGreaterThan(0)
    for (const control of controls) expect(control, control.getAttribute('aria-label') ?? control.textContent ?? '').toBeDisabled()
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'changed' } })
    fireEvent.change(screen.getByLabelText('Field 1 kind'), { target: { value: '0' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add field' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove field 1' }))
    expect(onDraftChange).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Record type name')).toHaveValue(g1.expectedStoredBody.name)
    expect(rendered()).toEqual(expectedRendering(list))
    expect(screen.queryByRole('button', { name: /save|publish|create|preview/i })).toBeNull()
    expect(screen.getByText(/not saved/)).toBeInTheDocument()
  })
})

// Goal6 lossless revision transport. Every expected revision is a literal: the host contract's decimal string (RecordsDraftHostContract.cs:56-57,72-99).
// Case names pair one-for-one with RecordsDraftTests.cs; the pairing table is in the Goal6 HANDOFF.md.
describe('Records local draft editor, React lane, lossless revision transport', () => {
  const rejectedNote = 'A host draft was not loaded because its revision is not a canonical decimal string.'
  const probeDiagnostics: readonly RecordsDiagnostic[] = [{ code: 'definition.revision_conflict', pointer: '/expectedRevision' }]
  const diagnosticsAt = (revision: string, generation: number): RecordsDiagnosticSet => ({ identity: g1.catalogue.definitionId, expectedRevision: revision, generation, diagnostics: probeDiagnostics })
  const nameInput = () => screen.getByLabelText('Record type name')
  // Each fails the host grammar: empty, leading zero, sign, negative zero, whitespace, decimal point, exponent, non-ASCII digits (BMP and the
  // surrogate pair U+1D7CF), and above Int64 (19 and 20 digits).
  const noncanonicalRevisions = ['', '00', '01', '+1', '-1', '-0', ' 1', '1 ', '1.0', '1e3', '\u0661', '\uFF11', '\uD835\uDFCF', '9223372036854775808', '9999999999999999999', '18446744073709551616']

  it.each(['0', '9007199254740992', '9007199254740993', '9223372036854775807'])('revision %s survives load, clean replacement, diagnostic matching and the edit callback unchanged', revision => {
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const loaded: RecordsDraft = { ...g1Draft(), expectedRevision: revision }
    const view = render(<RecordTypeDraftEditor value={loaded} kindOptions={kindOptions} diagnostics={diagnosticsAt(revision, 0)} onDraftChange={onDraftChange} />)
    expect(revisionShown()).toBe(revision); expect(rendered()).toEqual(expectedRendering(probeDiagnostics))

    const replaced: RecordsDraft = { ...loaded, versionId: 'replacement-version' }
    view.rerender(<RecordTypeDraftEditor value={replaced} kindOptions={kindOptions} diagnostics={diagnosticsAt(revision, 1)} onDraftChange={onDraftChange} />)
    expect(generationOf()).toBe('1'); expect(revisionShown()).toBe(revision); expect(rendered()).toEqual(expectedRendering(probeDiagnostics)); expect(changes).toHaveLength(0)

    fireEvent.change(nameInput(), { target: { value: 'local' } })
    expect(changes.at(-1)).toStrictEqual({ draft: { ...replaced, body: { ...replaced.body, name: 'local' } }, generation: 2 })
    expect(changes.at(-1)!.draft.expectedRevision).toBe(revision)
    view.rerender(<RecordTypeDraftEditor value={changes.at(-1)!.draft} kindOptions={kindOptions} diagnostics={diagnosticsAt(revision, 2)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics)); expect(revisionShown()).toBe(revision); expect(changes).toHaveLength(1)
  })

  it('a held incoming revision survives Keep and Discard byte-exactly', () => {
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const loaded: RecordsDraft = { ...g1Draft(), expectedRevision: '0' }
    const view = render(<RecordTypeDraftEditor value={loaded} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.change(nameInput(), { target: { value: 'local' } })
    view.rerender(<RecordTypeDraftEditor value={{ ...loaded, expectedRevision: '9007199254740993' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.queryByRole('alert')).toBeNull(); expect(revisionShown()).toBe('0')
    fireEvent.change(nameInput(), { target: { value: 'local again' } })
    expect(changes.at(-1)!.draft.expectedRevision).toBe('0'); expect(changes.at(-1)!.generation).toBe(2)

    const discarded: RecordsDraft = { ...loaded, expectedRevision: '9223372036854775807', body: { ...loaded.body, name: 'host' } }
    view.rerender(<RecordTypeDraftEditor value={discarded} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes.at(-1)).toStrictEqual({ draft: discarded, generation: 3 })
    expect(changes.at(-1)!.draft.expectedRevision).toBe('9223372036854775807'); expect(revisionShown()).toBe('9223372036854775807')
    view.rerender(<RecordTypeDraftEditor value={discarded} kindOptions={kindOptions} diagnostics={diagnosticsAt('9223372036854775807', 3)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics))
  })

  it('treats 9007199254740992 and 9007199254740993 as distinct revisions', () => {
    // Both are the same IEEE-754 double, so a Number-based key or comparison would conflate them.
    const onDraftChange = vi.fn(); const upper: RecordsDraft = { ...g1Draft(), expectedRevision: '9007199254740993' }
    const view = render(<RecordTypeDraftEditor value={upper} kindOptions={kindOptions} diagnostics={diagnosticsAt('9007199254740992', 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual([])
    view.rerender(<RecordTypeDraftEditor value={upper} kindOptions={kindOptions} diagnostics={diagnosticsAt('9007199254740993', 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics))

    // A clean host value that differs only between these two revisions is a replacement, not an echo.
    const lower: RecordsDraft = { ...upper, expectedRevision: '9007199254740992' }
    view.rerender(<RecordTypeDraftEditor value={lower} kindOptions={kindOptions} diagnostics={diagnosticsAt('9007199254740993', 1)} onDraftChange={onDraftChange} />)
    expect(generationOf()).toBe('1'); expect(revisionShown()).toBe('9007199254740992'); expect(rendered()).toEqual([])
    view.rerender(<RecordTypeDraftEditor value={lower} kindOptions={kindOptions} diagnostics={diagnosticsAt('9007199254740992', 1)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics))
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('advances the local generation on every edit while the revision stays the host value', () => {
    const changes: RecordsDraftChange[] = []
    render(<RecordTypeDraftEditor value={{ ...g1Draft(), expectedRevision: '9007199254740993' }} kindOptions={kindOptions} onDraftChange={change => changes.push(change)} />)
    expect(generationOf()).toBe('0')
    fireEvent.change(nameInput(), { target: { value: 'a' } })
    fireEvent.change(nameInput(), { target: { value: 'b' } })
    fireEvent.change(screen.getByLabelText('Section'), { target: { value: 'c' } })
    expect(changes.map(change => [change.generation, change.draft.expectedRevision])).toEqual([[1, '9007199254740993'], [2, '9007199254740993'], [3, '9007199254740993']])
    expect(generationOf()).toBe('3'); expect(revisionShown()).toBe('9007199254740993')
  })

  it.each(noncanonicalRevisions)('rejects the noncanonical revision %j on initial load without adopting or emitting', revision => {
    const onDraftChange = vi.fn()
    const view = render(<RecordTypeDraftEditor value={{ ...g1Draft(), expectedRevision: revision }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.getByText(rejectedNote)).toBeInTheDocument()
    expect(screen.queryByLabelText('Expected revision')).toBeNull(); expect(screen.queryByLabelText('Record type name')).toBeNull(); expect(screen.queryByRole('alert')).toBeNull()
    expect(onDraftChange).not.toHaveBeenCalled()

    // The first acceptable host value then loads as the initial draft; nothing of the rejected value survives.
    const accepted: RecordsDraft = { ...g1Draft(), expectedRevision: '7' }
    view.rerender(<RecordTypeDraftEditor value={accepted} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.queryByText(rejectedNote)).toBeNull(); expect(revisionShown()).toBe('7'); expect(generationOf()).toBe('0'); expect(onDraftChange).not.toHaveBeenCalled()
    fireEvent.change(nameInput(), { target: { value: 'local' } })
    expect(onDraftChange).toHaveBeenCalledTimes(1)
    expect(onDraftChange.mock.calls[0][0]).toStrictEqual({ draft: { ...accepted, body: { ...accepted.body, name: 'local' } }, generation: 1 })
  })

  it.each(noncanonicalRevisions)('rejects the noncanonical incoming revision %j without replacing, holding or emitting', revision => {
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const current: RecordsDraft = { ...g1Draft(), expectedRevision: '7' }
    const view = render(<RecordTypeDraftEditor value={current} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    const rejected: RecordsDraft = { ...current, identity: 'eam.rejected', expectedRevision: revision, body: { ...current.body, name: 'rejected' } }
    view.rerender(<RecordTypeDraftEditor value={rejected} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.getByText(rejectedNote)).toBeInTheDocument(); expect(screen.queryByRole('alert')).toBeNull()
    expect(nameInput()).toHaveValue(g1.expectedStoredBody.name); expect(revisionShown()).toBe('7'); expect(generationOf()).toBe('0'); expect(changes).toHaveLength(0)

    fireEvent.change(nameInput(), { target: { value: 'local' } })
    expect(changes.at(-1)).toStrictEqual({ draft: { ...current, body: { ...current.body, name: 'local' } }, generation: 1 })
    // Dirty: a rejected value is not held behind Keep/Discard either.
    view.rerender(<RecordTypeDraftEditor value={{ ...rejected, versionId: 'rejected-again' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.queryByRole('alert')).toBeNull(); expect(nameInput()).toHaveValue('local'); expect(revisionShown()).toBe('7'); expect(generationOf()).toBe('1'); expect(changes).toHaveLength(1)
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.queryByText(rejectedNote)).toBeNull(); expect(nameInput()).toHaveValue('local'); expect(changes).toHaveLength(1)
  })

  it.each(noncanonicalRevisions)('never shows diagnostics whose revision %j is noncanonical', revision => {
    const onDraftChange = vi.fn(); const current: RecordsDraft = { ...g1Draft(), expectedRevision: '7' }
    const view = render(<RecordTypeDraftEditor value={current} kindOptions={kindOptions} diagnostics={diagnosticsAt(revision, 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual([])
    view.rerender(<RecordTypeDraftEditor value={current} kindOptions={kindOptions} diagnostics={diagnosticsAt('7', 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics))
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('treats a null revision as absent and carries it unchanged', () => {
    // The host wire is string? ExpectedRevision (RecordsDraftHostContract.cs:156); JSON null means no revision. It is neither rejected nor defaulted.
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const absent = null as unknown as string; const draft: RecordsDraft = { ...g1Draft(), expectedRevision: absent }
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={{ identity: g1.catalogue.definitionId, expectedRevision: absent, generation: 0, diagnostics: probeDiagnostics }} onDraftChange={onDraftChange} />)
    expect(screen.queryByText(rejectedNote)).toBeNull(); expect(revisionShown()).toBe('None'); expect(rendered()).toEqual(expectedRendering(probeDiagnostics))
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={diagnosticsAt('0', 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual([])
    fireEvent.change(nameInput(), { target: { value: 'local' } })
    expect(changes).toHaveLength(1); expect(changes[0].draft.expectedRevision).toBeNull(); expect(changes[0].generation).toBe(1)
  })

  it.each([1, 9007199254740992])('rejects the JSON value %j as a revision on a draft or diagnostic set', value => {
    // A host that bypasses the types: the value arrives as a JSON number, never coerced to text. (null is absent; see the test above.)
    const revision = value as unknown as string; const onDraftChange = vi.fn()
    const view = render(<RecordTypeDraftEditor value={{ ...g1Draft(), expectedRevision: revision }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.getByText(rejectedNote)).toBeInTheDocument(); expect(screen.queryByLabelText('Record type name')).toBeNull()
    const current: RecordsDraft = { ...g1Draft(), expectedRevision: '1' }
    view.rerender(<RecordTypeDraftEditor value={current} kindOptions={kindOptions} diagnostics={diagnosticsAt(revision, 0)} onDraftChange={onDraftChange} />)
    expect(revisionShown()).toBe('1'); expect(rendered()).toEqual([])
    view.rerender(<RecordTypeDraftEditor value={current} kindOptions={kindOptions} diagnostics={diagnosticsAt('1', 0)} onDraftChange={onDraftChange} />)
    expect(rendered()).toEqual(expectedRendering(probeDiagnostics))
    expect(onDraftChange).not.toHaveBeenCalled()
  })
})

// Interface revision 2 (T-618 item 4, records-auth-42): a diagnostic's optional target and the separate warnings list. The literals are the
// T-618 preflight producer values; every expected row and text is a literal, never built from the input. Case names pair one-for-one with
// RecordsDraftTests.cs (Revision2_*).
describe('Records local draft editor, React lane, revision 2 targets and warnings', () => {
  const undeclared: RecordsDiagnostic = { code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: 'eam-core/eam.meter@1.0.0' }
  const unresolved: RecordsDiagnostic = { code: 'records.class.unresolved', pointer: '/class_id' }
  const retentionUnset: RecordsWarning = { code: 'records.retention.unset', pointer: '/retention_policy' }
  // Synthetic second warning, only so warning order is observable: the producer emits one warning code, and this one sorts before it.
  const auditUnset: RecordsWarning = { code: 'records.audit.unset', pointer: '/audit_policy' }
  const undeclaredRow = { code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: 'eam-core/eam.meter@1.0.0', text: 'records.reference.dependency_undeclared at /fields/0/reference for eam-core/eam.meter@1.0.0' }
  const unresolvedRow = { code: 'records.class.unresolved', pointer: '/class_id', target: null, text: 'records.class.unresolved at /class_id' }
  const retentionText = 'records.retention.unset at /retention_policy'
  const diagnosticsList = () => screen.queryByRole('list', { name: 'Diagnostics' })
  const warningsList = () => screen.queryByRole('list', { name: 'Warnings' })
  // target is null when the item carries no data-target attribute at all.
  const diagnosticRows = () => Array.from(diagnosticsList()?.querySelectorAll('li') ?? []).map(item => ({ code: item.getAttribute('data-code'), pointer: item.getAttribute('data-pointer'), target: item.getAttribute('data-target'), text: item.textContent }))
  const warningTexts = () => Array.from(warningsList()?.querySelectorAll('li') ?? []).map(item => item.textContent)
  // warnings undefined means the member is absent, not present-and-undefined.
  const preview = (draft: RecordsDraft, generation: number, diagnostics: readonly RecordsDiagnostic[], warnings?: readonly RecordsWarning[]): RecordsDiagnosticSet => warnings === undefined ? supplied(draft, generation, diagnostics) : { ...supplied(draft, generation, diagnostics), warnings }

  it('Revision2: a targeted diagnostic renders its target after the pointer and as data-target, in order beside an untargeted revision-1 one', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [undeclared, unresolved])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow])
    expect(diagnosticsList()!.querySelectorAll('li')[1].hasAttribute('data-target')).toBe(false)
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('Revision2: an empty-string or null target renders exactly like no target', () => {
    const draft = g1Draft()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [{ code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: '' }, { code: 'records.class.unresolved', pointer: '/class_id', target: null }])} onDraftChange={vi.fn()} />)
    expect(diagnosticRows()).toEqual([{ code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: null, text: 'records.reference.dependency_undeclared at /fields/0/reference' }, unresolvedRow])
    for (const item of Array.from(diagnosticsList()!.querySelectorAll('li'))) expect(item.hasAttribute('data-target')).toBe(false)
  })

  it('Revision2: a whitespace-only target is non-empty, so it renders verbatim and as data-target', () => {
    const draft = g1Draft()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [{ code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: ' ' }])} onDraftChange={vi.fn()} />)
    expect(diagnosticRows()).toEqual([{ code: 'records.reference.dependency_undeclared', pointer: '/fields/0/reference', target: ' ', text: 'records.reference.dependency_undeclared at /fields/0/reference for  ' }])
  })

  it('Revision2: the warnings list is preceded by one visible note that warnings do not block publication, and no note shows without warnings', () => {
    const draft = g1Draft()
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [unresolved], [retentionUnset])} onDraftChange={vi.fn()} />)
    expect(screen.getAllByText('Warnings do not block publication.')).toHaveLength(1)
    expect(warningsList()!.previousElementSibling!.textContent).toBe('Warnings do not block publication.')
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [unresolved])} onDraftChange={vi.fn()} />)
    expect(screen.queryByText('Warnings do not block publication.')).toBeNull()
  })

  it('Revision2: warnings render in their own Warnings list in order, apart from the diagnostics and never as a refusal', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [undeclared, unresolved], [retentionUnset, auditUnset])} onDraftChange={onDraftChange} />)
    expect(warningTexts()).toEqual([retentionText, 'records.audit.unset at /audit_policy'])
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow])
    const diagnostics = diagnosticsList()!; const warnings = warningsList()!
    expect(diagnostics.contains(warnings)).toBe(false); expect(warnings.contains(diagnostics)).toBe(false)
    expect(warnings.closest('[role="alert"]')).toBeNull()
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('Revision2: a warnings-only set shows no Diagnostics list, and absent or empty warnings show no Warnings list', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [], [retentionUnset])} onDraftChange={onDraftChange} />)
    expect(warningTexts()).toEqual([retentionText]); expect(diagnosticsList()).toBeNull()
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [unresolved])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([unresolvedRow]); expect(warningsList()).toBeNull()
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={preview(draft, 0, [unresolved], [])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([unresolvedRow]); expect(warningsList()).toBeNull()
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it.each<[string, Partial<RecordsDiagnosticSet>]>([
    ['identity', { identity: 'eam.other' }],
    ['revision', { expectedRevision: '2' }],
    ['generation', { generation: 1 }],
  ])('Revision2: shows neither list when the %s does not match the current draft', (_, mismatch) => {
    const draft = g1Draft(); const onDraftChange = vi.fn(); const matching = preview(draft, 0, [undeclared, unresolved], [retentionUnset])
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={{ ...matching, ...mismatch }} onDraftChange={onDraftChange} />)
    expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull()
    // Positive control: the same set, matching, shows both.
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={matching} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText])
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('Revision2: read-only still shows both lists', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} readOnly diagnostics={preview(draft, 0, [undeclared, unresolved], [retentionUnset])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText])
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('Revision2: an edit after warnings were shown moves the generation, so both lists disappear until a matching set arrives', () => {
    const draft = g1Draft(); const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const atZero = preview(draft, 0, [undeclared, unresolved], [retentionUnset])
    const view = render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={atZero} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText])
    // Warnings never block: the edit is accepted and reported.
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'local' } })
    expect(changes).toHaveLength(1); expect(changes[0].generation).toBe(1)
    expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull()
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} diagnostics={atZero} onDraftChange={onDraftChange} />)
    expect(generationOf()).toBe('1'); expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull()
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} diagnostics={preview(changes[0].draft, 1, [undeclared, unresolved], [retentionUnset])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText])
    expect(changes).toHaveLength(1)
  })
})
