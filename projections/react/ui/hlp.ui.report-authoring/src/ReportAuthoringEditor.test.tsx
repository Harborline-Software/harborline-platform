import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ReportAuthoringEditor, type ReportDraft } from './ReportAuthoringEditor'
import { ReportAuthoringScreen } from './ReportAuthoringScreen'

it('reports-auth-12 creates a report from nothing', () => {
  const create = vi.fn()
  render(<ReportAuthoringEditor value={null} onCreate={create} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Create report' }))
  expect(create).toHaveBeenCalledWith({ tenant: '', key: '', version: '', measures: [], parameters: [] })
})

it('reports-auth-13 refreshes the record-query resolver on every open', async () => {
  let options = ['finance.balance']
  const resolveMeasures = vi.fn(async () => options)
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} resolveMeasures={resolveMeasures} onCreate={() => {}} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  expect(await screen.findByRole('option', { name: 'finance.balance' })).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Close measure picker' }))
  options = ['finance.balance', 'finance.rent']
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  expect(await screen.findByRole('option', { name: 'finance.rent' })).toBeInTheDocument()
  expect(resolveMeasures).toHaveBeenCalledTimes(2)
})

it('reports-bound-9 resolves each typeahead change and renders an empty result', async () => {
  const resolver = vi.fn(async (_query: string) => [] as string[])
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} resolveMeasures={resolver} onCreate={() => {}} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('listbox', { name: 'Measures' })
  fireEvent.change(screen.getByLabelText('Search measures'), { target: { value: 'rent' } })
  expect(resolver).toHaveBeenLastCalledWith('rent')
  expect(screen.getByRole('listbox', { name: 'Measures' }).querySelectorAll('option')).toHaveLength(0)
})

it('reports-bound-1 binds a measure selected from the resolver', async () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} resolveMeasures={async () => ['finance.rent']} onCreate={() => {}} onChange={onChange} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add measure' }))
  expect(onChange).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.rent' }] })
})

it('reports-auth-15 renders the unknown measure refusal at its field', () => {
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.missing' }] }} onCreate={() => {}} onChange={() => {}} refusals={[{ code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.missing', catalogue: 'Main catalogue' }]} />)
  expect(screen.getByLabelText('Measure 1 refusal')).toHaveTextContent('report.measure_unknown finance.missing Main catalogue')
})

it('reports-auth-8 authors the closed typed parameter defaults', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [] }} onCreate={() => {}} onChange={onChange} />)
  const type = screen.getByLabelText('Parameter type')
  expect(Array.from(type.querySelectorAll('option')).map(x => x.value)).toEqual(['boolean', 'date_time', 'integer', 'float', 'string'])
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'limit' } })
  fireEvent.change(type, { target: { value: 'integer' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: '42' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(onChange).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [], parameters: [{ name: 'limit', type: 'integer', default: { kind: 'integer', value: 42, type: 'integer', is_valid: true } }] })
})

it('reports-auth-9 binds as_at to an effective-dated type', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [] }} onCreate={() => {}} onChange={onChange} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'as_at' } })
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: 'date_time' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: '2026-01-01T00:00:00Z' } })
  fireEvent.change(screen.getByLabelText('Effective-dated type'), { target: { value: 'lease' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(onChange).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [], parameters: [{ name: 'as_at', type: 'date_time', default: { kind: 'datetime', value: '2026-01-01T00:00:00Z', type: 'date_time', is_valid: true }, effective_dated_type: 'lease' }] })
})

it('reports-auth-22 renders a parameter refusal at the parameter field', () => {
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [{ name: 'bad', type: 'string', default: null }] } as unknown as ReportDraft} onCreate={() => {}} onChange={() => {}} refusals={[{ code: 'report.parameter_default_type_mismatch', pointer: '/parameters/0' }]} />)
  expect(screen.getByLabelText('Parameter 1 refusal')).toHaveTextContent('report.parameter_default_type_mismatch')
})

it('readOnly prevents changes while displaying existing values', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.rent' }] }} onCreate={() => {}} onChange={onChange} readOnly />)
  expect(screen.getByText('finance.rent')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Add parameter' })).toBeDisabled()
  expect(screen.getByLabelText('Parameter name')).toBeDisabled()
})

it.each([
  ['boolean', 'true', true], ['date_time', '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'],
  ['integer', '42', 42], ['float', '2.5', 2.5], ['string', 'hello', 'hello'],
])('reports-auth-8 writes %s defaults in the closed grammar', (kind, input, expected) => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [] }} onCreate={() => {}} onChange={onChange} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'p' } })
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: kind } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: input } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(onChange).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [], parameters: [{ name: 'p', type: kind, default: { kind: kind === 'date_time' ? 'datetime' : kind, value: expected, type: kind, is_valid: true } }] })
})

it('identity and revision change clears pending parameter edits', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.old' }] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'other', version: '2', measures: [{ measure: 'finance.new' }] }} />)
  expect(screen.getByText('finance.old')).toBeInTheDocument()
  expect(screen.getByLabelText('Parameter name')).toHaveValue('pending')
  expect(screen.getByLabelText('Report key')).toHaveValue('r')
  expect(screen.getByRole('button', { name: 'Add parameter' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Discard edits and load' }))
  expect(screen.getByLabelText('Parameter name')).toHaveValue('')
})

it('reports-auth-22 refuses an untyped default before submitting', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [] }} onCreate={() => {}} onChange={onChange} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'p' } })
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: 'integer' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: 'not a number' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(onChange).not.toHaveBeenCalled()
  expect(screen.getByRole('alert')).toHaveTextContent('Enter a valid default')
})

it('missing resolver presents an empty picker without an error', () => {
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} onCreate={() => {}} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  expect(screen.getByRole('listbox', { name: 'Measures' }).querySelectorAll('option')).toHaveLength(0)
  expect(screen.queryByRole('alert')).toBeNull()
})

it('a pending draft can be kept when the host changes revision', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...props.value, version: '2' }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(props.onChange).toHaveBeenCalledWith(expect.objectContaining({ version: '1' }))
})

it('reports-auth-8 reports-auth-9 reports-bound-1 authors the shared conformance definition', async () => {
  const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.ui.report-authoring/definition.json'), 'utf8'))
  let authored: ReportDraft = { tenant: 'tenant-r', key: 'report-a', version: '1.0.0', measures: [], parameters: [] }
  const view = render(<ReportAuthoringEditor value={authored} onCreate={() => {}} onChange={next => { authored = next }} />)
  for (const row of [
    ['enabled', 'boolean', 'true', ''], ['as_at', 'date_time', '2026-01-01T00:00:00+00:00', 'lease'],
    ['count', 'integer', '42', ''], ['ratio', 'float', '2.5', ''], ['label', 'string', 'hello', ''],
  ]) {
    fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: row[0] } })
    fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: row[1] } })
    fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: row[2] } })
    if (row[3]) fireEvent.change(screen.getByLabelText('Effective-dated type'), { target: { value: row[3] } })
    fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
    view.rerender(<ReportAuthoringEditor value={authored} onCreate={() => {}} onChange={next => { authored = next }} />)
  }
  view.rerender(<ReportAuthoringEditor value={authored} resolveMeasures={async () => ['finance.rent']} onCreate={() => {}} onChange={next => { authored = next }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add measure' }))
  expect(authored).toEqual(fixture)
})

it('reports-auth-12 edits identity coordinates on a newly created report', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: '', key: '', version: '', measures: [], parameters: [] }} onCreate={() => {}} onChange={onChange} />)
  fireEvent.change(screen.getByLabelText('Report key'), { target: { value: 'rent-roll' } })
  expect(onChange).toHaveBeenCalledWith({ tenant: '', key: 'rent-roll', version: '', measures: [], parameters: [] })
})

it('reports-auth-8 removes a parameter declaration', () => {
  const onChange = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [], parameters: [{ name: 'p', type: 'string', default: { kind: 'string', value: 'x', type: 'string', is_valid: true } }] }} onCreate={() => {}} onChange={onChange} />)
  fireEvent.click(screen.getByRole('button', { name: 'Remove parameter p' }))
  expect(onChange).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [], parameters: [] })
})

it('the host shell wraps the report editor', () => {
  render(<ReportAuthoringScreen value={null} onCreate={() => {}} onChange={() => {}} shell={{ render: body => <main>{body}</main> }} />)
  expect(screen.getByRole('main')).toContainElement(screen.getByRole('button', { name: 'Create report' }))
})

it('reports-bound-9 reports a resolver failure without treating it as an empty result', async () => {
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} resolveMeasures={async () => { throw new Error('unavailable') }} onCreate={() => {}} onChange={() => {}} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Measures could not be loaded')
})

it('pending edits kept on the old revision accumulate after successive changes', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [], parameters: [] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'first' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...props.value, version: '2' }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'second' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(props.onChange.mock.lastCall?.[0].parameters.map((p: {name: string}) => p.name)).toEqual(['first', 'second'])
})

it('reports-auth-12 readOnly permits discard but disables keeping edits', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} readOnly value={{ ...props.value, version: '2' }} />)
  expect(screen.getByRole('button', { name: 'Discard edits and load' })).toBeEnabled()
  expect(screen.getByRole('button', { name: 'Keep editing' })).toBeDisabled()
})

it('reports-bound-9 presents a host labelled option while binding its path', async () => {
  const changed = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} resolveMeasures={async () => [{ id: 'finance.rent', label: 'Rent' }]} onCreate={() => {}} onChange={changed} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'Rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add measure' }))
  expect(changed).toHaveBeenCalledWith({ tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.rent' }] })
})

it('identity replacement closes the old measure picker after discarding edits', async () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [] }, resolveMeasures: async () => ['finance.old'], onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.old' })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...props.value, key: 'other' }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Discard edits and load' }))
  expect(screen.queryByLabelText('Measures')).toBeNull()
})

it('pending replacement hides refusals belonging to the incoming revision', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.old' }] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...props.value, version: '2', measures: [{ measure: 'finance.new' }] }} refusals={[{ code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.new', catalogue: 'Main' }]} />)
  expect(screen.queryByLabelText('Measure 1 refusal')).toBeNull()
})

it('the report screen renders without a host shell', () => {
  render(<ReportAuthoringScreen value={null} onCreate={() => {}} onChange={() => {}} />)
  expect(screen.getByRole('button', { name: 'Create report' })).toBeInTheDocument()
})

it('a new host revision of the same definition waits for pending edits', () => {
  const value = { tenant: 't', key: 'r', version: '1.0.0', measures: [] }
  const props = { value, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} revision="1" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} revision="2" />)
  expect(screen.getByRole('button', { name: 'Add parameter' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Discard edits and load' }))
  expect(screen.getByLabelText('Parameter name')).toHaveValue('')
})

it('a created draft fences a later host revision when edits are pending', () => {
  const value = { tenant: 't', key: 'r', version: '1.0.0', measures: [] }
  const props = { onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} value={null} revision={null} />)
  view.rerender(<ReportAuthoringEditor {...props} value={value} revision="1" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={value} revision="2" />)
  expect(screen.getByRole('button', { name: 'Add parameter' })).toBeDisabled()
})

it('reports-auth-8 same-revision host echo does not overwrite the current draft', () => {
  const initial = { tenant: 't', key: 'r', version: '1', measures: [] }
  const props = { onCreate: () => {}, onChange: () => {} }
  const view = render(<ReportAuthoringEditor {...props} value={initial} revision="a" />)
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...initial, measures: [{ measure: 'finance.rent' }] }} revision="a" />)
  expect(screen.queryByText('finance.rent')).not.toBeInTheDocument()
})

it('reports-bound-9 fences a selected measure when the report is replaced', async () => {
  const props = { onCreate: () => {}, onChange: () => {}, resolveMeasures: async () => ['finance.rent'] }
  const view = render(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'r', version: '1', measures: [] }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'next', version: '1', measures: [] }} />)
  expect(screen.getByRole('button', { name: 'Discard edits and load' })).toBeInTheDocument()
})

it('reports-auth-12 keeps subsequent edits across repeated replacement decisions', () => {
  const value = { tenant: 't', key: 'r', version: '1', measures: [] }
  const props = { onCreate: () => {}, onChange: () => {} }
  const view = render(<ReportAuthoringEditor {...props} value={value} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'first' } })
  view.rerender(<ReportAuthoringEditor {...props} value={value} revision="b" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  view.rerender(<ReportAuthoringEditor {...props} value={value} revision="c" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  expect(screen.getByText('first')).toBeInTheDocument()
})

it('reports-auth-12 clears pending input when the report is removed', () => {
  const props = { onCreate: () => {}, onChange: () => {} }
  const view = render(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'old', version: '1', measures: [] }} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'old input' } })
  view.rerender(<ReportAuthoringEditor {...props} value={null} />)
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'new', version: '1', measures: [] }} />)
  expect(screen.getByLabelText('Parameter name')).toHaveValue('')
})

it('reports-auth-8 refuses a datetime outside the Reports wire format', () => {
  const change = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} onCreate={() => {}} onChange={change} />)
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: 'date_time' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: '2026-01-01 00:00:00Z' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(change).not.toHaveBeenCalled()
})

it('reports-auth-12 accepts an echoed identity edit while parameter input is pending', () => {
  const original = { tenant: 't', key: 'old', version: '1', measures: [] }
  const change = vi.fn()
  const props = { onCreate: () => {}, onChange: change }
  const view = render(<ReportAuthoringEditor {...props} value={original} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  fireEvent.change(screen.getByLabelText('Report key'), { target: { value: 'new' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...original, key: 'new' }} revision="a" />)
  expect(screen.queryByRole('button', { name: 'Keep editing' })).not.toBeInTheDocument()
})

it('reports-bound-9 closes the picker on a clean replacement while a lookup is in flight', async () => {
  let finish!: (value: string[]) => void
  const resolver = () => new Promise<string[]>(resolve => { finish = resolve })
  const props = { onCreate: () => {}, onChange: () => {}, resolveMeasures: resolver }
  const view = render(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'old', version: '1', measures: [] }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'new', version: '1', measures: [] }} />)
  finish(['finance.old'])
  expect(screen.queryByRole('listbox', { name: 'Measures' })).not.toBeInTheDocument()
})

it('reports-auth-12 decision open preserves the retained report after picker query changes', async () => {
  const props = { onCreate: () => {}, onChange: () => {}, resolveMeasures: async () => ['finance.rent'] }
  const old = { tenant: 't', key: 'old', version: '1', measures: [] }
  const view = render(<ReportAuthoringEditor {...props} value={old} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...old, key: 'new' }} />)
  fireEvent.change(screen.getByLabelText('Search measures'), { target: { value: 'x' } })
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  expect(screen.getByLabelText('Report key')).toHaveValue('old')
})

it('reports-auth-12 keeps fencing a retained draft when a further host replacement arrives', () => {
  const props = { onCreate: () => {}, onChange: () => {} }
  const old = { tenant: 't', key: 'old', version: '1', measures: [] }
  const view = render(<ReportAuthoringEditor {...props} value={old} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...old, key: 'new' }} revision="b" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: '' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...old, key: 'third' }} revision="c" />)
  expect(screen.getByRole('button', { name: 'Discard edits and load' })).toBeInTheDocument()
})

it('reports-auth-12 keeps retained snapshot when an identity echo arrives while kept', () => {
  const props = { onCreate: () => {}, onChange: () => {} }
  const original = { tenant: 't', key: 'old', version: '1', measures: [] }
  const view = render(<ReportAuthoringEditor {...props} value={original} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...original, key: 'new' }} revision="b" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Report key'), { target: { value: 'edited' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...original, key: 'edited', measures: [{ measure: 'host.other' }] }} revision="b" />)
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...original, key: 'third' }} revision="c" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  expect(screen.queryByText('host.other')).not.toBeInTheDocument()
})

it('reports-bound-9 tenant edit invalidates old tenant lookup', async () => {
  let finish!: (value: string[]) => void
  const props = { onCreate: () => {}, onChange: vi.fn(), resolveMeasures: () => new Promise<string[]>(r => { finish = r }) }
  const view = render(<ReportAuthoringEditor {...props} value={{ tenant: 'old', key: 'r', version: '1', measures: [] }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  fireEvent.change(screen.getByLabelText('Tenant'), { target: { value: 'new' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 'new', key: 'r', version: '1', measures: [] }} />)
  finish(['finance.old'])
  expect(screen.queryByLabelText('Measures')).not.toBeInTheDocument()
})

it('reports-auth-8 parameter echo of kept draft does not reopen replacement', () => {
  const props = { onCreate: () => {}, onChange: vi.fn() }
  const old = { tenant: 't', key: 'old', version: '1', measures: [], parameters: [] }
  const view = render(<ReportAuthoringEditor {...props} value={old} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'limit' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...old, key: 'new' }} revision="b" />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: 'true' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  const emitted = props.onChange.mock.lastCall![0]
  view.rerender(<ReportAuthoringEditor {...props} value={emitted} revision="b" />)
  expect(screen.queryByRole('button', { name: 'Keep editing' })).not.toBeInTheDocument()
})

it('reports-auth-15 hides held replacement refusals after keeping until the host reconciles', () => {
  const old = { tenant: 't', key: 'old', version: '1', measures: [{ measure: 'finance.old' }] }
  const props = { onCreate: () => {}, onChange: () => {} }
  const refusal = [{ code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.new', catalogue: 'Main' }]
  const view = render(<ReportAuthoringEditor {...props} value={old} revision="a" />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ ...old, key: 'new' }} revision="b" refusals={refusal} />)
  fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))
  expect(screen.queryByLabelText('Measure 1 refusal')).not.toBeInTheDocument()
  view.rerender(<ReportAuthoringEditor {...props} value={old} revision="a" refusals={refusal} />)
  expect(screen.getByLabelText('Measure 1 refusal')).toHaveTextContent('report.measure_unknown')
})

it.each(['2026-02-30T00:00:00Z', '2026-02-29T00:00:00Z', '2026-04-31T00:00:00Z', '2100-02-29T00:00:00Z', '2026-01-01T00:00:00+14:01'])('reports-auth-8 refuses the impossible calendar datetime %s', text => {
  const change = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} onCreate={() => {}} onChange={change} />)
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: 'date_time' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: text } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(change).not.toHaveBeenCalled()
  expect(screen.getByRole('alert')).toHaveTextContent('Enter a valid default.')
})

it.each(['2028-02-29T00:00:00Z', '2000-02-29T00:00:00Z', '2026-12-31T23:59:59+14:00'])('reports-auth-8 accepts the real calendar datetime %s', text => {
  const change = vi.fn()
  render(<ReportAuthoringEditor value={{ tenant: 't', key: 'r', version: '1', measures: [] }} onCreate={() => {}} onChange={change} />)
  fireEvent.change(screen.getByLabelText('Parameter type'), { target: { value: 'date_time' } })
  fireEvent.change(screen.getByLabelText('Parameter default'), { target: { value: text } })
  fireEvent.click(screen.getByRole('button', { name: 'Add parameter' }))
  expect(change.mock.lastCall![0].parameters[0].default).toEqual({ kind: 'datetime', value: text, type: 'date_time', is_valid: true })
})

it('reports-auth-15 removing a declaration hides the old verdict until fresh refusals arrive', () => {
  const declared = (name: string) => ({ name, type: 'string' as const, default: { kind: 'string' as const, value: 'x', type: 'string' as const, is_valid: true } })
  const value: ReportDraft = { tenant: 't', key: 'r', version: '1', measures: [{ measure: 'finance.missing' }, { measure: 'finance.rent' }], parameters: [declared('bad'), declared('good')] }
  const verdict = [{ code: 'report.parameter_default_type_mismatch', pointer: '/parameters/0' }, { code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.missing', catalogue: 'Main' }]
  const onChange = vi.fn()
  const view = render(<ReportAuthoringEditor value={value} onCreate={() => {}} onChange={onChange} refusals={verdict} />)
  expect(screen.getByLabelText('Parameter 1 refusal')).toHaveTextContent('report.parameter_default_type_mismatch')
  fireEvent.click(screen.getByRole('button', { name: 'Remove parameter bad' }))
  fireEvent.click(screen.getByRole('button', { name: 'Remove measure finance.missing' }))
  expect(screen.queryByLabelText('Parameter 1 refusal')).toBeNull()
  expect(screen.queryByLabelText('Measure 1 refusal')).toBeNull()
  const echoed = onChange.mock.lastCall![0]
  view.rerender(<ReportAuthoringEditor value={echoed} onCreate={() => {}} onChange={onChange} refusals={verdict} />)
  expect(screen.queryByLabelText('Parameter 1 refusal')).toBeNull()
  expect(screen.queryByLabelText('Measure 1 refusal')).toBeNull()
  view.rerender(<ReportAuthoringEditor value={echoed} onCreate={() => {}} onChange={onChange} refusals={[{ code: 'report.measure_unknown', pointer: '/measures/0/measure', measure: 'finance.rent', catalogue: 'Main' }]} />)
  expect(screen.getByLabelText('Measure 1 refusal')).toHaveTextContent('report.measure_unknown finance.rent Main')
})

it('reports-bound-9 a read-only measure selection does not fence a host replacement', async () => {
  const props = { onCreate: () => {}, onChange: () => {}, readOnly: true, resolveMeasures: async () => ['finance.rent'] }
  const view = render(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'old', version: '1', measures: [{ measure: 'finance.old' }] }} />)
  fireEvent.click(screen.getByRole('button', { name: 'Choose measure' }))
  await screen.findByRole('option', { name: 'finance.rent' })
  fireEvent.change(screen.getByLabelText('Measures'), { target: { value: 'finance.rent' } })
  view.rerender(<ReportAuthoringEditor {...props} value={{ tenant: 't', key: 'new', version: '1', measures: [{ measure: 'finance.new' }] }} />)
  expect(screen.getByText('finance.new')).toBeInTheDocument()
  expect(screen.queryByText('finance.old')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Keep editing' })).toBeNull()
})

it.each(['with', 'after'])('reports-auth-12 accepts a replacement arriving %s the read-only switch', timing => {
  const old = { tenant: 't', key: 'old', version: '1', measures: [] }
  const next = { ...old, key: 'new' }
  const props = { onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} value={old} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  if (timing === 'after') view.rerender(<ReportAuthoringEditor {...props} value={old} readOnly />)
  view.rerender(<ReportAuthoringEditor {...props} value={next} readOnly />)
  expect(screen.getByRole('button', { name: 'Keep editing' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Discard edits and load' })).toBeEnabled()
  fireEvent.click(screen.getByRole('button', { name: 'Discard edits and load' }))
  expect(screen.getByLabelText('Report key')).toHaveValue('new')
  expect(props.onChange).not.toHaveBeenCalled()
})
