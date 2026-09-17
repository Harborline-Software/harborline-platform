import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DataExchangeAuthoringEditor, emptyDataExchangeDraft } from '../DataExchangeAuthoringEditor'
import type { DataExchangeAuthoringCatalogue, DataExchangeRunSummary } from '../DataExchangeAuthoringEditor.types'

const catalogue: DataExchangeAuthoringCatalogue = {
  sourceCapabilities: [{ id: 'connector.csv/v1', label: 'CSV upload' }],
  canonicalTargets: [{ id: 'records.customer/v1', label: 'Customer' }],
  datatypes: [{ id: 'string', label: 'Text' }],
  transforms: [{ id: 'trimToNull', label: 'Trim to null' }],
  schedules: [{ id: 'schedule.nightly', label: 'Nightly' }],
}

describe('DataExchangeAuthoringEditor React projection', () => {
  it('authors the bounded inbound definition without accepting credential values', () => {
    const changed = vi.fn()
    const discover = vi.fn()
    const dryRun = vi.fn()
    render(<DataExchangeAuthoringEditor
      value={emptyDataExchangeDraft()}
      catalogue={catalogue}
      canCommit={false}
      onChange={changed}
      onDiscoverSource={discover}
      onDryRun={dryRun}
      onCommit={vi.fn()}
    />)

    expect(screen.getByText('hl:tabular-mapping/v1')).toBeInTheDocument()
    expect(screen.getByText('https://schemas.harborline.software/mapping/tabular/v1')).toBeInTheDocument()
    expect(screen.getByText('1.0.0')).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Secret reference' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Password')).toBeNull()

    fireEvent.change(screen.getByRole('textbox', { name: 'Definition name' }), { target: { value: 'Customer import' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Customer import' }))
    fireEvent.click(screen.getByRole('button', { name: 'Discover source' }))
    fireEvent.click(screen.getByRole('button', { name: 'Create dry run' }))
    expect(discover).toHaveBeenCalledOnce()
    expect(dryRun).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Commit reviewed run' })).toBeDisabled()
  })

  it('narrows discovered columns, authors a canonical mapping, and blocks stale promotion', () => {
    const changed = vi.fn()
    const commit = vi.fn()
    const value = {
      ...emptyDataExchangeDraft(),
      discoveredColumns: [{ name: 'CustomerNumber', selected: true }, { name: 'Ignored', selected: true }],
      mappings: [{ sourceColumn: 'CustomerNumber', canonicalTarget: 'records.customer/v1', targetPointer: '/customerNumber', datatype: 'string', required: true, nullValue: '', defaultValue: '', separator: '', transform: 'trimToNull' }],
    }
    const staleRun: DataExchangeRunSummary = {
      dryRunId: 'dry-7', status: 'Ready', stale: true, candidateCheckpoint: 'cursor:7',
      census: { applied: 0, skipped: 0, conflicted: 0, rejected: 0, failed: 0, halted: 0 },
      refusals: ['mapping.changed'],
    }
    render(<DataExchangeAuthoringEditor value={value} catalogue={catalogue} run={staleRun} canCommit onChange={changed} onDiscoverSource={vi.fn()} onDryRun={vi.fn()} onCommit={commit} />)

    fireEvent.click(screen.getByRole('checkbox', { name: 'Include Ignored' }))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ discoveredColumns: [{ name: 'CustomerNumber', selected: true }, { name: 'Ignored', selected: false }] }))
    expect(screen.getByDisplayValue('/customerNumber')).toBeInTheDocument()
    expect(screen.getByText('mapping.changed')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Commit reviewed run' }))
    expect(commit).not.toHaveBeenCalled()
  })
})
