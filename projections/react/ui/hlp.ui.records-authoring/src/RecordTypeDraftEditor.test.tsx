import { fireEvent, render, screen } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { RecordTypeDraftEditor } from './RecordTypeDraftEditor'
import { emptyRecordsDraft, type RecordsClassOption, type RecordsDiagnostic, type RecordsDiagnosticSet, type RecordsDraft, type RecordsDraftChange, type RecordsRoleOption, type RecordsWarning, type RecordTypeBody, type RecordTypeField } from './RecordsDraftTypes'

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
    // The existing Boolean floor is carried, never offered as a control: the only checkboxes are the two revision 3 header ones.
    expect(screen.getAllByRole('checkbox').map(box => box.getAttribute('aria-label')).sort()).toEqual(['Amendments require a reason', 'Keep history'])
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
  const noteCount = () => screen.queryAllByText('Warnings do not block publication.').length
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
    expect(warningsList()!.previousElementSibling!.tagName).toBe('P'); expect(warningsList()!.previousElementSibling!.className).toBe('hl-records-authoring__note')
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
    expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull(); expect(noteCount()).toBe(0)
    // Positive control: the same set, matching, shows both.
    view.rerender(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} diagnostics={matching} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText]); expect(noteCount()).toBe(1)
    expect(onDraftChange).not.toHaveBeenCalled()
  })

  it('Revision2: read-only still shows both lists', () => {
    const draft = g1Draft(); const onDraftChange = vi.fn()
    render(<RecordTypeDraftEditor value={draft} kindOptions={kindOptions} readOnly diagnostics={preview(draft, 0, [undeclared, unresolved], [retentionUnset])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText]); expect(noteCount()).toBe(1)
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
    expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull(); expect(noteCount()).toBe(0)
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} diagnostics={atZero} onDraftChange={onDraftChange} />)
    expect(generationOf()).toBe('1'); expect(diagnosticsList()).toBeNull(); expect(warningsList()).toBeNull(); expect(noteCount()).toBe(0)
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} diagnostics={preview(changes[0].draft, 1, [undeclared, unresolved], [retentionUnset])} onDraftChange={onDraftChange} />)
    expect(diagnosticRows()).toEqual([undeclaredRow, unresolvedRow]); expect(warningTexts()).toEqual([retentionText])
    expect(changes).toHaveLength(1)
  })
})

// Interface revision 3 header (semantics.edits, notEdited, hostOptions; producer spellings RecordTypeDocument.cs:60-171). Written from the
// interface, not the implementation. Controls are found by accessible name and options by visible text, never by option value. Every base
// body carries unknown members at the body, envelope and field level, so each exact expected body also proves preservation. Case names pair
// one-for-one with RecordsDraftTests.cs (Revision3_*).
describe('Records local draft editor, React lane, revision 3 header', () => {
  type Body = Record<string, unknown>
  const classOptions: readonly RecordsClassOption[] = [{ classId: 'eam.equipment' }, { classId: 'eam.facility' }]
  const roleOptions: readonly RecordsRoleOption[] = [{ roleId: 'asset-manager' }, { roleId: 'records-clerk' }]
  const without = (body: Body, ...members: string[]): Body => Object.fromEntries(Object.entries(body).filter(([member]) => !members.includes(member)))
  // G1 (class_id eam.equipment, record_class master, no creation, amendment or history member) plus unknown members, then the case's members.
  const headerBody = (members: Body = {}): Body => {
    const stored = structuredClone(g1.expectedStoredBody)
    return { ...stored, future_member: { kept: [1, 'two'] }, envelope: { ...stored.envelope, future_envelope_member: true }, fields: stored.fields.map((field, index) => index === 0 ? { ...field, future_field_member: 'kept' } : field), ...members }
  }
  // Every header member at once, plus an opaque traits value and the retention clock, so each edit must change exactly its own members.
  const fullHeader = (): Body => headerBody({ traits: [{ opaque: 'kept' }], retention_clock_field_id: 'asset_tag', creation_gate: 'role', creation_role_id: 'records-clerk', amendment_policy: { reason_required: true, mode: 'supersession', future_policy_member: 'kept' }, history_policy: 'enabled' })
  const draftWith = (body: Body): RecordsDraft => ({ ...g1Draft(), body: body as RecordTypeBody })
  const reported = (body: Body, generation: number): RecordsDraftChange => ({ draft: draftWith(body), generation })
  const header = (body: Body, options: { classOptions?: readonly RecordsClassOption[]; roleOptions?: readonly RecordsRoleOption[]; readOnly?: boolean } = {}) => {
    const changes: RecordsDraftChange[] = []
    const view = render(<RecordTypeDraftEditor value={draftWith(body)} kindOptions={kindOptions} {...options} onDraftChange={change => changes.push(change)} />)
    return { changes, view }
  }
  const select = (name: string) => screen.getByRole('combobox', { name }) as HTMLSelectElement
  const absent = (name: string) => screen.queryByRole('combobox', { name })
  const optionTexts = (name: string) => Array.from(select(name).options).map(option => option.textContent)
  const selectedText = (name: string) => select(name).selectedOptions[0]?.textContent
  const choose = (name: string, text: string) => {
    const option = Array.from(select(name).options).find(item => item.textContent === text)
    if (option === undefined) throw new Error(`${name} offers no option "${text}"`)
    fireEvent.change(select(name), { target: { value: option.value } })
  }
  const checkbox = (name: string) => screen.getByRole('checkbox', { name }) as HTMLInputElement

  it('Revision3: header controls appear only after the definition kind is chosen, and nothing is selected for the host', () => {
    const changes: RecordsDraftChange[] = []
    render(<RecordTypeDraftEditor value={emptyRecordsDraft()} kindOptions={kindOptions} classOptions={classOptions} roleOptions={roleOptions} onDraftChange={change => changes.push(change)} />)
    for (const name of ['Class', 'Record class', 'Creation gate', 'Creation role', 'Amendment mode']) expect(absent(name), name).toBeNull()
    for (const name of ['Amendments require a reason', 'Keep history']) expect(screen.queryByRole('checkbox', { name }), name).toBeNull()
    expect(screen.queryByText(creationNote)).toBeNull()
    fireEvent.change(screen.getByLabelText('Definition kind'), { target: { value: 'Records' } })
    expect(changes).toStrictEqual([{ draft: { identity: '', versionId: '', definitionKind: 'Records', body: {} }, generation: 1 }])
    expect(optionTexts('Class')).toEqual(['Not set', 'eam.equipment', 'eam.facility']); expect(selectedText('Class')).toBe('Not set')
    expect(select('Record class')).toBeInTheDocument()
    expect(optionTexts('Creation gate')).toEqual(['Not set (form_only)', 'form_only', 'role', 'engine']); expect(selectedText('Creation gate')).toBe('Not set (form_only)')
    expect(absent('Creation role')).toBeNull(); expect(selectedText('Amendment mode')).toBe('No mode')
    // A draft not yet created gets both policies from creation, so both show checked and disabled; nothing is written for them.
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    expect(screen.getByText(creationNote)).toBeInTheDocument(); expect(changes).toHaveLength(1)
  })

  it('Revision3: Class writes an offered class_id exactly, and Not set removes the member', () => {
    const base = headerBody(); const { changes } = header(base, { classOptions })
    expect(optionTexts('Class')).toEqual(['Not set', 'eam.equipment', 'eam.facility']); expect(selectedText('Class')).toBe('eam.equipment'); expect(changes).toHaveLength(0)
    choose('Class', 'eam.facility')
    expect(changes).toStrictEqual([reported({ ...base, class_id: 'eam.facility' }, 1)])
    choose('Class', 'Not set')
    expect(changes).toStrictEqual([reported({ ...base, class_id: 'eam.facility' }, 1), reported(without(base, 'class_id'), 2)])
    expect('class_id' in changes[1].draft.body).toBe(false)
  })

  it('Revision3: a current class the host does not offer is shown as not offered and kept', () => {
    const base = headerBody({ class_id: 'eam.legacy' }); const { changes } = header(base, { classOptions })
    expect(optionTexts('Class')[0]).toBe('Not set')
    expect([...optionTexts('Class')].sort()).toEqual(['Not set', 'eam.equipment', 'eam.facility', 'eam.legacy (not offered)'].sort())
    expect(selectedText('Class')).toBe('eam.legacy (not offered)'); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1)])
    expect(selectedText('Class')).toBe('eam.legacy (not offered)')
  })

  it('Revision3: with no class options only Not set and the current class are offered', () => {
    const first = header(headerBody())
    expect([...optionTexts('Class')].sort()).toEqual(['Not set', 'eam.equipment (not offered)'].sort()); expect(selectedText('Class')).toBe('eam.equipment (not offered)')
    first.view.unmount()
    const second = header(without(headerBody(), 'class_id'))
    expect(optionTexts('Class')).toEqual(['Not set']); expect(selectedText('Class')).toBe('Not set')
    expect(first.changes).toHaveLength(0); expect(second.changes).toHaveLength(0)
  })

  it('Revision3: the editor never selects a class or a creation role on its own, even when one option is offered', () => {
    const { changes } = header(without(headerBody({ creation_gate: 'role' }), 'class_id'), { classOptions: [{ classId: 'eam.equipment' }], roleOptions: [{ roleId: 'asset-manager' }] })
    expect(selectedText('Class')).toBe('Not set'); expect(selectedText('Creation role')).toBe('Not set')
    expect(changes).toHaveLength(0)
  })

  it.each([['reference', 'master'], ['master', 'transactional'], ['transactional', 'reference']])('Revision3: Record class writes the producer spelling %s exactly', (spelling, current) => {
    const base = headerBody({ record_class: current }); const { changes } = header(base)
    expect(selectedText('Record class')).toBe(current)
    choose('Record class', spelling)
    expect(changes).toStrictEqual([reported({ ...base, record_class: spelling }, 1)])
  })

  it('Revision3: an unknown current record class spelling is shown and kept', () => {
    const base = headerBody({ record_class: 'Master' }); const { changes } = header(base)
    expect(selectedText('Record class')).toContain('Master'); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1)])
  })

  it.each(['role', 'engine', 'form_only'])('Revision3: Creation gate writes %s exactly', gate => {
    const base = headerBody(); const { changes } = header(base, { roleOptions })
    choose('Creation gate', gate)
    // Offered roles are never selected for the host: the gate alone is written.
    expect(changes).toStrictEqual([reported({ ...base, creation_gate: gate }, 1)])
    expect(selectedText('Creation gate')).toBe(gate)
    if (gate === 'role') expect(selectedText('Creation role')).toBe('Not set'); else expect(absent('Creation role')).toBeNull()
  })

  it('Revision3: Not set (form_only) removes creation_gate', () => {
    const base = headerBody({ creation_gate: 'engine' }); const { changes } = header(base)
    choose('Creation gate', 'Not set (form_only)')
    expect(changes).toStrictEqual([reported(without(base, 'creation_gate'), 1)])
    expect('creation_gate' in changes[0].draft.body).toBe(false)
  })

  it.each<[string, Body]>([['form_only', { creation_gate: 'form_only' }], ['engine', { creation_gate: 'engine' }], ['Not set (form_only)', {}]])('Revision3: choosing %s on a role gate removes creation_role_id in the same change', (text, gate) => {
    const base = headerBody({ creation_gate: 'role', creation_role_id: 'records-clerk' }); const { changes } = header(base, { roleOptions })
    choose('Creation gate', text)
    expect(changes).toStrictEqual([reported({ ...without(base, 'creation_gate', 'creation_role_id'), ...gate }, 1)])
    expect('creation_role_id' in changes[0].draft.body).toBe(false); expect(absent('Creation role')).toBeNull()
  })

  it('Revision3: choosing role keeps an existing creation_role_id', () => {
    const base = headerBody({ creation_gate: 'engine', creation_role_id: 'records-clerk' }); const { changes } = header(base, { roleOptions })
    choose('Creation gate', 'role')
    expect(changes).toStrictEqual([reported({ ...base, creation_gate: 'role' }, 1)])
    expect(selectedText('Creation role')).toBe('records-clerk')
  })

  it.each<[string, Body]>([['absent', {}], ['form_only', { creation_gate: 'form_only' }], ['engine', { creation_gate: 'engine' }]])('Revision3: Creation role is not offered when the gate is %s', (_, gate) => {
    const { changes } = header(headerBody({ ...gate, creation_role_id: 'records-clerk' }), { roleOptions })
    expect(absent('Creation role')).toBeNull(); expect(changes).toHaveLength(0)
  })

  it('Revision3: Creation role writes an offered role exactly, and Not set removes the member', () => {
    const base = headerBody({ creation_gate: 'role' }); const { changes } = header(base, { roleOptions })
    expect(optionTexts('Creation role')).toEqual(['Not set', 'asset-manager', 'records-clerk']); expect(selectedText('Creation role')).toBe('Not set'); expect(changes).toHaveLength(0)
    choose('Creation role', 'records-clerk')
    expect(changes).toStrictEqual([reported({ ...base, creation_role_id: 'records-clerk' }, 1)])
    choose('Creation role', 'Not set')
    expect(changes).toStrictEqual([reported({ ...base, creation_role_id: 'records-clerk' }, 1), reported(base, 2)])
    expect('creation_role_id' in changes[1].draft.body).toBe(false)
  })

  it('Revision3: a current role the host does not offer is shown as not offered and kept, and no role options offer only Not set', () => {
    const base = headerBody({ creation_gate: 'role', creation_role_id: 'legacy-role' }); const first = header(base, { roleOptions })
    expect(optionTexts('Creation role')[0]).toBe('Not set')
    expect([...optionTexts('Creation role')].sort()).toEqual(['Not set', 'asset-manager', 'legacy-role (not offered)', 'records-clerk'].sort())
    expect(selectedText('Creation role')).toBe('legacy-role (not offered)'); expect(first.changes).toHaveLength(0)
    fireEvent.click(checkbox('Keep history'))
    expect(first.changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1)])
    first.view.unmount()
    const second = header(headerBody({ creation_gate: 'role' }))
    expect(optionTexts('Creation role')).toEqual(['Not set']); expect(second.changes).toHaveLength(0)
  })

  it('Revision3: checking writes reason_required true, Amendment mode adds and removes mode, and unchecking removes the policy', () => {
    const base = headerBody(); const { changes } = header(base)
    expect(checkbox('Amendments require a reason')).not.toBeChecked(); expect(absent('Amendment mode')).toBeNull()
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes.at(-1)).toStrictEqual(reported({ ...base, amendment_policy: { reason_required: true } }, 1))
    expect(checkbox('Amendments require a reason')).toBeChecked()
    expect(optionTexts('Amendment mode')).toEqual(['No mode', 'supersession']); expect(selectedText('Amendment mode')).toBe('No mode')
    choose('Amendment mode', 'supersession')
    expect(changes.at(-1)).toStrictEqual(reported({ ...base, amendment_policy: { reason_required: true, mode: 'supersession' } }, 2))
    choose('Amendment mode', 'No mode')
    expect(changes.at(-1)).toStrictEqual(reported({ ...base, amendment_policy: { reason_required: true } }, 3))
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes.at(-1)).toStrictEqual(reported(base, 4))
    expect('amendment_policy' in changes[3].draft.body).toBe(false); expect(absent('Amendment mode')).toBeNull(); expect(changes).toHaveLength(4)
  })

  // Not in the interface's required list: a malformed host policy must still render, show as absent, and never be spread into the new one.
  it.each<[string, unknown]>([['null', null], ['a string', 'ab'], ['an array', [1]]])('Revision3: a %s amendment policy renders unchecked with no Amendment mode, and checking replaces it with reason_required true', (_, policy) => {
    const base = headerBody({ amendment_policy: policy }); const { changes } = header(base)
    expect(checkbox('Amendments require a reason')).not.toBeChecked(); expect(absent('Amendment mode')).toBeNull(); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes.at(-1)).toStrictEqual(reported({ ...base, amendment_policy: { reason_required: true } }, 1))
  })

  it('Revision3: a non-string header value shows as not set and is kept unchanged', () => {
    const base = headerBody({ class_id: 42, record_class: 7, creation_gate: true }); const { changes } = header(base, { classOptions })
    expect(selectedText('Class')).toBe('Not set'); expect(selectedText('Record class')).toBe('Not set'); expect(selectedText('Creation gate')).toBe('Not set (form_only)')
    expect(changes).toHaveLength(0)
  })

  // A host-declared policy cannot be unchecked (the producer has no 'off' value and save carries an omitted declaration forward).
  it('Revision3: No mode keeps reason_required and the other policy members, and a host-declared policy cannot be unchecked', () => {
    const base = headerBody({ amendment_policy: { reason_required: true, mode: 'supersession', future_policy_member: 'kept' } })
    const first = header(base)
    expect(checkbox('Amendments require a reason')).toBeChecked(); expect(selectedText('Amendment mode')).toBe('supersession')
    choose('Amendment mode', 'No mode')
    expect(first.changes).toStrictEqual([reported({ ...base, amendment_policy: { reason_required: true, future_policy_member: 'kept' } }, 1)])
    first.view.unmount()
    const second = header(base)
    expect(checkbox('Amendments require a reason')).toBeDisabled()
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(second.changes).toHaveLength(0)
  })

  it('Revision3: checking with an existing policy writes reason_required true and keeps its other members', () => {
    // A policy present without reason_required true (malformed for the producer, carried as given): unchecked, with its mode shown.
    const base = headerBody({ amendment_policy: { mode: 'supersession', future_policy_member: 'kept' } }); const { changes } = header(base)
    expect(checkbox('Amendments require a reason')).not.toBeChecked(); expect(selectedText('Amendment mode')).toBe('supersession'); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toStrictEqual([reported({ ...base, amendment_policy: { mode: 'supersession', future_policy_member: 'kept', reason_required: true } }, 1)])
  })

  it('Revision3: Keep history writes history_policy enabled, and unchecking removes it', () => {
    const base = headerBody(); const { changes } = header(base)
    expect(checkbox('Keep history')).not.toBeChecked()
    fireEvent.click(checkbox('Keep history'))
    expect(changes.at(-1)).toStrictEqual(reported({ ...base, history_policy: 'enabled' }, 1)); expect(checkbox('Keep history')).toBeChecked()
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1), reported(base, 2)])
    expect('history_policy' in changes[1].draft.body).toBe(false)
  })

  it.each<[string, () => void, (body: Body) => Body]>([
    ['Class', () => choose('Class', 'eam.facility'), body => ({ ...body, class_id: 'eam.facility' })],
    ['Class Not set', () => choose('Class', 'Not set'), body => without(body, 'class_id')],
    ['Record class', () => choose('Record class', 'reference'), body => ({ ...body, record_class: 'reference' })],
    ['Creation gate engine', () => choose('Creation gate', 'engine'), body => ({ ...without(body, 'creation_role_id'), creation_gate: 'engine' })],
    ['Creation role', () => choose('Creation role', 'asset-manager'), body => ({ ...body, creation_role_id: 'asset-manager' })],
    ['Creation role Not set', () => choose('Creation role', 'Not set'), body => without(body, 'creation_role_id')],
    ['Amendment mode No mode', () => choose('Amendment mode', 'No mode'), body => ({ ...body, amendment_policy: { reason_required: true, future_policy_member: 'kept' } })],
  ])('Revision3: the %s edit changes only its own members and reports exactly one change at the next generation', (_, edit, expected) => {
    const base = fullHeader(); const given = structuredClone(base); const { changes } = header(base, { classOptions, roleOptions })
    edit()
    expect(changes).toStrictEqual([reported(expected(fullHeader()), 1)])
    expect(base).toStrictEqual(given)
  })

  // From the independent review: the producer has no 'off' policy value and save carries an omitted declaration forward (ADR-0097).
  it('Revision3: a policy the host draft declares cannot be removed, so both checkboxes are checked and disabled and toggling reports no change', () => {
    const { changes } = header(fullHeader(), { classOptions, roleOptions })
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    fireEvent.click(checkbox('Amendments require a reason')); fireEvent.click(checkbox('Keep history'))
    expect(changes).toHaveLength(0)
  })

  it('Revision3: a clean replacement that declares history disables its checkbox, and one that does not leaves it enabled', () => {
    const base = headerBody(); const { view } = header(base)
    expect(checkbox('Keep history')).not.toBeDisabled()
    view.rerender(<RecordTypeDraftEditor value={{ ...draftWith({ ...base, history_policy: 'enabled' }), versionId: '2.0.0' }} kindOptions={kindOptions} onDraftChange={() => {}} />)
    expect(checkbox('Keep history')).toBeChecked(); expect(checkbox('Keep history')).toBeDisabled()
    view.rerender(<RecordTypeDraftEditor value={{ ...draftWith(base), versionId: '3.0.0' }} kindOptions={kindOptions} onDraftChange={() => {}} />)
    expect(checkbox('Keep history')).not.toBeChecked(); expect(checkbox('Keep history')).not.toBeDisabled()
  })

  it('Revision3: two header edits on one render build on the local draft, so the second change keeps the first', () => {
    const base = headerBody(); const { changes } = header(base, { classOptions })
    choose('Class', 'eam.facility'); fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, class_id: 'eam.facility' }, 1), reported({ ...base, class_id: 'eam.facility', history_policy: 'enabled' }, 2)])
  })

  it('Revision3: malformed checkbox values show unchecked and enabled, and checking writes the admitted value', () => {
    const base = headerBody({ history_policy: 'disabled', amendment_policy: { reason_required: false } }); const { changes } = header(base)
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).not.toBeChecked(); expect(checkbox(name), name).not.toBeDisabled() }
    fireEvent.click(checkbox('Keep history')); fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1), reported({ ...base, history_policy: 'enabled', amendment_policy: { reason_required: true } }, 2)])
  })

  it('Revision3: a non-string creation role, an unknown gate spelling, an unknown mode and an empty class are shown as given and kept', () => {
    const first = header(headerBody({ creation_gate: 'role', creation_role_id: 42 }), { roleOptions })
    expect(selectedText('Creation role')).toBe('Not set'); expect(first.changes).toHaveLength(0); first.view.unmount()
    const second = header(headerBody({ creation_gate: 'form-only', amendment_policy: { reason_required: true, mode: 'edit_in_place' }, class_id: '' }), { classOptions })
    expect(selectedText('Creation gate')).toBe('form-only (not a current spelling)'); expect(selectedText('Amendment mode')).toBe('edit_in_place (not a current spelling)')
    expect(optionTexts('Class')).toEqual(['Not set', 'eam.equipment', 'eam.facility']); expect(selectedText('Class')).toBe('Not set')
    expect(second.changes).toHaveLength(0)
  })

  // From the recheck: a draft not yet created (empty identity) gets an absent policy from CreateDraftAsync (RecordTypeDefinitionStore.cs:153-154).
  const creationNote = 'Creation sets any policy that is not set, so it cannot be turned off.'
  const uncreated = (body: Body): RecordsDraft => ({ identity: '', versionId: '', definitionKind: 'Records', body: body as RecordTypeBody })
  const uncreatedHeader = (body: Body) => {
    const changes: RecordsDraftChange[] = []
    const view = render(<RecordTypeDraftEditor value={uncreated(body)} kindOptions={kindOptions} classOptions={classOptions} onDraftChange={change => changes.push(change)} />)
    return { changes, view }
  }

  it('Revision3: on a draft not yet created both absent policies show checked and disabled with the creation note, and toggling reports no change', () => {
    const first = uncreatedHeader({})
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    expect(screen.getByText(creationNote)).toBeInTheDocument(); expect(selectedText('Amendment mode')).toBe('No mode')
    fireEvent.click(checkbox('Amendments require a reason')); fireEvent.click(checkbox('Keep history')); choose('Amendment mode', 'No mode')
    expect(first.changes).toHaveLength(0); first.view.unmount()
    const second = header(headerBody())
    expect(screen.queryByText(creationNote)).toBeNull()
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).not.toBeChecked(); expect(checkbox(name), name).not.toBeDisabled() }
    expect(second.changes).toHaveLength(0)
  })

  it('Revision3: on a draft not yet created choosing a mode writes reason_required true with the mode, and the policy stays locked', () => {
    const { changes } = uncreatedHeader({})
    choose('Amendment mode', 'supersession')
    expect(changes).toStrictEqual([{ draft: uncreated({ amendment_policy: { reason_required: true, mode: 'supersession' } }), generation: 1 }])
    expect(checkbox('Amendments require a reason')).toBeChecked(); expect(checkbox('Amendments require a reason')).toBeDisabled(); expect(screen.getByText(creationNote)).toBeInTheDocument()
    choose('Amendment mode', 'No mode')
    expect(changes.at(-1)).toStrictEqual({ draft: uncreated({ amendment_policy: { reason_required: true } }), generation: 2 })
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toHaveLength(2)
  })

  it('Revision3: on a draft not yet created a malformed policy shows unchecked and enabled, and checking writes the admitted value and locks it', () => {
    const { changes } = uncreatedHeader({ history_policy: 'disabled', amendment_policy: 'yes' })
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).not.toBeChecked(); expect(checkbox(name), name).not.toBeDisabled() }
    expect(absent('Amendment mode')).toBeNull(); expect(screen.queryByText(creationNote)).toBeNull()
    fireEvent.click(checkbox('Keep history')); fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toStrictEqual([{ draft: uncreated({ history_policy: 'enabled', amendment_policy: 'yes' }), generation: 1 }, { draft: uncreated({ history_policy: 'enabled', amendment_policy: { reason_required: true } }), generation: 2 }])
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    expect(screen.getByText(creationNote)).toBeInTheDocument()
    fireEvent.click(checkbox('Keep history')); fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toHaveLength(2)
  })

  it('Revision3: Discard loads the held host draft as the host body, so a policy it declares cannot be unchecked', () => {
    const base = headerBody(); const { changes, view } = header(base, { classOptions })
    choose('Class', 'eam.facility')
    const incoming: RecordsDraft = { ...draftWith({ ...base, history_policy: 'enabled' }), versionId: '2.0.0' }
    view.rerender(<RecordTypeDraftEditor value={incoming} kindOptions={kindOptions} classOptions={classOptions} onDraftChange={change => changes.push(change)} />)
    expect(checkbox('Keep history')).not.toBeChecked(); expect(checkbox('Keep history')).not.toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes).toStrictEqual([reported({ ...base, class_id: 'eam.facility' }, 1), { draft: incoming, generation: 2 }])
    expect(checkbox('Keep history')).toBeChecked(); expect(checkbox('Keep history')).toBeDisabled()
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toHaveLength(2)
  })

  it('Revision3: a host echo of the local draft is not a replacement, so a locally checked policy stays enabled and removable', () => {
    const base = headerBody(); const { changes, view } = header(base)
    fireEvent.click(checkbox('Keep history'))
    expect(checkbox('Keep history')).toBeChecked(); expect(checkbox('Keep history')).not.toBeDisabled()
    view.rerender(<RecordTypeDraftEditor value={changes[0].draft} kindOptions={kindOptions} onDraftChange={change => changes.push(change)} />)
    expect(checkbox('Keep history')).not.toBeDisabled()
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, history_policy: 'enabled' }, 1), reported(base, 2)])
  })

  it('Revision3: unchecking a locally added policy that has a mode removes the whole policy', () => {
    const base = headerBody(); const { changes } = header(base)
    fireEvent.click(checkbox('Amendments require a reason')); choose('Amendment mode', 'supersession')
    expect(checkbox('Amendments require a reason')).not.toBeDisabled()
    fireEvent.click(checkbox('Amendments require a reason'))
    expect(changes).toStrictEqual([reported({ ...base, amendment_policy: { reason_required: true } }, 1), reported({ ...base, amendment_policy: { reason_required: true, mode: 'supersession' } }, 2), reported(base, 3)])
  })

  it('Revision3: an empty record class, creation gate, creation role or mode and a non-string mode show as not set, and an unrelated edit keeps each one', () => {
    const firstBase = headerBody({ record_class: '', creation_gate: '', amendment_policy: { reason_required: true, mode: '' } }); const first = header(firstBase, { classOptions })
    expect(selectedText('Record class')).toBe('Not set'); expect(selectedText('Creation gate')).toBe('Not set (form_only)'); expect(selectedText('Amendment mode')).toBe('No mode')
    choose('Class', 'eam.facility')
    expect(first.changes).toStrictEqual([reported({ ...firstBase, class_id: 'eam.facility' }, 1)]); first.view.unmount()
    const secondBase = headerBody({ creation_gate: 'role', creation_role_id: '', amendment_policy: { reason_required: true, mode: 7 } }); const second = header(secondBase, { roleOptions })
    expect(selectedText('Creation role')).toBe('Not set'); expect(selectedText('Amendment mode')).toBe('No mode')
    fireEvent.click(checkbox('Keep history'))
    expect(second.changes).toStrictEqual([reported({ ...secondBase, history_policy: 'enabled' }, 1)])
  })

  it('Revision3: on a draft not yet created a null policy is absent, so both show checked and disabled with the note, and choosing a mode replaces the null', () => {
    const { changes } = uncreatedHeader({ amendment_policy: null, history_policy: null })
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    expect(screen.getByText(creationNote)).toBeInTheDocument(); expect(selectedText('Amendment mode')).toBe('No mode')
    fireEvent.click(checkbox('Amendments require a reason')); fireEvent.click(checkbox('Keep history'))
    expect(changes).toHaveLength(0)
    choose('Amendment mode', 'supersession')
    expect(changes).toStrictEqual([{ draft: uncreated({ amendment_policy: { reason_required: true, mode: 'supersession' }, history_policy: null }), generation: 1 }])
  })

  it('Revision3: the new-draft rule follows the empty identity alone, not an empty version or a missing revision', () => {
    const first = render(<RecordTypeDraftEditor value={{ ...uncreated({}), versionId: '1.0.0' }} kindOptions={kindOptions} onDraftChange={() => {}} />)
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).toBeChecked(); expect(checkbox(name), name).toBeDisabled() }
    expect(screen.getByText(creationNote)).toBeInTheDocument(); first.unmount()
    const { expectedRevision: _revision, ...noRevision } = draftWith(headerBody())
    render(<RecordTypeDraftEditor value={noRevision} kindOptions={kindOptions} onDraftChange={() => {}} />)
    for (const name of ['Amendments require a reason', 'Keep history']) { expect(checkbox(name), name).not.toBeChecked(); expect(checkbox(name), name).not.toBeDisabled() }
    expect(screen.queryByText(creationNote)).toBeNull()
  })

  it('Revision3: Keep edits leaves the host body as it was, so a policy only the held draft declares stays addable and removable', () => {
    const base = headerBody(); const { changes, view } = header(base, { classOptions })
    choose('Class', 'eam.facility')
    view.rerender(<RecordTypeDraftEditor value={{ ...draftWith({ ...base, history_policy: 'enabled' }), versionId: '2.0.0' }} kindOptions={kindOptions} classOptions={classOptions} onDraftChange={change => changes.push(change)} />)
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(checkbox('Keep history')).not.toBeChecked(); expect(checkbox('Keep history')).not.toBeDisabled()
    fireEvent.click(checkbox('Keep history'))
    expect(checkbox('Keep history')).not.toBeDisabled()
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toStrictEqual([reported({ ...base, class_id: 'eam.facility' }, 1), reported({ ...base, class_id: 'eam.facility', history_policy: 'enabled' }, 2), reported({ ...base, class_id: 'eam.facility' }, 3)])
  })

  it('Revision3: a draft adopted after a rejected revision locks the policies it declares', () => {
    const changes: RecordsDraftChange[] = []; const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
    const view = render(<RecordTypeDraftEditor value={{ ...draftWith(headerBody()), expectedRevision: '01' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(screen.queryByRole('checkbox', { name: 'Keep history' })).toBeNull()
    view.rerender(<RecordTypeDraftEditor value={{ ...draftWith(headerBody({ history_policy: 'enabled' })), expectedRevision: '7' }} kindOptions={kindOptions} onDraftChange={onDraftChange} />)
    expect(checkbox('Keep history')).toBeChecked(); expect(checkbox('Keep history')).toBeDisabled()
    fireEvent.click(checkbox('Keep history'))
    expect(changes).toHaveLength(0)
  })

  it('Revision3: read-only disables every header control and reports no change', () => {
    const { changes } = header(fullHeader(), { classOptions, roleOptions, readOnly: true })
    for (const name of ['Class', 'Record class', 'Creation gate', 'Creation role', 'Amendment mode']) expect(select(name), name).toBeDisabled()
    for (const name of ['Amendments require a reason', 'Keep history']) expect(checkbox(name), name).toBeDisabled()
    choose('Class', 'eam.facility'); choose('Class', 'Not set'); choose('Record class', 'reference'); choose('Creation gate', 'engine'); choose('Creation role', 'asset-manager'); choose('Amendment mode', 'No mode')
    fireEvent.click(checkbox('Amendments require a reason')); fireEvent.click(checkbox('Keep history'))
    expect(changes).toHaveLength(0)
    expect(selectedText('Class')).toBe('eam.equipment'); expect(selectedText('Creation gate')).toBe('role'); expect(selectedText('Creation role')).toBe('records-clerk')
    expect(checkbox('Amendments require a reason')).toBeChecked(); expect(checkbox('Keep history')).toBeChecked()
  })
})

// Interface revision 4 references (semantics.edits, notEdited, hostOptions, readOnly; producer reference shape RecordTypeIdentity.cs:44-97, the
// exactly-one-target rule RecordTypeIdentity.cs:275-279, snake_case_lower spellings RecordTypeDocument.cs:271-284). Written from the interface, not
// the implementation. Controls are found by accessible name, buttons by name and options by visible text, never by option value. Every base body
// carries unknown members at the body, envelope and field level, and every base reference carries required_trait_id, a pin and an unknown member,
// so each exact expected body also proves preservation. Case names pair one-for-one with RecordsDraftTests.cs (Revision4_*).
describe('Records local draft editor, React lane, revision 4 references', () => {
  type Json = Record<string, unknown>
  const classOptions: readonly RecordsClassOption[] = [{ classId: 'eam.equipment' }, { classId: 'eam.facility' }]
  const targetKind = 'Field 4 reference target kind'; const targetType = 'Field 4 reference target type'; const targetClass = 'Field 4 reference target class'
  const cardinality = 'Field 4 reference cardinality'; const onDelete = 'Field 4 reference on delete'; const parent = 'Field 4 reference is the parent edge'
  const bothTargets: Json = { target_type_id: 'eam.space', target_class_id: 'eam.facility' }
  const without = (value: Json, ...members: string[]): Json => Object.fromEntries(Object.entries(value).filter(([member]) => !members.includes(member)))
  // A producer-shaped reference to the given targets (RecordReferenceDefinition): cardinality one, on_delete block and the canonical parent false,
  // plus the members every reference edit must carry unchanged: required_trait_id, the cross-package pin and an unknown member.
  const siteReference = (targets: Json = { target_type_id: 'eam.space' }): Json => ({ ...targets, cardinality: 'one', on_delete: 'block', parent: false, required_trait_id: 'eam.locatable', pin: { package_id: 'eam-core', definition_id: 'eam.space', version: '1.0.0', digest: 'sha256:5f1c', interface_version: 1 }, future_reference_member: 'kept' })
  // G1 (fields 1 and 2 bound, field 3 notes with neither a binding nor a reference) plus unknown members at the body, envelope and field level,
  // then a fourth field "site" with no binding, an unknown member and the given members.
  const referenceBody = (site: Json = {}): Json => {
    const stored = structuredClone(g1.expectedStoredBody)
    return { ...stored, future_member: { kept: [1, 'two'] }, envelope: { ...stored.envelope, future_envelope_member: true }, fields: [...stored.fields.map((field, index) => index === 0 ? { ...field, future_field_member: 'kept' } : field), { field_key: 'site', display_name: 'Site', future_field_member: 'kept', ...site }] }
  }
  // A copy of the body with one field (0-based) replaced; the site field is index 3.
  const withField = (body: Json, index: number, update: (field: Json) => Json): Json => ({ ...body, fields: (body.fields as Json[]).map((field, current) => current === index ? update(field) : field) })
  const withSiteReference = (body: Json, update: (reference: Json) => Json): Json => withField(body, 3, field => ({ ...field, reference: update(field.reference as Json) }))
  const siteReferenceOf = (change: RecordsDraftChange) => (change.draft.body.fields![3] as Json).reference as Json
  const draftWith = (body: Json): RecordsDraft => ({ ...g1Draft(), body: body as RecordTypeBody })
  const reported = (body: Json, generation: number): RecordsDraftChange => ({ draft: draftWith(body), generation })
  const references = (body: Json, options: { classOptions?: readonly RecordsClassOption[]; readOnly?: boolean } = {}) => {
    const changes: RecordsDraftChange[] = []
    const view = render(<RecordTypeDraftEditor value={draftWith(body)} kindOptions={kindOptions} {...options} onDraftChange={change => changes.push(change)} />)
    return { changes, view }
  }
  const select = (name: string) => screen.getByRole('combobox', { name }) as HTMLSelectElement
  const absent = (name: string) => screen.queryByLabelText(name)
  const optionTexts = (name: string) => Array.from(select(name).options).map(option => option.textContent)
  const sortedOptionTexts = (name: string) => [...optionTexts(name)].sort()
  const selectedText = (name: string) => select(name).selectedOptions[0]?.textContent
  const choose = (name: string, text: string) => {
    const option = Array.from(select(name).options).find(item => item.textContent === text)
    if (option === undefined) throw new Error(`${name} offers no option "${text}"`)
    fireEvent.change(select(name), { target: { value: option.value } })
  }
  const checkbox = (name: string) => screen.getByRole('checkbox', { name }) as HTMLInputElement
  const textInput = (name = targetType) => screen.getByLabelText(name) as HTMLInputElement
  const enter = (text: string, name = targetType) => fireEvent.change(textInput(name), { target: { value: text } })
  const button = (name: string) => screen.queryByRole('button', { name })
  const press = (name: string) => fireEvent.click(screen.getByRole('button', { name }))

  it('Revision4: Make field N a reference appears only on a field with neither a binding nor a reference and adds exactly an empty reference', () => {
    const base = referenceBody({ reference: siteReference() }); const given = structuredClone(base); const { changes } = references(base)
    // Fields 1 and 2 are bound, field 3 (notes) has neither member, and field 4 (site) already has a reference.
    expect([1, 2, 3, 4].map(n => button(`Make field ${n} a reference`) !== null)).toEqual([false, false, true, false])
    expect([1, 2, 3, 4].map(n => button(`Remove reference from field ${n}`) !== null)).toEqual([false, false, false, true])
    expect(changes).toHaveLength(0)
    press('Make field 3 a reference')
    expect(changes).toStrictEqual([reported(withField(base, 2, field => ({ ...field, reference: {} })), 1)])
    expect(button('Make field 3 a reference')).toBeNull(); expect(button('Remove reference from field 3')).not.toBeNull()
    expect(selectedText('Field 3 reference target kind')).toBe('Not set'); expect(selectedText('Field 3 reference cardinality')).toBe('Not set'); expect(selectedText('Field 3 reference on delete')).toBe('Not set'); expect(checkbox('Field 3 reference is the parent edge')).not.toBeChecked()
    expect(base).toStrictEqual(given)
  })

  it('Revision4: Remove reference from field N deletes the reference member only', () => {
    const base = referenceBody({ reference: siteReference() }); const given = structuredClone(base); const { changes } = references(base)
    press('Remove reference from field 4')
    expect(changes).toStrictEqual([reported(withField(given, 3, field => without(field, 'reference')), 1)])
    expect('reference' in (changes[0].draft.body.fields![3] as Json)).toBe(false)
    expect(absent(targetKind)).toBeNull(); expect(button('Remove reference from field 4')).toBeNull(); expect(button('Make field 4 a reference')).not.toBeNull()
    expect(base).toStrictEqual(given)
  })

  it('Revision4: a new reference is built by successive edits on the local draft, one change per edit', () => {
    const base = referenceBody(); const { changes } = references(base, { classOptions })
    press('Make field 3 a reference')
    choose('Field 3 reference target kind', 'Record type')
    enter('eam.space', 'Field 3 reference target type')
    choose('Field 3 reference cardinality', 'many'); choose('Field 3 reference on delete', 'cascade'); fireEvent.click(checkbox('Field 3 reference is the parent edge'))
    const step = (reference: Json, generation: number) => reported(withField(base, 2, field => ({ ...field, reference })), generation)
    expect(changes).toStrictEqual([step({}, 1), step({ target_type_id: '' }, 2), step({ target_type_id: 'eam.space' }, 3), step({ target_type_id: 'eam.space', cardinality: 'many' }, 4), step({ target_type_id: 'eam.space', cardinality: 'many', on_delete: 'cascade' }, 5), step({ target_type_id: 'eam.space', cardinality: 'many', on_delete: 'cascade', parent: true }, 6)])
  })

  it.each<[string, Json]>([
    ['Not set', {}],
    ['Record type', { target_type_id: 'eam.space' }],
    ['Record type', { target_type_id: '' }],
    ['Class', { target_class_id: 'eam.facility' }],
    ['Class', { target_class_id: '' }],
  ])('Revision4: the target kind offers Record type, Class and Not set and shows %s for %j', (shown, targets) => {
    const { changes } = references(referenceBody({ reference: siteReference(targets) }), { classOptions })
    expect(sortedOptionTexts(targetKind)).toEqual(['Class', 'Not set', 'Record type']); expect(selectedText(targetKind)).toBe(shown)
    // One target kind shows only its own target control; both controls are for a reference carrying both targets.
    if (shown === 'Record type') expect(absent(targetClass)).toBeNull()
    if (shown === 'Class') expect(absent(targetType)).toBeNull()
    expect(changes).toHaveLength(0)
  })

  it.each<[string, string, Json, Json]>([
    ['Class', 'a Record type target', { target_type_id: 'eam.space' }, { target_class_id: '' }],
    ['Not set', 'a Record type target', { target_type_id: 'eam.space' }, {}],
    ['Record type', 'a Class target', { target_class_id: 'eam.facility' }, { target_type_id: '' }],
    ['Not set', 'a Class target', { target_class_id: 'eam.facility' }, {}],
    ['Record type', 'both targets', bothTargets, { target_type_id: 'eam.space' }],
    ['Class', 'both targets', bothTargets, { target_class_id: 'eam.facility' }],
    ['Not set', 'both targets', bothTargets, {}],
    ['Record type', 'no target', {}, { target_type_id: '' }],
    ['Class', 'no target', {}, { target_class_id: '' }],
  ])('Revision4: choosing the target kind %s on %s keeps a value of that kind and removes the other target member', (kind, _, targets, expected) => {
    const base = referenceBody({ reference: siteReference(targets) }); const { changes } = references(base, { classOptions })
    choose(targetKind, kind)
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...without(reference, 'target_type_id', 'target_class_id'), ...expected })), 1)])
    expect(selectedText(targetKind)).toBe(kind)
  })

  it.each<[string, Json, string, string]>([
    ['named', bothTargets, 'eam.space', 'eam.facility'],
    ['empty', { target_type_id: '', target_class_id: '' }, '', 'Not set'],
  ])('Revision4: %s target members both present as strings show Both targets present (choose one) and both target controls', (_, targets, typeValue, classText) => {
    const base = referenceBody({ reference: siteReference(targets) }); const { changes } = references(base, { classOptions })
    expect(selectedText(targetKind)).toBe('Both targets present (choose one)')
    expect(sortedOptionTexts(targetKind)).toEqual(['Both targets present (choose one)', 'Class', 'Not set', 'Record type'])
    expect(textInput()).toHaveValue(typeValue); expect(selectedText(targetClass)).toBe(classText); expect(changes).toHaveLength(0)
    choose(targetKind, 'Class')
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => without(reference, 'target_type_id')), 1)])
    expect(selectedText(targetKind)).toBe('Class'); expect(absent(targetType)).toBeNull(); expect(selectedText(targetClass)).toBe(classText)
  })

  it('Revision4: the Record type target is free text that writes target_type_id exactly', () => {
    const base = referenceBody({ reference: siteReference() }); const { changes } = references(base, { classOptions })
    expect(selectedText(targetKind)).toBe('Record type'); expect(textInput()).toHaveValue('eam.space'); expect(absent(targetClass)).toBeNull()
    enter('eam.building'); enter('')
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, target_type_id: 'eam.building' })), 1), reported(withSiteReference(base, reference => ({ ...reference, target_type_id: '' })), 2)])
    expect(selectedText(targetKind)).toBe('Record type')
  })

  it('Revision4: the Class target is chosen only from host class options, and Not set writes an empty target_class_id', () => {
    const base = referenceBody({ reference: siteReference({ target_class_id: '' }) }); const { changes } = references(base, { classOptions })
    expect(optionTexts(targetClass)).toEqual(['Not set', 'eam.equipment', 'eam.facility']); expect(selectedText(targetClass)).toBe('Not set'); expect(absent(targetType)).toBeNull(); expect(changes).toHaveLength(0)
    choose(targetClass, 'eam.facility'); choose(targetClass, 'Not set')
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, target_class_id: 'eam.facility' })), 1), reported(base, 2)])
    expect('target_class_id' in siteReferenceOf(changes[1])).toBe(true); expect(selectedText(targetKind)).toBe('Class')
  })

  it('Revision4: a current class target the host does not offer is shown as not offered and kept, and no class options offer only Not set', () => {
    const base = referenceBody({ reference: siteReference({ target_class_id: 'eam.legacy' }) }); const first = references(base, { classOptions })
    expect(sortedOptionTexts(targetClass)).toEqual(['Not set', 'eam.equipment', 'eam.facility', 'eam.legacy (not offered)']); expect(selectedText(targetClass)).toBe('eam.legacy (not offered)')
    fireEvent.click(checkbox(parent))
    expect(first.changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, parent: true })), 1)])
    expect(selectedText(targetClass)).toBe('eam.legacy (not offered)'); first.view.unmount()
    const second = references(referenceBody({ reference: siteReference({ target_class_id: '' }) }))
    expect(optionTexts(targetClass)).toEqual(['Not set']); expect(selectedText(targetClass)).toBe('Not set'); second.view.unmount()
    const third = references(referenceBody({ reference: siteReference({ target_class_id: 'eam.facility' }) }))
    expect(sortedOptionTexts(targetClass)).toEqual(['Not set', 'eam.facility (not offered)']); expect(selectedText(targetClass)).toBe('eam.facility (not offered)')
    expect(second.changes).toHaveLength(0); expect(third.changes).toHaveLength(0)
  })

  it('Revision4: the editor never selects a class target on its own, even when exactly one class is offered', () => {
    const one: readonly RecordsClassOption[] = [{ classId: 'eam.equipment' }]
    const first = references(referenceBody({ reference: siteReference({ target_class_id: '' }) }), { classOptions: one })
    expect(selectedText(targetClass)).toBe('Not set'); expect(first.changes).toHaveLength(0); first.view.unmount()
    const base = referenceBody({ reference: siteReference() }); const second = references(base, { classOptions: one })
    choose(targetKind, 'Class')
    expect(second.changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...without(reference, 'target_type_id'), target_class_id: '' })), 1)])
    expect(selectedText(targetClass)).toBe('Not set')
  })

  it('Revision4: the parent checkbox writes parent true, and unchecking writes parent false without removing the member', () => {
    const base = referenceBody({ reference: siteReference() }); const { changes } = references(base)
    expect(checkbox(parent)).not.toBeChecked()
    fireEvent.click(checkbox(parent)); expect(checkbox(parent)).toBeChecked()
    fireEvent.click(checkbox(parent))
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, parent: true })), 1), reported(base, 2)])
    expect(siteReferenceOf(changes[1]).parent).toBe(false)
    // A reference with no parent member yet: unchecking still writes false, the canonical form, rather than removing it.
    press('Make field 3 a reference'); fireEvent.click(checkbox('Field 3 reference is the parent edge')); fireEvent.click(checkbox('Field 3 reference is the parent edge'))
    expect(changes.slice(2)).toStrictEqual([reported(withField(base, 2, field => ({ ...field, reference: {} })), 3), reported(withField(base, 2, field => ({ ...field, reference: { parent: true } })), 4), reported(withField(base, 2, field => ({ ...field, reference: { parent: false } })), 5)])
  })

  it('Revision4: a host reference with parent true shows the checkbox checked, and unchecking writes parent false', () => {
    const base = referenceBody({ reference: { ...siteReference(), parent: true } }); const { changes } = references(base)
    expect(checkbox(parent)).toBeChecked(); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox(parent))
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, parent: false })), 1)])
    expect(checkbox(parent)).not.toBeChecked()
  })

  it.each<[string, () => void, (reference: Json) => Json]>([
    ['Target kind Record type', () => choose(targetKind, 'Record type'), reference => without(reference, 'target_class_id')],
    ['Target kind Class', () => choose(targetKind, 'Class'), reference => without(reference, 'target_type_id')],
    ['Target kind Not set', () => choose(targetKind, 'Not set'), reference => without(reference, 'target_type_id', 'target_class_id')],
    ['Target type', () => enter('eam.building'), reference => ({ ...reference, target_type_id: 'eam.building' })],
    ['Target class', () => choose(targetClass, 'eam.equipment'), reference => ({ ...reference, target_class_id: 'eam.equipment' })],
    ['Target class Not set', () => choose(targetClass, 'Not set'), reference => ({ ...reference, target_class_id: '' })],
    ['Cardinality', () => choose(cardinality, 'many'), reference => ({ ...reference, cardinality: 'many' })],
    ['On delete', () => choose(onDelete, 'orphan'), reference => ({ ...reference, on_delete: 'orphan' })],
    ['Parent', () => fireEvent.click(checkbox(parent)), reference => ({ ...reference, parent: true })],
  ])('Revision4: the %s edit changes only its own reference members, carries pin and required_trait_id unchanged and never mutates the given value', (_, edit, expected) => {
    // Both targets are present, so every reference control is shown at once.
    const base = referenceBody({ reference: siteReference(bothTargets) }); const given = structuredClone(base); const { changes } = references(base, { classOptions })
    edit()
    expect(changes).toStrictEqual([reported(withSiteReference(referenceBody({ reference: siteReference(bothTargets) }), expected), 1)])
    expect(base).toStrictEqual(given)
  })

  it.each<[string, unknown]>([['null', null], ['a string', 'ab'], ['an array', [1]]])('Revision4: a %s reference is treated as absent, so Make field N a reference is offered only without a binding and never spreads it', (_, malformed) => {
    // Field 1 is bound and field 4 (site) has no binding; both carry the malformed reference.
    const base = withField(referenceBody({ reference: malformed }), 0, field => ({ ...field, reference: malformed })); const { changes } = references(base)
    expect(absent('Field 1 reference target kind')).toBeNull(); expect(absent(targetKind)).toBeNull()
    expect(button('Make field 1 a reference')).toBeNull(); expect(button('Remove reference from field 1')).toBeNull(); expect(button('Remove reference from field 4')).toBeNull()
    expect(changes).toHaveLength(0)
    press('Make field 4 a reference')
    expect(changes).toStrictEqual([reported(withField(base, 3, field => ({ ...field, reference: {} })), 1)])
  })

  it.each<[string, unknown]>([['null', null], ['a string', 'text'], ['an array', [1]]])('Revision4: a %s binding is treated as absent, so Make field N a reference is offered and the binding is carried unchanged', (_, malformed) => {
    const base = referenceBody({ binding: malformed }); const { changes } = references(base)
    expect(absent('Field 4 kind')).toBeNull(); expect(changes).toHaveLength(0)
    expect(button('Make field 4 a reference')).not.toBeNull()
    press('Make field 4 a reference')
    expect(changes).toStrictEqual([reported(withField(base, 3, field => ({ ...field, reference: {} })), 1)])
    expect(selectedText(targetKind)).toBe('Not set')
  })

  it('Revision4: a non-string cardinality or on_delete shows as not set and is kept unless edited', () => {
    const base = referenceBody({ reference: { ...siteReference(), cardinality: 1, on_delete: null } }); const { changes } = references(base)
    expect(selectedText(cardinality)).toBe('Not set'); expect(selectedText(onDelete)).toBe('Not set'); expect(changes).toHaveLength(0)
    fireEvent.click(checkbox(parent)); choose(cardinality, 'many')
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, parent: true })), 1), reported(withSiteReference(base, reference => ({ ...reference, parent: true, cardinality: 'many' })), 2)])
    expect('on_delete' in siteReferenceOf(changes[1])).toBe(true); expect(selectedText(onDelete)).toBe('Not set')
  })

  it.each([['one', 'many'], ['many', 'one']])('Revision4: Cardinality writes the producer spelling %s exactly', (spelling, current) => {
    const base = referenceBody({ reference: { ...siteReference(), cardinality: current } }); const { changes } = references(base)
    expect(selectedText(cardinality)).toBe(current)
    choose(cardinality, spelling)
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, cardinality: spelling })), 1)])
  })

  it.each([['block', 'cascade'], ['orphan', 'block'], ['cascade', 'orphan']])('Revision4: On delete writes the producer spelling %s exactly', (spelling, current) => {
    const base = referenceBody({ reference: { ...siteReference(), on_delete: current } }); const { changes } = references(base)
    expect(selectedText(onDelete)).toBe(current)
    choose(onDelete, spelling)
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, on_delete: spelling })), 1)])
  })

  it('Revision4: a revision 1 reference on a bound field keeps its controls and edits, and is not offered Make field N a reference', () => {
    // The R5 field: a revision 1 reference beside a binding (the producer's binding conflict, carried as given), plus a carried trait.
    const r5Field = refusalCase('R5-reference-binding-conflict').createRequest!.fields[0]
    const base = referenceBody({ binding: structuredClone(r5Field.binding), reference: { ...structuredClone(r5Field.reference), required_trait_id: 'eam.locatable' } }); const { changes } = references(base)
    expect(button('Make field 4 a reference')).toBeNull(); expect(button('Remove reference from field 4')).not.toBeNull()
    expect(selectedText(targetKind)).toBe('Record type'); expect(textInput()).toHaveValue('eam.asset-class'); expect(selectedText(cardinality)).toBe('one'); expect(selectedText(onDelete)).toBe('block')
    enter('eam.space'); choose(cardinality, 'many'); choose(onDelete, 'orphan')
    expect(changes).toStrictEqual([reported(withSiteReference(base, reference => ({ ...reference, target_type_id: 'eam.space' })), 1), reported(withSiteReference(base, reference => ({ ...reference, target_type_id: 'eam.space', cardinality: 'many' })), 2), reported(withSiteReference(base, reference => ({ ...reference, target_type_id: 'eam.space', cardinality: 'many', on_delete: 'orphan' })), 3)])
  })

  it('Revision4: read-only disables every reference control and reports no change', () => {
    const { changes } = references(referenceBody({ reference: siteReference(bothTargets) }), { classOptions, readOnly: true })
    for (const name of [targetKind, targetClass, cardinality, onDelete]) expect(select(name), name).toBeDisabled()
    expect(textInput()).toBeDisabled(); expect(checkbox(parent)).toBeDisabled()
    for (const name of ['Make field 3 a reference', 'Remove reference from field 4']) expect(screen.getByRole('button', { name }), name).toBeDisabled()
    choose(targetKind, 'Class'); choose(targetClass, 'eam.equipment'); choose(cardinality, 'many'); choose(onDelete, 'cascade'); enter('eam.building'); fireEvent.click(checkbox(parent))
    press('Make field 3 a reference'); press('Remove reference from field 4')
    expect(changes).toHaveLength(0)
    expect(selectedText(targetKind)).toBe('Both targets present (choose one)'); expect(textInput()).toHaveValue('eam.space'); expect(selectedText(targetClass)).toBe('eam.facility')
    expect(checkbox(parent)).not.toBeChecked(); expect(button('Make field 3 a reference')).not.toBeNull()
  })
})
