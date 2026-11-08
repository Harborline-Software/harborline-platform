import { fireEvent, render, screen, within } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { BookingAuthoringEditor } from './BookingAuthoringEditor'
import { BookingAuthoringScreen } from './BookingAuthoringScreen'
import type { BookingCatalogue, BookingDiagnosticSet, BookingDraft, BookingDraftChange } from './BookingDraftTypes'

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
})
