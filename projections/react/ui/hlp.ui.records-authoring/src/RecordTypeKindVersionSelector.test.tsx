import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RecordTypeDraftEditor } from './RecordTypeDraftEditor'
import type { RecordsDiagnostic, RecordsDiagnosticSet, RecordsDraft, RecordsDraftChange, RecordsJsonObject, RecordsKindOption, RecordTypeField } from './RecordsDraftTypes'

// M11 item 6: tests for the EXISTING field kind/version selector only. No production path changes. Every expected output is a literal.
// Behaviour under test (RecordTypeDraftEditor.tsx):
//   :32  chooseFieldKind: option = kindOptions[Number(choice)]; the `choice === ''` guard is the only defence against Number('') === 0.
//        The chosen kind is spread over the existing kind object, so parameters and unknown kind members survive.
//   :49  offered = index of the option whose kindId AND version equal the field kind's kind_id AND version (strict ===), else -1.
//   :54  the select's value is String(offered), or '' with a leading `${kind_id ?? ''} ${version ?? ''} (not offered)` option.
//   :55  string and number parameters render as inputs; other parameter values (objects) do not.
//   :24  host values: an echo of the current identity/version/revision is not a replacement; a different one waits while dirty.
//   :25  change(): local edit, advances the local generation and emits { draft, generation }.
//   :26-27 load()/keep(): Discard emits the incoming draft at the next generation; Keep drops it without emitting.
//   :37  diagnostics show only for the current identity, the same revision (both absent counts as same) and the current generation.
//   :45  the revision is shown verbatim, or 'None' when absent.
// Paired one-for-one with projections/blazor/ui/hlp.ui.records-authoring.tests/RecordTypeKindVersionSelectorTests.cs.

// The four rows of the test-host kind registry (conformance/hlp.ui.records-authoring/source-fixtures/records-current-grammar.json,
// grammarFacts.testHostKindRegistry), written as literals.
const registry: readonly RecordsKindOption[] = [{ kindId: 'count', version: '1.0.0' }, { kindId: 'text', version: '1.0.0' }, { kindId: 'date', version: '1.0.0' }, { kindId: 'date', version: '2.0.0' }]
const registryOptions = [['0', 'count 1.0.0'], ['1', 'text 1.0.0'], ['2', 'date 1.0.0'], ['3', 'date 2.0.0']]
const probe: readonly RecordsDiagnostic[] = [{ code: 'probe.selector', pointer: '/fields/0/binding/kind' }]
const constraints = { required: false, minimum_count: 0, maximum_count: 1, read_role_ids: [] }
const field = (key: string, kind: RecordsJsonObject): RecordTypeField => ({ field_key: key, display_name: `Display ${key}`, binding: { kind, constraints }, future_field_member: 'kept' })
const bodyOf = (fields: readonly RecordTypeField[]) => ({ name: 'Asset Class', future_member: { kept: [1, 'two'] }, fields })
// Absent is a missing member or JSON null (RecordsDraftTypes.ts:13-16); the member is added only when the row supplies one.
const draftOf = (revision: string | null | undefined, fields: readonly RecordTypeField[]): RecordsDraft => {
  const draft: RecordsDraft = { identity: 'eam.asset-class', versionId: '1.0.0', definitionKind: 'Records', body: bodyOf(fields) }
  return revision === undefined ? draft : { ...draft, expectedRevision: revision as string }
}
const probeAt = (revision: string, generation: number): RecordsDiagnosticSet => ({ identity: 'eam.asset-class', expectedRevision: revision, generation, diagnostics: probe })

const select = (n: number) => screen.getByLabelText(`Field ${n} kind`) as HTMLSelectElement
const optionsOf = (n: number) => Array.from(select(n).options).map(option => [option.value, option.textContent])
const choose = (n: number, value: string) => fireEvent.change(select(n), { target: { value } })
const input = (label: string) => screen.getByLabelText(label) as HTMLInputElement
const generationOf = () => screen.getByRole('form', { name: 'Record type draft' }).getAttribute('data-generation')
const revisionShown = () => screen.getByLabelText('Expected revision').textContent
const rendered = () => Array.from(screen.queryByRole('list', { name: 'Diagnostics' })?.querySelectorAll('li') ?? []).map(item => item.textContent)
const mount = (value: RecordsDraft, kindOptions: readonly RecordsKindOption[], diagnostics?: RecordsDiagnosticSet) => {
  const changes: RecordsDraftChange[] = []
  const onDraftChange = (change: RecordsDraftChange) => { changes.push(change) }
  const view = render(<RecordTypeDraftEditor value={value} kindOptions={kindOptions} diagnostics={diagnostics} onDraftChange={onDraftChange} />)
  const show = (next: RecordsDraft, nextOptions: readonly RecordsKindOption[], nextDiagnostics?: RecordsDiagnosticSet) => view.rerender(<RecordTypeDraftEditor value={next} kindOptions={nextOptions} diagnostics={nextDiagnostics} onDraftChange={onDraftChange} />)
  return { changes, show }
}

describe('Records kind/version selector, React lane', () => {
  // Gap 1: the "(not offered)" branch (:54). Matching is exact on both members (:49): no case folding, no version normalisation.
  it.each([
    { kind: { kind_id: 'legacy', version: '0.9.0' }, text: 'legacy 0.9.0 (not offered)' },
    { kind: { kind_id: 'date', version: '3.0.0' }, text: 'date 3.0.0 (not offered)' },
    { kind: { kind_id: 'Date', version: '1.0.0' }, text: 'Date 1.0.0 (not offered)' },
    { kind: { kind_id: 'count', version: '1.0' }, text: 'count 1.0 (not offered)' },
    { kind: { kind_id: 'date' }, text: 'date  (not offered)' },
    { kind: {}, text: '  (not offered)' },
  ])('shows $text as the selected placeholder for a kind/version the host does not offer', ({ kind, text }) => {
    const { changes } = mount(draftOf('9007199254740993', [field('gauge', kind), field('label', { kind_id: 'text', version: '1.0.0', parameters: {} })]), registry)
    expect(optionsOf(1)).toEqual([['', text], ...registryOptions])
    expect(select(1).value).toBe(''); expect(select(1).selectedIndex).toBe(0)
    // An offered kind on another field gets no placeholder and is selected by its index.
    expect(optionsOf(2)).toEqual(registryOptions); expect(select(2).value).toBe('1')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0')
  })

  // Gap 2: the '' placeholder. Number('') is 0, so without the guard at :32 this would bind kindOptions[0] (count 1.0.0).
  it('choosing the "" placeholder of a not-offered kind emits nothing; a real choice then replaces the placeholder', () => {
    const host = draftOf('9007199254740993', [field('gauge', { kind_id: 'legacy', version: '0.9.0', parameters: { scale: 2 }, future_kind_member: 'kept' })])
    const { changes } = mount(host, registry)
    choose(1, '')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0')
    expect(optionsOf(1)).toEqual([['', 'legacy 0.9.0 (not offered)'], ...registryOptions]); expect(select(1).value).toBe('')

    choose(1, '3')
    expect(changes).toStrictEqual([{ draft: { identity: 'eam.asset-class', versionId: '1.0.0', expectedRevision: '9007199254740993', definitionKind: 'Records', body: bodyOf([field('gauge', { kind_id: 'date', version: '2.0.0', parameters: { scale: 2 }, future_kind_member: 'kept' })]) }, generation: 1 }])
    expect(optionsOf(1)).toEqual(registryOptions); expect(select(1).value).toBe('3'); expect(generationOf()).toBe('1')
  })

  it('choosing "" on an offered kind emits nothing, so Number("") === 0 never binds count 1.0.0', () => {
    const host = draftOf('9007199254740993', [field('gauge', { kind_id: 'date', version: '2.0.0', parameters: {} })])
    const { changes, show } = mount(host, registry)
    expect(select(1).value).toBe('3')
    // jsdom has no '' option here, so the change event carries '' exactly as the guard at :32 sees it.
    choose(1, '')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0')
    show(host, registry)
    expect(select(1).value).toBe('3'); expect(optionsOf(1)).toEqual(registryOptions); expect(changes).toEqual([])
  })

  // Gap 3: kindOptions changing between renders. Options are positional (:54), the selection is re-derived each render (:49), and the
  // choice indexes the kindOptions of the render that handles it (:32). A new list is not an edit: no emission, no generation change.
  it('a reordered kindOptions list re-selects by kind and version without emitting, and the next choice indexes the new list', () => {
    const host = draftOf('9007199254740993', [field('gauge', { kind_id: 'date', version: '2.0.0', parameters: {} }), field('quantity', { kind_id: 'count', version: '1.0.0', parameters: {} })])
    const { changes, show } = mount(host, registry, probeAt('9007199254740993', 0))
    expect(select(1).value).toBe('3'); expect(select(2).value).toBe('0'); expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind'])

    const reordered: readonly RecordsKindOption[] = [{ kindId: 'date', version: '2.0.0' }, { kindId: 'count', version: '1.0.0' }]
    show(host, reordered, probeAt('9007199254740993', 0))
    expect(optionsOf(1)).toEqual([['0', 'date 2.0.0'], ['1', 'count 1.0.0']]); expect(select(1).value).toBe('0'); expect(select(2).value).toBe('1')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0'); expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind'])

    // '0' is date 2.0.0 in the new list; in the original registry it would have been count 1.0.0.
    choose(2, '0')
    expect(changes).toStrictEqual([{ draft: { identity: 'eam.asset-class', versionId: '1.0.0', expectedRevision: '9007199254740993', definitionKind: 'Records', body: bodyOf([field('gauge', { kind_id: 'date', version: '2.0.0', parameters: {} }), field('quantity', { kind_id: 'date', version: '2.0.0', parameters: {} })]) }, generation: 1 }])
    expect(select(2).value).toBe('0'); expect(generationOf()).toBe('1')
    // The generation-0 diagnostics are now stale (:37).
    expect(rendered()).toEqual([])
  })

  it('kindOptions that drop the current kind/version show it as not offered without emitting; restoring them re-selects it', () => {
    const host = draftOf('9007199254740993', [field('gauge', { kind_id: 'date', version: '2.0.0', parameters: { format: 'iso' } })])
    const { changes, show } = mount(host, registry, probeAt('9007199254740993', 0))
    const withoutDate2: readonly RecordsKindOption[] = [{ kindId: 'count', version: '1.0.0' }, { kindId: 'text', version: '1.0.0' }, { kindId: 'date', version: '1.0.0' }]
    show(host, withoutDate2, probeAt('9007199254740993', 0))
    expect(optionsOf(1)).toEqual([['', 'date 2.0.0 (not offered)'], ['0', 'count 1.0.0'], ['1', 'text 1.0.0'], ['2', 'date 1.0.0']]); expect(select(1).value).toBe('')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0'); expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind'])

    show(host, [], probeAt('9007199254740993', 0))
    expect(optionsOf(1)).toEqual([['', 'date 2.0.0 (not offered)']]); expect(select(1).value).toBe('')
    expect(input('Field 1 parameter format').value).toBe('iso')

    show(host, registry, probeAt('9007199254740993', 0))
    expect(optionsOf(1)).toEqual(registryOptions); expect(select(1).value).toBe('3')
    expect(changes).toEqual([]); expect(generationOf()).toBe('0'); expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind'])
  })

  // Gap 4: non-empty parameters survive a selector change (:32 spreads the existing kind). The revision rides along byte-exactly,
  // absent stays absent (missing member stays missing, null stays null), and the host's object is never mutated.
  it.each([
    { label: '9007199254740993', revision: '9007199254740993', shown: '9007199254740993' },
    { label: '9223372036854775807', revision: '9223372036854775807', shown: '9223372036854775807' },
    { label: 'absent (no member)', revision: undefined, shown: 'None' },
    { label: 'absent (JSON null)', revision: null, shown: 'None' },
  ])('non-empty parameters and unknown kind members survive a selector change with revision $label', ({ revision, shown }) => {
    const host = draftOf(revision, [field('gauge', { kind_id: 'text', version: '1.0.0', parameters: { max_length: 120, unit: 'mm', nested: { keep: [1, 'two'] } }, future_kind_member: 'kept' }), field('quantity', { kind_id: 'count', version: '1.0.0', parameters: {} })])
    const { changes } = mount(host, registry)
    expect(input('Field 1 parameter max_length').value).toBe('120'); expect(input('Field 1 parameter max_length').type).toBe('number')
    expect(input('Field 1 parameter unit').value).toBe('mm'); expect(input('Field 1 parameter unit').type).toBe('text')
    expect(screen.queryByLabelText('Field 1 parameter nested')).toBeNull()

    choose(1, '2')
    const chosen = bodyOf([field('gauge', { kind_id: 'date', version: '1.0.0', parameters: { max_length: 120, unit: 'mm', nested: { keep: [1, 'two'] } }, future_kind_member: 'kept' }), field('quantity', { kind_id: 'count', version: '1.0.0', parameters: {} })])
    expect(changes).toStrictEqual([{ draft: draftOf(revision, chosen.fields), generation: 1 }])
    expect(changes[0].draft.expectedRevision).toBe(revision); expect('expectedRevision' in changes[0].draft).toBe(revision !== undefined)
    expect(input('Field 1 parameter max_length').value).toBe('120'); expect(input('Field 1 parameter unit').value).toBe('mm'); expect(select(1).value).toBe('2')
    expect(revisionShown()).toBe(shown); expect(generationOf()).toBe('1')
    expect(host.body.fields![0].binding!.kind).toStrictEqual({ kind_id: 'text', version: '1.0.0', parameters: { max_length: 120, unit: 'mm', nested: { keep: [1, 'two'] } }, future_kind_member: 'kept' })

    // A later parameter edit keeps the chosen kind/version.
    fireEvent.change(input('Field 1 parameter unit'), { target: { value: 'cm' } })
    expect(changes).toHaveLength(2); expect(changes[1].generation).toBe(2); expect(changes[1].draft.expectedRevision).toBe(revision)
    expect(changes[1].draft.body.fields![0].binding!.kind).toStrictEqual({ kind_id: 'date', version: '1.0.0', parameters: { max_length: 120, unit: 'cm', nested: { keep: [1, 'two'] } }, future_kind_member: 'kept' })
  })

  // Generation separation and stale-result handling around a selector change (:24-27, :37). Local state only; nothing is written.
  it('a selector change is a local edit: the generation advances, earlier diagnostics go stale, an echo is no reset, and a new host revision waits', () => {
    const host = draftOf('9007199254740993', [field('gauge', { kind_id: 'text', version: '1.0.0', parameters: { unit: 'mm' } })])
    const { changes, show } = mount(host, registry, probeAt('9007199254740993', 0))
    expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind'])

    choose(1, '2')
    expect(changes).toStrictEqual([{ draft: draftOf('9007199254740993', [field('gauge', { kind_id: 'date', version: '1.0.0', parameters: { unit: 'mm' } })]), generation: 1 }])
    expect(generationOf()).toBe('1'); expect(rendered()).toEqual([])

    // The host echoes the edited draft: same identity, version and revision, so it is not a replacement.
    show(changes[0].draft, registry, probeAt('9007199254740993', 0))
    expect(screen.queryByRole('alert')).toBeNull(); expect(select(1).value).toBe('2'); expect(generationOf()).toBe('1'); expect(rendered()).toEqual([])
    show(changes[0].draft, registry, probeAt('9007199254740993', 1))
    expect(rendered()).toEqual(['probe.selector at /fields/0/binding/kind']); expect(changes).toHaveLength(1)

    // A different host revision arrives while the selection is a local edit: it waits behind Keep/Discard.
    show(draftOf('9007199254740994', [field('gauge', { kind_id: 'count', version: '1.0.0', parameters: { unit: 'mm' } })]), registry, probeAt('9007199254740993', 1))
    expect(screen.getByRole('alert')).toBeInTheDocument(); expect(select(1).value).toBe('2'); expect(revisionShown()).toBe('9007199254740993'); expect(generationOf()).toBe('1'); expect(changes).toHaveLength(1)
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.queryByRole('alert')).toBeNull(); expect(select(1).value).toBe('2'); expect(revisionShown()).toBe('9007199254740993'); expect(changes).toHaveLength(1)

    choose(1, '3')
    expect(changes[1]).toStrictEqual({ draft: draftOf('9007199254740993', [field('gauge', { kind_id: 'date', version: '2.0.0', parameters: { unit: 'mm' } })]), generation: 2 })

    const later = draftOf('9007199254740995', [field('gauge', { kind_id: 'count', version: '1.0.0', parameters: { unit: 'mm' } })])
    show(later, registry)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes[2]).toStrictEqual({ draft: draftOf('9007199254740995', [field('gauge', { kind_id: 'count', version: '1.0.0', parameters: { unit: 'mm' } })]), generation: 3 })
    expect(select(1).value).toBe('0'); expect(revisionShown()).toBe('9007199254740995'); expect(generationOf()).toBe('3'); expect(changes).toHaveLength(3)
  })
})
