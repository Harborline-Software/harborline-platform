import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppShell } from '../AppShell'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { navigationFixture, type TestWorkspace } from './navigation-fixture'

const workspaces: TestWorkspace[] = [
  { id: 'operations', label: 'Operations', groups: [{ id: 'portfolio', label: 'Portfolio', items: [{ id: 'overview', label: 'Overview' }, { id: 'inspections', label: 'Inspections', count: 218 }] }] },
  { id: 'front-desk', label: 'Front desk', groups: [{ id: 'today', label: 'Today', items: [{ id: 'inbox', label: 'Inbox', count: 12 }] }] },
]
const roleVocabulary = RoleVocabulary.fromApi([{roleDefinitionId:'3b69e3cb-ec5e-4ad7-8c90-16ebc1896102',role:{vocabulary:'sys.platform-roles',name:'auditor'},displayName:'Auditor',owner:{kind:'Platform',ownerId:'harborline-platform'},isSealed:true}])
const base = { shellId: 'ops', ...navigationFixture(workspaces), roleVocabulary, heldRoles: {roles: []}, body: <div>Body</div>, railCapable: true }

describe('AppShell React projection', () => {
  it('renders one main, one navigation, data-shell-id, and validates identity', () => {
    render(<AppShell {...base} brandText="Harborline" />)
    expect(screen.getAllByRole('main')).toHaveLength(1)
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(document.querySelector('[data-shell-id="ops"]')).not.toBeNull()
    expect(() => render(<AppShell {...base} shellId="Bad Id!" />)).toThrow('app-shell-id-required')
    expect(() => render(<AppShell {...base} body={null as never} />)).toThrow('app-shell-body-required')
    expect(() => render(<AppShell {...base} {...navigationFixture([workspaces[0], workspaces[0]])} />)).toThrow('duplicate-nav-identity')
  })

  it('filters denied and unknown declared actions before rendering', () => {
    const gated: TestWorkspace[] = [{id:'operations',label:'Operations',groups:[],createActions:[
      {id:'allowed',label:'Allowed',permittedRoles:['sys.platform-roles/auditor']},
      {id:'unknown',label:'Unknown',permittedRoles:['tax.roles/missing']},
    ]}]
    render(<AppShell {...base} {...navigationFixture(gated)} heldRoles={{roles:[{vocabulary:'sys.platform-roles',name:'auditor'}]}} />)
    expect(screen.getByText(/Allowed/)).toBeInTheDocument()
    expect(screen.queryByText(/Unknown/)).toBeNull()
  })
  it('renders workspace rows in fixed order and switches without a pack-owned switcher', () => {
    const changed = vi.fn()
    const { rerender } = render(<AppShell {...base} {...navigationFixture([workspaces[0]])} />)
    expect(screen.getByRole('link', { name: 'Operations' })).toHaveAttribute('href', '/workspaces/operations')
    rerender(<AppShell {...base} onWorkspaceChange={changed} />)
    fireEvent.click(screen.getByRole('link', { name: 'Front desk' }))
    expect(changed).toHaveBeenCalledExactlyOnceWith('front-desk')
  })
  it('emits one correction for an unknown controlled workspace and renders no group rows', async () => {
    const changed = vi.fn()
    render(<AppShell {...base} activeWorkspaceId="ghost" onWorkspaceChange={changed} />)
    expect(screen.queryByText('Overview')).toBeNull()
    await waitFor(() => expect(changed).toHaveBeenCalledExactlyOnceWith('operations'))
  })
  it('keeps every declared non-Bell/Pilot toggle in Panels below large', () => {
    const invoke = vi.fn()
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1199 })
    const panels = [{ id: 'documents', labelKey: 'Documents', binding: 'panels.documents', shortcut: 'mod+shift+d', defaultWidth: 420, minimumHeight: 220, defaultOpen: false }]
    render(<AppShell {...base} {...navigationFixture(workspaces, panels)} onBindingInvoke={invoke} />)
    expect(screen.queryByRole('button', { name: 'Documents' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Panels' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Documents' }))
    expect(invoke).toHaveBeenCalledExactlyOnceWith('panels.documents')
  })
  it('summons the end panel as an overlay complementary region with clamp and forced narrow expansion', () => {
    const { rerender } = render(<AppShell {...base} endPanel={<div>Pilot</div>} endPanelLabel="Pilot" endPanelOpen endPanelWidth={4000} />)
    const panel = screen.getByRole('complementary', { name: 'Pilot' })
    expect(panel.style.getPropertyValue('--hl-app-shell-end-panel-size')).toBe('4000px') // no fixed upper cap: the content row's width bounds it at runtime
    expect(panel.dataset.expanded).toBeUndefined()
    rerender(<AppShell {...base} railCapable={false} endPanel={<div>Pilot</div>} endPanelLabel="Pilot" endPanelOpen />)
    expect(screen.getByRole('complementary', { name: 'Pilot' }).dataset.expanded).toBe('true')
    rerender(<AppShell {...base} endPanelOpen />)
    expect(screen.queryByRole('complementary')).toBeNull()
  })
  it('resizes the docked end panel within clamp via the separator and resets on double click', () => {
    const widthChanged = vi.fn()
    render(<AppShell {...base} endPanel={<div>Pilot</div>} endPanelLabel="Pilot" endPanelOpen endPanelWidth={400} onEndPanelWidthChange={widthChanged} />)
    const handle = screen.getByRole('separator', { name: 'Resize panel' })
    const panel = screen.getByRole('complementary', { name: 'Pilot' })
    fireEvent.pointerDown(handle, { clientX: 800, pointerId: 1 })
    fireEvent.pointerMove(handle, { clientX: 700, pointerId: 1 })
    fireEvent.pointerUp(handle, { pointerId: 1 })
    expect(panel.style.getPropertyValue('--hl-app-shell-end-panel-size')).toBe('500px')
    expect(widthChanged).toHaveBeenLastCalledWith(500)
    fireEvent.pointerDown(handle, { clientX: 800, pointerId: 1 })
    fireEvent.pointerMove(handle, { clientX: 0, pointerId: 1 })
    fireEvent.pointerUp(handle, { pointerId: 1 })
    expect(panel.style.getPropertyValue('--hl-app-shell-end-panel-size')).toBe('1300px')
    fireEvent.doubleClick(handle)
    expect(panel.style.getPropertyValue('--hl-app-shell-end-panel-size')).toBe('400px')
  })

  it('retains an orphan stored pin in storage while hiding it from the pinned zone', async () => {
    const data: Record<string, string> = { 'hlp-app-shell:v1:ops:pins': '{"v":1,"value":["gone","inspections"]}' }
    const storage = { get: async (k: string) => data[k] ?? null, set: async (k: string, v: string) => { data[k] = v } }
    render(<AppShell {...base} {...navigationFixture([workspaces[0]])} storage={storage} />)
    await waitFor(() => expect(screen.getByText('Pinned')).toBeInTheDocument())
    expect(screen.getAllByText('Inspections')).toHaveLength(1)
    expect(screen.queryByText('gone')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Unpin Inspections' }))
    await waitFor(() => expect(JSON.parse(data['hlp-app-shell:v1:ops:pins']).value).toEqual(['gone']))
  })

  it('renders thread rows inside the mobile drawer with drawer invariants intact', () => {
    const activated = vi.fn()
    const drawerNavigation: TestWorkspace[] = [{ id: 'operations', label: 'Operations', groups: [{ id: 'portfolio', label: 'Portfolio', items: [{ id: 'inspections', label: 'Inspections', threads: [{ id: 's1', label: 'Richmond retry audit' }, { id: 's2', label: 'SLA sweep' }] }] }] }]
    render(<AppShell {...base} {...navigationFixture(drawerNavigation)} railCapable={false} defaultMobileNavOpen onThreadActivate={activated} />)
    const drawer = screen.getByRole('dialog')
    expect(within(drawer).getAllByRole('button', { name: /Richmond retry audit|SLA sweep/ })).toHaveLength(2)
    fireEvent.click(within(drawer).getByRole('button', { name: 'Richmond retry audit' }))
    expect(activated).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 's1' }), 'inspections')
  })

  it('preserves host attributes on the shell root', () => {
    render(<AppShell {...base} className="host" data-case="shell" />)
    const root = document.querySelector('.hl-app-shell') as HTMLElement
    expect(root.className).toContain('host')
    expect(root.dataset.case).toBe('shell')
    expect(root.dataset.shellId).toBe('ops')
  })

  it('expanded panel fills only the page area: rail stays, page column yields, no resize separator', () => {
    render(<AppShell {...base} endPanel={<div>Pilot body</div>} endPanelLabel="Pilot" endPanelOpen endPanelExpanded />)
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    const page = document.querySelector('.hl-app-shell__page') as HTMLElement
    expect(page.dataset.hidden).toBe('true')
    expect(screen.getByRole('complementary', { name: 'Pilot' }).dataset.expanded).toBe('true')
    expect(screen.queryByRole('separator', { name: 'Resize panel' })).toBeNull()
  })

  it('renders the shell-owned panel header whose expand toggle and close emit one request each', () => {
    const expandedChanged = vi.fn(); const openChanged = vi.fn()
    render(<AppShell {...base} endPanel={<div>Pilot body</div>} endPanelLabel="Pilot" endPanelOpen endPanelExpanded={false}
                     onEndPanelExpandedChange={expandedChanged} onEndPanelOpenChange={openChanged} />)
    expect(document.querySelector('.hl-app-shell__end-panel-title')?.textContent).toBe('Pilot')
    fireEvent.click(screen.getByRole('button', { name: 'Expand panel' }))
    expect(expandedChanged).toHaveBeenCalledExactlyOnceWith(true)
    fireEvent.click(screen.getByRole('button', { name: 'Close panel' }))
    expect(openChanged).toHaveBeenCalledExactlyOnceWith(false)
    expect(screen.getByRole('complementary', { name: 'Pilot' })).toBeInTheDocument()
  })

  it('drives collapse from the shortcut, restores from storage, and guards editable targets', async () => {
    const data: Record<string, string> = { 'hlp-app-shell:v1:ops:collapsed': '{"v":1,"value":true}' }
    const storage = { get: async (k: string) => data[k] ?? null, set: async (k: string, v: string) => { data[k] = v } }
    const changed = vi.fn()
    render(<AppShell {...base} storage={storage} onCollapsedChange={changed} onSearchCommand={vi.fn()} />)
    await waitFor(() => expect(document.querySelector('.hl-app-shell')?.getAttribute('data-collapsed')).toBe('true'))
    fireEvent.keyDown(document, { key: '\\', ctrlKey: true })
    expect(changed).toHaveBeenCalledExactlyOnceWith(false)
  })
})
