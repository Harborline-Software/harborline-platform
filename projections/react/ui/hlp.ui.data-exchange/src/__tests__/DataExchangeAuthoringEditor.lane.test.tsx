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
  datatypes: [{ id: 'string', label: 'Text' }],
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
    expect(within(evidenceSection()).getByText(expected.staleness)).toBeInTheDocument()
  })

  it('an editable admission keeps the authoring controls enabled', () => {
    const { input } = fixtureCase('data-exchange.read-only')
    render(<DataExchangeAuthoringEditor value={draftOf(input.value)} catalogue={catalogue} run={runOf(input.run)} canCommit canPublish {...intents()} />)
    expect(screen.getByRole('textbox', { name: 'Definition name' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Publish definition' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Commit reviewed run' })).toBeEnabled()
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
    render(<DataExchangeAuthoringEditor value={emptyDataExchangeDraft()} catalogue={catalogue} canCommit={false} {...intents()} />)
    expect(within(screen.getByRole('group', { name: 'Discovered source shape' })).getByText('No source columns discovered.')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Canonical mappings' })).getByText('No mappings authored.')).toBeInTheDocument()
    expect(within(evidenceSection()).getByText('No dry run recorded.')).toBeInTheDocument()
  })
})
