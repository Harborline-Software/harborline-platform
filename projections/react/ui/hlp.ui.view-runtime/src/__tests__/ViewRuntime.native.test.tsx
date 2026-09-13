import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ViewRuntime } from '../ViewRuntime'
import type { ViewDefinition, ViewRuntimeRow } from '../ViewRuntime.types'

const definition: ViewDefinition = { id: 'view-assets', kind: 'views.entity-list/grid', version: '1', packKey: 'harborline.platform', body: { fields: [{ id: 'asset', label: 'Asset' }, { id: 'status', label: 'Status' }, { id: 'owner', label: 'Owner' }] } }
const rows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'Pier', status: 'Open', owner: 'Riley' }, { id: 'a2', asset: 'Pump', status: 'Review', owner: 'Morgan' }]
const seededFormsList: ViewDefinition = { ...definition, id: 'view-forms' }
const catalogue = new Map([[`${seededFormsList.id}@${seededFormsList.version}`, { definitionId: seededFormsList.id, definitionVersion: seededFormsList.version, packKey: seededFormsList.packKey }]])

describe('ViewRuntime React projection', () => {
  it('maps the seeded Forms list through one source-map accessor that round-trips to its catalogue definition', () => {
    render(<ViewRuntime definition={seededFormsList} rows={rows} />)
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
  it('omits the source-map accessor rather than emitting partial provenance for an unpacked definition', () => {
    for (const packKey of [undefined, '', ' ']) {
      const view = render(<ViewRuntime definition={{ ...definition, packKey }} rows={rows} />)
      expect(view.container.querySelector('.hl-view-runtime')).not.toHaveAttribute('data-definition-source')
      expect(view.container.querySelector('.hl-view-runtime')).not.toHaveAttribute('title')
      view.unmount()
    }
  })
  it('keeps definition provenance at the React projection boundary without mutating the supplied envelope', () => {
    const envelope = Object.freeze({ ...definition, body: Object.freeze({ ...definition.body, fields: Object.freeze([...definition.body.fields]) }) })
    render(<ViewRuntime definition={envelope} rows={rows} />)
    expect(envelope).toEqual(definition)
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
