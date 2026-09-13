import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ViewRuntime } from '../ViewRuntime'
import type { ViewDefinition, ViewRuntimeRow } from '../ViewRuntime.types'

const definition: ViewDefinition = { id: 'view-assets', kind: 'views.entity-list/grid', version: '1', body: { fields: [{ id: 'asset', label: 'Asset' }, { id: 'status', label: 'Status' }, { id: 'owner', label: 'Owner' }] } }
const rows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'Pier', status: 'Open', owner: 'Riley' }, { id: 'a2', asset: 'Pump', status: 'Review', owner: 'Morgan' }]

describe('ViewRuntime React projection', () => {
  it('maps a grid definition fields and caller rows onto data-grid with the definition accessor', () => {
    render(<ViewRuntime definition={definition} rows={rows} />)
    expect(screen.getAllByRole('columnheader').map(node => node.textContent)).toEqual(['Asset', 'Status', 'Owner'])
    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(document.querySelector('.hl-view-runtime')).toHaveAttribute('data-definition-id', 'view-assets')
    expect(document.querySelector('.hl-view-runtime')).toHaveAttribute('data-definition-version', '1')
  })
  it('renders unknown kinds inertly and without console output', () => {
    const consoleSpies = (['log', 'info', 'warn', 'error', 'debug'] as const)
      .map(method => vi.spyOn(console, method).mockImplementation(() => undefined))
    try {
      const { container } = render(<ViewRuntime definition={{ ...definition, kind: 'views.unknown' }} rows={rows} />)
      expect(container).toBeEmptyDOMElement()
      for (const consoleSpy of consoleSpies) expect(consoleSpy).not.toHaveBeenCalled()
    } finally {
      for (const consoleSpy of consoleSpies) consoleSpy.mockRestore()
    }
  })
  it('preserves the known grid definition when the caller supplies an empty row set', () => {
    render(<ViewRuntime definition={definition} empty="No matching assets." rows={[]} />)
    expect(screen.getAllByRole('columnheader')).toHaveLength(3)
    expect(screen.getByRole('gridcell')).toHaveTextContent('No matching assets.')
  })
  it('normalizes missing, null, and non-string row values before passing them to data-grid', () => {
    render(<ViewRuntime definition={definition} rows={[{ id: 'a1', asset: null, status: 42 }]} />)
    expect(screen.getAllByRole('gridcell').map(node => node.textContent)).toEqual(['', '42', ''])
  })
  it('preserves long caller content for the delegated grid', () => {
    const content = 'A caller-owned value that is deliberately long enough to exercise the runtime handoff.'
    render(<ViewRuntime definition={definition} rows={[{ id: 'a1', asset: content, status: 'Open', owner: 'Riley' }]} />)
    expect(screen.getByRole('gridcell', { name: content })).toHaveTextContent(content)
  })
})
