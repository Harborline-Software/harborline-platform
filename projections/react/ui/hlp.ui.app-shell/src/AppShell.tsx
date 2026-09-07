import * as React from 'react'
import type { HeldRoleSet, RoleVocabulary } from '@harborline-software/contracts/authorization'
import { APP_LAYOUT_RAIL_QUERY, AppLayout } from '@harborline-platform/hlp.ui.app-layout'
import { DockDivider } from './DockDivider'
import { ScopeSwitcher, type ScopeSwitcherProps } from './ScopeSwitcher'
import { ShellRail, validateNav } from './ShellRail'
import { persistenceKey, useShellAxis, useShellShortcuts, type ShellStorage } from './shell-state'
import { mapPackNavigationDeclaration, type ShellWorkspaceViewModel } from './pack-navigation-mapper'
import { DOCK_MINIMUM_PANE_WIDTH, clampDockWidth, createDockLayout, dockNodeMinimum, readDockState, replaceDockNode, serializeDockState, type DockContainerKind, type DockNode, type DockStateSnapshot } from './dock-model'
import { SHELL_LAYOUT, panelEarnsOverflow, panelHeaderForm, panelMinimumHeight, roundHalfUp, shellAddress, shellBreakpoint, type PackNavigationDeclaration, type PackPanelDeclaration, type ShellBreakpoint, type ShellFooterIdentity, type ShellNavigationState, type ShellNavItem, type ShellNavThread, type ShellSystemItem } from './types'

export const APP_SHELL_RAIL_QUERY = APP_LAYOUT_RAIL_QUERY
const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean'
const isString = (value: unknown): value is string => typeof value === 'string'
const isNumber = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value > 0
const isStringArray = (value: unknown): value is string[] => Array.isArray(value) && value.every(isString)

export interface AppShellProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  shellId: string; navigation: PackNavigationDeclaration; navigationState?: ShellNavigationState; resolveLabel?: (labelKey: string) => string; onBindingInvoke?: (binding: string) => void; roleVocabulary: RoleVocabulary; heldRoles: HeldRoleSet; body: React.ReactNode
  brand?: React.ReactNode; brandText?: string; tenantMarkLabel?: string
  activeItemId?: string; onNavigate?: (item: ShellNavItem) => void
  onThreadActivate?: (thread: ShellNavThread, ownerId: string) => void; onThreadAction?: (thread: ShellNavThread, actionId: string) => void
  onThreadRename?: (thread: ShellNavThread, value: string) => void; onThreadRenameCancel?: (thread: ShellNavThread) => void
  collapsed?: boolean; defaultCollapsed?: boolean; onCollapsedChange?: (collapsed: boolean) => void
  railWidth?: number; defaultRailWidth?: number; onRailWidthChange?: (width: number) => void; railResizeLabel?: string
  pinnedItemIds?: readonly string[]; defaultPinnedItemIds?: readonly string[]; onPinsChange?: (ids: readonly string[]) => void; pinCap?: number; groupCap?: number; workspaceCap?: number; recentCap?: number
  activeWorkspaceId?: string; defaultActiveWorkspaceId?: string; onWorkspaceChange?: (id: string) => void
  workspacePinnedIds?: readonly string[]; defaultWorkspacePinnedIds?: readonly string[]; onWorkspacePinnedChange?: (ids: readonly string[]) => void
  headerSwitcher?: Omit<ScopeSwitcherProps, 'shellId' | 'storage' | 'presentation'>
  headerLeading?: React.ReactNode; headerCenter?: React.ReactNode; windowMenu?: React.ReactNode
  pageHeader?: React.ReactNode; railFooter?: React.ReactNode
  footerIdentity?: ShellFooterIdentity; systemItems?: readonly ShellSystemItem[]
  notificationCount?: number; onNotificationsCommand?: () => void; onPilotCommand?: () => void; onInspectorCommand?: () => void
  notificationLabel?: string; pilotLabel?: string; findLabel?: string; windowMenuLabel?: string
  endPanel?: React.ReactNode; endPanelLabel?: string; endPanelOpen?: boolean; defaultEndPanelOpen?: boolean; onEndPanelOpenChange?: (open: boolean) => void
  endPanelExpanded?: boolean; defaultEndPanelExpanded?: boolean; onEndPanelExpandedChange?: (expanded: boolean) => void
  endPanelWidth?: number; onEndPanelWidthChange?: (width: number) => void; endPanelResizeLabel?: string; endPanelExpandLabel?: string; endPanelRestoreLabel?: string; endPanelCloseLabel?: string
  openPanelIds?: readonly string[]; defaultOpenPanelIds?: readonly string[]; onOpenPanelIdsChange?: (ids: readonly string[]) => void; panelContent?: (panel: PackPanelDeclaration) => React.ReactNode
  defaultDockState?: unknown; onDockStateChange?: (state: DockStateSnapshot) => void; dockResizeLabel?: string
  panelToolbar?: (panel: PackPanelDeclaration) => React.ReactNode; panelOpenItem?: (panel: PackPanelDeclaration) => ShellPanelOpenItem | undefined
  onPanelPopOut?: (request: ShellPanelPopOutRequest) => void
  panelOverflowLabel?: string; panelPopOutLabel?: string; panelExpandLabel?: string; panelRestoreLabel?: string; panelTreeLabel?: string
  spread?: boolean; defaultSpread?: boolean; onSpreadChange?: (spread: boolean) => void; spreadUnavailable?: boolean; spreadUnavailableReason?: string
  storage?: ShellStorage
  shortcuts?: { toggleNavigation?: string | null; openSearch?: string | null; toggleRail?: string | null; commandSurface?: string | null; create?: string | null; inspector?: string | null }
  onSearchCommand?: () => void
  headerFixed?: boolean; contentScroll?: 'main' | 'page'; mobileNavOpen?: boolean; defaultMobileNavOpen?: boolean; onMobileNavOpenChange?: (open: boolean) => void
  mobileNavLabel?: string; railCapable?: boolean; pinnedLabel?: string; pinnedEmptyLabel?: string; groupsLabel?: string; workspacesLabel?: string; recentLabel?: string; suggestedLabel?: string; moreActionsLabel?: string
}

function useRailCapable(override: boolean | undefined) {
  const [matches, setMatches] = React.useState(() => override ?? (typeof window !== 'undefined' && window.matchMedia(APP_SHELL_RAIL_QUERY).matches))
  React.useEffect(() => {
    if (override !== undefined) { setMatches(override); return }
    const query = window.matchMedia(APP_SHELL_RAIL_QUERY); const update = (event: MediaQueryListEvent) => setMatches(event.matches)
    setMatches(query.matches); query.addEventListener('change', update); return () => query.removeEventListener('change', update)
  }, [override])
  return matches
}

// The shell clamps and counts panes against the MEASURED inline size of its own box, so a narrow scene inside a
// wide viewport is sized correctly and the Blazor twin (which measures the same box) cannot diverge. The viewport
// is the fallback until the first measurement arrives, and where ResizeObserver is unavailable.
// The second return value is whether the box has been MEASURED - the shell has either heard from the observer
// or learned there will be no observer. Nothing that has to be remembered against a placement class, and nothing
// that is written back to the host, may happen before it turns true, or a pre-measurement guess is stamped and
// persisted as if someone had authored it.
function useShellInlineSize(element: React.RefObject<HTMLDivElement | null>, fallback: number): readonly [number, boolean] {
  const [measured, setMeasured] = React.useState<number | null>(null)
  const [measurementSettled, setMeasurementSettled] = React.useState(false)
  React.useEffect(() => {
    const node = element.current
    if (node === null || typeof ResizeObserver === 'undefined') { setMeasurementSettled(true); return }
    // Only a report the shell ACCEPTS settles the measurement. A shell mounted hidden (a collapsed tab, a
    // display:none route, content-visibility) reports width 0 first; treating that as measured would stamp the
    // restored tree with the pre-measurement VIEWPORT class, and the real width that follows would then read as
    // a class crossing and emit tree:null. Blazor is immune the same way (measuredInlineSize takes widths > 0).
    const observer = new ResizeObserver(entries => { const width = entries[0]?.contentRect.width ?? 0; if (width <= 0) return; setMeasured(width); setMeasurementSettled(true) })
    observer.observe(node)
    return () => observer.disconnect()
  }, [element])
  return [measured ?? fallback, measurementSettled] as const
}

function useBreakpoint() {
  const current = () => { const width = typeof window === 'undefined' ? 1200 : window.innerWidth; return { width, breakpoint: shellBreakpoint(width) } }
  const [viewport, setViewport] = React.useState(current)
  React.useEffect(() => { const update = () => setViewport(current()); window.addEventListener('resize', update); return () => window.removeEventListener('resize', update) }, [])
  return viewport
}

export function AppShell({ shellId, navigation, navigationState, resolveLabel, onBindingInvoke, roleVocabulary, heldRoles, body, brand, brandText = 'Harborline', tenantMarkLabel = 'Tenant', activeItemId, onNavigate, onThreadActivate, onThreadAction, onThreadRename, onThreadRenameCancel, collapsed, defaultCollapsed = false, onCollapsedChange, railWidth, defaultRailWidth = SHELL_LAYOUT.railWidth, onRailWidthChange, railResizeLabel = 'Resize navigation rail — Command backslash toggles it', pinnedItemIds, defaultPinnedItemIds, onPinsChange, pinCap = 8, groupCap = 7, workspaceCap = 7, recentCap = 5, activeWorkspaceId, defaultActiveWorkspaceId, onWorkspaceChange, workspacePinnedIds, defaultWorkspacePinnedIds, onWorkspacePinnedChange, headerSwitcher, headerLeading, headerCenter, windowMenu, pageHeader, railFooter, footerIdentity, systemItems = [], notificationCount = 0, onNotificationsCommand, onPilotCommand, onInspectorCommand, notificationLabel = 'Notifications', pilotLabel = 'Pilot', findLabel = 'Find', windowMenuLabel = 'Window menu', endPanel, endPanelLabel = 'Panel', endPanelOpen, defaultEndPanelOpen = false, onEndPanelOpenChange, endPanelExpanded, defaultEndPanelExpanded = false, onEndPanelExpandedChange, endPanelWidth = 400, onEndPanelWidthChange, endPanelResizeLabel = 'Resize panel', endPanelExpandLabel = 'Expand panel', endPanelRestoreLabel = 'Restore panel', endPanelCloseLabel = 'Close panel', openPanelIds, defaultOpenPanelIds, onOpenPanelIdsChange, panelContent, defaultDockState, onDockStateChange, dockResizeLabel = 'Resize the dock', panelToolbar, panelOpenItem, onPanelPopOut, panelOverflowLabel = 'More actions in', panelPopOutLabel = 'Pop out', panelExpandLabel = 'Expand', panelRestoreLabel = 'Restore', panelTreeLabel = 'Toggle the tree in', spread, defaultSpread = false, onSpreadChange, spreadUnavailable = false, spreadUnavailableReason, storage, shortcuts, onSearchCommand, headerFixed, contentScroll, mobileNavOpen, defaultMobileNavOpen, onMobileNavOpenChange, mobileNavLabel = 'Navigation', railCapable: railCapableOverride, pinnedLabel = 'Pinned', pinnedEmptyLabel = 'Nothing pinned yet', groupsLabel = 'Groups', workspacesLabel = 'Workspaces', recentLabel = 'Recent', suggestedLabel = 'Suggested', moreActionsLabel = 'Panels', className, style, ...attributes }: AppShellProps) {
  if (!shellId || !/^[a-z0-9-]+$/.test(shellId)) throw new Error('app-shell-id-required')
  if (body === null || body === undefined) throw new Error('app-shell-body-required')
  const navigationView = React.useMemo(() => mapPackNavigationDeclaration(navigation, { resolveLabel, state: navigationState, roleVocabulary, heldRoles }), [navigation, navigationState, resolveLabel, roleVocabulary, heldRoles])
  const { workspaces, modes, panels } = navigationView
  const notificationsAvailable = panels.some(panel => panel.id === 'notifications')
  const pilotAvailable = panels.some(panel => panel.id === 'pilot')
  validateNav(workspaces)
  const railCapable = useRailCapable(railCapableOverride); const viewport = useBreakpoint(); const breakpoint = viewport.breakpoint
  const shellElement = React.useRef<HTMLDivElement>(null); const [shellInlineSize, measurementSettled] = useShellInlineSize(shellElement, viewport.width)
  const [isCollapsed, setCollapsed] = useShellAxis(collapsed, defaultCollapsed, persistenceKey(shellId, 'collapsed'), storage, isBoolean)
  const [storedRailWidth, setRailWidth] = useShellAxis(railWidth, defaultRailWidth, persistenceKey(shellId, 'rail-width'), storage, isNumber)
  const [pins, setPins] = useShellAxis<readonly string[]>(pinnedItemIds, defaultPinnedItemIds ?? [], persistenceKey(shellId, 'pins'), storage, isStringArray)
  const [activeWs, setActiveWs] = useShellAxis(activeWorkspaceId, defaultActiveWorkspaceId ?? workspaces[0]?.id ?? '', persistenceKey(shellId, 'switcher', 'workspace', 'active'), storage, isString)
  const [panelOpen, setPanelOpen] = useShellAxis(endPanelOpen, defaultEndPanelOpen, null, undefined, isBoolean)
  const [panelExpanded, setPanelExpanded] = useShellAxis(endPanelExpanded, defaultEndPanelExpanded, null, undefined, isBoolean)
  const [restoredDock] = React.useState(() => readDockState(defaultDockState, panels))
  const [dockWidths, setDockWidths] = React.useState<Record<string, number>>(() => restoredDock?.widths ?? {})
  const [orderedOpenIds, setOrderedOpenIds] = useShellAxis<readonly string[]>(openPanelIds, defaultOpenPanelIds ?? restoredDock?.openPanelIds ?? panels.filter(panel => panel.defaultOpen).map(panel => panel.id), null, undefined, isStringArray)
  const [dockSpread, setDockSpread] = useShellAxis(spread, defaultSpread, null, undefined, isBoolean)
  const workspace = React.useMemo(() => workspaces.find(candidate => candidate.id === activeWs), [workspaces, activeWs])
  const corrected = React.useRef<string | null>(null)
  React.useEffect(() => { if (activeWorkspaceId !== undefined && !workspace && workspaces.length > 0 && corrected.current !== activeWorkspaceId) { corrected.current = activeWorkspaceId; onWorkspaceChange?.(workspaces[0].id) } }, [activeWorkspaceId, workspace, workspaces, onWorkspaceChange])
  const toggleRail = React.useCallback(() => { const next = !isCollapsed; setCollapsed(next); onCollapsedChange?.(next) }, [isCollapsed, setCollapsed, onCollapsedChange])
  const activateWorkspace = React.useCallback((index: number) => { const next = workspaces[index]; if (!next) return; setActiveWs(next.id); onWorkspaceChange?.(next.id) }, [workspaces, setActiveWs, onWorkspaceChange])
  const create = workspace?.createActions?.find(action => action.id === workspace.defaultCreateActionId) ?? workspace?.createActions?.[0]
  useShellShortcuts({ toggleRail: shortcuts?.toggleRail ?? shortcuts?.toggleNavigation, commandSurface: shortcuts?.commandSurface ?? shortcuts?.openSearch, create: shortcuts?.create, inspector: shortcuts?.inspector, onToggleRail: toggleRail, onCommandSurface: () => onSearchCommand?.(), onCreate: () => { if ((workspace?.createActions.length ?? 0) <= 3 && create) onBindingInvoke?.(create.binding) }, onInspector: () => onInspectorCommand?.(), onWorkspace: activateWorkspace })
  const openDeclaredPanel = (panel: PackPanelDeclaration) => { if (orderedOpenIds.includes(panel.id)) return false; const next = [...orderedOpenIds, panel.id]; setOrderedOpenIds(next); onOpenPanelIdsChange?.(next); onBindingInvoke?.(panel.binding); return true }
  const closeDeclaredPanel = (id: string) => { const next = orderedOpenIds.filter(panelId => panelId !== id); setOrderedOpenIds(next); onOpenPanelIdsChange?.(next) }

  const railDrag = React.useRef<{ x: number; width: number } | null>(null)
  const updateRailWidth = (next: number) => { const width = Math.max(120, roundHalfUp(next)); setRailWidth(width); onRailWidthChange?.(width) }
  const sideNav = <div className="hl-app-shell__rail" data-shell-region="rail">
    <section className="hl-app-shell__rail-head" data-shell-zone="head" aria-label="Tenant"><span className="hl-app-shell__wordmark">{brandText}</span></section>
    <div className="hl-app-shell__rail-scroll" data-shell-scroll-region>
      {modes.length ? <section className="hl-app-shell__zone" data-shell-zone="mode">{modes.map(mode => <button key={mode.id} type="button" className="hl-app-shell__mode" onClick={() => { const index = workspaces.findIndex(candidate => candidate.id === mode.workspaceIds[0]); if (index >= 0) activateWorkspace(index) }}>{mode.label}</button>)}</section> : null}
      <PrimaryAction workspace={workspace} onBindingInvoke={onBindingInvoke} />
      <LimitedZone label={workspacesLabel} zone="workspaces" cap={workspaceCap} items={workspaces} render={candidate => <a className="hl-app-shell__rail-link" href={shellAddress('workspaces', candidate.id)} aria-current={candidate.id === workspace?.id ? 'page' : undefined} onClick={() => activateWorkspace(workspaces.indexOf(candidate))}>{candidate.icon}<span className="hl-app-shell__rail-label">{candidate.label}</span>{candidate.count !== undefined ? <span className="hl-app-shell__count" data-shell-count>{candidate.count}</span> : null}</a>} />
      {workspace ? <ShellRail workspace={workspace} activeItemId={activeItemId} pinnedItemIds={pins} pinCap={pinCap} groupCap={groupCap} pinnedLabel={pinnedLabel} pinnedEmptyLabel={pinnedEmptyLabel} groupsLabel={groupsLabel} onBindingInvoke={onBindingInvoke} onNavigate={onNavigate} onPinToggle={(id, nextPinned) => { const next = nextPinned ? [...pins, id] : pins.filter(pin => pin !== id); setPins(next); onPinsChange?.(next) }} onThreadActivate={onThreadActivate} onThreadAction={onThreadAction} onThreadRename={onThreadRename} onThreadRenameCancel={onThreadRenameCancel} /> : <section className="hl-app-shell__zone" data-shell-zone="pinned" data-zone-cap={pinCap}><div className="hl-app-shell__zone-label">{pinnedLabel}</div><p className="hl-app-shell__zone-empty">{pinnedEmptyLabel}</p></section>}
      {workspace?.recent?.length ? <LimitedZone label={recentLabel} zone="recent" cap={recentCap} items={workspace.recent} render={item => <RailLink item={item} activeItemId={activeItemId} onNavigate={onNavigate} />} /> : null}
      {workspace?.suggested ? <section className="hl-app-shell__zone" data-shell-zone="suggested" data-zone-cap="1" aria-label={suggestedLabel}><div className="hl-app-shell__zone-label">{suggestedLabel}</div><RailLink item={workspace.suggested} activeItemId={activeItemId} onNavigate={onNavigate} /></section> : null}
    </div>
    <section className="hl-app-shell__rail-footer" data-shell-zone="footer" aria-label="Account and system">
      {footerIdentity ? <div className="hl-app-shell__identity" data-shell-row>{footerIdentity.icon}<span className="hl-app-shell__rail-label">{footerIdentity.label}</span><span className="hl-app-shell__identity-role">{footerIdentity.role}</span></div> : null}
      {systemItems.map(item => <a key={item.id} className="hl-app-shell__rail-link" href={shellAddress('system', item.id)} onClick={() => item.invoke?.()}>{item.icon}<span className="hl-app-shell__rail-label">{item.label}</span></a>)}
      {railFooter}
    </section>
    <div className="hl-app-shell__rail-resize" role="separator" tabIndex={0} aria-orientation="vertical" aria-label={railResizeLabel} aria-valuemin={SHELL_LAYOUT.railMinimum} aria-valuemax={10000} aria-valuenow={storedRailWidth} title={railResizeLabel} onPointerDown={event => { railDrag.current = { x: event.clientX, width: storedRailWidth }; event.currentTarget.setPointerCapture?.(event.pointerId) }} onPointerMove={event => { if (railDrag.current) updateRailWidth(railDrag.current.width + (event.clientX - railDrag.current.x) * (getComputedStyle(event.currentTarget).direction === 'rtl' ? -1 : 1)) }} onPointerUp={() => { railDrag.current = null }} onKeyDown={event => { if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') { event.preventDefault(); const direction = event.key === 'ArrowRight' ? 1 : -1; updateRailWidth(storedRailWidth + direction * (document.dir === 'rtl' ? -8 : 8)) } }} />
  </div>

  const header = <div className="hl-app-shell__header" data-shell-region="bar">
    <div className="hl-app-shell__bar-slot hl-app-shell__mark" data-shell-bar-slot="mark" data-tenant-mark role="img" aria-label={tenantMarkLabel}>{brand ?? <span aria-hidden="true">{brandText.slice(0, 1)}</span>}</div>
    <div className="hl-app-shell__bar-slot" data-shell-bar-slot="window-menu">{windowMenu ?? <button type="button" className="hl-app-shell__bar-control" aria-label={windowMenuLabel}><MenuIcon /></button>}{headerLeading}{headerSwitcher ? <ScopeSwitcher {...headerSwitcher} shellId={shellId} storage={storage} presentation="header" /> : null}</div>
    <div className="hl-app-shell__bar-slot" data-shell-bar-slot="rail-toggle"><button type="button" className="hl-app-shell__bar-control" aria-label={mobileNavLabel} aria-keyshortcuts="Control+\\ Meta+\\" aria-expanded={!isCollapsed} onClick={toggleRail}><RailIcon /></button></div>
    <div className="hl-app-shell__bar-slot" data-shell-bar-slot="find"><button type="button" className="hl-app-shell__bar-control" aria-label={findLabel} aria-keyshortcuts="Control+K Meta+K" onClick={onSearchCommand}><SearchIcon /></button></div>
    <div className="hl-app-shell__bar-slot hl-app-shell__breadcrumb" data-shell-bar-slot="breadcrumb">{headerCenter ?? workspace?.label ?? workspacesLabel}</div>
    <div className="hl-app-shell__bar-slot hl-app-shell__cluster" data-shell-bar-slot="cluster">
      {notificationsAvailable ? <button type="button" className="hl-app-shell__bar-control" aria-label={notificationLabel} onClick={() => { if (openDeclaredPanel(panels.find(panel => panel.id === 'notifications')!)) onNotificationsCommand?.() }}><BellIcon />{notificationCount > 0 ? <span className="hl-app-shell__notification-count" data-notification-count>{notificationCount}</span> : null}</button> : null}
      {pilotAvailable ? <button type="button" className="hl-app-shell__bar-control hl-app-shell__pilot" aria-label={pilotLabel} onClick={() => { if (openDeclaredPanel(panels.find(panel => panel.id === 'pilot')!)) onPilotCommand?.() }}>{pilotLabel}</button> : null}
      <PanelActions panels={panels} resolveLabel={resolveLabel} onOpen={openDeclaredPanel} label={moreActionsLabel} breakpoint={breakpoint} />
    </div>
  </div>

  const declaredOpenPanels = orderedOpenIds.map(id => panels.find(panel => panel.id === id)).filter((panel): panel is PackPanelDeclaration => panel !== undefined)
  // The rail only takes row space when it is on screen; capacity and the width clamp both count it, so the row
  // never holds a docked pane it cannot fit (rail + dock + content floor <= the measured shell width).
  const railInlineSize = railCapable && !isCollapsed ? storedRailWidth : 0
  const dockLayout = createDockLayout(declaredOpenPanels, dockSpread, breakpoint, shellInlineSize, railInlineSize, spreadUnavailable, spreadUnavailableReason); const showDock = panelContent !== undefined && dockLayout.renderRoot !== null
  const showPanel = panelOpen && endPanel !== null && endPanel !== undefined; const expandedFill = showPanel && (panelExpanded || !railCapable); const docked = showPanel && !expandedFill
  const panelAside = showPanel ? <EndPanel label={endPanelLabel} docked={docked} width={endPanelWidth} onWidthChange={onEndPanelWidthChange} resizeLabel={endPanelResizeLabel} expanded={panelExpanded} expandLabel={endPanelExpandLabel} restoreLabel={endPanelRestoreLabel} closeLabel={endPanelCloseLabel} onToggleExpanded={() => { const next = !panelExpanded; setPanelExpanded(next); onEndPanelExpandedChange?.(next) }} onClose={() => { setPanelOpen(false); onEndPanelOpenChange?.(false) }}>{endPanel}</EndPanel> : null
  // The AUTHORED tree and the DERIVED placement are two different things, so they carry two different
  // invalidation sets and never share a field: dockLayout above is rebuilt every render from every input, and
  // the tree the user dragged (or the host restored) is remembered against the open set, the spread and the
  // PLACEMENT class only - the class the measured box falls in. Placement is constant inside a class, so a
  // re-measurement within one keeps the arrangement while crossing a class stops describing it; the remembered
  // {signature, root} is kept either way, so coming back to that class restores it rather than re-deriving.
  // The rail is deliberately absent: it moves placement and the clamp, never what the user arranged.
  const signature = JSON.stringify([declaredOpenPanels, dockSpread, dockLayout.placementClass])
  // ...and nothing is STAMPED with a class before the box has been measured. A restored tree (or one dragged
  // in that first paint) is held PENDING: it is rendered, so the first paint is the arrangement the user left
  // rather than a derived guess that flashes, but it carries no signature and is never emitted. The first
  // measurement stamps whatever is pending with the class the MEASURED box falls in - the pre-measurement
  // viewport class would make a narrow scene inside a wide viewport read as a class crossing, and the shell
  // would then emit tree:null and the host would persist the loss.
  const [resizedTree, setResizedTree] = React.useState<{ signature: string; root: DockNode } | null>(null)
  const [stamped, setStamped] = React.useState(false)
  // The restored root is held as STATE, not re-read from the restore each render, so a reset can clear it while
  // the shell is still unmeasured - Blazor's ResetDock clears pendingRestoredRoot for the same reason.
  const [pendingRestoredRoot, setPendingRestoredRoot] = React.useState<DockNode | null>(() => restoredDock?.root ?? null)
  if (!stamped && measurementSettled) { setStamped(true); const pending = resizedTree?.root ?? pendingRestoredRoot; if (pending !== null) setResizedTree({ signature, root: pending }) }
  const pendingRoot = !stamped ? resizedTree?.root ?? pendingRestoredRoot : null
  const authoredRoot = pendingRoot ?? (resizedTree?.signature === signature ? resizedTree.root : null)
  const root = authoredRoot ?? dockLayout.renderRoot
  const resetDock = () => { setResizedTree(null); setPendingRestoredRoot(null) }
  const resizeNode = (target: DockNode, replacement: DockNode) => setResizedTree({ signature, root: replaceDockNode(root!, target, replacement) })
  const containerKinds = new Map(dockLayout.containers.map(container => [container.panel.id, container.kind]))
  // chrome-spec: dock width belongs to the panel that opened the dock — the first still-DOCKED panel in open
  // order. A sheet never carries one, so a panel demoted to a sheet leaves the dock with no remembered width.
  const openingPanel = declaredOpenPanels.find(panel => containerKinds.get(panel.id) === 'docked')
  // The clamp is the model's (dock-model.ts), applied to every requested width alike — dragged, remembered and
  // restored — so no route can push the aside past the content floor.
  const dockWidth = openingPanel ? clampDockWidth(dockWidths[openingPanel.id] ?? openingPanel.defaultWidth, shellInlineSize, railInlineSize) : null
  const dockDrag = React.useRef<{ x: number; width: number } | null>(null)
  const setDockWidth = (next: number) => { if (openingPanel) setDockWidths({ ...dockWidths, [openingPanel.id]: clampDockWidth(next, shellInlineSize, railInlineSize) }) }
  // ONE seam, keyed by VALUE: the emitted JSON is the effect's dependency, so a re-derived-but-equal tree emits
  // nothing and an in-place-equal state cannot loop. The host persists it; the shell never touches storage.
  // ...and only the AUTHORED tree is persisted: a derived reset is emitted as null, or the host would reload
  // it as an arrangement someone asked for.
  const dockStateJson = JSON.stringify(serializeDockState(orderedOpenIds, authoredRoot, dockWidths))
  React.useEffect(() => { if (!measurementSettled) return; onDockStateChange?.(JSON.parse(dockStateJson) as DockStateSnapshot) }, [dockStateJson, measurementSettled])
  // chrome-spec §6:318-330 — the container owns the chrome. Expand and the tree toggle are the shell's own state;
  // pop out is a DECLARED action that only asks the host, and the panel stays docked until the host confirms
  // through the same persistence seam (it drops the id from the open set).
  const [expandedPanelId, setExpandedPanelId] = React.useState<string | null>(null)
  const [treeHidden, setTreeHidden] = React.useState<ReadonlySet<string>>(() => new Set())
  const chrome: PanelChrome = {
    content: panelContent!, toolbar: panelToolbar, openItem: panelOpenItem, resolveLabel, invokeBinding: onBindingInvoke,
    close: closeDeclaredPanel, containers: containerKinds,
    popOut: onPanelPopOut === undefined ? undefined : panelId => onPanelPopOut({ panelId, state: JSON.parse(dockStateJson) as DockStateSnapshot }),
    expandedPanelId, setExpanded: setExpandedPanelId, treeHidden, toggleTree: id => setTreeHidden(previous => { const next = new Set(previous); if (!next.delete(id)) next.add(id); return next }),
    labels: { overflow: panelOverflowLabel, popOut: panelPopOutLabel, expand: panelExpandLabel, restore: panelRestoreLabel, tree: panelTreeLabel },
  }
  const dockAside = showDock ? <aside className="hl-app-shell__dock" data-shell-region="dock" data-shell-spread={dockLayout.spread} data-has-docked={dockLayout.containers.some(container => container.kind === 'docked')} style={dockWidth === null ? undefined : { ['--hl-app-shell-dock-size' as string]: `${dockWidth}px` }}>
    {renderDockNode(root!, chrome, resizeNode, resetDock)}
    {dockLayout.root ? <button type="button" className="hl-app-shell__reset" aria-label="Reset panels" onClick={resetDock}>Reset panels</button> : null}
    <button type="button" className="hl-app-shell__spread" aria-label="Spread panels" disabled={dockLayout.spreadUnavailable} onClick={() => { const next = !dockSpread; setDockSpread(next); onSpreadChange?.(next) }}>Spread</button>
    {dockLayout.spreadUnavailable && dockLayout.spreadUnavailableReason ? <span data-spread-unavailable-reason role="status">{dockLayout.spreadUnavailableReason}</span> : null}
    {openingPanel ? <div className="hl-app-shell__dock-resize" role="separator" tabIndex={0} aria-orientation="vertical" aria-label={dockResizeLabel} title={dockResizeLabel} aria-valuemin={DOCK_MINIMUM_PANE_WIDTH} aria-valuemax={10000} aria-valuenow={dockWidth!} data-opening-panel-id={openingPanel.id}
      onPointerDown={event => { dockDrag.current = { x: event.clientX, width: dockWidth! }; event.currentTarget.setPointerCapture?.(event.pointerId) }}
      onPointerMove={event => { const start = dockDrag.current; if (start) setDockWidth(start.width + (start.x - event.clientX) * (getComputedStyle(event.currentTarget).direction === 'rtl' ? -1 : 1)) }}
      onPointerUp={() => { dockDrag.current = null }}
      onKeyDown={event => { if (event.ctrlKey || event.metaKey || event.altKey) return; if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') return; event.preventDefault(); setDockWidth(dockWidth! + (event.key === 'ArrowLeft' ? 8 : -8)) }} /> : null}
  </aside> : null
  const inner = showPanel || showDock ? <div className="hl-app-shell__content-row"><div className="hl-app-shell__page" data-shell-region="content" data-shell-scroll-region data-hidden={expandedFill || undefined}>{body}</div>{dockAside}{panelAside}</div> : <div className="hl-app-shell__page" data-shell-region="content" data-shell-scroll-region>{body}</div>
  const content = pageHeader !== null && pageHeader !== undefined ? <div className="hl-app-shell__content-col"><div className="hl-app-shell__page-header" data-shell-region="pageHeader">{pageHeader}</div>{inner}</div> : inner
  return <div {...attributes} ref={shellElement} className={`hl-app-shell${className ? ` ${className}` : ''}`} style={{ ...style, ['--hl-app-shell-rail-size' as string]: `${storedRailWidth}px` }} data-shell-id={shellId} data-shell-breakpoint={breakpoint} data-shell-content-floor={SHELL_LAYOUT.contentFloor} data-collapsed={isCollapsed || undefined} data-open-panel-count={declaredOpenPanels.length || undefined} data-spread={dockSpread} data-end-panel-open={showPanel || undefined} data-end-panel-expanded={expandedFill || undefined}>
    <AppLayout body={content} header={header} sideNav={sideNav} sideNavOpen={!isCollapsed} headerFixed={headerFixed} contentScroll={contentScroll} mobileNavOpen={mobileNavOpen} defaultMobileNavOpen={defaultMobileNavOpen} onMobileNavOpenChange={onMobileNavOpenChange} mobileNavLabel={mobileNavLabel} railCapable={railCapableOverride} />
  </div>
}

function LimitedZone<T extends { id: string }>({ label, zone, cap, items, render }: { label: string; zone: string; cap: number; items: readonly T[]; render: (item: T) => React.ReactNode }) {
  const [expanded, setExpanded] = React.useState(false); const visible = expanded ? items : items.slice(0, cap)
  return <section className="hl-app-shell__zone" data-shell-zone={zone} data-zone-cap={cap} aria-label={label}><div className="hl-app-shell__zone-label">{label}</div>{visible.map(item => <div key={item.id} className="hl-app-shell__rail-row" data-shell-row>{render(item)}</div>)}{!expanded && items.length > cap ? <button type="button" className="hl-app-shell__show-more" onClick={() => setExpanded(true)}>Show {items.length - cap} more</button> : null}</section>
}

function RailLink({ item, activeItemId, onNavigate }: { item: ShellNavItem; activeItemId?: string; onNavigate?: (item: ShellNavItem) => void }) { return <a className="hl-app-shell__rail-link" href={shellAddress(item.kind ?? 'workspaces', item.id)} aria-current={item.id === activeItemId ? 'page' : undefined} onClick={() => onNavigate?.(item)}>{item.icon}<span className="hl-app-shell__rail-label">{item.label}</span>{item.count !== undefined ? <span className="hl-app-shell__count" data-shell-count>{item.count}</span> : null}</a> }

function PrimaryAction({ workspace, onBindingInvoke }: { workspace?: ShellWorkspaceViewModel; onBindingInvoke?: (binding: string) => void }) {
  const [open, setOpen] = React.useState(false); const actions = workspace?.createActions ?? []
  if (actions.length === 0 && workspace?.createGuidance) return <section className="hl-app-shell__zone" data-shell-zone="primary-action"><p className="hl-app-shell__capability-guidance" data-capability-guidance>{workspace.createGuidance}</p></section>
  if (actions.length === 0 || actions.length > 3) return null
  const primary = actions.find(action => action.id === workspace?.defaultCreateActionId) ?? actions[0]
  if (actions.length === 1) return <section className="hl-app-shell__zone" data-shell-zone="primary-action" data-zone-cap="1"><button type="button" className="hl-app-shell__create" aria-keyshortcuts="Control+N Meta+N" onClick={() => onBindingInvoke?.(primary.binding)}>+ {primary.label}</button></section>
  return <section className="hl-app-shell__zone" data-shell-zone="primary-action" data-zone-cap={actions.length}><div className="hl-app-shell__split-create"><button type="button" className="hl-app-shell__create" aria-keyshortcuts="Control+N Meta+N" onClick={() => onBindingInvoke?.(primary.binding)}>+ {primary.label}</button><button type="button" className="hl-app-shell__create-menu" aria-label={`More ${primary.label} actions`} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}><svg aria-hidden="true" width="12" height="12" viewBox="0 0 12 12"><path d="m2 4 4 4 4-4" fill="none" stroke="currentColor" strokeLinecap="round" /></svg></button>{open ? <div role="menu" className="hl-app-shell__row-menu">{actions.filter(action => action.id !== primary.id).map(action => <button key={action.id} type="button" role="menuitem" className="hl-app-shell__row-menu-item" onClick={() => { setOpen(false); onBindingInvoke?.(action.binding) }}>+ {action.label}</button>)}</div> : null}</div></section>
}

function PanelActions({ panels, resolveLabel = key => key, onOpen, label, breakpoint }: { panels: readonly PackPanelDeclaration[]; resolveLabel?: (key: string) => string; onOpen: (panel: PackPanelDeclaration) => void; label: string; breakpoint: ShellBreakpoint }) {
  const large = breakpoint === 'large' || breakpoint === 'extra-large'
  const declared = panels.filter(panel => panel.id !== 'notifications' && panel.id !== 'pilot')
  return <>{large ? declared.map(panel => <div key={panel.id} className="hl-app-shell__panel-action" data-action-id={panel.id}><button type="button" className="hl-app-shell__bar-control" aria-label={resolveLabel(panel.labelKey ?? `panels.${panel.id}`)} onClick={() => onOpen(panel)}>{resolveLabel(panel.labelKey ?? `panels.${panel.id}`)}</button></div>) : null}<PanelsMenu panels={large ? [] : declared} resolveLabel={resolveLabel} onOpen={onOpen} label={label} /></>
}

function PanelsMenu({ panels, resolveLabel, onOpen, label }: { panels: readonly PackPanelDeclaration[]; resolveLabel: (key: string) => string; onOpen: (panel: PackPanelDeclaration) => void; label: string }) { const [open, setOpen] = React.useState(false); if (!panels.length) return null; return <div className="hl-app-shell__actions-overflow"><button type="button" className="hl-app-shell__bar-control" aria-label={label} aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen(!open)}><MenuDotsIcon /></button>{open ? <div role="menu" aria-label={label} className="hl-app-shell__row-menu">{panels.map(panel => <div key={panel.id} role="none" className="hl-app-shell__panel-action" data-action-id={panel.id}><button type="button" role="menuitem" className="hl-app-shell__row-menu-item" onClick={() => { setOpen(false); onOpen(panel) }}>{resolveLabel(panel.labelKey ?? `panels.${panel.id}`)}</button></div>)}</div> : null}</div> }

export interface ShellPanelOpenItem { id: string; label: string; close: () => void }
export interface ShellPanelPopOutRequest { panelId: string; state: DockStateSnapshot }
interface PanelChrome {
  content: (panel: PackPanelDeclaration) => React.ReactNode; toolbar?: (panel: PackPanelDeclaration) => React.ReactNode
  openItem?: (panel: PackPanelDeclaration) => ShellPanelOpenItem | undefined; resolveLabel?: (key: string) => string
  invokeBinding?: (binding: string) => void; close: (id: string) => void; containers: ReadonlyMap<string, DockContainerKind>
  popOut?: (panelId: string) => void; expandedPanelId: string | null; setExpanded: (id: string | null) => void
  treeHidden: ReadonlySet<string>; toggleTree: (id: string) => void
  labels: { overflow: string; popOut: string; expand: string; restore: string; tree: string }
}

function renderDockNode(node: DockNode, chrome: PanelChrome, resize: (target: DockNode, replacement: DockNode) => void, reset: () => void, fraction = 1): React.ReactNode {
  const containers = chrome.containers
  const geometry = { flex: `${fraction} 1 0px`, minInlineSize: dockNodeMinimum(node, true, containers) }
  if (node.kind === 'split') {
    const vertical = node.orientation === 'horizontal', first = dockNodeMinimum(node.first, vertical, containers), second = dockNodeMinimum(node.second, vertical, containers)
    return <div className="hl-app-shell__dock-split" style={geometry} data-orientation={node.orientation} data-split-ratio={node.ratio}>{renderDockNode(node.first, chrome, resize, reset, node.ratio)}{first > 0 && second > 0 ? <DockDivider vertical={vertical} fraction={node.ratio} minimum={first} secondMinimum={second} change={ratio => resize(node, { ...node, ratio })} reset={reset} /> : <div className="hl-app-shell__dock-divider" aria-hidden="true" />}{renderDockNode(node.second, chrome, resize, reset, 1 - node.ratio)}</div>
  }
  // chrome-spec §6:238-240 - where the stack's minimums together exceed the pane, THE PANE scrolls; no panel
  // resolves the conflict by starving its one flexible slot, so the pane carries the overflow, never a body.
  return <div className="hl-app-shell__dock-pane" style={geometry} data-shell-pane-scroll data-stack-depth={node.panels.length} data-pane-minimum-sum={node.panels.reduce((sum, panel) => sum + panelMinimumHeight(panel), 0)} data-has-docked={node.panels.some(panel => containers.get(panel.id) === 'docked')}>{node.panels.map((panel, index) => <React.Fragment key={panel.id}><DockPanel panel={panel} chrome={chrome} fraction={node.fractions[index]} />{index === 0 && node.panels.length === 2 && node.panels.every(p => containers.get(p.id) === 'docked') ? <DockDivider vertical={false} fraction={node.fractions[0]} minimum={panelMinimumHeight(panel)} secondMinimum={panelMinimumHeight(node.panels[1])} change={ratio => resize(node, { ...node, fractions: [ratio, 1 - ratio] })} reset={reset} /> : null}</React.Fragment>)}</div>
}

function DockPanel({ panel, chrome, fraction }: { panel: PackPanelDeclaration; chrome: PanelChrome; fraction: number }) {
  const kind = chrome.containers.get(panel.id) ?? 'docked'
  const label = chrome.resolveLabel?.(panel.labelKey ?? `panels.${panel.id}`) ?? panel.labelKey ?? panel.id
  // chrome-spec §6:188-190 - form 2 is EARNED by opening one item; a panel that does not open one is a title.
  const form = panelHeaderForm(panel)
  const item = form === 'toggle-chip' ? chrome.openItem?.(panel) : undefined
  // "Closing the document brings the tree back, even if it was hidden": no open item, no hidden tree.
  const treeHidden = item !== undefined && chrome.treeHidden.has(panel.id)
  const expanded = chrome.expandedPanelId === panel.id
  // chrome-spec §6:325-328 - docked: the whole group; sheet: close only. The shell never opens a window.
  const docked = kind === 'docked'
  const control = 'hl-app-shell__end-panel-control'
  return <section className="hl-app-shell__dock-panel" data-shell-panel-id={panel.id} data-shell-container-kind={kind} data-panel-header-form={form} data-panel-expanded={expanded || undefined} data-panel-tree={form === 'toggle-chip' ? (treeHidden ? 'hidden' : 'shown') : undefined}
    style={{ minBlockSize: `${panelMinimumHeight(panel)}px`, ['--hl-app-shell-panel-fraction' as string]: fraction }}>
    {/* chrome-spec §6:180-186 - the affordance group is the SHELL's and it lives in the header, so the pop-out
        chord is the HEADER's route: Shift+Enter typed into the body (a note, a filter) is the body's. The
        Blazor mirror is the same handler on the same element, with the browser default suppressed in
        dock-divider.js because :preventDefault cannot vary per key. */}
    <div className="hl-app-shell__end-panel-header"
      onKeyDown={event => { if (!docked || panel.popOut !== true) return; if (event.ctrlKey || event.metaKey || event.altKey || !event.shiftKey || event.key !== 'Enter') return; event.preventDefault(); chrome.popOut?.(panel.id) }}>
      {form === 'toggle-chip' ? <button type="button" className={`${control} hl-app-shell__panel-tree`} data-panel-tree-toggle aria-label={`${chrome.labels.tree} ${label}`} aria-expanded={!treeHidden} onClick={() => chrome.toggleTree(panel.id)}><TreeIcon /></button> : null}
      {item ? <span className="hl-app-shell__panel-chip" data-panel-item-chip data-item-id={item.id}>{item.label}<button type="button" className={control} data-panel-item-close aria-label={`Close ${item.label}`} onClick={item.close}><CloseIcon /></button></span> : <span className="hl-app-shell__end-panel-title">{label}</span>}
      <span className="hl-app-shell__panel-affordances" data-panel-affordances>
        {docked && panelEarnsOverflow(panel) ? <button type="button" className={control} data-panel-overflow aria-haspopup="menu" aria-label={`${chrome.labels.overflow} ${label}`}><MenuDotsIcon /></button> : null}
        {docked && panel.popOut === true ? <button type="button" className={control} data-panel-pop-out aria-keyshortcuts="Shift+Enter" aria-label={`${chrome.labels.popOut} ${label}`} onClick={() => chrome.popOut?.(panel.id)}><PopOutIcon /></button> : null}
        {docked ? <button type="button" className={control} data-panel-expand aria-pressed={expanded} aria-label={`${expanded ? chrome.labels.restore : chrome.labels.expand} ${label}`} onClick={() => chrome.setExpanded(expanded ? null : panel.id)}>{expanded ? <RestoreIcon /> : <ExpandIcon />}</button> : null}
        <button type="button" className={control} data-panel-close data-sheet-close={!docked || undefined} aria-label={`Close ${panel.id}`} onClick={() => chrome.close(panel.id)}><CloseIcon /></button>
      </span>
    </div>
    <div className="hl-app-shell__panel-toolbar" data-shell-panel-toolbar>{chrome.toolbar?.(panel)}</div>
    <div className="hl-app-shell__end-panel-body" data-shell-panel-body-scroll role="region" aria-label={panel.id} tabIndex={0}>{chrome.content(panel)}</div>
    {panel.footer ? <div className="hl-app-shell__panel-footer" data-shell-panel-footer data-footer-kind={panel.footer.kind}>{panel.footer.binding ? <button type="button" className="hl-app-shell__panel-footer-action" onClick={() => chrome.invokeBinding?.(panel.footer!.binding!)}>{chrome.resolveLabel?.(panel.footer.labelKey) ?? panel.footer.labelKey}</button> : <span>{chrome.resolveLabel?.(panel.footer.labelKey) ?? panel.footer.labelKey}</span>}</div> : null}
  </section>
}

function EndPanel({ label, docked, width, onWidthChange, resizeLabel, expanded, expandLabel, restoreLabel, closeLabel, onToggleExpanded, onClose, children }: { label: string; docked: boolean; width: number; onWidthChange?: (width: number) => void; resizeLabel: string; expanded: boolean; expandLabel: string; restoreLabel: string; closeLabel: string; onToggleExpanded: () => void; onClose: () => void; children: React.ReactNode }) {
  const [size, setSize] = React.useState(width); React.useEffect(() => setSize(width), [width]); const drag = React.useRef<{ x: number; size: number } | null>(null)
  return <aside className="hl-app-shell__end-panel" aria-label={label} data-shell-region="dock" data-docked={docked || undefined} data-expanded={!docked || undefined} style={{ ['--hl-app-shell-end-panel-size' as string]: `${size}px` }}>{docked ? <div className="hl-app-shell__end-panel-resize" role="separator" aria-orientation="vertical" aria-label={resizeLabel} title={resizeLabel} onPointerDown={event => { drag.current = { x: event.clientX, size }; event.currentTarget.setPointerCapture?.(event.pointerId) }} onPointerMove={event => { if (drag.current) setSize(Math.max(0, roundHalfUp(drag.current.size + drag.current.x - event.clientX))) }} onPointerUp={() => { drag.current = null; onWidthChange?.(size) }} onDoubleClick={() => { setSize(width); onWidthChange?.(width) }} /> : null}<div className="hl-app-shell__end-panel-header"><span className="hl-app-shell__end-panel-title">{label}</span><button type="button" className="hl-app-shell__end-panel-control" aria-label={expanded ? restoreLabel : expandLabel} aria-pressed={expanded} onClick={onToggleExpanded}>{expanded ? <RestoreIcon /> : <ExpandIcon />}</button><button type="button" className="hl-app-shell__end-panel-control" aria-label={closeLabel} onClick={onClose}><CloseIcon /></button></div><div className="hl-app-shell__end-panel-body" data-shell-scroll-region role="region" aria-label={label} tabIndex={0}>{children}</div></aside>
}

const MenuIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeLinecap="round"><path d="M2 4h12M2 8h8M2 12h12" /></svg>
const RailIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor"><rect x="1.5" y="2" width="13" height="12" rx="2"/><path d="M5.5 2v12"/></svg>
const SearchIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor"><circle cx="7" cy="7" r="4.5"/><path d="m10.5 10.5 3 3"/></svg>
const BellIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor"><path d="M3 11h10l-1.2-1.7V6a3.8 3.8 0 0 0-7.6 0v3.3L3 11Z"/><path d="M6.5 13a1.7 1.7 0 0 0 3 0"/></svg>
const MenuDotsIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><circle cx="3" cy="8" r="1.2"/><circle cx="8" cy="8" r="1.2"/><circle cx="13" cy="8" r="1.2"/></svg>
const ExpandIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 20 20" fill="none" stroke="currentColor"><path d="M12 3h5v5M8 17H3v-5M17 3l-6 6M3 17l6-6"/></svg>
const RestoreIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 20 20" fill="none" stroke="currentColor"><path d="M3 12h5v5M17 8h-5V3M8 12l-5 5M17 3l-5 5"/></svg>
const TreeIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor"><path d="M2 4h5M2 8h9M2 12h9"/></svg>
const PopOutIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor"><path d="M6 3H3v10h10V9M10 2h4v4M14 2 8 8"/></svg>
const CloseIcon = () => <svg aria-hidden="true" width="14" height="14" viewBox="0 0 20 20" fill="none" stroke="currentColor"><path d="m5 5 10 10M15 5 5 15"/></svg>
