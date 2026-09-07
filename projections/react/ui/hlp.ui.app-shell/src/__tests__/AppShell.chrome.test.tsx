import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell } from '../AppShell'
import { SHELL_LAYOUT, SHELL_RAIL_ZONE_ORDER, shellAddress, shellBreakpoint } from '../types'
import { navigationFixture, type TestWorkspace } from './navigation-fixture'

const calls = { create: vi.fn(), search: vi.fn(), inspector: vi.fn(), workspace: vi.fn() }
const workspaces: TestWorkspace[] = [{
  id: 'operations', label: 'Operations', count: 7,
  createActions: [{ id: 'asset', label: 'New asset', binding: 'assets.create' }, { id: 'inspection', label: 'New inspection', binding: 'inspections.create' }, { id: 'grey-create', label: 'Grey create', permittedRoles: ['tax.roles/missing'] }], defaultCreateActionId: 'asset',
  groups: [{ id: 'portfolio', label: 'Portfolio', items: [{ id: 'asset-17', label: 'Pump renamed', kind: 'assets', count: 4 }] }],
  recent: [{ id: 'run-8841', label: 'Morning run', kind: 'runs' }], suggested: { id: 'asset-18', label: 'Review valve', kind: 'assets' },
}, { id: 'reports', label: 'Reports', groups: [] }]
const livePanels = [
  { id: 'notifications', labelKey: 'Notifications', binding: 'panels.notifications', shortcut: 'mod+shift+b', defaultWidth: 360, minimumHeight: 180, defaultOpen: false },
  { id: 'pilot', labelKey: 'Pilot', binding: 'panels.pilot', shortcut: 'mod+shift+p', defaultWidth: 400, minimumHeight: 300, defaultOpen: false },
]
const fixture = navigationFixture(workspaces, livePanels)
const base = { shellId: 'chrome', ...fixture, navigation: { ...fixture.navigation, modeSwitch: { modes: [{ id: 'operate', labelKey: 'Operations mode', workspaceIds: ['operations'] }] } }, roleVocabulary: RoleVocabulary.fromApi([]), heldRoles: { roles: [] }, body: <button>Content action</button>, railCapable: true, footerIdentity: { label: 'Chris', role: 'Inspector' }, systemItems: [{ id: 'pilot', label: 'Pilot' }, { id: 'settings', label: 'Settings' }, { id: 'help', label: 'Help' }], notificationCount: 3, onBindingInvoke: (binding: string) => { if (binding === 'assets.create') calls.create() }, onSearchCommand: calls.search, onInspectorCommand: calls.inspector, onWorkspaceChange: calls.workspace }

describe('Harborline chrome contract', () => {
  it('renders the actual kernel bar and nine rail zones in their immutable order', () => {
    render(<AppShell {...base} />)
    expect([...document.querySelectorAll('[data-shell-bar-slot]')].map(node => node.getAttribute('data-shell-bar-slot'))).toEqual(['mark', 'window-menu', 'rail-toggle', 'find', 'breadcrumb', 'cluster'])
    expect([...document.querySelectorAll('[data-shell-zone]')].map(node => node.getAttribute('data-shell-zone'))).toEqual(SHELL_RAIL_ZONE_ORDER)
    expect(document.querySelector('.hl-app-layout__frame')?.firstElementChild).toHaveClass('hl-app-layout__header')
  })

  it('uses absence instead of disabled affordances and always states Pinned emptiness', () => {
    const view = render(<AppShell {...base} {...navigationFixture([{ id: 'empty', label: 'Empty', groups: [] }])} />)
    expect(document.querySelector('[data-shell-zone="primary-action"]')).toBeNull()
    expect(document.querySelector('[data-shell-zone="mode"]')).toBeNull()
    expect(screen.getByText('Nothing pinned yet')).toBeInTheDocument()
    view.rerender(<AppShell {...base} activeWorkspaceId="operations" />)
    fireEvent.click(screen.getByRole('button', { name: 'More New asset actions' }))
    expect(screen.queryByText('Grey create')).toBeNull()
    expect(document.querySelectorAll('[disabled]')).toHaveLength(0)
  })

  it('keeps exactly one tenant mark on screen with the rail open or gone', () => {
    const view = render(<AppShell {...base} />)
    expect(document.querySelectorAll('[data-tenant-mark]')).toHaveLength(1)
    expect(screen.getByRole('img', { name: 'Tenant' })).toHaveAttribute('data-tenant-mark', 'true')
    fireEvent.click(screen.getByRole('button', { name: 'Navigation' }))
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(document.querySelectorAll('[data-tenant-mark]')).toHaveLength(1)
    view.unmount()
  })

  it('makes the named end-panel scroll region keyboard reachable', () => {
    render(<AppShell {...base} endPanel={<p>Panel details</p>} endPanelLabel="Pilot" endPanelOpen />)
    const region = screen.getByRole('region', { name: 'Pilot' })
    expect(region).toHaveAttribute('data-shell-scroll-region', 'true')
    expect(region).toHaveAttribute('tabindex', '0')
  })

  it('keeps workload counts on rows and the unread count on the bell, never a dot', () => {
    render(<AppShell {...base} />)
    expect(document.querySelector('[data-shell-zone="workspaces"] [data-shell-count]')).toHaveTextContent('7')
    expect(document.querySelector('[data-shell-zone="groups"] [data-shell-count]')).toHaveTextContent('4')
    expect(document.querySelector('[data-notification-count]')).toHaveTextContent('3')
    expect(document.querySelector('[data-notification-dot]')).toBeNull()
    expect(document.querySelector('[data-shell-zone="footer"] [data-shell-count]')).toBeNull()
  })

  it('starts the resizable rail at 216px and preserves content before the shrinking dock', () => {
    const changed = vi.fn()
    render(<AppShell {...base} onRailWidthChange={changed} endPanel={<button>Dock action</button>} endPanelOpen />)
    const root = document.querySelector('.hl-app-shell') as HTMLElement
    expect(root.style.getPropertyValue('--hl-app-shell-rail-size')).toBe('216px')
    const handle = screen.getByRole('separator', { name: /Resize navigation rail/ })
    fireEvent.pointerDown(handle, { clientX: 216, pointerId: 1 }); fireEvent.pointerMove(handle, { clientX: 240, pointerId: 1 })
    expect(root.style.getPropertyValue('--hl-app-shell-rail-size')).toBe('240px')
    expect(document.querySelector('.hl-app-shell__page')).toHaveClass('hl-app-shell__page')
    expect(document.querySelector('.hl-app-shell__end-panel')).toHaveAttribute('data-docked', 'true')
  })

  it('binds Command-K, N, backslash, Shift-I and 1-9 while leaving Command-I unbound', () => {
    calls.create.mockClear(); calls.search.mockClear(); calls.inspector.mockClear(); calls.workspace.mockClear()
    render(<AppShell {...base} />)
    fireEvent.keyDown(document, { key: 'k', metaKey: true }); fireEvent.keyDown(document, { key: 'n', metaKey: true }); fireEvent.keyDown(document, { key: 'i', metaKey: true }); fireEvent.keyDown(document, { key: 'i', metaKey: true, shiftKey: true }); fireEvent.keyDown(document, { key: '2', metaKey: true })
    expect(calls.search).toHaveBeenCalledTimes(1); expect(calls.create).toHaveBeenCalledTimes(1); expect(calls.inspector).toHaveBeenCalledTimes(1); expect(calls.workspace).toHaveBeenLastCalledWith('reports')
    fireEvent.keyDown(document, { key: '\\', metaKey: true }); expect(screen.queryByRole('navigation')).toBeNull()
  })

  it('declared panel toggles are absent at 1199 and present at 1200', () => {
    const original = window.innerWidth
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1199 })
    const panels = [
      ...livePanels,
      { id: 'documents', labelKey: 'Documents', binding: 'panels.documents', shortcut: 'mod+shift+d', defaultWidth: 420, minimumHeight: 220, defaultOpen: false },
      { id: 'runs', labelKey: 'Runs', binding: 'panels.runs', shortcut: 'mod+shift+r', defaultWidth: 420, minimumHeight: 220, defaultOpen: false },
      { id: 'inspector', labelKey: 'Inspector', binding: 'panels.inspector', shortcut: 'mod+shift+i', defaultWidth: 420, minimumHeight: 220, defaultOpen: false },
    ]
    render(<AppShell {...base} {...navigationFixture(workspaces, panels)} />)
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pilot' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Documents' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Panels' })).toBeInTheDocument()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 }); fireEvent(window, new Event('resize'))
    expect(['documents', 'runs', 'inspector']).toEqual([...document.querySelectorAll('[data-shell-bar-slot="cluster"] > [data-action-id]')].map(node => node.getAttribute('data-action-id')))
    expect(screen.queryByRole('button', { name: 'Panels' })).toBeNull()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: original })
  })

  it('follows visual tab order: bar, rail, content, dock', () => {
    render(<AppShell {...base} endPanel={<button>Dock action</button>} endPanelOpen />)
    const controls = [...document.querySelectorAll<HTMLElement>('button,a[href]')]
    expect(controls.indexOf(screen.getByRole('button', { name: 'Find' }))).toBeLessThan(controls.indexOf(screen.getByRole('link', { name: /Operations7/ })))
    expect(controls.indexOf(screen.getByRole('link', { name: 'Morning run' }))).toBeLessThan(controls.indexOf(screen.getByRole('button', { name: 'Content action' })))
    expect(controls.indexOf(screen.getByRole('button', { name: 'Content action' }))).toBeLessThan(controls.indexOf(screen.getByRole('button', { name: 'Dock action' })))
  })

  it('derives stable addresses from identity, never labels', () => {
    expect(shellAddress('assets', 'PSV-2201')).toBe('/assets/PSV-2201')
    const view = render(<AppShell {...base} />)
    expect(screen.getByRole('link', { name: /Pump renamed4/ })).toHaveAttribute('href', '/assets/asset-17')
    view.rerender(<AppShell {...base} {...navigationFixture([{ ...workspaces[0], groups: [{ id: 'portfolio', items: [{ id: 'asset-17', label: 'Different label', kind: 'assets' }] }] }, workspaces[1]])} />)
    expect(screen.getByRole('link', { name: 'Different label' })).toHaveAttribute('href', '/assets/asset-17')
  })

  it('declares all Material breakpoints and content-floor constants', () => {
    expect(SHELL_LAYOUT).toEqual({ barHeight: 34, railWidth: 216, railMinimum: 120, contentFloor: 420 })
    expect([599, 600, 839, 840, 1199, 1200, 1599, 1600].map(shellBreakpoint)).toEqual(['compact', 'medium', 'medium', 'expanded', 'expanded', 'large', 'large', 'extra-large'])
  })
})
