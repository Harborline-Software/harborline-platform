import { render, screen, fireEvent } from '@testing-library/react'
import { useState } from 'react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi } from 'vitest'
import { SchedulingAuthoringEditor, emptyScheduleDraft, type ScheduleBody } from './index'
import * as scheduling from './index'
import { readFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'

const root = existsSync(resolve(process.cwd(), 'conformance')) ? process.cwd() : resolve(process.cwd(), '../../../..')
const fixtures = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.scheduling-authoring/fixtures.yaml'), 'utf8'))
const catalogue = { types: [{ id: 'types.asset', label: 'Asset' }], legality: [{ id: 'legality.safety', label: 'Safety rules' }], backends: [{ id: 'prototype.exhaustive-proof.v2', label: 'Prototype', guarantees: ['feasible_proof'] }], modules: [{ id: 'precedence', label: 'Precedence', scope: 'chain' as const }] }

describe('Schedule authoring', () => {
  // Oracle: literal JSON array positions; holes serialize as null, as required by JSON.stringify.
  it.each([
    [1, '[null]', '[\n  null\n]'],
    [2, '[null,null]', '[\n  null,\n  null\n]'],
  ])('fallback serializer preserves sparse array holes: %s', (length, compact, indented) => {
    const raw = Object.getOwnPropertyDescriptor(JSON, 'rawJSON')!
    try {
      Object.defineProperty(JSON, 'rawJSON', { ...raw, value: undefined })
      const value = new Array(Number(length))
      expect(scheduling.stringifyScheduleBody(value)).toBe(compact)
      expect(scheduling.stringifyScheduleBody(value, 2)).toBe(indented)
    } finally {
      Object.defineProperty(JSON, 'rawJSON', raw)
    }
  })

  // Oracle: literal producer JSON, exact JSON number grammar and the existing numeric-input normalization rule.
  it.each(['no rawJSON', 'no source text', 'neither'])('preserves exact JSON without native support: %s', mode => {
    const nativeParse = JSON.parse
    const raw = Object.getOwnPropertyDescriptor(JSON, 'rawJSON')!
    const isRaw = Object.getOwnPropertyDescriptor(JSON, 'isRawJSON')!
    let actual: ScheduleBody | string = '{"name":"Inspection","basis":{"kind":"threshold","value":9007199254740993},"solver":{"parameters":{"limit":79228162514264337593543950335,"seed":7,"$schedule.number":"keep","quoted":"1e30 9007199254740993","nested":[9007199254740993,true,null,{"rawJSON":"9007199254740993"}]}}}'
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    try {
      if (mode !== 'no source text') {
        Object.defineProperty(JSON, 'rawJSON', { ...raw, value: undefined })
        Object.defineProperty(JSON, 'isRawJSON', { ...isRaw, value: undefined })
      }
      if (mode !== 'no rawJSON') vi.spyOn(JSON, 'parse').mockImplementation((source, reviver) => nativeParse(source, reviver ? function (key, value) { return reviver.call(this, key, value) } : undefined))
      render(<Host />)
      expect(screen.getByLabelText('Threshold')).toHaveValue('9007199254740993')
      expect(screen.getByLabelText('Backend parameters')).toHaveValue(`{
  "limit": 79228162514264337593543950335,
  "seed": 7,
  "$schedule.number": "keep",
  "quoted": "1e30 9007199254740993",
  "nested": [
    9007199254740993,
    true,
    null,
    {
      "rawJSON": "9007199254740993"
    }
  ]
}`)
      fireEvent.change(screen.getByLabelText('Schedule name'), { target: { value: 'Updated' } })
      fireEvent.change(screen.getByLabelText('Backend parameters'), { target: { value: '{"limit":79228162514264337593543950335,"seed":8,"$schedule.number":"keep","quoted":"1e30 9007199254740993","nested":[9007199254740993,true,null,{"rawJSON":"9007199254740993"}]}' } }); fireEvent.blur(screen.getByLabelText('Backend parameters'))
      expect(scheduling.stringifyScheduleBody(actual as ScheduleBody)).toBe('{"name":"Updated","basis":{"kind":"threshold","value":9007199254740993},"solver":{"parameters":{"limit":79228162514264337593543950335,"seed":8,"$schedule.number":"keep","quoted":"1e30 9007199254740993","nested":[9007199254740993,true,null,{"rawJSON":"9007199254740993"}]}}}')
      expect(screen.queryByRole('alert')).toBeNull()
      expect(scheduling.stringifyScheduleBody({ omitted: undefined, zero: 0, empty: [] }, 2)).toBe('{\n  "zero": 0,\n  "empty": []\n}')
      const cyclic: Record<string, ScheduleBody> = {}; cyclic.self = cyclic
      expect(() => scheduling.stringifyScheduleBody(cyclic)).toThrow(TypeError)
      if (mode !== 'no source text') expect(() => JSON.stringify(actual)).toThrow('Use stringifyScheduleBody to serialize exact Schedule numbers on this runtime.')
    } finally {
      vi.restoreAllMocks()
      Object.defineProperty(JSON, 'rawJSON', raw)
      Object.defineProperty(JSON, 'isRawJSON', isRaw)
    }
  })

  // Oracle: interface numericInput keeps rejected edits uncommitted; literal field errors and bodies.
  it.each([
    ['Threshold', 'Work budget', 'Threshold must be an exact decimal with at most 15 significant digits and scale 28.', 'Work budget must be a 32-bit integer in plain decimal notation.', '25', '20'],
    ['Work budget', 'Threshold', 'Work budget must be a 32-bit integer in plain decimal notation.', 'Threshold must be an exact decimal with at most 15 significant digits and scale 28.', '20', '25'],
  ])('numeric errors stay with their own field: %s', (first, second, firstError, secondError, firstValid, secondValid) => {
    let actual: ScheduleBody = { basis: { kind: 'threshold', value: 12.5 }, solver: { parameters: {}, work_budget: 10 } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    fireEvent.change(screen.getByLabelText(first), { target: { value: 'abc' } }); fireEvent.blur(screen.getByLabelText(first))
    fireEvent.blur(screen.getByLabelText(second))
    expect(screen.getByRole('alert')).toHaveTextContent(firstError)
    expect(screen.getByLabelText(first)).toHaveValue('abc')
    expect(actual).toEqual({ basis: { kind: 'threshold', value: 12.5 }, solver: { parameters: {}, work_budget: 10 } })
    fireEvent.change(screen.getByLabelText(second), { target: { value: 'bad' } }); fireEvent.blur(screen.getByLabelText(second))
    expect(screen.getAllByRole('alert').map(alert => alert.textContent).sort()).toEqual([firstError, secondError].sort())
    fireEvent.change(screen.getByLabelText(first), { target: { value: firstValid } }); fireEvent.blur(screen.getByLabelText(first))
    expect(screen.getByRole('alert')).toHaveTextContent(secondError)
    expect(screen.getByLabelText(second)).toHaveValue('bad')
    fireEvent.change(screen.getByLabelText(second), { target: { value: secondValid } }); fireEvent.blur(screen.getByLabelText(second))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(actual).toEqual({ basis: { kind: 'threshold', value: 25 }, solver: { parameters: {}, work_budget: 20 } })
  })

  // Oracle: literal JSON-object errors and original body; duplicate labels still identify different controls.
  it.each([0, 1, 2])('parameter errors stay with their own textarea: %s', first => {
    let actual: ScheduleBody = { solver: { parameters: {} }, modules: [{ module: 'one', parameters: {} }, { module: 'two', parameters: {} }] }
    const modules = [{ id: 'one', label: 'Shared', scope: 'chain' as const }, { id: 'two', label: 'Shared', scope: 'chain' as const }]
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={{ modules }} onChange={next => { actual = next; setBody(next) }} /> }
    const view = render(<Host />)
    const control = (index: number) => view.container.querySelectorAll('textarea')[index]
    const second = (first + 1) % 3
    const errors = ['Backend parameters must be a JSON object.', 'Shared parameters must be a JSON object.', 'Shared parameters must be a JSON object.']
    fireEvent.change(control(first), { target: { value: '{' } }); fireEvent.blur(control(first))
    fireEvent.blur(control(second))
    expect(screen.getByRole('alert')).toHaveTextContent(errors[first])
    expect(control(first)).toHaveValue('{')
    expect(actual).toEqual({ solver: { parameters: {} }, modules: [{ module: 'one', parameters: {} }, { module: 'two', parameters: {} }] })
    fireEvent.change(control(second), { target: { value: '[]' } }); fireEvent.blur(control(second))
    expect(screen.getAllByRole('alert')).toHaveLength(2)
    fireEvent.change(control(first), { target: { value: '{}' } }); fireEvent.blur(control(first))
    expect(screen.getByRole('alert')).toHaveTextContent(errors[second])
    expect(control(second)).toHaveValue('[]')
    fireEvent.change(control(second), { target: { value: '{}' } }); fireEvent.blur(control(second))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(actual).toEqual({ solver: { parameters: {} }, modules: [{ module: 'one', parameters: {} }, { module: 'two', parameters: {} }] })
  })

  // Oracle: optional ScheduleDefinition.Escalation; a present escalation requires both after and to.
  it.each([
    { initial: { after: 'PT1H', to: 'team.operations' }, first: 'Escalation after', last: 'Escalation to', partial: { name: 'Inspection', escalation: { after: '', to: 'team.operations' } } },
    { initial: { after: 'PT1H', to: 'team.operations' }, first: 'Escalation to', last: 'Escalation after', partial: { name: 'Inspection', escalation: { after: 'PT1H', to: '' } } },
    { initial: { after: 'PT1H' }, first: 'Escalation after', last: null, partial: null },
    { initial: { to: 'team.operations' }, first: 'Escalation to', last: null, partial: null },
  ])('scheduling-auth-5: clearing the last escalation value removes the optional object ($first then $last)', ({ initial, first, last, partial }) => {
    let actual: ScheduleBody = { name: 'Inspection', escalation: initial }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)

    fireEvent.change(screen.getByLabelText(first), { target: { value: '' } })
    if (last !== null) {
      expect(actual).toEqual(partial)
      fireEvent.change(screen.getByLabelText(last), { target: { value: '' } })
    }

    expect(actual).toEqual({ name: 'Inspection' })
  })

  // Oracle: ScheduleDefinition.Solver is optional; None names no planner (schedule interface revision 12).
  it('scheduling-bound-1: clearing Backend removes the solver binding for a no-planner Schedule', () => {
    let actual: ScheduleBody = { name: 'Inspection', problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: { seed: 7 }, guarantee: 'feasible_proof', work_budget: 10 } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={catalogue} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)

    fireEvent.change(screen.getByLabelText('Problem class'), { target: { value: 'None' } })
    fireEvent.change(screen.getByLabelText('Backend'), { target: { value: '' } })

    expect(actual).toEqual({ name: 'Inspection', problem_class: 'None' })
  })

  it('scheduling-eng-19: repeated module labels retain unique control identities and exactly one own label', () => {
    const modules = [{ id: 'one', label: 'Shared', scope: 'chain' as const }, { id: 'two', label: 'Shared', scope: 'chain' as const }, { id: 'three', label: 'Backend', scope: 'chain' as const }]
    const view = render(<SchedulingAuthoringEditor value={{ solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {}, guarantee: 'feasible_proof', work_budget: 10 }, modules: [{ module: 'one', parameters: {} }, { module: 'two', parameters: {} }, { module: 'three', parameters: {} }], basis: { kind: 'event_anchored_chain', nodes: [{ node_id: 'same' }, { node_id: 'same' }] } }} catalogue={{ modules }} onChange={() => {}} />)
    // Oracle: HTML ID uniqueness and explicit label association; literal supplied labels.
    const ids = [...view.container.querySelectorAll('[id]')].map(element => element.id)
    expect(new Set(ids).size).toBe(ids.length)
    const textareas = [...view.container.querySelectorAll('textarea')]
    expect(textareas).toHaveLength(4)
    textareas.forEach((control, index) => {
      const ownLabels = [...view.container.querySelectorAll('label')].filter(label => label.htmlFor === control.id)
      expect(ownLabels).toHaveLength(1)
      expect(ownLabels[0].firstChild?.textContent).toBe(['Backend parameters', 'Shared parameters', 'Shared parameters', 'Backend parameters'][index])
      expect(control.labels).toHaveLength(1)
      expect(ownLabels[0].control).toBe(control)
    })
  })
  // Oracle: HTML label element and WAI explicit-label guidance; gallery quality fixture
  // requires reactEqualsBlazor. Window/tolerance labels are direct fieldset children in both lanes.
  it.each(['scheduling-authoring.from-empty', 'scheduling-authoring.bound', 'scheduling-authoring.refusals'])('scheduling-eng-19: %s has direct labels explicitly linked to controls', scenario => {
    const value = scenario === 'scheduling-authoring.bound' ? fixtures.cases[1].expected : {}
    const view = render(<SchedulingAuthoringEditor value={value} catalogue={catalogue} onChange={() => {}} refusals={scenario.endsWith('.refusals') ? [{ code: 'scheduling.definition.name_required', pointer: '/name' }] : []} />)
    for (const legend of ['Validity window', 'On-time tolerance']) {
      const group = screen.getByRole('group', { name: legend })
      expect([...group.children].filter(child => child.tagName !== 'LEGEND').map(child => child.tagName)).toEqual(['LABEL', 'LABEL'])
    }
    for (const control of view.container.querySelectorAll('input,select,textarea')) {
      const label = control.closest('label')!
      expect(label?.tagName).toBe('LABEL')
      expect(control.id).not.toBe('')
      expect(label.htmlFor).toBe(control.id)
      expect(label.control).toBe(control)
    }
  })
  it('scheduling-auth-11 scheduling-bound-1: review R1 leaves a no-planner body unchanged on parameter blur', async () => {
    let actual: ScheduleBody = { name: 'Inspection', starts: { kind: 'form', target_id: 'forms.inspection' }, due_date: '2027-01-01T00:00:00Z' }
    let changes = 0
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { changes++; actual = next; setBody(next) }} /> }
    render(<Host />)
    const user = userEvent.setup()
    screen.getByLabelText('Backend parameters').focus()
    await user.tab()
    expect(actual).toEqual({ name: 'Inspection', starts: { kind: 'form', target_id: 'forms.inspection' }, due_date: '2027-01-01T00:00:00Z' })
    expect(changes).toBe(0)
  })
  // Oracle: slice lines 28/38 and producer wire grammar require parameters to be an object.
  it.each([['Backend', 'prototype.exhaustive-proof.v2', 'backend_id'], ['Guarantee', 'feasible_proof', 'guarantee'], ['Work budget', '10', 'work_budget']])('scheduling-bound-1: first %s edit materialises empty solver parameters', (label, typed, member) => {
    let actual: ScheduleBody = label === 'Guarantee' ? { solver: { backend_id: 'prototype.exhaustive-proof.v2' } } : {}
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={catalogue} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    // Guarantee is available only after a backend is chosen; use a host-supplied partial binding.
    fireEvent.change(screen.getByLabelText(label), { target: { value: typed } })
    fireEvent.blur(screen.getByLabelText(label))
    expect(actual.solver).toEqual({ ...(label === 'Guarantee' ? { backend_id: 'prototype.exhaustive-proof.v2' } : {}), parameters: {}, [member]: label === 'Work budget' ? 10 : typed })
  })
  it('scheduling-bound-4: work budget preserves negative keyboard input and refuses nonfinite text', async () => {
    let actual: ScheduleBody = { solver: { parameters: {}, work_budget: 10 } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    const user = userEvent.setup()
    const budget = screen.getByLabelText('Work budget')
    await user.clear(budget); await user.type(budget, '-1')
    expect(budget).toHaveValue('-1')
    await user.tab()
    expect(actual).toEqual({ solver: { parameters: {}, work_budget: -1 } })
    const edited = screen.getByLabelText('Work budget')
    await user.clear(edited); await user.type(edited, 'abc'); await user.tab()
    expect(actual).toEqual({ solver: { parameters: {}, work_budget: -1 } })
    expect(screen.getByRole('alert')).toHaveTextContent('Work budget must be a 32-bit integer in plain decimal notation.')
  })
  it.each([['12.5', 12.5], ['-12.5', -12.5]])('scheduling-auth-2: review R2 preserves keyboard threshold text %s', async (typed, expected) => {
    let actual: ScheduleBody = { basis: { kind: 'threshold', reading: 'meter.hours', crossing: 'above' } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    const user = userEvent.setup()
    const threshold = screen.getByLabelText('Threshold')
    await user.type(threshold, String(typed))
    expect(threshold).toHaveValue(typed)
    await user.tab()
    expect(actual).toEqual({ basis: { kind: 'threshold', reading: 'meter.hours', crossing: 'above', value: expected } })
  })
  it('scheduling-auth-2 scheduling-auth-3: review R3 removes the optional window after choosing a chain', () => {
    let actual: ScheduleBody = { name: 'Inspection', basis: { kind: 'completion_offset', offset: 'P2D' }, window: { earliest: 'PT0S', latest: 'P1D' } }
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} onChange={next => { actual = next; setBody(next) }} /> }
    render(<Host />)
    fireEvent.change(screen.getByLabelText('Basis'), { target: { value: 'event_anchored_chain' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remove validity window' }))
    expect(actual).toEqual({ name: 'Inspection', basis: { kind: 'event_anchored_chain' } })
    expect(screen.getByLabelText('Window earliest')).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Remove validity window' })).toBeNull()
  })
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
    for (const [label, value] of [['Crossing', 'below'], ['Threshold', '12.5'], ['Missed behaviour', 'record_and_replan'], ['Escalation after', 'PT1H'], ['Escalation to', 'team.operations']]) { fireEvent.change(screen.getByLabelText(label), { target: { value } }); fireEvent.blur(screen.getByLabelText(label)) }
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
  // Oracle: literal fixture bodies, optional producer members and the specified numeric-input error text.
  it.each(fixtures.cases.filter((entry: { steps?: unknown }) => entry.steps))('scheduling-auth-1 scheduling-auth-3 scheduling-auth-4 scheduling-auth-5 scheduling-auth-6 scheduling-auth-11 scheduling-auth-13 scheduling-bound-1 scheduling-bound-3 scheduling-bound-4: lane-parity $id', (entry: { initial?: ScheduleBody; initialJson?: string; steps: string[][]; expected?: ScheduleBody; expectedJson?: string; expectedChanges?: number; expectedEditedThresholdJson?: string; error?: string; expectedThresholdWire?: string[]; expectedThresholdDisplay?: string; unchangedThresholdEdit?: boolean }) => {
    let actual: ScheduleBody | string = entry.initialJson ?? entry.initial ?? {}
    let changes = 0
    function Host() { const [body, setBody] = useState(actual); return <SchedulingAuthoringEditor value={body} catalogue={catalogue} onChange={next => { changes++; actual = next; setBody(next) }} /> }
    render(<Host />)
    if (entry.unchangedThresholdEdit) expect(screen.getByLabelText('Threshold')).toHaveValue(entry.expectedThresholdDisplay)
    for (const [label, value, action] of entry.steps) {
      if (action === 'expect-error') { expect(screen.getByRole('alert')).toHaveTextContent(value); continue }
      if (action === 'expect-no-errors') { expect(screen.queryByRole('alert')).toBeNull(); continue }
      if (action === 'toggle') fireEvent.click(screen.getByLabelText(label))
      else if (action === 'click') fireEvent.click(screen.getByRole('button', { name: label }))
      else { fireEvent.change(screen.getByLabelText(label), { target: { value } }); fireEvent.blur(screen.getByLabelText(label)) }
      if (entry.error) {
        expect(screen.getByRole('alert')).toHaveTextContent(entry.error)
        expect(actual).toEqual(entry.expected)
        expect(changes).toBe(0)
      }
    }
    if (entry.expectedJson) expect(JSON.stringify(actual)).toBe(entry.expectedJson)
    else expect(actual).toStrictEqual(entry.expected)
    if (entry.expectedThresholdDisplay) {
      expect(screen.getByLabelText('Threshold')).toHaveValue(entry.expectedThresholdDisplay)
      if (entry.unchangedThresholdEdit) expect(changes).toBe(entry.expectedChanges ?? 0)
      else {
        const edit = entry.expectedThresholdDisplay + (entry.expectedThresholdDisplay.includes('.') ? '0' : '.0')
        fireEvent.change(screen.getByLabelText('Threshold'), { target: { value: edit } })
        fireEvent.blur(screen.getByLabelText('Threshold'))
      }
      expect(screen.queryByRole('alert')).toBeNull()
      expect(screen.getByLabelText('Threshold')).toHaveValue(entry.expectedThresholdDisplay)
      if (entry.expectedJson) expect(JSON.stringify(actual)).toBe(entry.expectedJson)
      else expect(actual).toStrictEqual(entry.expected)
    }
    if (entry.expectedThresholdWire) expect(entry.expectedThresholdWire).toContain(JSON.stringify(((actual as ScheduleBody).basis as ScheduleBody).value))
    if (entry.expectedEditedThresholdJson) {
      fireEvent.change(screen.getByLabelText('Threshold'), { target: { value: '25' } }); fireEvent.blur(screen.getByLabelText('Threshold'))
      expect(screen.getByLabelText('Threshold')).toHaveValue('25')
      expect(JSON.stringify(actual)).toBe(entry.expectedEditedThresholdJson)
      expect(screen.queryByRole('alert')).toBeNull()
    }
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
      // Renderer oracle: supplied corpus payload, including the open generator case;
      // this pins faithful display, not which refusal the producer should generate.
      entry.refusals.forEach((refusal: { code: string; pointer: string; target?: string; detail?: unknown }, index: number) => {
        const item = view.container.querySelectorAll('li')[index]
        expect(item.querySelectorAll('code')[0]).toHaveTextContent(refusal.code)
        expect(item.querySelectorAll('code')[1]).toHaveTextContent(refusal.pointer)
        if (refusal.target) expect(item.querySelector('span')?.textContent?.trim()).toBe(refusal.target)
        if (refusal.detail !== undefined) expect(JSON.parse(item.querySelector('pre')!.textContent!)).toEqual(refusal.detail)
      })
      expect(view.container.querySelector('[aria-live="polite"]')).toHaveAttribute('aria-atomic', 'true')
      expect(view.queryByRole('button', { name: 'Publish' })).toBeNull()
      view.unmount()
    }
  })
})
