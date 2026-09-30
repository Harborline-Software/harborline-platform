import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DataExchangeAuthoringEditor, emptyDataExchangeDraft } from '../DataExchangeAuthoringEditor'
import type { DataExchangeAuthoringCatalogue, DataExchangeAuthoringDraft, DataExchangeRunSummary } from '../DataExchangeAuthoringEditor.types'

// The fixture is the authority for both lanes: the Blazor bUnit suite reads these same cases.
interface FixtureCase { readonly id: string; readonly input: Record<string, any>; readonly expected: Record<string, any> }
const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-exchange/fixtures.yaml'), 'utf8')) as { readonly cases: readonly FixtureCase[] }
const fixtureCase = (id: string) => fixture.cases.find(entry => entry.id === id)!
const draftOf = (input?: Partial<DataExchangeAuthoringDraft>): DataExchangeAuthoringDraft => ({ ...emptyDataExchangeDraft(), ...input })
const runOf = (input: any): DataExchangeRunSummary | undefined => input ?? undefined

const catalogue: DataExchangeAuthoringCatalogue = {
  sourceCapabilities: [{ id: 'connector.csv/v1', label: 'CSV upload' }],
  formats: [{ id: 'csv', label: 'CSV' }],
  canonicalTargets: [{ id: 'records.customer/v1', label: 'Customer' }],
  datatypes: [{ id: 'string', label: 'Text' }, { id: 'integer', label: 'Integer' }],
  transforms: [{ id: 'trimToNull', label: 'Trim to null' }],
  schedules: [{ id: 'schedule.nightly', label: 'Nightly' }],
}
const intents = () => ({ onChange: vi.fn(), onDiscoverSource: vi.fn(), onDryRun: vi.fn(), onCommit: vi.fn(), onSaveDraft: vi.fn(), onPublish: vi.fn() })
const evidenceSection = () => screen.getByRole('group', { name: 'Persisted run evidence' })

describe('DataExchangeAuthoringEditor ADR 0096 lane conformance (React)', () => {
  it('readOnly_admission_disables_every_input_and_publish (data-exchange-auth-9)', () => {
    const { input, expected } = fixtureCase('data-exchange.read-only')
    const handlers = intents()
    const { container } = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} run={runOf(input.run)} canCommit={input.canCommit} canPublish={input.canPublish} readOnly={input.readOnly} {...handlers} />)

    const controls = [...container.querySelectorAll('input, select, textarea, button')]
    expect(controls.length).toBeGreaterThan(20)
    expect(expected.everyControlDisabled).toBe(true)
    for (const control of controls) expect(control, control.outerHTML).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Publish definition' }).hasAttribute('disabled')).toBe(!expected.publishEnabled)
    expect(screen.getByRole('button', { name: 'Commit reviewed run' }).hasAttribute('disabled')).toBe(!expected.commitEnabled)
    for (const control of controls) { fireEvent.click(control); fireEvent.change(control, { target: { value: 'x' } }) }
    for (const handler of Object.values(handlers)) expect(handler).not.toHaveBeenCalled()
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(input.value.name)
    expect(within(evidenceSection()).getByText(expected.staleness)).toBeInTheDocument()
  })

  it('an editable admission keeps the authoring controls enabled and emits each intent', () => {
    const { input } = fixtureCase('data-exchange.read-only')
    const handlers = intents()
    render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} run={runOf(input.run)} canCommit canPublish {...handlers} />)
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Publish definition' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Commit reviewed run' })).toBeEnabled()
    expect(screen.queryByRole('group', { name: 'Authoring refusals' })).toBeNull()
    for (const name of ['Discover source', 'Save draft', 'Publish definition', 'Create dry run', 'Commit reviewed run']) fireEvent.click(screen.getByRole('button', { name }))
    for (const handler of [handlers.onDiscoverSource, handlers.onSaveDraft, handlers.onPublish, handlers.onDryRun, handlers.onCommit]) expect(handler).toHaveBeenCalledOnce()
  })

  it.each([
    ['Definition name', 'Customer intake v2', 'name'], ['Source capability', 'connector.csv/v1', 'sourceCapability'], ['Connector version', '2.5.0', 'connectorVersion'],
    ['Format', '', 'formatCapability'], ['Secret reference', 'secretref:other', 'secretReference'], ['Replay policy', 'overwrite', 'replayPolicy'],
    ['Schedule reference', 'schedule.nightly', 'scheduleReference'], ['Reference dataset', 'dataset.other', 'referenceDataset'],
    ['Pack distribution', 'pack://other', 'packDistribution'], ['Feed distribution', 'feed://other', 'feedDistribution'],
  ] as const)('the %s field emits its edit on the draft', (label, next, key) => {
    const handlers = intents()
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} canCommit={false} {...handlers} />)
    fireEvent.change(screen.getByLabelText(label), { target: { value: next } })
    expect(handlers.onChange).toHaveBeenCalledOnce()
    expect(handlers.onChange.mock.lastCall![0][key]).toBe(next)
  })

  it('mapping rows and external keys emit the edited draft', () => {
    const { input } = fixtureCase('data-exchange.read-only')
    const handlers = intents()
    render(<DataExchangeAuthoringEditor value={draftOf({ ...input.value, discoveredColumns: [{ name: 'Ignored', selected: false }, { name: 'CustomerNumber', selected: true }], externalKeyColumns: ['CustomerNumber', 'Region'] })} catalogue={catalogue} canCommit={false} {...handlers} />)
    const last = () => handlers.onChange.mock.lastCall![0] as DataExchangeAuthoringDraft
    expect(screen.getByRole('textbox', { name: 'External key columns' })).toHaveValue('CustomerNumber, Region')
    expect(within(screen.getByLabelText('Mapping 1 source column')).getAllByRole('option').map(option => option.getAttribute('value'))).toEqual(['', 'CustomerNumber'])
    fireEvent.click(screen.getByRole('checkbox', { name: 'Include Ignored' }))
    expect(last().discoveredColumns.map(column => column.selected)).toEqual([true, true])
    const edits: readonly [string, string | boolean, keyof DataExchangeAuthoringDraft['mappings'][number]][] = [
      ['Mapping 1 source column', 'Ignored', 'sourceColumn'], ['Mapping 1 canonical target', '', 'canonicalTarget'], ['Mapping 1 target pointer', '/number', 'targetPointer'], ['Mapping 1 datatype', 'integer', 'datatype'],
      ['Mapping 1 required', false, 'required'], ['Mapping 1 null', 'NULL', 'nullValue'], ['Mapping 1 default', '0', 'defaultValue'], ['Mapping 1 separator', ';', 'separator'], ['Mapping 1 transform', '', 'transform'],
    ]
    for (const [label, next, key] of edits) {
      const control = screen.getByLabelText(label)
      if (typeof next === 'boolean') fireEvent.click(control); else fireEvent.change(control, { target: { value: next } })
      expect(last().mappings).toHaveLength(1)
      expect(last().mappings[0][key]).toBe(next)
    }
    fireEvent.change(screen.getByRole('textbox', { name: 'External key columns' }), { target: { value: ' A , ,B ' } })
    expect(last().externalKeyColumns).toEqual(['A', 'B'])
    fireEvent.click(screen.getByRole('button', { name: 'Add mapping' }))
    // Ignored was selected above, so it is now the first selected column the new row defaults to.
    expect(last().mappings.at(-1)).toEqual({ sourceColumn: 'Ignored', canonicalTarget: '', targetPointer: '', datatype: 'string', required: false, nullValue: '', defaultValue: '', separator: '', transform: '' })
    fireEvent.change(screen.getByLabelText('Mapping 2 target pointer'), { target: { value: '/second' } })
    expect(last().mappings.map(mapping => mapping.targetPointer)).toEqual(['/number', '/second'])
    fireEvent.click(screen.getByRole('button', { name: 'Remove mapping 2' }))
    expect(last().mappings.map(mapping => mapping.targetPointer)).toEqual(['/number'])
  })

  it('pending edits survive a revision change until the author keeps or discards them (data-exchange-eng-22)', () => {
    const { input, expected } = fixtureCase('data-exchange.pending-revision')
    const handlers = intents()
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={input.canCommit} canPublish {...handlers} />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: input.edit.name } })
    expect(handlers.onChange).toHaveBeenLastCalledWith(expect.objectContaining({ name: input.edit.name, identity: input.value.identity, expectedRevision: input.value.expectedRevision }))
    expect(screen.queryByRole('alert')).toBeNull()

    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.incoming)} catalogue={catalogue} canCommit={input.canCommit} canPublish {...handlers} />)
    expect(screen.getByRole('alert')).toHaveTextContent(expected.pendingAlert)
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(expected.keptName)
    expect(screen.getByRole('button', { name: 'Publish definition' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Save draft' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Create dry run' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Publish definition' }))
    fireEvent.click(screen.getByRole('button', { name: 'Create dry run' }))
    expect(handlers.onPublish).not.toHaveBeenCalled()
    expect(handlers.onDryRun).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Keep edits' }))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(expected.keptName)
    expect(screen.getByRole('button', { name: 'Publish definition' })).toBeEnabled()

    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.otherIdentity)} catalogue={catalogue} canCommit={input.canCommit} canPublish {...handlers} />)
    expect(screen.getByRole('alert')).toHaveTextContent(expected.pendingAlert)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(expected.otherIdentityName)
    expect(handlers.onChange).toHaveBeenLastCalledWith(expect.objectContaining({ identity: input.otherIdentity.identity, name: expected.otherIdentityName }))
  })

  it('a host echo of the same revision keeps the edit pending for the next revision change', () => {
    const { input, expected } = fixtureCase('data-exchange.pending-revision')
    const handlers = intents()
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={false} {...handlers} />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: input.edit.name } })
    view.rerender(<DataExchangeAuthoringEditor value={draftOf({ ...input.value, name: input.edit.name })} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.queryByRole('alert')).toBeNull()
    const incoming = draftOf(input.incoming)
    view.rerender(<DataExchangeAuthoringEditor value={incoming} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.getByRole('alert')).toHaveTextContent(expected.pendingAlert)
    view.rerender(<DataExchangeAuthoringEditor value={incoming} catalogue={catalogue} canCommit={false} canPublish {...handlers} />)
    expect(screen.getByRole('alert')).toHaveTextContent(expected.pendingAlert)
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(input.edit.name)
  })

  it('a new identity at the same revision is a revision change too', () => {
    const { input, expected } = fixtureCase('data-exchange.pending-revision')
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={false} {...intents()} />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: input.edit.name } })
    view.rerender(<DataExchangeAuthoringEditor value={draftOf({ ...input.value, identity: input.otherIdentity.identity })} catalogue={catalogue} canCommit={false} {...intents()} />)
    expect(screen.getByRole('alert')).toHaveTextContent(expected.pendingAlert)
  })

  it('a saved revision that echoes the local content is adopted without an alert', () => {
    const { input } = fixtureCase('data-exchange.pending-revision')
    const handlers = intents()
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={false} {...handlers} />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: input.edit.name } })
    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.incoming)} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    view.rerender(<DataExchangeAuthoringEditor value={draftOf({ ...input.incoming, expectedRevision: '5', name: input.edit.name })} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.queryByRole('alert')).toBeNull()
    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.otherIdentity)} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('a revision change without pending edits is adopted silently', () => {
    const { input, expected } = fixtureCase('data-exchange.pending-revision')
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={false} {...intents()} />)
    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.incoming)} catalogue={catalogue} canCommit={false} {...intents()} />)
    expect(screen.queryByRole('alert')).toBeNull()
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(expected.discardedName)
  })

  it('discarding a pending edit adopts the incoming server revision', () => {
    const { input, expected } = fixtureCase('data-exchange.pending-revision')
    const handlers = intents()
    const view = render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} canCommit={false} {...handlers} />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: input.edit.name } })
    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.incoming)} catalogue={catalogue} canCommit={false} {...handlers} />)
    fireEvent.click(screen.getByRole('button', { name: 'Discard edits' }))
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toHaveValue(expected.discardedName)
    expect(handlers.onChange).toHaveBeenLastCalledWith(expect.objectContaining({ expectedRevision: input.incoming.expectedRevision, name: expected.discardedName }))
    view.rerender(<DataExchangeAuthoringEditor value={draftOf(input.otherIdentity)} catalogue={catalogue} canCommit={false} {...handlers} />)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it.each(['data-exchange.run-evidence', 'data-exchange.stale-review', 'data-exchange.read-only'])('renders the server census and staleness from %s verbatim and exposes no run-evidence control (data-exchange-run-4)', id => {
    const { input, expected } = fixtureCase(id)
    render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} run={runOf(input.run)} canCommit={input.canCommit} readOnly={input.readOnly} {...intents()} />)
    const section = evidenceSection()
    expect(within(section).getByText(expected.staleness)).toBeInTheDocument()
    if (expected.census) expect(within(section).getByText(expected.census)).toBeInTheDocument()
    for (const value of expected.evidence ?? []) expect(within(section).getByText(value)).toBeInTheDocument()
    expect(section.querySelectorAll('input, select, textarea, button, [contenteditable]')).toHaveLength(0)
    expect(screen.getByRole('button', { name: 'Commit reviewed run' }).hasAttribute('disabled')).toBe(!expected.commitEnabled)
  })

  it('accepts both catalogue option shapes and falls back when formats are omitted', () => {
    const { formats: _omitted, ...withoutFormats } = catalogue
    const bare: DataExchangeAuthoringCatalogue = { ...withoutFormats, sourceCapabilities: ['connector.sftp/v1'], schedules: ['schedule.hourly', { id: 'schedule.nightly', label: 'Nightly' }] }
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={bare} canCommit={false} {...intents()} />)
    expect(within(screen.getByRole('combobox', { name: 'Source capability' })).getByRole('option', { name: 'connector.sftp/v1' })).toHaveValue('connector.sftp/v1')
    expect(within(screen.getByRole('combobox', { name: 'Schedule reference' })).getByRole('option', { name: 'schedule.hourly' })).toHaveValue('schedule.hourly')
    expect(within(screen.getByRole('combobox', { name: 'Schedule reference' })).getByRole('option', { name: 'Nightly' })).toHaveValue('schedule.nightly')
    expect(within(screen.getByRole('combobox', { name: 'Format' })).getByRole('option', { name: 'CSV' })).toHaveValue('csv')
  })

  it('renders fallback sections for an empty source shape, no mappings and no dry run', () => {
    const view = render(<DataExchangeAuthoringEditor value={draftOf(fixtureCase('data-exchange.read-only').input.value)} catalogue={catalogue} run={runOf(fixtureCase('data-exchange.read-only').input.run)} canCommit={false} {...intents()} />)
    for (const fallback of ['No source columns discovered.', 'No mappings authored.', 'No dry run recorded.']) expect(screen.queryByText(fallback)).toBeNull()
    view.unmount()
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} canCommit {...intents()} />)
    expect(screen.getByRole('button', { name: 'Commit reviewed run' })).toBeDisabled()
    expect(within(screen.getByRole('group', { name: 'Discovered source shape' })).getByText('No source columns discovered.')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Canonical mappings' })).getByText('No mappings authored.')).toBeInTheDocument()
    expect(within(evidenceSection()).getByText('No dry run recorded.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Add mapping' }))
    expect(screen.getByLabelText('Mapping 1 source column')).toHaveValue('')
  })

  it('commit stays closed for a current run that is not Ready', () => {
    const { input } = fixtureCase('data-exchange.run-evidence')
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} run={{ ...input.run, status: 'Pending' }} canCommit {...intents()} />)
    expect(screen.getByRole('button', { name: 'Commit reviewed run' })).toBeDisabled()
  })

  it('save and publish tolerate a host that supplies neither optional intent', () => {
    const { onSaveDraft: _save, onPublish: _publish, ...required } = intents()
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} canCommit={false} canPublish {...required} />)
    expect(() => { fireEvent.click(screen.getByRole('button', { name: 'Save draft' })); fireEvent.click(screen.getByRole('button', { name: 'Publish definition' })) }).not.toThrow()
  })

  it('the empty draft is blank apart from its csv and append defaults', () => {
    expect(emptyDataExchangeDraft()).toEqual({ name: '', sourceCapability: '', connectorVersion: '', formatCapability: 'csv', secretReference: '', discoveredColumns: [], mappings: [], externalKeyColumns: [], replayPolicy: 'append', scheduleReference: '', referenceDataset: '', packDistribution: '', feedDistribution: '' })
    const { container } = render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} canCommit={false} {...intents()} />)
    expect(container.querySelector('form.hl-data-exchange-authoring')).not.toBeNull()
  })
})
