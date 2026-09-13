import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ViewRuntime } from '../ViewRuntime'
import type { ViewRenderPlan, ViewRuntimeRow } from '../ViewRuntime.types'

const plan: ViewRenderPlan = { definitionHash: 'sha256:view-assets', definitionId: 'view-assets', definitionVersion: '1', packKey: 'harborline.platform', packVersion: '1.0.0', definitionKind: 'ViewDefinition', bindings: { viewKind: 'views.entity-list/grid', parameters: { fields: [{ id: 'asset', label: 'Asset' }, { id: 'status', label: 'Status' }, { id: 'owner', label: 'Owner' }] } } }
const rows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'Pier', status: 'Open', owner: 'Riley' }, { id: 'a2', asset: 'Pump', status: 'Review', owner: 'Morgan' }]
const seededFormsList: ViewRenderPlan = { ...plan, definitionId: 'view-forms' }
const catalogue = new Map([[`${seededFormsList.definitionId}@${seededFormsList.definitionVersion}`, { definitionId: seededFormsList.definitionId, definitionVersion: seededFormsList.definitionVersion, packKey: seededFormsList.packKey }]])

describe('ViewRuntime React projection', () => {
  it('maps the seeded Forms list through one source-map accessor that round-trips to its catalogue definition', () => {
    render(<ViewRuntime plan={seededFormsList} rows={rows} />)
    expect(screen.getAllByRole('columnheader').map(node => node.textContent)).toEqual(['Asset', 'Status', 'Owner'])
    expect(screen.getAllByRole('row')).toHaveLength(3)
    const runtime = document.querySelector('.hl-view-runtime')
    const source = JSON.parse(runtime?.getAttribute('data-definition-source') ?? '')
    expect(source).toEqual({ definitionId: 'view-forms', definitionVersion: '1', packKey: 'harborline.platform' })
    expect(catalogue.get(`${source.definitionId}@${source.definitionVersion}`)).toEqual(source)
    expect(runtime).not.toHaveAttribute('data-definition-id')
    expect(runtime).not.toHaveAttribute('data-definition-version')
    expect(runtime).toHaveAttribute('title', `Definition source: ${JSON.stringify(source)}`)
  })
  it('keeps plan provenance at the React projection boundary without mutating the supplied artifact', () => {
    const artifact = Object.freeze({ ...plan, bindings: Object.freeze({ ...plan.bindings }) })
    render(<ViewRuntime plan={artifact} rows={rows} />)
    expect(artifact).toEqual(plan)
  })
  it('renders unknown kinds inertly and without console output', () => {
    const consoleSpies = (['log', 'info', 'warn', 'error', 'debug'] as const)
      .map(method => vi.spyOn(console, method).mockImplementation(() => undefined))
    try {
      const { container } = render(<ViewRuntime plan={{ ...plan, bindings: { ...plan.bindings, viewKind: 'views.unknown' } }} rows={rows} />)
      expect(container).toBeEmptyDOMElement()
      for (const consoleSpy of consoleSpies) expect(consoleSpy).not.toHaveBeenCalled()
    } finally {
      for (const consoleSpy of consoleSpies) consoleSpy.mockRestore()
    }
  })
  it('preserves the known grid definition when the caller supplies an empty row set', () => {
    render(<ViewRuntime plan={plan} empty="No matching assets." rows={[]} />)
    expect(screen.getAllByRole('columnheader')).toHaveLength(3)
    expect(screen.getByRole('gridcell')).toHaveTextContent('No matching assets.')
  })
  it('normalizes missing, null, and non-string row values before passing them to data-grid', () => {
    render(<ViewRuntime plan={plan} rows={[{ id: 'a1', asset: null, status: 42 }]} />)
    expect(screen.getAllByRole('gridcell').map(node => node.textContent)).toEqual(['', '42', ''])
  })
  it('preserves long caller content for the delegated grid', () => {
    const content = 'A caller-owned value that is deliberately long enough to exercise the runtime handoff.'
    render(<ViewRuntime plan={plan} rows={[{ id: 'a1', asset: content, status: 'Open', owner: 'Riley' }]} />)
    expect(screen.getByRole('gridcell', { name: content })).toHaveTextContent(content)
  })
})
