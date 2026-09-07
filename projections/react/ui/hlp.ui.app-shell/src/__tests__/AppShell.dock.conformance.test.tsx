import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell } from '../AppShell'
import { closeDockPanel, createDockLayout, dockPanes, openDockPanel, setDockSpread, type DockNode } from '../dock-model'
import type { PackNavigationDeclaration, PackPanelDeclaration } from '../types'
import { shellBreakpoint } from '../types'

type ExpectedTree = { kind: 'pane'; panels: string[]; fractions: number[] } | { kind: 'split'; orientation: 'horizontal' | 'vertical'; ratio: number; first: ExpectedTree; second: ExpectedTree }
type Transition = { action: 'open' | 'close'; panelId: string; expectedOpenPanelIds: string[]; expectedTree: ExpectedTree | null; expectedHostEvents: string[] }
type AffordanceExpectation = Pick<Transition, 'expectedOpenPanelIds' | 'expectedTree' | 'expectedHostEvents'>
type AffordanceCase = { panelId: string; open: AffordanceExpectation; duplicateOpen: AffordanceExpectation }
type Fixture = { declaredPanels: PackPanelDeclaration[]; openingOrder: string[]; transitions: Transition[]; affordanceEquivalence: { initialOpenPanelIds: string[]; cases: AffordanceCase[] }; expected: { defaultPanes: string[][]; maximumDerivedStackDepth: number; evenSplit: number[]; pilotMinimumHeight: number; minimumPaneWidth: number; spreadInitially: boolean; spreadPaneCount: number; bodyScrollRegionsPerPanel: number } }
const fixture: Fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.app-shell/dock-splitter-v1.json'), 'utf8')) as Fixture
type AdaptationStep = { width: number; expectedBreakpoint: string; expectedContainerKinds: Record<string, string> }
type AdaptationFixture = {
  breakpointLadder: { width: number; expected: string }[]; initialOpenPanelIds: string[]; sheetCloseTarget: number; transitions: { id: string; openPanelIds?: string[]; steps: AdaptationStep[] }[]
  classPlacementCases: { widths: number[]; expectedBreakpoint: string; openPanelIds: string[]; expectedContainerKinds: Record<string, string> }[]
  contentFloorCases: { width: number; spread: boolean; openPanelIds: string[]; expectedContainerKinds: Record<string, string> }[]
  spreadAvailabilityCases: { width: number; spreadUnavailable: boolean; reason: string | null; expectedDisabled: boolean }[]
}
const adaptation: AdaptationFixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.app-shell/adaptation-v1.json'), 'utf8')) as AdaptationFixture
const navigation: PackNavigationDeclaration = { seedWorkspaces: [{ id: 'operations', labelKey: 'Operations' }], panelSet: fixture.declaredPanels }
const vocabulary = RoleVocabulary.fromApi([])

function projectTree(node: DockNode | null): ExpectedTree | null {
  if (node === null) return null
  if (node.kind === 'pane') return { kind: 'pane', panels: node.panels.map(panel => panel.id), fractions: [...node.fractions] }
  return { kind: 'split', orientation: node.orientation, ratio: node.ratio, first: projectTree(node.first)!, second: projectTree(node.second)! }
}

function renderedTree(node: Element | null): ExpectedTree | null {
  if (node === null) return null
  if (node.classList.contains('hl-app-shell__dock-pane')) {
    const panels = [...node.children].filter(child => child.hasAttribute('data-shell-panel-id')) as HTMLElement[]
    return { kind: 'pane', panels: panels.map(panel => panel.dataset.shellPanelId!), fractions: panels.map(panel => Number(panel.style.getPropertyValue('--hl-app-shell-panel-fraction'))) }
  }
  const children = [...node.children].filter(child => !child.classList.contains('hl-app-shell__dock-divider'))
  return { kind: 'split', orientation: node.getAttribute('data-orientation') as 'horizontal' | 'vertical', ratio: Number(node.getAttribute('data-split-ratio')), first: renderedTree(children[0])!, second: renderedTree(children[1])! }
}

function openButtonName(panelId: string) {
  return panelId === 'notifications' ? 'Notifications' : panelId === 'pilot' ? 'Pilot' : `panels.${panelId}`
}

describe('AppShell dock splitter shared conformance', () => {
  it('maps every shared Material boundary through the pure breakpoint function', () => {
    expect(adaptation.breakpointLadder.map(({ width }) => shellBreakpoint(width))).toEqual(adaptation.breakpointLadder.map(({ expected }) => expected))
  })

  it('replays sheet-to-dock transitions without replacing panel bodies', () => {
    for (const transition of adaptation.transitions) {
      const first = transition.steps[0]
      const initial = transition.openPanelIds ?? adaptation.initialOpenPanelIds
      const refs = new Map<string, HTMLDivElement | null>()
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: first.width })
      const view = render(<AppShell shellId={`adapt-${transition.id}`} navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>}
        defaultOpenPanelIds={initial} panelContent={panel => <div ref={element => { refs.set(panel.id, element) }} data-test-panel-body={panel.id}>{panel.id}</div>} railCapable />)
      const bodies = new Map(refs)
      for (const step of transition.steps) {
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: step.width })
        fireEvent(window, new Event('resize'))
        expect(view.container.querySelector('[data-shell-id]')?.getAttribute('data-shell-breakpoint'), `${transition.id}:${step.width}`).toBe(step.expectedBreakpoint)
        expect([...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => panel.getAttribute('data-shell-panel-id')), `${transition.id}:${step.width}`).toEqual(initial)
        for (const [id, kind] of Object.entries(step.expectedContainerKinds)) {
          const panel = view.container.querySelector(`[data-shell-panel-id="${id}"]`)
          expect(panel?.getAttribute('data-shell-container-kind'), `${transition.id}:${step.width}:${id}`).toBe(kind)
          if (kind !== 'docked') expect(panel?.querySelector('[data-sheet-close]')).not.toBeNull()
          expect(refs.get(id), `${transition.id}:${step.width}:${id}:identity`).toBe(bodies.get(id))
        }
      }
      view.unmount()
    }
  })

  it('keeps four-panel placement constant throughout each shared ladder class', () => {
    for (const row of adaptation.classPlacementCases) {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: row.widths[0] })
      const view = render(<AppShell shellId="class-placement" navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>}
        defaultOpenPanelIds={row.openPanelIds} panelContent={panel => <div>{panel.id}</div>} railCapable />)
      for (const width of row.widths) {
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
        fireEvent(window, new Event('resize'))
        expect(view.container.querySelector('[data-shell-id]')?.getAttribute('data-shell-breakpoint')).toBe(row.expectedBreakpoint)
        expect(Object.fromEntries([...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => [panel.getAttribute('data-shell-panel-id'), panel.getAttribute('data-shell-container-kind')])), `${width}`).toEqual(row.expectedContainerKinds)
        // Mirrors the gallery adaptation row at the same mount shape (ticket 266): the close control the
        // fixture sizes at sheetCloseTarget is carried by exactly the non-docked panels of this class. The
        // gallery measures the 44px box; jsdom has no layout, so this suite owns the declaration.
        expect(Object.fromEntries([...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => [panel.getAttribute('data-shell-panel-id'), panel.querySelector('[data-sheet-close]') !== null])), `${width}`)
          .toEqual(Object.fromEntries(Object.entries(row.expectedContainerKinds).map(([id, kind]) => [id, kind !== 'docked'])))
      }
      view.unmount()
    }
  })

  it('keeps the content floor by moving the newest panels to sheets first', () => {
    for (const floorCase of adaptation.contentFloorCases) {
      const panels = floorCase.openPanelIds.map(id => fixture.declaredPanels.find(panel => panel.id === id)!)
      const layout = createDockLayout(panels, floorCase.spread, shellBreakpoint(floorCase.width), floorCase.width)
      expect(Object.fromEntries(layout.containers.map(container => [container.panel.id, container.kind])), `${floorCase.width}:${floorCase.spread}`).toEqual(floorCase.expectedContainerKinds)
    }
  })

  it('renders spread availability from the explicit field with its reason', () => {
    for (const spreadCase of adaptation.spreadAvailabilityCases) {
      Object.defineProperty(window, 'innerWidth', { configurable: true, value: spreadCase.width })
      const view = render(<AppShell shellId={`spread-${spreadCase.width}`} navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>}
        defaultOpenPanelIds={adaptation.initialOpenPanelIds} panelContent={panel => <div>{panel.id}</div>}
        spreadUnavailable={spreadCase.spreadUnavailable} spreadUnavailableReason={spreadCase.reason ?? undefined} railCapable />)
      const control = view.getByRole('button', { name: 'Spread panels' })
      expect(control.hasAttribute('disabled')).toBe(spreadCase.expectedDisabled)
      expect(view.container.querySelector('[data-spread-unavailable-reason]')?.textContent ?? null).toBe(spreadCase.reason)
      view.unmount()
    }
  })

  it('opens every declared panel in order without eviction and derives no stack deeper than two', () => {
    const layout = fixture.openingOrder.reduce((state, id) => openDockPanel(state, fixture.declaredPanels.find(panel => panel.id === id)!), createDockLayout([], fixture.expected.spreadInitially))
    expect(layout.openPanels.map(panel => panel.id)).toEqual(fixture.openingOrder)
    expect(dockPanes(layout.root).map(pane => pane.panels.map(panel => panel.id))).toEqual(fixture.expected.defaultPanes)
    expect(Math.max(...dockPanes(layout.root).map(pane => pane.panels.length))).toBe(fixture.expected.maximumDerivedStackDepth)
    expect(dockPanes(layout.root)[0].fractions).toEqual(fixture.expected.evenSplit)
    expect(layout.spread).toBe(false)
  })

  it('enters spread only through the explicit field and preserves declared minima', () => {
    const closed = createDockLayout(fixture.declaredPanels, false)
    const spread = setDockSpread(closed, true)
    expect(closed.spread).toBe(false)
    expect(spread.spread).toBe(true)
    expect(dockPanes(spread.root)).toHaveLength(fixture.expected.spreadPaneCount)
    expect(spread.minimumPaneWidth).toBe(fixture.expected.minimumPaneWidth)
    expect(spread.openPanels.find(panel => panel.id === 'pilot')?.minimumHeight).toBe(fixture.expected.pilotMinimumHeight)
  })

  it('replays every fixture transition through the pure dock model', () => {
    let layout = createDockLayout([], fixture.expected.spreadInitially)
    for (const transition of fixture.transitions) {
      const panel = fixture.declaredPanels.find(candidate => candidate.id === transition.panelId)!
      layout = transition.action === 'open' ? openDockPanel(layout, panel) : closeDockPanel(layout, transition.panelId)
      expect(layout.openPanels.map(open => open.id), transition.panelId).toEqual(transition.expectedOpenPanelIds)
      expect(projectTree(layout.root), transition.panelId).toEqual(transition.expectedTree)
    }
  })

  it('renders one body scroll region per open panel with no nested body scroller', () => {
    render(<AppShell shellId="dock-fixture" navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>} defaultOpenPanelIds={fixture.openingOrder} panelContent={panel => <div>{panel.id} body</div>} spread={false} railCapable />)
    const panels = [...document.querySelectorAll('[data-shell-panel-id]')]
    expect(panels).toHaveLength(fixture.openingOrder.length)
    expect(panels.map(panel => panel.getAttribute('data-shell-panel-id'))).toEqual(fixture.openingOrder)
    for (const panel of panels) {
      expect(panel.querySelectorAll('[data-shell-panel-body-scroll]')).toHaveLength(fixture.expected.bodyScrollRegionsPerPanel)
      expect(panel.querySelector('[data-shell-panel-body-scroll] [data-shell-scroll-region]')).toBeNull()
    }
  })

  it('replays every fixture transition through the rendered shell and ordered host events', () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
    const events: string[] = []
    render(<AppShell shellId="dock-actions" navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>} panelContent={panel => <div>{panel.id}</div>} railCapable
      onOpenPanelIdsChange={ids => events.push(`openPanelIdsChanged:${ids.join(',')}`)} onBindingInvoke={binding => events.push(`bindingInvoked:${binding}`)}
      onNotificationsCommand={() => events.push('notificationsCommand')} onPilotCommand={() => events.push('pilotCommand')} />)
    for (const transition of fixture.transitions) {
      events.length = 0
      fireEvent.click(screen.getByRole('button', { name: transition.action === 'open' ? openButtonName(transition.panelId) : `Close ${transition.panelId}` }))
      expect(events, transition.panelId).toEqual(transition.expectedHostEvents)
      expect([...document.querySelectorAll('[data-shell-panel-id]')].map(panel => panel.getAttribute('data-shell-panel-id')), transition.panelId).toEqual(transition.expectedOpenPanelIds)
      expect(renderedTree(document.querySelector('.hl-app-shell__dock')?.firstElementChild ?? null), transition.panelId).toEqual(transition.expectedTree)
    }
  })

  it('keeps every ordinary panel open equivalent through inline and overflow affordances', () => {
    expect(fixture.affordanceEquivalence.cases.map(panelCase => panelCase.panelId)).toEqual(fixture.declaredPanels.filter(panel => panel.id !== 'notifications' && panel.id !== 'pilot').map(panel => panel.id))
    for (const panelCase of fixture.affordanceEquivalence.cases) {
      for (const width of [1200, 1199]) {
        Object.defineProperty(window, 'innerWidth', { configurable: true, value: width })
        const events: string[] = []
        const view = render(<AppShell shellId={`dock-${panelCase.panelId}-${width}`} navigation={navigation} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body={<div>Body</div>}
          defaultOpenPanelIds={fixture.affordanceEquivalence.initialOpenPanelIds} panelContent={panel => <div>{panel.id}</div>} railCapable
          onOpenPanelIdsChange={ids => events.push(`openPanelIdsChanged:${ids.join(',')}`)} onBindingInvoke={binding => events.push(`bindingInvoked:${binding}`)} />)
        for (const stepName of ['open', 'duplicateOpen'] as const) {
          events.length = 0
          if (width < 1200) fireEvent.click(view.getByRole('button', { name: 'Panels' }))
          fireEvent.click(view.container.querySelector(`[data-action-id="${panelCase.panelId}"] button`)!)
          const expected = panelCase[stepName]
          expect(events, `${panelCase.panelId}:${width}:${stepName}`).toEqual(expected.expectedHostEvents)
          expect([...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => panel.getAttribute('data-shell-panel-id')), `${panelCase.panelId}:${width}:${stepName}`).toEqual(expected.expectedOpenPanelIds)
          expect(renderedTree(view.container.querySelector('.hl-app-shell__dock')?.firstElementChild ?? null), `${panelCase.panelId}:${width}:${stepName}`).toEqual(expected.expectedTree)
        }
        view.unmount()
      }
    }
  })
})
