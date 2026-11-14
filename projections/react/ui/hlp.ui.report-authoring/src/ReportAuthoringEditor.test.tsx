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

it('readOnly disables pending replacement decisions', () => {
  const props = { value: { tenant: 't', key: 'r', version: '1', measures: [] }, onCreate: vi.fn(), onChange: vi.fn() }
  const view = render(<ReportAuthoringEditor {...props} />)
  fireEvent.change(screen.getByLabelText('Parameter name'), { target: { value: 'pending' } })
  view.rerender(<ReportAuthoringEditor {...props} readOnly value={{ ...props.value, version: '2' }} />)
  expect(screen.getByRole('button', { name: 'Discard edits and load' })).toBeDisabled()
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
