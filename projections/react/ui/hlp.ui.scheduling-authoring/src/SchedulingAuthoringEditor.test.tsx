import { fireEvent, render, screen, within } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import { SchedulingAuthoringEditor } from './SchedulingAuthoringEditor'
import { SchedulingAuthoringScreen } from './SchedulingAuthoringScreen'
import type { SchedulingCatalogue, SchedulingDiagnostic, SchedulingDiagnosticSet, SchedulingDraft, SchedulingDraftChange } from './SchedulingDraftTypes'

// The shared conformance fixture both lanes read. Expected bodies and refusal lines are literals written from the ScheduleDefinition
// grammar, never captured from an editor (T-724 ruling 121).
type FixtureStep = { readonly set: string; readonly value: string } | { readonly click: string }
interface FixtureCase { readonly id: string; readonly input: { readonly draft: SchedulingDraft; readonly steps?: readonly FixtureStep[]; readonly diagnostics?: readonly SchedulingDiagnostic[] }; readonly expected: { readonly body?: Record<string, unknown>; readonly refusalList?: readonly string[]; readonly invalid?: readonly string[] } }
const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.ui.scheduling-authoring/fixtures.yaml'), 'utf8')) as { readonly catalogue: SchedulingCatalogue; readonly cases: readonly FixtureCase[] }
// The producer's own refusal conformance file (L487). The editor never reads it; it is the oracle for what the Refusal list must show.
interface RefusalCase { readonly id: string; readonly definition: Record<string, unknown>; readonly refusals: readonly SchedulingDiagnostic[] }
const refusalFile = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.blocks.scheduling/authoring-refusals.json'), 'utf8')) as { readonly cases: readonly RefusalCase[] }
const fixtureCase = (id: string) => fixture.cases.find(item => item.id === id)!
const catalogue = fixture.catalogue

const editor = (value: SchedulingDraft, options: { readonly readOnly?: boolean; readonly diagnostics?: SchedulingDiagnosticSet } = {}) => {
  const changes: SchedulingDraftChange[] = []
  const onDraftChange = (change: SchedulingDraftChange) => { changes.push(change) }
  const view = render(<SchedulingAuthoringEditor value={value} catalogue={catalogue} diagnostics={options.diagnostics} readOnly={options.readOnly} onDraftChange={onDraftChange} />)
  return { view, changes, onDraftChange, last: () => changes[changes.length - 1] }
}
const type = (label: string, value: string) => fireEvent.change(screen.getByLabelText(label), { target: { value } })
const click = (name: string) => fireEvent.click(screen.getByRole('button', { name }))
// Replays a fixture case's steps through the rendered editor's labelled controls, exactly as the Blazor lane does.
const replay = (steps: readonly FixtureStep[]) => { for (const step of steps) { if ('set' in step) type(step.set, step.value); else click(step.click) } }
const draft = (body: Record<string, unknown>, extra: Partial<SchedulingDraft> = {}): SchedulingDraft => ({ identity: 'schedule.inspection', versionId: '1.0.0', expectedRevision: '3', body, ...extra })
const listed = () => { const list = screen.queryByRole('list', { name: 'Refusal list' }); return list ? within(list).getAllByRole('listitem') : [] }
const announcement = () => screen.getByRole('status', { name: 'Refusal announcements' }).textContent
const controlNames = () => [...document.querySelectorAll('input, select, textarea')].map(control => control.getAttribute('aria-label') ?? '')

describe('hlp.ui.scheduling-authoring (T-491/schedule-editor)', () => {
  it('scheduling-eng-19, scheduling-auth-1, -2, -3, -4, -5, -12, -13: a fixed-calendar Schedule authored from nothing is exactly the fixture body', () => {
    const item = fixtureCase('scheduling-authoring.schedule-from-nothing')
    const { last } = editor(item.input.draft)
    replay(item.input.steps!)

    expect(last().draft.body).toEqual(item.expected.body)
    expect(last().draft.identity).toBe('')
  })

  it('scheduling-eng-19, scheduling-auth-2, -5, -6, -12: an event-anchored chain authored from nothing is exactly the fixture body', () => {
    const item = fixtureCase('scheduling-authoring.chain-from-nothing')
    const { last } = editor(item.input.draft)
    replay(item.input.steps!)

    expect(last().draft.body).toEqual(item.expected.body)
  })

  it('scheduling-bound-1, scheduling-bound-2, scheduling-bound-3, scheduling-bound-4, scheduling-auth-11: a solver backend and the precedence module are named and parameterised from the editor', () => {
    const item = fixtureCase('scheduling-authoring.solver-and-module')
    const { last } = editor(item.input.draft)
    replay(item.input.steps!)

    expect(last().draft.body).toEqual(item.expected.body)
  })

  it('scheduling-auth-27: the Refusal list shows every refusal with its code and pointer, and marks the fields it can place', () => {
    const item = fixtureCase('scheduling-authoring.refusal-list')
    const value = item.input.draft
    editor(value, { diagnostics: { identity: value.identity, expectedRevision: value.expectedRevision, generation: 0, diagnostics: item.input.diagnostics! } })

    expect(listed().map(entry => entry.textContent)).toEqual(item.expected.refusalList)
    for (const field of item.expected.invalid!) expect(screen.getByLabelText(field)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText('Problem class')).not.toHaveAttribute('aria-invalid')
  })

  it('scheduling-auth-27, L487: every refusal case in the scheduling conformance file appears in the Refusal list before publish', () => {
    let refused = 0
    for (const item of refusalFile.cases) {
      const value = draft(item.definition, { identity: `schedule.${item.id}`, expectedRevision: null })
      const diagnostics = item.refusals.map(({ code, pointer }) => ({ code, pointer }))
      const { view } = editor(value, { diagnostics: { identity: value.identity, expectedRevision: null, generation: 0, diagnostics } })

      expect(listed().map(entry => [entry.getAttribute('data-code'), entry.getAttribute('data-pointer')]), item.id).toEqual(item.refusals.map(({ code, pointer }) => [code, pointer]))
      if (item.refusals.length === 0) expect(screen.getByText('No refusals.')).toBeInTheDocument()
      refused += item.refusals.length
      view.unmount()
    }
    expect(refused).toBeGreaterThan(20)
  })

  it('scheduling-auth-27: a polite live region announces only the refusals that newly arrived', () => {
    const value = draft({ name: '', starts: { kind: 'form', target_id: 'forms.inspection' } })
    const { view, onDraftChange } = editor(value)
    expect(screen.getByRole('status', { name: 'Refusal announcements' })).toHaveAttribute('aria-live', 'polite')
    expect(announcement()).toBe('')

    const name = { code: 'scheduling.definition.name_required', pointer: '/name' }
    const due = { code: 'scheduling.definition.due_date_invalid', pointer: '/due_date' }
    view.rerender(<SchedulingAuthoringEditor value={value} catalogue={catalogue} diagnostics={{ identity: value.identity, expectedRevision: '3', generation: 0, diagnostics: [name] }} onDraftChange={onDraftChange} />)
    expect(announcement()).toBe('1 new refusal: scheduling.definition.name_required at /name')
    view.rerender(<SchedulingAuthoringEditor value={value} catalogue={catalogue} diagnostics={{ identity: value.identity, expectedRevision: '3', generation: 0, diagnostics: [name, due] }} onDraftChange={onDraftChange} />)
    expect(announcement()).toBe('1 new refusal: scheduling.definition.due_date_invalid at /due_date')
  })

  it('scheduling-auth-27: refusals for another identity, revision or local generation are stale and never shown', () => {
    const value = draft({ name: '' })
    const refusal = [{ code: 'scheduling.definition.name_required', pointer: '/name' }]
    for (const stale of [{ identity: 'schedule.other', expectedRevision: '3', generation: 0 }, { identity: 'schedule.inspection', expectedRevision: '4', generation: 0 }, { identity: 'schedule.inspection', expectedRevision: '3', generation: 1 }]) {
      const { view } = editor(value, { diagnostics: { ...stale, diagnostics: refusal } })
      expect(listed()).toHaveLength(0)
      expect(screen.getByLabelText('Name')).not.toHaveAttribute('aria-invalid')
      view.unmount()
    }
  })

  it('scheduling-auth-6: each chain node states its scope in text, so scope never rests on colour alone (WCAG 1.4.1)', () => {
    editor(draft({ basis: { kind: 'event_anchored_chain', anchor: 'surgery.completed', nodes: [{ node_id: 'first-check', offset: 'PT4H', tolerance: { early: 'PT0S', late: 'PT0S' } }, { node_id: 'second-check', after_node_id: 'first-check', offset: 'P1D', tolerance: { early: 'PT0S', late: 'PT0S' } }] } }))

    expect(screen.getByText('Node 1 scope: follows the chain anchor surgery.completed')).toBeInTheDocument()
    expect(screen.getByText('Node 2 scope: follows node first-check')).toBeInTheDocument()
    expect(screen.queryByLabelText('Window earliest')).toBeNull()
  })

  it('scheduling-auth-6: a node follows only the anchor or an earlier node, and removing a node keeps the others', () => {
    const { last } = editor(draft({ basis: { kind: 'event_anchored_chain', anchor: 'a', nodes: [{ node_id: 'one', offset: 'PT1H', tolerance: { early: 'PT0S', late: 'PT0S' } }, { node_id: 'two', after_node_id: 'one', offset: 'PT1H', tolerance: { early: 'PT0S', late: 'PT0S' } }] } }))

    expect([...(screen.getByLabelText('Node 1 follows') as HTMLSelectElement).options].map(option => option.value)).toEqual([''])
    expect([...(screen.getByLabelText('Node 2 follows') as HTMLSelectElement).options].map(option => option.value)).toEqual(['', 'one'])
    type('Node 2 follows', '')
    expect(last().draft.body.basis).toEqual({ kind: 'event_anchored_chain', anchor: 'a', nodes: [{ node_id: 'one', offset: 'PT1H', tolerance: { early: 'PT0S', late: 'PT0S' } }, { node_id: 'two', offset: 'PT1H', tolerance: { early: 'PT0S', late: 'PT0S' } }] })
    click('Remove node 1')
    expect(last().draft.body.basis).toEqual({ kind: 'event_anchored_chain', anchor: 'a', nodes: [{ node_id: 'two', offset: 'PT1H', tolerance: { early: 'PT0S', late: 'PT0S' } }] })
  })

  it('scheduling-bound-6: supply is shown read-only from the shared availability runtime and no edit writes a member for it', () => {
    const item = fixtureCase('scheduling-authoring.schedule-from-nothing')
    const { last } = editor(item.input.draft)
    const supply = screen.getByRole('note', { name: 'Supply' })

    expect(supply).toHaveTextContent('Supply is read from the shared availability runtime, from each Resource\'s own availability source, set on the Resource in the Booking editor. It cannot be changed here.')
    expect(supply.querySelectorAll('input, select, textarea, button')).toHaveLength(0)
    replay(item.input.steps!)
    expect(Object.keys(last().draft.body).sort()).toEqual(['basis', 'legality_ref', 'missed_behaviour', 'name', 'on_type_id', 'starts', 'tolerance', 'window'])
  })

  it('scheduling-bound-7: the editor offers no resource, capability or calendar picker; value-domain resolvers are carried to the Protocol and Constraint editors', () => {
    editor(draft({ problem_class: 'Workforce', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {} }, modules: [{ module: 'resource_requirements' }] }))

    expect(controlNames().filter(name => /resource|capabilit|calendar/i.test(name))).toEqual([])
  })

  it('scheduling-auth-11, scheduling-auth-15: a draft from the create action starts at None with a single due date, no solver and no chain chosen', () => {
    const { changes } = editor({ identity: '', versionId: '', body: {} })

    expect(screen.getByLabelText('Problem class')).toHaveDisplayValue('None')
    expect(screen.getByLabelText('Basis')).toHaveDisplayValue('Single due date')
    expect(screen.getByLabelText('Due date')).toBeInTheDocument()
    expect(screen.queryByLabelText('Solver backend')).toBeNull()
    expect(screen.queryByLabelText('Constraint module')).toBeNull()
    expect(screen.queryByLabelText('Chain anchor')).toBeNull()
    expect(changes).toHaveLength(0)
  })

  it('scheduling-auth-11: choosing None removes problem_class, and the seven classes are offered by their names', () => {
    const { last } = editor(draft({ problem_class: 'Project' }))

    expect([...(screen.getByLabelText('Problem class') as HTMLSelectElement).options].map(option => option.value)).toEqual(['', 'Workforce', 'Timetabling', 'ProtocolAdmission', 'Project', 'JobShop', 'Mobility', 'CompositeRecovery'])
    type('Problem class', '')
    expect(last().draft.body).toEqual({})
  })

  it('scheduling-auth-2: a basis replaces the due date, a single due date removes the basis, and a chain removes the window', () => {
    const { last } = editor(draft({ due_date: '2027-01-01T00:00:00Z', window: { earliest: '2026-12-25T00:00:00Z', latest: '2027-01-08T00:00:00Z' } }))

    type('Basis', 'completion_offset')
    expect(last().draft.body).toEqual({ basis: { kind: 'completion_offset' }, window: { earliest: '2026-12-25T00:00:00Z', latest: '2027-01-08T00:00:00Z' } })
    type('Completion offset', 'P30D')
    type('Basis', 'event_anchored_chain')
    expect(last().draft.body).toEqual({ basis: { kind: 'event_anchored_chain' } })
    type('Basis', '')
    expect(last().draft.body).toEqual({})
  })

  it('scheduling-auth-2: a threshold basis takes a reading, a crossing and a decimal value', () => {
    const { last } = editor(draft({}))
    type('Basis', 'threshold')
    type('Threshold reading', 'odometer')
    type('Threshold crossing', 'above')
    type('Threshold value', '10000.5')
    type('Threshold value', 'not a number')

    expect(last().draft.body).toEqual({ basis: { kind: 'threshold', reading: 'odometer', crossing: 'above', value: 10000.5 } })
  })

  it('scheduling-auth-3, scheduling-auth-4: emptying both bounds removes the window and the tolerance', () => {
    const { last } = editor(draft({ window: { earliest: '-P1D', latest: 'P1D' }, tolerance: { early: 'PT0S', late: 'PT1H' } }))
    type('Window earliest', '')
    expect(last().draft.body.window).toEqual({ latest: 'P1D' })
    type('Window latest', '')
    type('Tolerance early', '')
    type('Tolerance late', '')

    expect(last().draft.body).toEqual({})
  })

  it('scheduling-auth-12: changing what it starts clears the target, and targets come from the chosen kind', () => {
    const { last } = editor(draft({ starts: { kind: 'form', target_id: 'forms.inspection' } }))
    type('Starts', 'workflow')

    expect(last().draft.body.starts).toEqual({ kind: 'workflow' })
    expect([...(screen.getByLabelText('Starts target') as HTMLSelectElement).options].map(option => option.value)).toEqual(['', 'workflows.check'])
    type('Starts', '')
    expect(last().draft.body).toEqual({})
  })

  it('scheduling-bound-4: the guarantee offers only what the chosen backend declares; a value it does not offer is shown, not rewritten', () => {
    editor(draft({ problem_class: 'Project', solver: { backend_id: 'prototype.greedy-first-fit.v1', parameters: {}, guarantee: 'optimal_proof' } }))

    expect([...(screen.getByLabelText('Solver guarantee') as HTMLSelectElement).options].map(option => option.textContent)).toEqual(['Not set', 'optimal_proof (not offered)', 'bounded_heuristic'])
    expect(screen.getByLabelText('Solver guarantee')).toHaveValue('optimal_proof')
  })

  it('scheduling-bound-1: changing the backend keeps its parameters, budget and guarantee; Not set removes the solver', () => {
    const { last } = editor(draft({ problem_class: 'Project', solver: { backend_id: 'prototype.greedy-first-fit.v1', parameters: { seed: 1 }, guarantee: 'bounded_heuristic', work_budget: 5 } }))
    type('Solver backend', 'prototype.exhaustive-proof.v2')
    expect(last().draft.body.solver).toEqual({ backend_id: 'prototype.exhaustive-proof.v2', parameters: { seed: 1 }, guarantee: 'bounded_heuristic', work_budget: 5 })
    type('Work budget', '')
    type('Solver guarantee', '')
    expect(last().draft.body.solver).toEqual({ backend_id: 'prototype.exhaustive-proof.v2', parameters: { seed: 1 } })
    type('Solver backend', '')

    expect(last().draft.body).toEqual({ problem_class: 'Project' })
  })

  it('scheduling-bound-1: backend parameters accept a JSON object and leave the draft unchanged for anything else', () => {
    const { last, changes } = editor(draft({ problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {} } }))
    type('Backend parameters', '{"seed": 7}')
    expect(last().draft.body.solver).toEqual({ backend_id: 'prototype.exhaustive-proof.v2', parameters: { seed: 7 } })
    const before = changes.length
    for (const text of ['{"seed":', '[1]', '3', 'null']) type('Backend parameters', text)

    expect(changes).toHaveLength(before)
  })

  it('scheduling-bound-2: a work budget is a whole number; other text leaves the draft unchanged', () => {
    const { last, changes } = editor(draft({ problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {} } }))
    type('Work budget', '12')
    const before = changes.length
    type('Work budget', '1.5')

    expect(changes).toHaveLength(before)
    expect(last().draft.body.solver).toEqual({ backend_id: 'prototype.exhaustive-proof.v2', parameters: {}, work_budget: 12 })
  })

  it('scheduling-bound-2: changing the module drops its parameters, other modules are kept, and removing the last constraint removes the parameters', () => {
    const { last } = editor(draft({ problem_class: 'Project', modules: [{ module: 'precedence', parameters: { constraints: [{ id: 'p', beforeActivityId: 'a', afterActivityId: 'b', minimumGapSlots: 2 }] } }, { module: 'no_overlap' }] }))
    click('Remove precedence constraint 1')
    expect(last().draft.body.modules).toEqual([{ module: 'precedence' }, { module: 'no_overlap' }])
    type('Constraint module', 'time_windows')
    expect(last().draft.body.modules).toEqual([{ module: 'time_windows' }, { module: 'no_overlap' }])
    type('Constraint module', '')

    expect(last().draft.body.modules).toEqual([{ module: 'no_overlap' }])
  })

  it('scheduling-auth-13: the legality rule set is cited by id; one the host does not offer is shown as not offered', () => {
    const { last } = editor(draft({ legality_ref: 'legality.retired' }))

    expect(screen.getByLabelText('Legality rule set')).toHaveDisplayValue('legality.retired (not offered)')
    type('Legality rule set', '')
    expect(last().draft.body).toEqual({})
  })

  it('scheduling-eng-19: unshown members (envelope, grace, escalation) are carried unchanged through every edit', () => {
    const { last } = editor(draft({ envelope: { owner: 'fleet' }, grace: 'P1D', escalation: { after: 'P2D', to: 'role.supervisor' } }))
    type('Name', 'Inspection')

    expect(last().draft.body).toEqual({ envelope: { owner: 'fleet' }, grace: 'P1D', escalation: { after: 'P2D', to: 'role.supervisor' }, name: 'Inspection' })
  })

  it('scheduling-eng-19: read-only disables every control and emits nothing, including Keep and Discard after a new revision arrives', () => {
    const value = draft({ name: 'Inspection' })
    const { view, changes, onDraftChange } = editor(value)
    type('Name', 'Inspection 2')
    view.rerender(<SchedulingAuthoringEditor value={{ ...value, expectedRevision: '4' }} catalogue={catalogue} readOnly onDraftChange={onDraftChange} />)
    const before = changes.length

    for (const control of document.querySelectorAll('input, select, textarea')) expect(control).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Keep edits' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(changes).toHaveLength(before)
  })

  it('scheduling-eng-19: a new revision waits behind Keep or Discard while local edits exist, and replaces a clean draft silently', () => {
    const value = draft({ name: 'Inspection' })
    const { view, changes, onDraftChange, last } = editor(value)
    view.rerender(<SchedulingAuthoringEditor value={{ ...value, expectedRevision: '4', body: { name: 'Clean' } }} catalogue={catalogue} onDraftChange={onDraftChange} />)
    expect(screen.getByLabelText('Name')).toHaveValue('Clean')
    expect(screen.queryByRole('alert')).toBeNull()

    type('Name', 'Mine')
    view.rerender(<SchedulingAuthoringEditor value={{ ...value, expectedRevision: '5', body: { name: 'Theirs' } }} catalogue={catalogue} onDraftChange={onDraftChange} />)
    expect(screen.getByLabelText('Name')).toHaveValue('Mine')
    click('Discard edits')
    expect(last().draft).toEqual({ ...value, expectedRevision: '5', body: { name: 'Theirs' } })
    expect(changes.map(change => change.generation)).toEqual([2, 3])
  })

  it('scheduling-eng-19: adopting a clean replacement clears text a field could not commit, so the field shows the new draft', () => {
    const value = draft({ problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {} } })
    const { view, onDraftChange } = editor(value)
    type('Backend parameters', '{"seed":')
    view.rerender(<SchedulingAuthoringEditor value={{ ...value, expectedRevision: '4' }} catalogue={catalogue} onDraftChange={onDraftChange} />)

    expect(screen.getByLabelText('Backend parameters')).toHaveValue('{}')
  })

  it('scheduling-eng-19: discarding edits for a replacement clears text a field could not commit, so the field shows the new draft', () => {
    const value = draft({ name: 'Inspection', problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {}, work_budget: 5 } })
    const { view, onDraftChange } = editor(value)
    type('Name', 'Mine')
    type('Work budget', 'abc')
    view.rerender(<SchedulingAuthoringEditor value={{ ...value, expectedRevision: '5' }} catalogue={catalogue} onDraftChange={onDraftChange} />)
    click('Discard edits')

    expect(screen.getByLabelText('Work budget')).toHaveValue('5')
  })

  it('scheduling-eng-19: a host draft whose revision is not canonical is refused, never repaired', () => {
    editor(draft({ name: 'Inspection' }, { expectedRevision: '03' }))

    expect(screen.getByText('A host draft was not loaded because its revision is not a canonical decimal string.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Name')).toBeNull()
  })

  it('scheduling-eng-19: the screen renders the editor inside the host shell', () => {
    render(<SchedulingAuthoringScreen shell={{ render: body => <main aria-label="Host shell">{body}</main> }} value={draft({})} catalogue={catalogue} onDraftChange={() => undefined} />)

    expect(within(screen.getByRole('main', { name: 'Host shell' })).getByRole('form', { name: 'Schedule definition draft' })).toBeInTheDocument()
  })
})
