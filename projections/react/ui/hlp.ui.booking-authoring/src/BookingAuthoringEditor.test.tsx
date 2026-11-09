import { fireEvent, render, screen, within } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { BookingAuthoringEditor } from './BookingAuthoringEditor'
import { BookingAuthoringScreen } from './BookingAuthoringScreen'
import { hasAcceptableBookingRevision, isCanonicalBookingRevision, type BookingCatalogue, type BookingDiagnosticSet, type BookingDraft, type BookingDraftChange } from './BookingDraftTypes'

// The shared conformance fixture both lanes read. Expected bodies are literals written from the Booking grammar, never captured
// from an editor (T-724 ruling 121); hlp.blocks.calendar.tests admits the same bodies through BookingDefinitionAdmission.
type FixtureStep = { readonly set: string; readonly value: string } | { readonly check: string } | { readonly click: string }
interface FixtureCase { readonly id: string; readonly input: { readonly draft: BookingDraft; readonly steps?: readonly FixtureStep[]; readonly diagnostics?: BookingDiagnosticSet['diagnostics'] }; readonly expected: { readonly body?: Record<string, unknown>; readonly byField?: Record<string, string[]>; readonly unplaced?: string[] } }
const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.ui.booking-authoring/fixtures.yaml'), 'utf8')) as { readonly catalogue: BookingCatalogue; readonly cases: readonly FixtureCase[] }
const fixtureCase = (id: string) => fixture.cases.find(item => item.id === id)!
const catalogue = fixture.catalogue

const editor = (value: BookingDraft, options: { readonly readOnly?: boolean; readonly diagnostics?: BookingDiagnosticSet; readonly catalogue?: BookingCatalogue } = {}) => {
  const changes: BookingDraftChange[] = []
  const view = render(<BookingAuthoringEditor value={value} catalogue={options.catalogue ?? catalogue} diagnostics={options.diagnostics} readOnly={options.readOnly} onDraftChange={change => changes.push(change)} />)
  return { view, changes, last: () => changes[changes.length - 1] }
}
const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })
const check = (label: string) => fireEvent.click(screen.getByLabelText(label))
// Replays a fixture case's steps through the rendered editor's labelled controls, exactly as the Blazor lane does.
const replay = (steps: readonly FixtureStep[]) => { for (const step of steps) { if ('set' in step) type(step.set, step.value); else if ('check' in step) check(step.check); else fireEvent.click(screen.getByRole('button', { name: step.click })) } }
const resourceDraft = (body: Record<string, unknown>, extra: Partial<BookingDraft> = {}): BookingDraft => ({ identity: 'resource.room', versionId: '1.0.0', expectedRevision: '3', body: { kind: 'resource', ...body }, ...extra })

describe('hlp.ui.booking-authoring (T-606/member-editors)', () => {
  it('booking-eng-20, booking-auth-1, -2, -3, -5: a Resource authored from nothing is exactly the fixture body, envelope carried unchanged', () => {
    const item = fixtureCase('booking-authoring.resource-from-nothing')
    const { last } = editor(item.input.draft)
    replay(item.input.steps!)

    expect(last().draft.body).toEqual(item.expected.body)
    expect(last().draft.identity).toBe('')
  })

  it('booking-eng-20, booking-auth-6, -7, -10: a Bookable authored from nothing is exactly the fixture body', () => {
    const item = fixtureCase('booking-authoring.bookable-from-nothing')
    const { last } = editor(item.input.draft)
    replay(item.input.steps!)

    expect(last().draft.body).toEqual(item.expected.body)
  })

  it('booking-eng-20: admission refusals render by field, and a pointer the editor shows no field for is listed with its pointer', () => {
    const item = fixtureCase('booking-authoring.refusals-by-field')
    const draft = item.input.draft
    editor(draft, { diagnostics: { identity: draft.identity, expectedRevision: draft.expectedRevision, generation: 0, diagnostics: item.input.diagnostics! } })

    for (const [field, codes] of Object.entries(item.expected.byField!)) {
      expect(within(screen.getByRole('list', { name: `${field} refusals` })).getAllByRole('listitem').map(entry => entry.textContent)).toEqual(codes)
      expect(screen.getByLabelText(field)).toHaveAttribute('aria-invalid', 'true')
    }
    expect(within(screen.getByRole('list', { name: 'Other refusals' })).getAllByRole('listitem').map(entry => entry.textContent)).toEqual(item.expected.unplaced)
  })

  it('booking-eng-20: refusals for another identity, revision or local generation are stale and never shown', () => {
    const draft = resourceDraft({ name: '' })
    const refusal = [{ code: 'booking.definition.name_required', pointer: '/name' }]
    for (const stale of [{ identity: 'resource.other', expectedRevision: '3', generation: 0 }, { identity: 'resource.room', expectedRevision: '4', generation: 0 }, { identity: 'resource.room', expectedRevision: '3', generation: 1 }]) {
      const { view } = editor(draft, { diagnostics: { ...stale, diagnostics: refusal } })
      expect(screen.queryByRole('list', { name: 'Name refusals' })).toBeNull()
      view.unmount()
    }
  })

  it('read-only disables every control and emits nothing, including Keep and Discard after a new revision arrives', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes } = editor(draft)
    type('Name', 'Room 2')
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '4' }} catalogue={catalogue} readOnly onDraftChange={change => changes.push(change)} />)
    const before = changes.length

    for (const control of screen.getAllByRole('textbox')) expect(control).toBeDisabled()
    for (const control of screen.getAllByRole('combobox')) expect(control).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Keep edits' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Discard edits' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes).toHaveLength(before)
  })

  it('a new identity or revision never silently overwrites local edits: Keep keeps them, Discard loads the host draft', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes, last } = editor(draft)
    type('Name', 'Local name')
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '4', body: { kind: 'resource', name: 'Host name' } }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)

    expect(screen.getByRole('alert')).toHaveTextContent('A different draft or revision arrived while you have local edits.')
    expect(screen.getByLabelText('Name')).toHaveValue('Local name')
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(screen.getByLabelText('Name')).toHaveValue('Host name')
    expect(last().draft.expectedRevision).toBe('4')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('a clean draft is replaced by a new revision without asking, and Keep leaves local edits in place', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes } = editor(draft)
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '4', body: { kind: 'resource', name: 'Replaced' } }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)
    expect(screen.getByLabelText('Name')).toHaveValue('Replaced')
    expect(screen.queryByRole('alert')).toBeNull()

    type('Name', 'Mine')
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '5' }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.getByLabelText('Name')).toHaveValue('Mine')
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('a host draft whose revision is not a canonical decimal string is refused, never repaired', () => {
    const { changes } = editor({ identity: 'resource.room', versionId: '1.0.0', expectedRevision: '03', body: { kind: 'resource' } })
    expect(screen.getByRole('status')).toHaveTextContent('revision is not a canonical decimal string')
    expect(screen.queryByLabelText('Name')).toBeNull()
    expect(changes).toHaveLength(0)
  })

  it('booking-auth-7: clearing the last required resource retains the required empty array', () => {
    const { last } = editor({ identity: 'bookable.x', versionId: '1', body: { kind: 'bookable', requires: ['room'] } })
    check('Required resource Training room')
    expect(last().draft.body.requires).toEqual([])
  })

  it('booking-eng-20: an established kind cannot be changed and its fields stay intact', () => {
    const { changes } = editor(resourceDraft({ from_type_id: 'type.room', availability_from: 'base-hours' }))
    expect(screen.getByLabelText('Definition kind')).toBeDisabled()
    type('Definition kind', 'bookable')
    expect(changes).toHaveLength(0)
    expect(screen.getByLabelText('Availability source')).toHaveValue('base-hours')
  })

  it('booking-auth-1: options take either shape, a current value the host does not offer is shown, and no offer shows a fallback note', () => {
    editor(resourceDraft({ from_type_id: 'type.retired' }))
    const types = screen.getByLabelText('Resource record type')
    expect(within(types).getByRole('option', { name: 'Room' })).toHaveValue('type.room')
    expect(within(types).getByRole('option', { name: 'type.nurse' })).toHaveValue('type.nurse')
    expect(within(types).getByRole('option', { name: 'type.retired (not offered)' })).toHaveValue('type.retired')
    expect(types).toHaveValue('type.retired')
  })

  it('an empty option list shows its fallback note instead of choices', () => {
    editor(resourceDraft({}), { catalogue: {} })
    expect(screen.getByText('No resource record types offered.')).toBeInTheDocument()
    expect(screen.getByText(/No maintenance sources offered/)).toBeInTheDocument()
  })

  it('booking-auth-2: choosing exclusive capacity removes the pool size; pool keeps it', () => {
    const { last } = editor(resourceDraft({ capacity_kind: 'pool', pool_size: 3 }))
    type('Capacity', 'exclusive')
    expect(last().draft.body).toEqual({ kind: 'resource', capacity_kind: 'exclusive' })
    expect(screen.queryByLabelText('Pool size')).toBeNull()
  })

  it('booking-auth-3: an emptied buffer removes the member; text that is not a number leaves the draft unchanged', () => {
    const { last, changes } = editor(resourceDraft({ setup_minutes: 10 }))
    type('Setup minutes', '')
    expect(last().draft.body).toEqual({ kind: 'resource' })
    const before = changes.length
    type('Cleanup minutes', 'ten')
    expect(changes).toHaveLength(before)
  })

  it('booking-auth-5: maintenance is chosen from the record state the host offers, and unchecking the last source removes the member', () => {
    const { last } = editor(resourceDraft({ maintenance_windows: ['field.under_repair'] }))
    check('Maintenance from Out of service')
    expect(last().draft.body.maintenance_windows).toEqual(['field.under_repair', 'field.out_of_service'])
    check('Maintenance from field.under_repair'); check('Maintenance from Out of service')
    expect(last().draft.body).toEqual({ kind: 'resource' })
  })

  it('booking-auth-6: a duration is edited in place and removed, and an invalid duration is not added', () => {
    const { last, changes } = editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable', duration_intervals_minutes: [30, 60] } })
    type('Duration 2 minutes', '45')
    expect(last().draft.body.duration_intervals_minutes).toEqual([30, 45])
    fireEvent.click(screen.getByRole('button', { name: 'Remove duration 1' }))
    expect(last().draft.body.duration_intervals_minutes).toEqual([45])
    const before = changes.length
    type('New duration minutes', ''); fireEvent.click(screen.getByRole('button', { name: 'Add duration' }))
    expect(changes).toHaveLength(before)
  })

  it('booking-auth-7: required resources keep the order they were chosen, and a required id the host does not offer is shown', () => {
    const { last } = editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable', requires: ['pump'] } })
    expect(screen.getByText('pump (not offered)')).toBeInTheDocument()
    check('Required resource instructor'); check('Required resource Training room')
    expect(last().draft.body.requires).toEqual(['pump', 'instructor', 'room'])
  })

  it('booking-auth-10: the waitlist is only a flag; clearing an absent flag writes nothing and clearing true writes false', () => {
    const { last } = editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable', waitlist: true } })
    check('Waitlist')
    expect(last().draft.body.waitlist).toBe(false)
    expect(screen.getByLabelText('Waitlist')).not.toBeChecked()
  })

  it('members the editor does not author are carried unchanged through an edit', () => {
    const body = { kind: 'bookable', envelope: { cascade_layer: 'domain_package' }, book_gate: [{ capability: 'training.book' }], eligibility_expression: 'x == 1', require_all: true, future_member: { a: 1 } }
    const { last } = editor({ identity: 'bookable.x', versionId: '1.0.0', body })
    type('Name', 'Renamed')
    expect(last().draft.body).toEqual({ ...body, name: 'Renamed' })
  })

  it('booking-eng-20: the screen renders the editor inside the host-supplied shell', () => {
    render(<BookingAuthoringScreen shell={{ render: body => <main aria-label="Host shell">{body}</main> }} value={{ identity: '', versionId: '', body: {} }} catalogue={catalogue} onDraftChange={vi.fn()} />)
    expect(within(screen.getByRole('main', { name: 'Host shell' })).getByRole('form', { name: 'Booking definition draft' })).toBeInTheDocument()
  })
  it('booking-eng-20: the revision grammar admits only "0" or [1-9][0-9]* up to Int64.MaxValue, compared as text', () => {
    // Literals from the canonical revision rule shared with Records and Rules (ADR 0096 option and revision shapes).
    const table: readonly (readonly [unknown, boolean])[] = [['0', true], ['7', true], ['10', true], ['9223372036854775806', true], ['9223372036854775807', true], ['999999999999999999', true],
      ['9223372036854775808', false], ['10000000000000000000', false], ['00', false], ['01', false], ['', false], ['1a', false], [' 1', false], ['-1', false], ['/', false], [':', false], [7, false], [null, false]]
    expect(table.map(([text]) => isCanonicalBookingRevision(text))).toEqual(table.map(([, expected]) => expected))
    expect([undefined, null, '3', '03'].map(expectedRevision => hasAcceptableBookingRevision({ expectedRevision }))).toEqual([true, true, true, false])
  })

  it('booking-eng-20: every Resource pointer renders under its own field, and a Bookable member on a Resource is listed with its pointer', () => {
    const pointers: readonly (readonly [string, string])[] = [['/name', 'Name'], ['/from_type_id', 'Resource record type'], ['/capacity_kind', 'Capacity'], ['/pool_size', 'Pool size'], ['/setup_minutes', 'Setup minutes'], ['/cleanup_minutes', 'Cleanup minutes'], ['/maintenance_windows/0', 'Maintenance'], ['/availability_from', 'Availability source']]
    const draft = resourceDraft({ capacity_kind: 'pool' })
    editor(draft, { diagnostics: { identity: draft.identity, expectedRevision: draft.expectedRevision, generation: 0, diagnostics: [...pointers.map(([pointer]) => ({ code: `code${pointer}`, pointer })), { code: 'code.on', pointer: '/on_type_id' }] } })
    for (const [pointer, field] of pointers) expect(within(screen.getByRole('list', { name: `${field} refusals` })).getByRole('listitem')).toHaveTextContent(`code${pointer}`)
    for (const field of ['Name', 'Resource record type', 'Capacity', 'Pool size', 'Setup minutes', 'Cleanup minutes', 'Availability source']) expect(screen.getByLabelText(field)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('group', { name: 'Maintenance' })).toHaveAttribute('aria-invalid', 'true')
    expect(within(screen.getByRole('list', { name: 'Other refusals' })).getAllByRole('listitem').map(item => item.textContent)).toEqual(['code.on at /on_type_id'])
  })

  it('booking-eng-20: every Bookable pointer renders under its own field, and a Resource member on a Bookable is listed with its pointer', () => {
    const pointers: readonly (readonly [string, string])[] = [['/name', 'Name'], ['/on_type_id', 'Offered against record type'], ['/duration_intervals_minutes/1', 'Durations'], ['/requires/0', 'Required resources'], ['/waitlist', 'Waitlist']]
    const draft: BookingDraft = { identity: 'bookable.x', versionId: '1.0.0', expectedRevision: '2', body: { kind: 'bookable' } }
    editor(draft, { diagnostics: { identity: 'bookable.x', expectedRevision: '2', generation: 0, diagnostics: [...pointers.map(([pointer]) => ({ code: `code${pointer}`, pointer })), { code: 'code.pool', pointer: '/pool_size' }, { code: 'code.root', pointer: '' }] } })
    for (const [pointer, field] of pointers) expect(within(screen.getByRole('list', { name: `${field} refusals` })).getByRole('listitem')).toHaveTextContent(`code${pointer}`)
    expect(screen.getByRole('group', { name: 'Durations' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('group', { name: 'Required resources' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Waitlist')).toHaveAttribute('aria-invalid', 'true')
    expect(within(screen.getByRole('list', { name: 'Other refusals' })).getAllByRole('listitem').map(item => item.textContent)).toEqual(['code.pool at /pool_size', 'code.root at '])
  })

  it('booking-eng-20: with nothing refused no field is marked invalid and no refusal list renders', () => {
    const draft = resourceDraft({ capacity_kind: 'pool', pool_size: 2 })
    editor(draft, { diagnostics: { identity: draft.identity, expectedRevision: draft.expectedRevision, generation: 0, diagnostics: [] } })
    for (const field of ['Name', 'Resource record type', 'Capacity', 'Pool size', 'Setup minutes', 'Availability source']) expect(screen.getByLabelText(field)).not.toHaveAttribute('aria-invalid')
    expect(screen.queryAllByRole('list')).toHaveLength(0)
  })

  it('booking-eng-20: each local change reports the next local generation, Discard reports one more, and a discarded draft is clean again', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes } = editor(draft)
    type('Name', 'A'); type('Name', 'B')
    expect(changes.map(change => change.generation)).toEqual([1, 2])
    expect(screen.getByRole('form', { name: 'Booking definition draft' })).toHaveAttribute('data-generation', '2')
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '4' }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes.map(change => change.generation)).toEqual([1, 2, 3])
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '5', body: { kind: 'resource', name: 'Five' } }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByLabelText('Name')).toHaveValue('Five')
    expect(screen.getByRole('form', { name: 'Booking definition draft' })).toHaveAttribute('data-generation', '4')
  })

  it('read-only ignores edits and Keep, so a waiting draft stays waiting', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes } = editor(draft)
    type('Name', 'Mine')
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '4' }} catalogue={catalogue} readOnly onDraftChange={change => changes.push(change)} />)
    type('Name', 'Ignored')
    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(changes).toHaveLength(1)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.getByRole('form', { name: 'Booking definition draft' })).toHaveAttribute('data-read-only', '')
  })

  it('a later host draft with a non-canonical revision is ignored: the current draft stays and nothing is emitted', () => {
    const draft = resourceDraft({ name: 'Room' })
    const { view, changes } = editor(draft)
    view.rerender(<BookingAuthoringEditor value={{ ...draft, expectedRevision: '04', body: { kind: 'resource', name: 'Bad' } }} catalogue={catalogue} onDraftChange={change => changes.push(change)} />)
    expect(screen.getByLabelText('Name')).toHaveValue('Room')
    expect(screen.getByText('A host draft was not loaded because its revision is not a canonical decimal string.')).toHaveAttribute('role', 'status')
    expect(changes).toHaveLength(0)
  })

  it('the header shows unassigned identity, version and revision, and offers a kind only until one is chosen', () => {
    editor({ identity: '', versionId: '', expectedRevision: null, body: {} })
    expect(screen.getByRole('status', { name: 'Identity' })).toHaveTextContent('Not yet assigned')
    expect(screen.getByRole('status', { name: 'Version' })).toHaveTextContent('Not yet assigned')
    expect(screen.getByRole('status', { name: 'Expected revision' })).toHaveTextContent('None')
    expect(within(screen.getByLabelText('Definition kind')).getAllByRole('option').map(option => option.textContent)).toEqual(['Choose a definition kind', 'Resource', 'Bookable'])
    type('Definition kind', 'bookable')
    expect(within(screen.getByLabelText('Definition kind')).getAllByRole('option').map(option => option.textContent)).toEqual(['Resource', 'Bookable'])
  })

  it('an unknown kind is shown as not a Booking kind and is never rewritten', () => {
    const { changes } = editor({ identity: 'x', versionId: '1', body: { kind: 'resource_pool' } })
    expect(within(screen.getByLabelText('Definition kind')).getAllByRole('option').map(option => option.textContent)).toEqual(['resource_pool (not a Booking kind)', 'Resource', 'Bookable'])
    expect(screen.queryByLabelText('Name')).toBeNull()
    type('Definition kind', 'resource_pool')
    expect(changes).toHaveLength(0)
  })

  it('booking-auth-2: clearing capacity removes it and the pool size; choosing pool shows an empty pool size; a stray pool size is still shown', () => {
    const { last, view } = editor(resourceDraft({ capacity_kind: 'pool', pool_size: 3 }))
    type('Capacity', '')
    expect(last().draft.body).toEqual({ kind: 'resource' })
    type('Capacity', 'pool')
    expect(last().draft.body).toEqual({ kind: 'resource', capacity_kind: 'pool' })
    expect(screen.getByLabelText('Pool size')).toHaveValue(null)
    view.unmount()
    editor(resourceDraft({ pool_size: 3 }))
    expect(screen.getByLabelText('Pool size')).toHaveValue(3)
    expect(within(screen.getByLabelText('Capacity')).getAllByRole('option').map(option => option.textContent)).toEqual(['Not set (exclusive)', 'exclusive', 'pool'])
  })

  it('booking-auth-2: a capacity outside the grammar is shown as not a current spelling', () => {
    editor(resourceDraft({ capacity_kind: 'scalar' }))
    expect(screen.getByLabelText('Capacity')).toHaveValue('scalar')
    expect(within(screen.getByLabelText('Capacity')).getByRole('option', { name: 'scalar (not a current spelling)' })).toBeInTheDocument()
  })

  it('booking-auth-6: an empty list shows its note, labels count from one, an invalid edit is a no-op and removing the last duration removes the member', () => {
    const { view } = editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable' } })
    expect(screen.getByText('No durations authored.')).toBeInTheDocument()
    view.unmount()
    const second = editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable', duration_intervals_minutes: [30, 60] } })
    expect(screen.queryByText('No durations authored.')).toBeNull()
    expect(screen.getByLabelText('Duration 1 minutes')).toHaveValue(30)
    expect(screen.getByRole('button', { name: 'Remove duration 2' })).toBeInTheDocument()
    type('Duration 1 minutes', 'x')
    expect(second.changes).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Remove duration 2' })); fireEvent.click(screen.getByRole('button', { name: 'Remove duration 1' }))
    expect(second.last().draft.body).toEqual({ kind: 'bookable' })
    type('New duration minutes', '45'); fireEvent.click(screen.getByRole('button', { name: 'Add duration' }))
    expect(second.last().draft.body).toEqual({ kind: 'bookable', duration_intervals_minutes: [45] })
    expect(screen.getByLabelText('New duration minutes')).toHaveValue(null)
  })

  it('emptying the name writes an empty name; emptying the availability source removes it', () => {
    const { last } = editor(resourceDraft({ name: 'Room', availability_from: 'supply.base-hours' }))
    type('Name', '')
    expect(last().draft.body).toEqual({ kind: 'resource', name: '', availability_from: 'supply.base-hours' })
    type('Availability source', '')
    expect(last().draft.body).toEqual({ kind: 'resource', name: '' })
  })

  it('booking-auth-1, -7: a not-offered note appears only for chosen ids the host does not offer, and an empty choice is never one', () => {
    editor({ identity: 'bookable.x', versionId: '1.0.0', body: { kind: 'bookable', on_type_id: '', requires: ['pump', 'room'] } })
    expect(screen.getAllByText(/\(not offered\)$/).map(note => note.textContent)).toEqual(['pump (not offered)'])
    expect(screen.getByLabelText('Required resource Training room')).toBeChecked()
    expect(screen.getByLabelText('Required resource instructor')).not.toBeChecked()
  })
})
