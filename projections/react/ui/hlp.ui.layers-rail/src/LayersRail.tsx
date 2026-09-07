import * as React from 'react'

import type { AspectLens, CanvasModel, CanvasNode, ProvenanceResolver } from '@harborline-platform/hlp.ui.aspect-lens'
import type { LayersState } from '@harborline-platform/hlp.ui.layers-state'
import { defaultRailLabels, type RailLabels } from '@harborline-platform/hlp.ui.rail-labels'
import { toneStyle } from '@harborline-platform/hlp.ui.tone-style'

export interface LayersRailProps extends React.HTMLAttributes<HTMLElement> {
  model: CanvasModel
  lenses: readonly AspectLens[]
  layers: LayersState
  provenance: ProvenanceResolver
  labels?: Partial<RailLabels>
  onInsert?: () => void
  compact?: boolean
  showProvenance?: boolean
}

function assertValid(model: CanvasModel, lenses: readonly AspectLens[]) {
  const lensIds = new Set<string>()
  const nodeIds = new Set<string>()
  const nodesById = new Map<string, CanvasNode>()
  for (const lens of lenses) {
    if (lensIds.has(lens.id)) throw new Error('duplicate-layer-identity')
    lensIds.add(lens.id)
  }
  for (const node of model.nodes) {
    if (nodeIds.has(node.id)) throw new Error('duplicate-layer-identity')
    nodeIds.add(node.id)
    const parent = node.parentId === null ? null : nodesById.get(node.parentId)
    if (node.depth < 0 || (node.depth === 0) !== (node.parentId === null) || (node.parentId !== null && (!parent || parent.depth + 1 !== node.depth))) {
      throw new Error('invalid-outline-tree')
    }
    nodesById.set(node.id, node)
  }
  if (model.selectedId !== null && !nodeIds.has(model.selectedId)) throw new Error('invalid-outline-tree')
}

export function LayersRail({
  model, lenses, layers, provenance, labels: overrides, onInsert, compact = false,
  showProvenance = true, className, ...attributes
}: LayersRailProps) {
  assertValid(model, lenses)
  const labels = React.useMemo(() => ({ ...defaultRailLabels, ...overrides }), [overrides])
  if (![labels.railRegion, labels.lensesHeading, labels.outlineHeading].every(value => value.trim())) {
    throw new Error('accessible-rail-label-required')
  }
  const activeLens = lenses.find(lens => lens.id === layers.activeId) ?? null
  const [collapsed, setCollapsed] = React.useState<ReadonlySet<string>>(new Set())
  const [focusedId, setFocusedId] = React.useState<string | null>(() => model.selectedId ?? model.nodes[0]?.id ?? null)
  const refs = React.useRef(new Map<string, HTMLButtonElement>())

  const visibleNodes = React.useMemo(() => {
    const hidden = new Set<string>()
    const visible: CanvasNode[] = []
    for (const node of model.nodes) {
      if (node.parentId !== null && hidden.has(node.parentId)) hidden.add(node.id)
      else visible.push(node)
      if (collapsed.has(node.id)) hidden.add(node.id)
    }
    return visible
  }, [collapsed, model.nodes])

  React.useLayoutEffect(() => {
    if (focusedId !== null && visibleNodes.some(node => node.id === focusedId)) return
    let candidate = model.nodes.find(node => node.id === focusedId)
    while (candidate?.parentId) {
      const parent = model.nodes.find(node => node.id === candidate?.parentId)
      if (parent && visibleNodes.some(node => node.id === parent.id)) {
        setFocusedId(parent.id)
        refs.current.get(parent.id)?.focus()
        return
      }
      candidate = parent
    }
    setFocusedId(visibleNodes[0]?.id ?? null)
  }, [focusedId, model.nodes, visibleNodes])

  const focusNode = (id: string) => {
    setFocusedId(id)
    queueMicrotask(() => refs.current.get(id)?.focus())
  }
  const move = (id: string, delta: number) => {
    const index = visibleNodes.findIndex(node => node.id === id)
    const target = visibleNodes[index + delta]
    if (target) focusNode(target.id)
  }
  const toggleCollapse = (node: CanvasNode) => {
    if (!node.hasChildren) return
    setCollapsed(current => {
      const next = new Set(current)
      if (next.has(node.id)) next.delete(node.id)
      else next.add(node.id)
      return next
    })
  }
  const onTreeKey = (event: React.KeyboardEvent, node: CanvasNode) => {
    if (event.key === 'ArrowDown') move(node.id, 1)
    else if (event.key === 'ArrowUp') move(node.id, -1)
    else if (event.key === 'Home' && visibleNodes[0]) focusNode(visibleNodes[0].id)
    else if (event.key === 'End' && visibleNodes.at(-1)) focusNode(visibleNodes.at(-1)!.id)
    else if (event.key === 'ArrowRight') {
      if (node.hasChildren && collapsed.has(node.id)) toggleCollapse(node)
      else {
        const child = visibleNodes.find(candidate => candidate.parentId === node.id)
        if (child) focusNode(child.id)
      }
    } else if (event.key === 'ArrowLeft') {
      if (node.hasChildren && !collapsed.has(node.id)) toggleCollapse(node)
      else if (node.parentId) focusNode(node.parentId)
    } else if (event.key === 'Enter' || event.key === ' ') model.select(node.id)
    else return
    event.preventDefault()
  }

  const onLensKey = (event: React.KeyboardEvent) => {
    if (/^[1-9]$/.test(event.key)) {
      const lens = lenses[Number(event.key) - 1]
      if (lens) layers.setActive(lens.id)
      event.preventDefault()
    } else if (event.key === 'Escape') {
      layers.clearActive()
      event.preventDefault()
    }
  }

  return (
    <aside {...attributes} aria-label={labels.railRegion} className={`hl-layers-rail${compact ? ' hl-layers-rail--compact' : ''}${className ? ` ${className}` : ''}`}>
      {!compact && activeLens ? (
        <div role="status" aria-live="polite" className="hl-layers-rail__status" style={{ borderColor: toneStyle(activeLens.tone).border }}>
          <span>{labels.viewingLens(activeLens.label)}</span>
          <button type="button" className="hl-layers-rail__status-close" aria-label={labels.exitLens} onClick={layers.clearActive}><svg aria-hidden="true" viewBox="0 0 16 16"><path d="M3 3l10 10M13 3 3 13"/></svg></button>
        </div>
      ) : null}
      <div role="group" aria-label={labels.lensesHeading} className="hl-layers-rail__lenses" onKeyDown={onLensKey}>
        {compact ? null : <h2>{labels.lensesHeading}</h2>}
        {lenses.map((lens, index) => {
          const active = layers.isActive(lens.id)
          const enabled = layers.isEnabled(lens.id)
          const passiveCount = !active && enabled ? model.nodes.filter(node => lens.project(node.id).active).length : 0
          return (
            <div key={lens.id} className="hl-layers-rail__lens" style={{ '--hl-lens-tone': toneStyle(lens.tone).swatch } as React.CSSProperties}>
              <button type="button" className="hl-layers-rail__lens-activate" aria-pressed={active} aria-label={labels.activateLens(lens.label)} title={labels.lensShortcutHint(index + 1)} onClick={() => layers.setActive(lens.id)}>
                <span aria-hidden="true" className="hl-layers-rail__swatch"/><span className="hl-layers-rail__optional-copy">{lens.label}</span>
                {passiveCount > 0 ? <span className="hl-layers-rail__count">{labels.passiveCount(passiveCount)}</span> : null}
              </button>
              <button type="button" className="hl-layers-rail__lens-toggle" aria-pressed={enabled} aria-label={labels.toggleLens(lens.label)} onClick={() => layers.toggleEnabled(lens.id)}><svg aria-hidden="true" viewBox="0 0 16 16"><path d="M1.5 8s2.25-4 6.5-4 6.5 4 6.5 4-2.25 4-6.5 4S1.5 8 1.5 8Z"/><circle cx="8" cy="8" r="2"/></svg></button>
            </div>
          )
        })}
      </div>
      <div className="hl-layers-rail__outline-header">
        {compact ? null : <h2>{labels.outlineHeading}</h2>}
        {onInsert ? <button type="button" className="hl-layers-rail__insert" aria-label={labels.insert} onClick={onInsert}><svg aria-hidden="true" viewBox="0 0 16 16"><path d="M8 3v10M3 8h10"/></svg></button> : null}
      </div>
      <div role="tree" aria-label={labels.outlineHeading} className="hl-layers-rail__tree">
        {visibleNodes.length === 0 ? <p>{activeLens?.empty ?? labels.empty}</p> : visibleNodes.map(node => {
          const lensState = activeLens?.project(node.id)
          const ghosted = activeLens?.kind === 'filter' && lensState?.active === false
          const source = showProvenance ? provenance.resolve(node.id) : null
          const unresolved = source !== null && (!source.resolved || source.source === 'unknown')
          return (
            <div key={node.id} role="treeitem" aria-level={node.depth + 1} aria-selected={model.selectedId === node.id} aria-expanded={node.hasChildren ? !collapsed.has(node.id) : undefined} className="hl-layers-rail__node" data-hl-ghosted={ghosted || undefined} style={{ '--hl-depth': node.depth } as React.CSSProperties}>
              {node.hasChildren ? <button type="button" className="hl-layers-rail__disclosure" aria-label={collapsed.has(node.id) ? labels.expandNode : labels.collapseNode} onClick={() => toggleCollapse(node)}><svg aria-hidden="true" viewBox="0 0 16 16"><path d={collapsed.has(node.id) ? 'M6 3l5 5-5 5' : 'M3 6l5 5 5-5'}/></svg></button> : <span aria-hidden="true" className="hl-layers-rail__spacer"/>}
              <button className="hl-layers-rail__node-control" ref={element => { if (element) refs.current.set(node.id, element); else refs.current.delete(node.id) }} type="button" tabIndex={focusedId === node.id ? 0 : -1} onFocus={() => setFocusedId(node.id)} onKeyDown={event => onTreeKey(event, node)} onClick={() => model.select(node.id)}>
                <span className="hl-layers-rail__node-label">{node.label}</span>{lensState?.badge ? <span className="hl-layers-rail__node-badge">{lensState.badge}</span> : null}
                {source ? <span className="hl-layers-rail__source">{unresolved ? labels.unresolvedSource : labels.source(source.source)}{source.locked ? ` · ${labels.locked}` : ''}</span> : null}
              </button>
            </div>
          )
        })}
      </div>
    </aside>
  )
}
