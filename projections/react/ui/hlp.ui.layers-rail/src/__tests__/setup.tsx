import * as React from 'react'
import { vi } from 'vitest'
import type { AspectLens, CanvasModel, CanvasNode, ProvenanceInfo, ProvenanceResolver } from '@harborline-platform/hlp.ui.aspect-lens'
import type { LayersState } from '@harborline-platform/hlp.ui.layers-state'
import { LayersRail, type LayersRailProps } from '../LayersRail'

export const baseNodes: CanvasNode[] = [
  { id: 'root', kind: 'section', label: 'Root', depth: 0, parentId: null, hasChildren: true },
  { id: 'child', kind: 'field', label: 'Child', depth: 1, parentId: 'root' },
  { id: 'sibling', kind: 'field', label: 'Sibling', depth: 0, parentId: null },
]
export const baseLenses: AspectLens[] = [
  { id: 'layout', label: 'Layout', tone: 'accent', kind: 'colorize', project: () => ({ active: true }) },
  { id: 'rules', label: 'Rules', tone: 'warning', kind: 'filter', empty: 'Add a rule', project: id => ({ active: id === 'child', badge: id === 'child' ? '1 rule' : undefined }) },
]
export const tenant: ProvenanceInfo = { source: 'tenant', chain: ['tenant'], overridden: false, locked: false, resolved: true }

// A host that owns lens and selection state the way an application would.
export function Host({ nodes = baseNodes, lenses = baseLenses, initialActive = 'layout' as string | null, initialEnabled, selected = null as string | null, onSelect, provenance, ...rest }:
  Partial<Omit<LayersRailProps, 'model' | 'lenses' | 'layers' | 'provenance'>> & { nodes?: CanvasNode[]; lenses?: AspectLens[]; initialActive?: string | null; initialEnabled?: string[]; selected?: string | null; onSelect?: (id: string) => void; provenance?: ProvenanceResolver }) {
  const [activeId, setActiveId] = React.useState<string | null>(initialActive)
  const [enabled, setEnabled] = React.useState<ReadonlySet<string>>(new Set(initialEnabled ?? (initialActive ? [initialActive] : [])))
  const [selectedId, setSelectedId] = React.useState<string | null>(selected)
  const layers: LayersState = {
    activeId, enabledIds: enabled,
    setActive: id => { setActiveId(id); setEnabled(current => new Set([...current, id])) },
    clearActive: () => setActiveId(null),
    toggleEnabled: id => setEnabled(current => { const next = new Set(current); if (!next.delete(id)) next.add(id); return next }),
    isEnabled: id => enabled.has(id), isActive: id => activeId === id,
  }
  const model: CanvasModel = { nodes, selectedId, select: id => { setSelectedId(id); onSelect?.(id) } }
  return <LayersRail {...rest} layers={layers} lenses={lenses} model={model} provenance={provenance ?? { resolve: () => tenant }}/>
}
export const spy = () => vi.fn()
