import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { RecordTypeDraftEditor } from './RecordTypeDraftEditor'
import type { RecordsDiagnosticSet, RecordsDraft, RecordsDraftChange, RecordTypeBody } from './RecordsDraftTypes'

afterEach(cleanup)

const draft = (body: RecordTypeBody = {}): RecordsDraft => ({ identity: 'eam.asset', versionId: '1.0.0', expectedRevision: '9007199254740993', definitionKind: 'Records', body })
const generation = () => screen.getByRole('form', { name: 'Record type draft' }).getAttribute('data-generation')
const mount = (host: RecordsDraft, readOnly = false, diagnostics?: RecordsDiagnosticSet) => {
  const changes: RecordsDraftChange[] = []
  const onDraftChange = (change: RecordsDraftChange) => changes.push(change)
  const view = render(<RecordTypeDraftEditor value={host} kindOptions={[]} readOnly={readOnly} diagnostics={diagnostics} onDraftChange={onDraftChange} />)
  const show = (next: RecordsDraft, nextReadOnly = false, nextDiagnostics?: RecordsDiagnosticSet) => view.rerender(<RecordTypeDraftEditor value={next} kindOptions={[]} readOnly={nextReadOnly} diagnostics={nextDiagnostics} onDraftChange={onDraftChange} />)
  return { changes, show }
}

describe('Record Type literal categories, React lane', () => {
  it.each<[string, RecordTypeBody]>([
    ['absent', {}],
    ['null', { categories: null }],
    ['empty array', { categories: [] }],
  ])('explicit Add on %s creates one empty string without changing the input', (_, body) => {
    const host = draft(body)
    const before = structuredClone(host)
    const { changes } = mount(host)
    expect(changes).toEqual([])
    expect(generation()).toBe('0')
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: [''] } }, generation: 1 }])
    expect(screen.getByLabelText('Category 1')).toHaveValue('')
    expect(host).toStrictEqual(before)
  })

  it('Add appends exactly one empty string after every original node', () => {
    const host = draft({ categories: [' A ', null, { future: [true, 7] }, ' A '], future: { kept: '東京' } })
    const { changes } = mount(host)
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: [' A ', null, { future: [true, 7] }, ' A ', ''], future: { kept: '東京' } } }, generation: 1 }])
    expect(screen.getByLabelText('Category 5')).toHaveValue('')
  })

  it.each<[string, RecordTypeBody]>([['absent', {}], ['null', { categories: null }]])('render and unrelated edits preserve %s categories', (_, body) => {
    const host = draft(body)
    const { changes } = mount(host)
    expect(changes).toEqual([])
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'Renamed' } })
    expect(changes).toStrictEqual([{ draft: { ...host, body: { ...body, name: 'Renamed' } }, generation: 1 }])
    expect(Object.hasOwn(changes[0]?.draft.body ?? {}, 'categories')).toBe(Object.hasOwn(body, 'categories'))
  })

  it.each<[string, unknown]>([
    ['text', 'tag'], ['empty text', ''], ['number', 7], ['false', false], ['object', { future: ['kept'] }],
  ])('a non-array %s stays raw and category mutation is unavailable', (_, categories) => {
    const host = draft({ categories, future: { kept: true } })
    const { changes } = mount(host)
    expect(screen.getByRole('button', { name: 'Add category' })).toBeDisabled()
    expect(screen.queryByLabelText('Category 1')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Remove category 1' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    expect(changes).toEqual([])
    expect(generation()).toBe('0')
    fireEvent.change(screen.getByLabelText('Record type name'), { target: { value: 'Local' } })
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories, future: { kept: true }, name: 'Local' } }, generation: 1 }])
  })

  it('editing a string uses its original array index after malformed slots', () => {
    const host = draft({ categories: [null, { future: 7 }, 'old', false, 'tail'] })
    const { changes } = mount(host)
    expect(screen.queryByLabelText('Category 1')).toBeNull()
    expect(screen.queryByLabelText('Category 2')).toBeNull()
    expect(screen.getByLabelText('Category 1 value')).toHaveTextContent('null')
    expect(screen.getByLabelText('Category 2 value')).toHaveTextContent('{"future":7}')
    fireEvent.change(screen.getByLabelText('Category 3'), { target: { value: 'new' } })
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: [null, { future: 7 }, 'new', false, 'tail'] } }, generation: 1 }])
    expect(screen.getByLabelText('Category 5')).toHaveValue('tail')
  })

  it.each(['', '  ', 'Mémoire 東京', 'duplicate'])('editing retains literal text %j', value => {
    const host = draft({ categories: ['old', 'duplicate'] })
    const { changes } = mount(host)
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value } })
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: [value, 'duplicate'] } }, generation: 1 }])
    expect(screen.getByLabelText('Category 1')).toHaveValue(value)
  })

  it('Remove deletes only the addressed duplicate and keeps relative order', () => {
    const host = draft({ categories: ['same', 'middle', 'same', 'tail'] })
    const { changes } = mount(host)
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 3' }))
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: ['same', 'middle', 'tail'] } }, generation: 1 }])
    expect(screen.getByLabelText('Category 3')).toHaveValue('tail')
  })

  it.each<[string, unknown]>([['null', null], ['number', 3], ['Boolean', true], ['object', { future: 'kept' }]])('Remove can explicitly address a %s slot without filtering any other slot', (_, malformed) => {
    const host = draft({ categories: ['first', malformed, null, 'last'] })
    const { changes } = mount(host)
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 2' }))
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: ['first', null, 'last'] } }, generation: 1 }])
    expect(screen.getByLabelText('Category 2 value')).toHaveTextContent('null')
    expect(screen.getByLabelText('Category 3')).toHaveValue('last')
  })

  it('removing the final category leaves an empty array', () => {
    const host = draft({ categories: ['only'] })
    const { changes } = mount(host)
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 1' }))
    expect(changes).toStrictEqual([{ draft: { ...host, body: { categories: [] } }, generation: 1 }])
  })

  it('category changes copy the complete body and preserve lifecycle metadata and input ownership', () => {
    const host = draft({ name: 'Asset', envelope: { tenant: 'tenant-a', section: 'assets', future: [null, 'literal'] }, fields: [{ field_key: 'serial', future: { nested: true } }], categories: ['old'], future: { nested: ['kept', 4] } })
    const before = structuredClone(host)
    const { changes } = mount(host)
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: ' local ' } })
    expect(changes).toStrictEqual([{ draft: { identity: 'eam.asset', versionId: '1.0.0', expectedRevision: '9007199254740993', definitionKind: 'Records', body: { name: 'Asset', envelope: { tenant: 'tenant-a', section: 'assets', future: [null, 'literal'] }, fields: [{ field_key: 'serial', future: { nested: true } }], categories: [' local '], future: { nested: ['kept', 4] } } }, generation: 1 }])
    expect(changes[0]?.draft).not.toBe(host)
    expect(changes[0]?.draft.body).not.toBe(host.body)
    expect(host).toStrictEqual(before)
  })

  it('each explicit category action advances only local generation once', () => {
    const host = draft({ categories: ['first'] })
    const { changes } = mount(host)
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    fireEvent.change(screen.getByLabelText('Category 2'), { target: { value: 'second' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 1' }))
    expect(changes).toStrictEqual([
      { draft: { ...host, body: { categories: ['first', ''] } }, generation: 1 },
      { draft: { ...host, body: { categories: ['first', 'second'] } }, generation: 2 },
      { draft: { ...host, body: { categories: ['second'] } }, generation: 3 },
    ])
    expect(generation()).toBe('3')
    expect(screen.getByLabelText('Expected revision')).toHaveTextContent('9007199254740993')
  })

  it('read-only category inputs and buttons emit nothing', () => {
    const host = draft({ categories: ['kept', null] })
    const { changes } = mount(host, true)
    expect(screen.getByLabelText('Category 1')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Add category' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Remove category 2' })).toBeDisabled()
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: 'ignored' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 2' }))
    expect(changes).toEqual([])
    expect(generation()).toBe('0')
    expect(host.body).toStrictEqual({ categories: ['kept', null] })
  })

  it.each(['01', '-1'])('an initially rejected revision %s exposes no category editor until an acceptable host arrives', expectedRevision => {
    const { changes, show } = mount({ ...draft({ categories: ['rejected'] }), expectedRevision })
    expect(screen.queryByRole('button', { name: 'Add category' })).toBeNull()
    expect(screen.queryByLabelText('Category 1')).toBeNull()
    expect(changes).toEqual([])
    expect(generation()).toBe('0')
    show(draft({ categories: ['accepted'] }))
    expect(screen.getByLabelText('Category 1')).toHaveValue('accepted')
    expect(changes).toEqual([])
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    expect(changes).toStrictEqual([{ draft: draft({ categories: ['accepted', ''] }), generation: 1 }])
  })

  it('a later rejected host does not replace categories or emit a change', () => {
    const host = draft({ categories: ['accepted'] })
    const { changes, show } = mount(host)
    show({ ...host, expectedRevision: '01', body: { categories: ['rejected'] } })
    expect(screen.getByLabelText('Category 1')).toHaveValue('accepted')
    expect(changes).toEqual([])
    expect(generation()).toBe('0')
  })

  it('a clean incoming revision adopts its complete categories without emitting', () => {
    const { changes, show } = mount(draft({ categories: ['old'] }))
    show({ ...draft({ categories: [null, 'incoming'], future: 9 }), expectedRevision: '9007199254740994' })
    expect(screen.getByLabelText('Category 1 value')).toHaveTextContent('null')
    expect(screen.getByLabelText('Category 2')).toHaveValue('incoming')
    expect(changes).toEqual([])
    expect(generation()).toBe('1')
  })

  it('dirty incoming Keep is silent and Discard emits only the complete later host draft', () => {
    const host = draft({ categories: ['host'] })
    const { changes, show } = mount(host)
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: 'local' } })
    const incoming = { ...draft({ categories: ['incoming'], future: { untouched: true } }), expectedRevision: '9007199254740994' }
    show(incoming)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.getByLabelText('Category 1')).toHaveValue('local')
    expect(changes).toStrictEqual([{ draft: draft({ categories: ['local'] }), generation: 1 }])
    expect(generation()).toBe('1')
    const later = { ...incoming, expectedRevision: '9007199254740995', body: { categories: [false, 'later'], future: { untouched: true } } }
    show(later)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes).toStrictEqual([{ draft: draft({ categories: ['local'] }), generation: 1 }, { draft: later, generation: 2 }])
    expect(screen.getByLabelText('Category 2')).toHaveValue('later')
    expect(generation()).toBe('2')
  })

  it('switching a dirty editor to read-only blocks category changes and pending incoming actions', () => {
    const host = draft({ categories: ['host'] })
    const { changes, show } = mount(host)
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: 'local' } })
    show({ ...host, expectedRevision: '9007199254740994', body: { categories: ['incoming'] } }, true)
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: 'ignored' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add category' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove category 1' }))
    expect(screen.getByRole('button', { name: 'Keep edits' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Discard edits' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes).toStrictEqual([{ draft: draft({ categories: ['local'] }), generation: 1 }])
    expect(generation()).toBe('1')
  })

  it('category controls stay absent until the definition kind is chosen', () => {
    const { changes } = mount({ identity: '', versionId: '', body: { categories: ['kept'] } })
    expect(screen.queryByRole('button', { name: 'Add category' })).toBeNull()
    expect(changes).toEqual([])
  })

  it('category edits invalidate old-generation diagnostics and show only exact matching host diagnostics', () => {
    const host = draft({ categories: ['host'] })
    const diagnostics: RecordsDiagnosticSet = { identity: 'eam.asset', expectedRevision: '9007199254740993', generation: 0, diagnostics: [{ code: 'probe.category', pointer: '/categories/0' }] }
    const { changes, show } = mount(host, false, diagnostics)
    expect(screen.getByRole('list', { name: 'Diagnostics' })).toHaveTextContent('probe.category at /categories/0')
    fireEvent.change(screen.getByLabelText('Category 1'), { target: { value: 'local' } })
    expect(screen.queryByRole('list', { name: 'Diagnostics' })).toBeNull()
    show(host, false, { ...diagnostics, generation: 1 })
    expect(screen.getByRole('list', { name: 'Diagnostics' })).toHaveTextContent('probe.category at /categories/0')
    show(host, false, { ...diagnostics, generation: 1, identity: 'eam.other' })
    expect(screen.queryByRole('list', { name: 'Diagnostics' })).toBeNull()
    show(host, false, { ...diagnostics, generation: 1, expectedRevision: '9007199254740994' })
    expect(screen.queryByRole('list', { name: 'Diagnostics' })).toBeNull()
    show(host, false, { ...diagnostics, generation: 1, expectedRevision: '01' })
    expect(screen.queryByRole('list', { name: 'Diagnostics' })).toBeNull()
    expect(changes).toStrictEqual([{ draft: draft({ categories: ['local'] }), generation: 1 }])
  })
})
