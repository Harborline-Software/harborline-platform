import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { AspectLens, CanvasModel, ProvenanceResolver } from '@harborline-platform/hlp.ui.aspect-lens'
import type { LayersState } from '@harborline-platform/hlp.ui.layers-state'
import { LayersRail } from '../LayersRail'

function setup() {
  const select = vi.fn()
  const model: CanvasModel = { selectedId: null, select, nodes: [
    { id: 'root', kind: 'section', label: 'Root', depth: 0, parentId: null, hasChildren: true },
    { id: 'child', kind: 'field', label: 'Child', depth: 1, parentId: 'root' },
  ] }
  const lenses: AspectLens[] = [{ id: 'layout', label: 'Layout', tone: 'accent', kind: 'colorize', project: () => ({ active: true }) }]
  const layers: LayersState = { activeId: 'layout', enabledIds: new Set(['layout']), setActive: vi.fn(), clearActive: vi.fn(), toggleEnabled: vi.fn(), isEnabled: id => id === 'layout', isActive: id => id === 'layout' }
  const provenance: ProvenanceResolver = { resolve: () => ({ source: 'tenant', chain: ['tenant'], overridden: false, locked: false, resolved: true }) }
  return { model, lenses, layers, provenance, select }
}

describe('LayersRail React projection', () => {
  it('renders the named region, lens group, tree, provenance, and selects once', () => {
    const values = setup()
    render(<LayersRail {...values}/>)
    expect(screen.getByRole('complementary', { name: 'Layers' })).toBeInTheDocument()
    expect(screen.getByRole('tree', { name: 'Outline' })).toBeInTheDocument()
    expect(screen.getAllByText('tenant')).not.toHaveLength(0)
    fireEvent.click(screen.getByText('Child'))
    expect(values.select).toHaveBeenCalledOnce()
  })

  it('moves focus to the visible ancestor when collapsing', () => {
    const values = setup()
    render(<LayersRail {...values}/>)
    const child = screen.getByText('Child').closest('button')!
    child.focus()
    fireEvent.click(screen.getByRole('button', { name: 'Collapse' }))
    expect(screen.queryByText('Child')).toBeNull()
    expect(screen.getByText('Root').closest('button')).toHaveFocus()
  })
})
