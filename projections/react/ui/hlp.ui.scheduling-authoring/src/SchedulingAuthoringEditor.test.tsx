import { render, screen, fireEvent } from '@testing-library/react'
import { useState } from 'react'
import { describe, it, expect } from 'vitest'
import { SchedulingAuthoringEditor, emptyScheduleDraft, type ScheduleBody } from './index'
import { readFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'

const root = existsSync(resolve(process.cwd(), 'conformance')) ? process.cwd() : resolve(process.cwd(), '../../../..')
const fixtures = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.scheduling-authoring/fixtures.yaml'), 'utf8'))
const catalogue = { types: [{ id: 'types.asset', label: 'Asset' }], legality: [{ id: 'legality.safety', label: 'Safety rules' }], backends: [{ id: 'prototype.exhaustive-proof.v2', label: 'Prototype', guarantees: ['feasible_proof'] }], modules: [{ id: 'precedence', label: 'Precedence', scope: 'chain' as const }] }

describe('Schedule authoring', () => {
  it('scheduling-auth-1 scheduling-auth-12 scheduling-auth-15 scheduling-eng-19: creates a Schedule from nothing', () => {
    let actual: ScheduleBody = emptyScheduleDraft()
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    fireEvent.change(screen.getByLabelText('Schedule name'), { target: { value: 'Inspection' } })
    fireEvent.change(screen.getByLabelText('Starts kind'), { target: { value: 'form' } })
    fireEvent.change(screen.getByLabelText('Starts target'), { target: { value: 'forms.inspection' } })
    fireEvent.change(screen.getByLabelText('Due date'), { target: { value: '2027-01-01T00:00:00Z' } })
    expect(actual).toEqual({ name: 'Inspection', starts: { kind: 'form', target_id: 'forms.inspection' }, due_date: '2027-01-01T00:00:00Z' })
  })
  it('scheduling-auth-15: requests creation without overwriting the displayed definition', () => {
    let created: ScheduleBody | undefined
    render(<SchedulingAuthoringEditor value={{ name: 'Existing' }} onChange={() => { throw new Error('Must not replace existing') }} onCreate={body => { created = body }} />)
    fireEvent.click(screen.getByRole('button', { name: 'Create Schedule' }))
    expect(created).toEqual({})
    expect(screen.getByLabelText('Schedule name')).toHaveValue('Existing')
  })
  it.each([
    ['fixed_calendar', 'Recurrence rule', 'FREQ=DAILY', { kind: 'fixed_calendar', rrule: 'FREQ=DAILY' }],
    ['threshold', 'Reading', 'meter.hours', { kind: 'threshold', reading: 'meter.hours' }],
  ])('scheduling-auth-2: authors %s while preserving host metadata', (kind, label, value, expected) => {
    let actual: ScheduleBody = { due_date: '2027-01-01T00:00:00Z', envelope: { identity: 'inspection' } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    fireEvent.change(screen.getByLabelText('Basis'), { target: { value: kind } })
    fireEvent.change(screen.getByLabelText(String(label)), { target: { value } })
    expect(actual).toEqual({ envelope: { identity: 'inspection' }, basis: expected })
  })
  it('scheduling-auth-2 scheduling-auth-5: edits threshold direction, value and escalation separately', () => {
    let actual: ScheduleBody = { basis: { kind: 'threshold' } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    for (const [label, value] of [['Crossing', 'below'], ['Threshold', '12.5'], ['Missed behaviour', 'record_and_replan'], ['Escalation after', 'PT1H'], ['Escalation to', 'team.operations']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
    expect(actual).toEqual({ basis: { kind: 'threshold', crossing: 'below', value: 12.5 }, missed_behaviour: 'record_and_replan', escalation: { after: 'PT1H', to: 'team.operations' } })
  })
  it('scheduling-bound-1 scheduling-bound-2: preserves unavailable references and refuses malformed parameter editing locally', () => {
    let actual: ScheduleBody = { on_type_id: 'types.retired', solver: { parameters: { seed: 7 } } }
    render(<SchedulingAuthoringEditor value={actual} onChange={next => { actual = next }} />)
    expect(screen.getByRole('option', { name: 'types.retired (not offered)' })).toBeVisible()
    fireEvent.blur(screen.getByLabelText('Backend parameters'), { target: { value: '[]' } })
    expect(screen.getByRole('alert')).toHaveTextContent('Backend parameters must be a JSON object.')
    fireEvent.blur(screen.getByLabelText('Backend parameters'), { target: { value: '{' } })
    expect(screen.getByRole('alert')).toHaveTextContent('Backend parameters must be a JSON object.')
    expect(actual).toEqual({ on_type_id: 'types.retired', solver: { parameters: { seed: 7 } } })
  })
  it('scheduling-bound-6: describes supply read-only and never adds availability to the body', () => {
    render(<SchedulingAuthoringEditor value={{}} onChange={() => {}} />)
    expect(screen.getByText(/shared availability runtime/)).toHaveTextContent("each Resource's own availability source")
    expect(screen.queryByLabelText('Availability source')).toBeNull()
  })
  it.each(fixtures.cases.filter((entry: { steps?: unknown }) => entry.steps))('scheduling-auth-1 scheduling-auth-3 scheduling-auth-4 scheduling-auth-5 scheduling-auth-11 scheduling-auth-13 scheduling-bound-1 scheduling-bound-3 scheduling-bound-4: lane-parity $id', (entry: { steps: string[][]; expected: ScheduleBody }) => {
    let actual: ScheduleBody = {}
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={catalogue} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    for (const [label, value] of entry.steps) fireEvent.change(screen.getByLabelText(label), { target: { value } })
    expect(actual).toEqual(entry.expected)
  })
  it('scheduling-auth-2 scheduling-auth-6: authors a chain with per-node tolerance then replaces the basis', () => {
    let actual: ScheduleBody = {}
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    fireEvent.change(screen.getByLabelText('Basis'), { target: { value: 'event_anchored_chain' } })
    fireEvent.change(screen.getByLabelText('Chain anchor'), { target: { value: 'event.install' } })
    fireEvent.click(screen.getByText('Add node'))
    for (const [label, value] of [['Node 1 id', 'check'], ['Node 1 offset', 'P1D'], ['Node 1 tolerance early', 'PT1H'], ['Node 1 tolerance late', 'PT2H']]) fireEvent.change(screen.getByLabelText(label), { target: { value } })
    expect(actual).toEqual({ basis: { kind: 'event_anchored_chain', anchor: 'event.install', nodes: [{ node_id: 'check', after_node_id: null, offset: 'P1D', tolerance: { early: 'PT1H', late: 'PT2H' } }] } })
    fireEvent.change(screen.getByLabelText('Basis'), { target: { value: 'completion_offset' } })
    fireEvent.change(screen.getByLabelText('Completion offset'), { target: { value: 'P2D' } })
    expect(actual).toEqual({ basis: { kind: 'completion_offset', offset: 'P2D' } })
  })
  it('scheduling-bound-2 scheduling-bound-7: binds one admitted module and parameters without defaulting chain scope', () => {
    let actual: ScheduleBody = {}
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={catalogue} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    expect(screen.getByRole('checkbox')).not.toBeChecked()
    expect(screen.getByText(/explicit opt-in/)).toBeVisible()
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.blur(screen.getByLabelText('Precedence parameters'), { target: { value: '{"constraints":[{"id":"after","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":1}]}' } })
    fireEvent.blur(screen.getByLabelText('Backend parameters'), { target: { value: '{"seed":7}' } })
    expect(actual).toEqual({ modules: [{ module: 'precedence', parameters: { constraints: [{ id: 'after', beforeActivityId: 'a', afterActivityId: 'b', minimumGapSlots: 1 }] } }], solver: { parameters: { seed: 7 } } })
  })
  it('scheduling-auth-27: displays every conformance refusal before publish without reinterpreting it', () => {
    const corpus = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.scheduling/authoring-refusals.json'), 'utf8'))
    for (const entry of corpus.cases) {
      const view = render(<SchedulingAuthoringEditor value={{}} onChange={() => {}} refusals={entry.refusals} />)
      expect(view.container.querySelectorAll('li')).toHaveLength(entry.refusals.length)
      // Generator detail is explicitly open under T-491/generator-refusal-detail.
      if (entry.id !== 'unsupported-configuration') for (const refusal of entry.refusals) expect(view.container.textContent).toContain(refusal.code)
      expect(view.container.querySelector('[aria-live="polite"]')).toHaveAttribute('aria-atomic', 'true')
      expect(view.queryByRole('button', { name: 'Publish' })).toBeNull()
      view.unmount()
    }
  })
})
