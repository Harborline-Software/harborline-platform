import { SHELL_BREAKPOINTS, SHELL_LAYOUT, panelMinimumHeight, shellBreakpoint, type PackPanelDeclaration, type ShellBreakpoint } from './types'

export const DOCK_MINIMUM_PANE_WIDTH = 300

export interface DockPane {
  kind: 'pane'
  panels: readonly PackPanelDeclaration[]
  fractions: readonly number[]
}

export interface DockSplit {
  kind: 'split'
  orientation: 'horizontal' | 'vertical'
  ratio: number
  first: DockNode
  second: DockNode
}

export type DockNode = DockPane | DockSplit
export type DockContainerKind = 'docked' | 'side-sheet' | 'bottom-sheet'
export interface DockPanelContainer { panel: PackPanelDeclaration; kind: DockContainerKind }
export interface DockLayout {
  openPanels: readonly PackPanelDeclaration[]; spread: boolean; root: DockNode | null; renderRoot: DockNode | null
  containers: readonly DockPanelContainer[]; minimumPaneWidth: number; spreadUnavailable: boolean; spreadUnavailableReason?: string
  breakpoint: ShellBreakpoint; shellInlineSize: number; railInlineSize: number; paneCapacity: number
  /** The class the MEASURED box falls in - what decides placement, and the only width axis the authored tree is remembered against. */
  placementClass: ShellBreakpoint
}

/* chrome-spec: the dock shrinks before the body pane keeps its floor, so the ceiling on a dock width is
   the row minus the rail minus that floor. The clamp lives HERE, in the model, and the stylesheet only
   paints `flex:0 0 var(--hl-app-shell-dock-size)` — whatever width the model hands the aside is the width
   the box takes, so the dock's inline end never leaves the content row. Below PaneMinimumWidth there is no
   dock to size: the newest-opened panel is a sheet, and pane CAPACITY is derived from this same ceiling
   (dockPaneCapacity) so the two can never disagree. The shell inline size is the MEASURED width of the shell
   box in both lanes — never the breakpoint class width, which is up to 340px away from it. */
export function dockWidthCeiling(shellInlineSize: number, railInlineSize: number, contentFloor: number = SHELL_LAYOUT.contentFloor): number {
  return shellInlineSize - railInlineSize - contentFloor
}

export function clampDockWidth(requested: number, shellInlineSize: number, railInlineSize: number, contentFloor: number = SHELL_LAYOUT.contentFloor): number {
  return Math.max(DOCK_MINIMUM_PANE_WIDTH, Math.min(Math.round(requested), dockWidthCeiling(shellInlineSize, railInlineSize, contentFloor)))
}

/** How many panes the row can hold beside the rail and the content floor. Same ceiling as the clamp. */
export function dockPaneCapacity(shellInlineSize: number, railInlineSize: number, contentFloor: number = SHELL_LAYOUT.contentFloor): number {
  if (!Number.isFinite(shellInlineSize)) return Number.POSITIVE_INFINITY
  return Math.max(0, Math.floor(dockWidthCeiling(shellInlineSize, railInlineSize, contentFloor) / DOCK_MINIMUM_PANE_WIDTH))
}

function pane(panels: readonly PackPanelDeclaration[]): DockPane {
  return { kind: 'pane', panels, fractions: panels.map(() => 1 / panels.length) }
}

function tree(panes: readonly DockPane[]): DockNode | null {
  return panes.reduce<DockNode | null>((root, next) => root === null ? next : { kind: 'split', orientation: 'horizontal', ratio: 0.5, first: root, second: next }, null)
}

export function createDockLayout(openPanels: readonly PackPanelDeclaration[], spread: boolean, breakpoint: ShellBreakpoint = 'large', shellInlineSize = Number.POSITIVE_INFINITY, railInlineSize = 0, spreadUnavailable = false, spreadUnavailableReason?: string): DockLayout {
  const depth = spread ? 1 : 2
  const sheetKind: DockContainerKind = breakpoint === 'compact' ? 'bottom-sheet' : 'side-sheet'
  // Placement is constant inside a breakpoint class (adaptation-v1 classPlacementCases), so capacity is counted
  // from the CLASS width - the narrowest row in the class. The class that governs PLACEMENT is the one the
  // MEASURED container falls in, never the viewport's: capacity and the width clamp then read ONE width, so a
  // narrow scene inside a wide viewport cannot dock a pane its own box cannot seat. The `breakpoint` argument
  // stays the media-query (viewport) class and drives sheet CHROME only - bottom sheet in compact, side sheet
  // elsewhere. The rail is subtracted in both, because it takes the row's space before the dock does.
  const placementClass = Number.isFinite(shellInlineSize) ? shellBreakpoint(shellInlineSize) : breakpoint
  const paneCapacity = Number.isFinite(shellInlineSize) ? dockPaneCapacity(SHELL_BREAKPOINTS[placementClass], railInlineSize) : Number.POSITIVE_INFINITY
  const dockedCount = Math.min(openPanels.length, Number.isFinite(paneCapacity) ? paneCapacity * depth : openPanels.length)
  const dockedPanels = openPanels.slice(0, dockedCount)
  const panes: DockPane[] = []
  const renderPanes: DockPane[] = []
  for (let index = 0; index < dockedPanels.length; index += depth) panes.push(pane(dockedPanels.slice(index, index + depth)))
  for (let index = 0; index < openPanels.length; index += depth) renderPanes.push(pane(openPanels.slice(index, index + depth)))
  return {
    openPanels: [...openPanels], spread, root: tree(panes), renderRoot: tree(renderPanes), minimumPaneWidth: DOCK_MINIMUM_PANE_WIDTH,
    containers: openPanels.map((panel, index) => ({ panel, kind: index < dockedCount ? 'docked' : sheetKind })), spreadUnavailable, spreadUnavailableReason, breakpoint, shellInlineSize, railInlineSize, paneCapacity, placementClass,
  }
}

export function openDockPanel(layout: DockLayout, panel: PackPanelDeclaration): DockLayout {
  return layout.openPanels.some(open => open.id === panel.id) ? layout : createDockLayout([...layout.openPanels, panel], layout.spread, layout.breakpoint, layout.shellInlineSize, layout.railInlineSize, layout.spreadUnavailable, layout.spreadUnavailableReason)
}

export function closeDockPanel(layout: DockLayout, panelId: string): DockLayout {
  return createDockLayout(layout.openPanels.filter(panel => panel.id !== panelId), layout.spread, layout.breakpoint, layout.shellInlineSize, layout.railInlineSize, layout.spreadUnavailable, layout.spreadUnavailableReason)
}

export function setDockSpread(layout: DockLayout, spread: boolean): DockLayout {
  return createDockLayout(layout.openPanels, spread, layout.breakpoint, layout.shellInlineSize, layout.railInlineSize, layout.spreadUnavailable, layout.spreadUnavailableReason)
}

export function dockPanes(node: DockNode | null): readonly DockPane[] {
  if (node === null) return []
  return node.kind === 'pane' ? [node] : [...dockPanes(node.first), ...dockPanes(node.second)]
}

export function dockNodeMinimum(node: DockNode, vertical: boolean, containers: ReadonlyMap<string, DockContainerKind>): number {
  if (node.kind === 'pane') {
    const panels = node.panels.filter(panel => containers.get(panel.id) === 'docked')
    // ONE minimum per panel: the clamp reads the SLOT-DERIVED floor the panel actually renders at, never the
    // raw declared number, or a vertical divider could be dragged below the panel's own min-block-size.
    return vertical ? (panels.length ? DOCK_MINIMUM_PANE_WIDTH : 0) : panels.reduce((sum, panel) => sum + panelMinimumHeight(panel), 0)
  }
  const first = dockNodeMinimum(node.first, vertical, containers), second = dockNodeMinimum(node.second, vertical, containers)
  return (node.orientation === 'horizontal') === vertical ? first + second : Math.max(first, second)
}

export function replaceDockNode(root: DockNode, target: DockNode, replacement: DockNode): DockNode {
  return root === target ? replacement : root.kind === 'pane' ? root : { ...root, first: replaceDockNode(root.first, target, replacement), second: replaceDockNode(root.second, target, replacement) }
}

/** Serialisable per-workspace dock state. The HOST persists it; the shell never touches storage. */
export interface DockStateSnapshot { version: 1; openPanelIds: readonly string[]; tree: DockNodeSnapshot | null; widths: Readonly<Record<string, number>> }
export type DockNodeSnapshot = { pane: readonly string[]; fractions: readonly number[] } | { orientation: 'horizontal' | 'vertical'; ratio: number; first: DockNodeSnapshot; second: DockNodeSnapshot }

function snapshotNode(node: DockNode): DockNodeSnapshot {
  return node.kind === 'pane' ? { pane: node.panels.map(panel => panel.id), fractions: [...node.fractions] } : { orientation: node.orientation, ratio: node.ratio, first: snapshotNode(node.first), second: snapshotNode(node.second) }
}

export function serializeDockState(openPanelIds: readonly string[], root: DockNode | null, widths: Readonly<Record<string, number>>): DockStateSnapshot {
  return { version: 1, openPanelIds: [...openPanelIds], tree: root === null ? null : snapshotNode(root), widths: { ...widths } }
}

const ratio = (value: unknown): number => typeof value === 'number' && Number.isFinite(value) ? Math.min(1, Math.max(0, value)) : 0.5

function readNode(value: unknown, known: ReadonlyMap<string, PackPanelDeclaration>): DockNode | null {
  if (typeof value !== 'object' || value === null) return null
  const node = value as Record<string, unknown>
  if (Array.isArray(node.pane)) {
    const panels = node.pane.map(id => typeof id === 'string' ? known.get(id) : undefined).filter((panel): panel is PackPanelDeclaration => panel !== undefined)
    if (panels.length === 0) return null
    const declared = Array.isArray(node.fractions) ? node.fractions : []
    const fractions = panels.map((_, index) => ratio(declared[index]))
    const total = fractions.reduce((sum, part) => sum + part, 0)
    return { kind: 'pane', panels, fractions: total > 0 ? fractions.map(part => part / total) : panels.map(() => 1 / panels.length) }
  }
  const first = readNode(node.first, known), second = readNode(node.second, known)
  if (first === null || second === null) return first ?? second
  return { kind: 'split', orientation: node.orientation === 'vertical' ? 'vertical' : 'horizontal', ratio: ratio(node.ratio), first, second }
}

/** Total: a malformed or stale snapshot yields null or a pruned state, never a throw. */
export function readDockState(value: unknown, panels: readonly PackPanelDeclaration[]): { openPanelIds: readonly string[]; root: DockNode | null; widths: Record<string, number> } | null {
  if (typeof value !== 'object' || value === null) return null
  const state = value as Record<string, unknown>
  if (state.version !== 1) return null
  const declared = new Map(panels.map(panel => [panel.id, panel]))
  const openPanelIds = (Array.isArray(state.openPanelIds) ? state.openPanelIds : []).filter((id): id is string => typeof id === 'string' && declared.has(id))
  const open = new Map(openPanelIds.map(id => [id, declared.get(id)!]))
  const widths: Record<string, number> = {}
  for (const [id, width] of Object.entries(typeof state.widths === 'object' && state.widths !== null ? state.widths as Record<string, unknown> : {})) if (declared.has(id) && typeof width === 'number' && Number.isFinite(width)) widths[id] = Math.max(DOCK_MINIMUM_PANE_WIDTH, Math.round(width))
  return { openPanelIds, root: readNode(state.tree, open), widths }
}
