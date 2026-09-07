import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell, type ShellPanelPopOutRequest } from '../AppShell'
import { dockNodeMinimum, type DockContainerKind, type DockNode } from '../dock-model'
import { SHELL_PANEL_SLOTS, panelEarnsOverflow, panelHeaderForm, panelMinimumHeight, panelSlotMinimum, roundHalfUp, type PackPanelDeclaration } from '../types'

const path = resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.app-shell/chrome-v1.json')
const fixture = JSON.parse(readFileSync(path, 'utf8'))
// Missing fixture names fail loudly rather than silently skipping a rule.
for (const name of ['panels', 'slotHeights', 'slotOrder', 'headerForms', 'openItem', 'sheetHeader', 'singleScroll', 'minimums', 'overfullPane', 'popOut', 'chordScope', 'passThroughKeys', 'fractionalDragRounding']) if (fixture[name] === undefined) throw new Error(`chrome-v1.json is missing "${name}"`)
const panels = fixture.panels as PackPanelDeclaration[]
const vocabulary = RoleVocabulary.fromApi([])

function mount(open: readonly string[], extra: Record<string, unknown> = {}, viewportWidth = 1800) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: viewportWidth })
  Object.defineProperty(window, 'PointerEvent', { configurable: true, value: MouseEvent })
  HTMLElement.prototype.setPointerCapture = vi.fn()
  const view = render(<AppShell shellId="chrome" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: panels }} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body="Body"
    railCapable openPanelIds={open} panelContent={panel => <span>{panel.id}</span>} panelToolbar={panel => <span data-test-toolbar={panel.id}>toolbar</span>} {...extra} />)
  const panel = (id: string) => view.container.querySelector<HTMLElement>(`[data-shell-panel-id="${id}"]`)!
  return { view, panel, affordances: (id: string) => [...panel(id).querySelectorAll('[data-panel-affordances] button')].map(button => button.getAttribute('data-panel-overflow') !== null ? 'overflow' : button.getAttribute('data-panel-pop-out') !== null ? 'pop-out' : button.getAttribute('data-panel-expand') !== null ? 'expand' : 'close') }
}

it('gives every panel the header form it earned and the invariant affordance group', () => {
  const { panel, affordances } = mount(fixture.headerForms.map((row: { panelId: string }) => row.panelId))
  for (const row of fixture.headerForms) {
    const declaration = panels.find(candidate => candidate.id === row.panelId)!
    expect([row.panelId, panelHeaderForm(declaration)]).toEqual([row.panelId, row.expectedForm])
    expect([row.panelId, panelEarnsOverflow(declaration)]).toEqual([row.panelId, row.earnsOverflow])
    expect([row.panelId, panel(row.panelId).getAttribute('data-panel-header-form')]).toEqual([row.panelId, row.expectedForm])
    expect([row.panelId, affordances(row.panelId)]).toEqual([row.panelId, row.expectedAffordances])
  }
})

it('renders the slots in declared order with the body the only scrolling one', () => {
  const { panel } = mount(['documents'], { panelOpenItem: () => undefined })
  const slots = [...panel('documents').children].map(child => child.className.includes('header') ? 'header' : child.className.includes('toolbar') ? 'toolbar' : child.className.includes('body') ? 'body' : 'footer')
  expect(slots).toEqual(fixture.slotOrder)
  expect(panel('documents').querySelectorAll('[data-shell-panel-body-scroll]')).toHaveLength(fixture.singleScroll.bodyScrollRegionsPerPanel)
  expect(panel('documents').querySelectorAll('[data-shell-panel-body-scroll] [data-shell-scroll-region], [data-shell-panel-body-scroll] [data-shell-panel-body-scroll]')).toHaveLength(fixture.singleScroll.nestedScrollRegionsInsideBody)
  // Header, toolbar and footer are pinned: none of them is a scroll region, so the body scrolls as one.
  const pinned = { header: '.hl-app-shell__end-panel-header', toolbar: '[data-shell-panel-toolbar]', footer: '[data-shell-panel-footer]' } as Record<string, string>
  for (const slot of fixture.singleScroll.pinnedSlots) {
    const element = panel('documents').querySelector(pinned[slot])!
    expect([slot, element !== null, element.hasAttribute('data-shell-scroll-region'), element.hasAttribute('data-shell-panel-body-scroll')]).toEqual([slot, true, false, false])
  }
})

it('takes each panel minimum from the slots above the body, never from the body', () => {
  const { panel } = mount(fixture.minimums.map((row: { panelId: string }) => row.panelId))
  expect(SHELL_PANEL_SLOTS).toEqual(fixture.slotHeights)
  for (const row of fixture.minimums) {
    const declaration = panels.find(candidate => candidate.id === row.panelId)!
    expect([row.panelId, panelSlotMinimum(declaration), panelMinimumHeight(declaration)]).toEqual([row.panelId, row.slotMinimum, row.expectedMinimum])
    expect([row.panelId, panel(row.panelId).style.minBlockSize]).toEqual([row.panelId, `${row.expectedMinimum}px`])
    // The one flexible slot is never given a floor of its own: that is what starves it.
    expect([row.panelId, panel(row.panelId).querySelector<HTMLElement>('[data-shell-panel-body-scroll]')!.style.minBlockSize]).toEqual([row.panelId, ''])
  }
})

// ONE minimum per panel. Every reader of a panel height floor - the rendered panel's min-block-size, the
// pane's published sum, the intra-pane divider and the splitter-tree height clamp - reads the SAME
// slot-derived derivation, never the raw declared number. Every panel kind (declared below / at / above the
// slot floor) x every slot combination (footer or not), and every shape the clamp recurses through.
it('derives one minimum per panel and reads that one derivation everywhere a height floor is read', () => {
  const kinds = [0, 40, 64, 65, 66, 92, 93, 180, 300]
  const combos: PackPanelDeclaration[] = kinds.flatMap(minimumHeight => [false, true].map(withFooter => ({
    id: `p${minimumHeight}${withFooter ? 'f' : ''}`, binding: 'b', shortcut: 's', defaultWidth: 360, minimumHeight, defaultOpen: false,
    ...(withFooter ? { footer: { kind: 'Claim', labelKey: 'panels.f' } } : {}),
  })))
  const containers = new Map<string, DockContainerKind>(combos.map(panel => [panel.id, 'docked' as const]))
  const pane = (panels: readonly PackPanelDeclaration[]): DockNode => ({ kind: 'pane', panels, fractions: panels.map(() => 1 / panels.length) })
  for (const panel of combos) {
    const derived = Math.max(panel.minimumHeight, SHELL_PANEL_SLOTS.header + SHELL_PANEL_SLOTS.toolbar + (panel.footer ? SHELL_PANEL_SLOTS.footer : 0))
    expect([panel.id, panelSlotMinimum(panel) + 0, panelMinimumHeight(panel)]).toEqual([panel.id, SHELL_PANEL_SLOTS.header + SHELL_PANEL_SLOTS.toolbar + (panel.footer ? SHELL_PANEL_SLOTS.footer : 0), derived])
    expect([panel.id, dockNodeMinimum(pane([panel]), false, containers)]).toEqual([panel.id, derived])
    // A sheet contributes nothing to the clamp, whatever it declares.
    expect([panel.id, dockNodeMinimum(pane([panel]), false, new Map([[panel.id, 'side-sheet' as DockContainerKind]]))]).toEqual([panel.id, 0])
  }
  for (const first of combos) for (const second of combos) {
    const stack = pane([first, second])
    const sum = panelMinimumHeight(first) + panelMinimumHeight(second)
    expect([first.id, second.id, dockNodeMinimum(stack, false, containers)]).toEqual([first.id, second.id, sum])
    // A vertical split stacks, so its floor is the sum; a horizontal one sits side by side, so it is the max.
    expect(dockNodeMinimum({ kind: 'split', orientation: 'vertical', ratio: 0.5, first: pane([first]), second: pane([second]) }, false, containers)).toBe(sum)
    expect(dockNodeMinimum({ kind: 'split', orientation: 'horizontal', ratio: 0.5, first: pane([first]), second: pane([second]) }, false, containers)).toBe(Math.max(panelMinimumHeight(first), panelMinimumHeight(second)))
  }
  // The fixture's discriminating panel: declared 40, slot-derived 93, and the clamp must read 93.
  const row = fixture.minimums.find((candidate: { panelId: string }) => candidate.panelId === 'tiny')
  const tiny = panels.find(candidate => candidate.id === row.panelId)!
  expect([panelMinimumHeight(tiny), dockNodeMinimum(pane([tiny]), false, new Map([[tiny.id, 'docked' as DockContainerKind]]))]).toEqual([row.expectedMinimum, row.expectedMinimum])
  const { panel: rendered, view } = mount([tiny.id])
  expect(rendered(tiny.id).style.minBlockSize).toBe(`${row.expectedMinimum}px`)
  expect(view.container.querySelector('.hl-app-shell__dock-pane')!.getAttribute('data-pane-minimum-sum')).toBe(String(row.expectedMinimum))
})

const chordHeader = (panel: HTMLElement) => panel.querySelector<HTMLElement>('.hl-app-shell__end-panel-header')!
const chordBody = (panel: HTMLElement) => panel.querySelector<HTMLElement>('[data-shell-panel-body-scroll] textarea')!

it('handles the pop-out chord from the header affordance group only, and prevents its default there', () => {
  const spec = fixture.chordScope
  for (const row of spec.rows) {
    const requests: ShellPanelPopOutRequest[] = []
    const { panel } = mount([spec.panelId], { panelContent: () => <textarea defaultValue="note" />, onPanelPopOut: (request: ShellPanelPopOutRequest) => requests.push(request) })
    const target = row.from === 'header' ? chordHeader(panel(spec.panelId)) : chordBody(panel(spec.panelId))
    // fireEvent returns false when a handler called preventDefault.
    const notPrevented = fireEvent.keyDown(target, { key: row.key, shiftKey: row.shiftKey })
    expect([row.from, requests.length, !notPrevented]).toEqual([row.from, row.expectedRequests, row.expectedDefaultPrevented])
  }
})

it('scrolls an overfull pane instead of starving a body', () => {
  const { view, panel } = mount(fixture.overfullPane.openPanelIds)
  const pane = view.container.querySelector<HTMLElement>('.hl-app-shell__dock-pane')!
  expect(pane.hasAttribute('data-shell-pane-scroll')).toBe(fixture.overfullPane.expectedPaneScrolls)
  expect(Number(pane.getAttribute('data-pane-minimum-sum'))).toBe(fixture.overfullPane.expectedPaneMinimumSum)
  expect(fixture.overfullPane.openPanelIds.map((id: string) => Number.parseInt(panel(id).style.minBlockSize, 10))).toEqual(fixture.overfullPane.expectedPanelMinimums)
})

it('shows the toggle and the open item chip only while an item is open', () => {
  const close = vi.fn()
  const item = fixture.openItem
  const { view, panel } = mount([item.panelId], { panelOpenItem: (panel: PackPanelDeclaration) => panel.id === item.panelId ? { id: item.itemId, label: item.itemLabel, close } : undefined })
  const chip = panel(item.panelId).querySelector('[data-panel-item-chip]')!
  expect([chip !== null, chip.getAttribute('data-item-id')]).toEqual([item.expectedChipVisible, item.itemId])
  fireEvent.click(panel(item.panelId).querySelector('[data-panel-tree-toggle]')!)
  expect(panel(item.panelId).getAttribute('data-panel-tree')).toBe(item.expectedTreeHiddenAfterToggle ? 'hidden' : 'shown')
  fireEvent.click(chip.querySelector('[data-panel-item-close]')!)
  expect(close).toHaveBeenCalledTimes(1)
  // "Closing the document brings the tree back, even if it was hidden."
  view.rerender(<AppShell shellId="chrome" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: panels }} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body="Body" railCapable openPanelIds={[item.panelId]} panelContent={panel => <span>{panel.id}</span>} panelOpenItem={() => undefined} />)
  expect(panel(item.panelId).getAttribute('data-panel-tree')).toBe(item.expectedTreeShownAfterItemClosed ? 'shown' : 'hidden')
})

it('gives a sheet the close affordance only', () => {
  const { affordances, panel } = mount([fixture.sheetHeader.panelId], {}, fixture.sheetHeader.viewportWidth)
  expect(panel(fixture.sheetHeader.panelId).getAttribute('data-shell-container-kind')).not.toBe('docked')
  expect(affordances(fixture.sheetHeader.panelId)).toEqual(fixture.sheetHeader.expectedAffordances)
})

it('asks the host to pop a declared panel out and leaves it docked until the host confirms', () => {
  const requests: ShellPanelPopOutRequest[] = []
  const spec = fixture.popOut
  const { view, panel } = mount(spec.openPanelIds, { onPanelPopOut: (request: ShellPanelPopOutRequest) => requests.push(request) })
  // The request carries the state the persistence seam would emit, so a width the user just set travels with it.
  for (let press = 0; press < spec.widthArrowLeftPresses; press += 1) fireEvent.keyDown(view.container.querySelector('.hl-app-shell__dock-resize')!, { key: 'ArrowLeft' })
  fireEvent.click(panel(spec.panelId).querySelector('[data-panel-pop-out]')!)
  expect(requests).toHaveLength(spec.expectedRequestCountAfterButton)
  expect([requests[0].panelId, requests[0].state.openPanelIds, requests[0].state.widths]).toEqual([spec.expectedPayload.panelId, spec.expectedPayload.openPanelIds, spec.expectedPayload.widths])
  fireEvent.keyDown(chordHeader(panel(spec.panelId)), { key: 'Enter', shiftKey: true })
  expect(requests).toHaveLength(spec.expectedRequestCountAfterShortcut)
  // The shell opened no window and undocked nothing: the panel is still where it was.
  expect(panel(spec.panelId).getAttribute('data-shell-container-kind')).toBe(spec.expectedStillDockedAfterRequest ? 'docked' : 'side-sheet')
  expect([...view.container.querySelectorAll('[data-shell-panel-id]')].map(node => node.getAttribute('data-shell-panel-id'))).toEqual(spec.expectedOpenPanelIdsAfterRequest)
  view.rerender(<AppShell shellId="chrome" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: panels }} roleVocabulary={vocabulary} heldRoles={{ roles: [] }} body="Body" railCapable openPanelIds={spec.expectedOpenPanelIdsAfterHostConfirms} panelContent={panel => <span>{panel.id}</span>} />)
  expect([...view.container.querySelectorAll('[data-shell-panel-id]')].map(node => node.getAttribute('data-shell-panel-id'))).toEqual(spec.expectedOpenPanelIdsAfterHostConfirms)
})

it('leaves a panel that did not declare pop-out with no pop-out route at all', () => {
  const requests: ShellPanelPopOutRequest[] = []
  const spec = fixture.popOut
  const { panel } = mount([spec.undeclaredPanelId], { onPanelPopOut: (request: ShellPanelPopOutRequest) => requests.push(request) })
  expect(panel(spec.undeclaredPanelId).querySelector('[data-panel-pop-out]')).toBeNull()
  fireEvent.keyDown(chordHeader(panel(spec.undeclaredPanelId)), { key: 'Enter', shiftKey: true })
  expect(requests).toEqual([])
})

it('lets every fixture-owned pass-through key reach the shell from a panel', () => {
  const spec = fixture.popOut
  const seen: string[] = []
  const requests: ShellPanelPopOutRequest[] = []
  const { panel } = mount([spec.panelId], { onKeyDown: (event: { key: string }) => seen.push(event.key), onPanelPopOut: (request: ShellPanelPopOutRequest) => requests.push(request) })
  for (const key of fixture.passThroughKeys) fireEvent.keyDown(panel(spec.panelId), { key })
  expect(seen).toEqual(fixture.passThroughKeys)
  expect(requests).toEqual([])
})

it('rounds a fractional drag half-up on both drag paths, the rule both shells share', () => {
  const rows = fixture.fractionalDragRounding.rows as { start: number; deltaX: number; expected: number }[]
  for (const row of rows) expect([row.deltaX, roundHalfUp(row.start + row.deltaX)]).toEqual([row.deltaX, row.expected])
  // Each row is a fresh drag from the fixture's declared start, so one row cannot inherit another's width.
  const rail = rows.map(row => {
    const widths: number[] = []
    const { view } = mount([], { onRailWidthChange: (width: number) => widths.push(width), defaultRailWidth: row.start })
    const separator = view.container.querySelector('.hl-app-shell__rail-resize')!
    fireEvent.pointerDown(separator, { clientX: 0 })
    fireEvent.pointerMove(separator, { clientX: row.deltaX })
    fireEvent.pointerUp(separator)
    view.unmount()
    return widths.at(-1)
  })
  expect(rail).toEqual(rows.map(row => row.expected))
  const endPanel = rows.map(row => {
    const widths: number[] = []
    const { view } = mount([], { endPanel: <span>End</span>, defaultEndPanelOpen: true, endPanelWidth: row.start, onEndPanelWidthChange: (width: number) => widths.push(width) })
    const separator = view.container.querySelector('.hl-app-shell__end-panel-resize')!
    fireEvent.pointerDown(separator, { clientX: 0 })
    fireEvent.pointerMove(separator, { clientX: -row.deltaX })
    fireEvent.pointerUp(separator, { clientX: -row.deltaX })
    view.unmount()
    return widths.at(-1)
  })
  expect(endPanel).toEqual(rows.map(row => row.expected))
})
