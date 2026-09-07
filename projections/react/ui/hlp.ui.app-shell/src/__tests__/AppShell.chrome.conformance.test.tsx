import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell } from '../AppShell'
import { mapPackNavigationDeclaration } from '../pack-navigation-mapper'
import { SHELL_LAYOUT, SHELL_RAIL_ZONE_ORDER, type PackNavigationDeclaration, type PackPanelDeclaration } from '../types'

const readFixture = <T,>(name: string): T => JSON.parse(readFileSync(resolve(import.meta.dirname, `../../../../../../conformance/hlp.ui.app-shell/${name}`), 'utf8')) as T
const vocabulary = RoleVocabulary.fromApi([])
const roles = { roles: [] }
const labels: Record<string, string> = { 'workspaces.operations': 'Operations', 'workspaces.definitions': 'Definitions', 'modes.operate': 'Operate', 'modes.configure': 'Configure', 'actions.asset.create': 'Create asset', 'panels.notes': 'Notes' }
const resolveLabel = (key: string) => labels[key] ?? key

describe('AppShell api#58 cross-projection conformance', () => {
  it('maps the real api#58 declaration fixture including modes, spines, actions, and typed panels', () => {
    const declaration = readFixture<PackNavigationDeclaration>('api-58-pack-navigation.json')
    const mapped = mapPackNavigationDeclaration(declaration, { resolveLabel, roleVocabulary: vocabulary, heldRoles: roles })
    expect(mapped.workspaces.map(workspace => workspace.id)).toEqual(['operations', 'definitions'])
    expect(mapped.modes.map(mode => mode.id)).toEqual(['operate', 'configure'])
    expect(mapped.panels.map(panel => panel.id)).toEqual(['documents', 'notes'])
    expect(mapped.workspaces[0].documentSpine[0].binding).toBe('documents.assets')
  })

  it('applies the shared planted-violation fixture to an actual React shell', () => {
    const declaration = readFixture<PackNavigationDeclaration>('api-58-pack-navigation.json')
    const mutations = readFixture<{ packAttemptsZoneOrder: string[]; deniedCreate: { permittedRoles: string[]; guidance: string }; notification: { unread: number; render: string; forbidden: string; panel: PackPanelDeclaration }; content: { attemptedFloor: number; requiredFloor: number } }>('chrome-law-mutations.json')
    const navigation: PackNavigationDeclaration = { ...declaration, panelSet: [mutations.notification.panel, ...(declaration.panelSet ?? [])], seedWorkspaces: declaration.seedWorkspaces.map((workspace, index) => index ? workspace : { ...workspace, createActions: [{ id: 'create-asset', verbKey: 'actions.asset.create', icon: 'plus', binding: 'assets.create', shortcut: 'mod+n', permittedRoles: mutations.deniedCreate.permittedRoles }] }) }
    render(<AppShell shellId="mutations" navigation={navigation} navigationState={{ capabilityGuidanceByBinding: { 'assets.create': mutations.deniedCreate.guidance }, recentByWorkspace: { operations: [{ id: 'recent', label: 'Recent' }] }, suggestedByWorkspace: { operations: { id: 'suggested', label: 'Suggested' } } }} resolveLabel={resolveLabel} roleVocabulary={vocabulary} heldRoles={roles} body={<button>Content</button>} notificationCount={mutations.notification.unread} railCapable footerIdentity={{ label: 'Chris', role: 'Inspector' }} data-pack-zone-order={mutations.packAttemptsZoneOrder.join(',')} />)
    const root = document.querySelector('[data-shell-id="mutations"]')
    const zones = [...document.querySelectorAll('[data-shell-zone]')].map(node => node.getAttribute('data-shell-zone'))
    expect(root).toHaveAttribute('data-pack-zone-order', mutations.packAttemptsZoneOrder.join(','))
    expect(zones).not.toEqual(mutations.packAttemptsZoneOrder)
    expect(zones).toEqual(SHELL_RAIL_ZONE_ORDER)
    expect(screen.queryByRole('button', { name: /Create asset/ })).toBeNull()
    expect(screen.getByText(mutations.deniedCreate.guidance)).toHaveAttribute('data-capability-guidance')
    expect(mutations.notification.render).toBe('count')
    expect(document.querySelector('[data-notification-count]')).toHaveTextContent(String(mutations.notification.unread))
    expect(mutations.notification.forbidden).toBe('dot')
    expect(document.querySelector('[data-notification-dot]')).toBeNull()
    expect(root).toHaveAttribute('data-shell-content-floor', String(mutations.content.requiredFloor))
    const css = readFileSync(resolve(import.meta.dirname, '../../../../../../specs/modules/ui/hlp.ui.app-shell/style.css'), 'utf8')
    expect(css).toMatch(new RegExp(`\\.hl-app-shell__page\\{[^}]*min-inline-size:${mutations.content.requiredFloor}px`))
    expect(css).not.toMatch(new RegExp(`\\.hl-app-shell__page\\{[^}]*min-inline-size:${mutations.content.attemptedFloor}px`))
    expect(SHELL_LAYOUT.contentFloor).toBe(mutations.content.requiredFloor)
    expect(SHELL_LAYOUT.contentFloor).toBeGreaterThan(mutations.content.attemptedFloor)
  })

  it('renders Bell and Pilot only when each panel is in the declared panel set', () => {
    const declaration = readFixture<PackNavigationDeclaration>('api-58-pack-navigation.json')
    const props = { shellId: 'panel-availability', resolveLabel, roleVocabulary: vocabulary, heldRoles: roles, body: <div>Body</div>, railCapable: true }
    const view = render(<AppShell {...props} navigation={declaration} />)
    expect(screen.queryByRole('button', { name: 'Notifications' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Pilot' })).toBeNull()
    const livePanels: PackPanelDeclaration[] = [
      { id: 'notifications', binding: 'panels.notifications.toggle', shortcut: 'mod+shift+b', defaultWidth: 360, minimumHeight: 180, defaultOpen: false },
      { id: 'pilot', binding: 'panels.pilot.toggle', shortcut: 'mod+shift+p', defaultWidth: 400, minimumHeight: 300, defaultOpen: false },
    ]
    view.rerender(<AppShell {...props} navigation={{ ...declaration, panelSet: [...livePanels, ...(declaration.panelSet ?? [])] }} />)
    expect(screen.getByRole('button', { name: 'Notifications' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pilot' })).toBeInTheDocument()
  })

  it('renders declared toggles only at the exact large boundary', () => {
    const declaration = readFixture<PackNavigationDeclaration>('api-58-pack-navigation.json')
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1199 })
    render(<AppShell shellId="breakpoint" navigation={declaration} resolveLabel={resolveLabel} roleVocabulary={vocabulary} heldRoles={roles} body={<div>Body</div>} railCapable />)
    expect(document.querySelector('[data-shell-bar-slot="cluster"] > [data-action-id="documents"]')).toBeNull()
    expect(screen.getByRole('button', { name: 'Panels' })).toBeInTheDocument()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 }); fireEvent(window, new Event('resize'))
    expect(document.querySelector('[data-shell-bar-slot="cluster"] > [data-action-id="documents"]')).not.toBeNull()
    expect(screen.queryByRole('button', { name: 'Panels' })).toBeNull()
  })
})
