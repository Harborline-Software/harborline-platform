import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell } from '../AppShell'
const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.app-shell/dividers-v1.json'), 'utf8'))
afterEach(() => vi.restoreAllMocks())
// A case names its own panel set, so `depth2` can open three panels: that puts a stacked pane inside
// the horizontal tree and replays the clamp and no-eviction rules against the divider two levels down.
for (const row of fixture.cases) it(`shared divider script ${row.id}: sizes, ARIA, reset, order and containers`, () => {
  const casePanels = row.panels.map((id: string) => fixture.panels.find((panel: {id:string}) => panel.id === id))
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1600 })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: row.extent, height: row.extent } as DOMRect)
  Object.defineProperty(window, 'PointerEvent', { configurable: true, value: MouseEvent })
  HTMLElement.prototype.setPointerCapture = vi.fn()
  HTMLElement.prototype.releasePointerCapture = vi.fn()
  const view = render(<AppShell shellId="dividers" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: casePanels }} roleVocabulary={RoleVocabulary.fromApi([])} heldRoles={{ roles: [] }} body="Body" defaultOpenPanelIds={row.panels} defaultSpread={row.spread} panelContent={p => <input aria-label={p.id} defaultValue={p.id} />} />)
  const bodies = [...view.container.querySelectorAll('input')]
  const divider = () => view.container.querySelector<HTMLElement>('.hl-app-shell__dock-divider[tabindex="0"]')!
  const check = (expected: number) => {
    expect(divider()).not.toBeNull()
    expect(divider().getAttribute('aria-orientation')).toBe(row.orientation)
    expect(Number(divider().getAttribute('aria-valuenow'))).toBeCloseTo(expected)
    expect(Number(divider().getAttribute('aria-valuemin'))).toBe(row.min)
    expect(Number(divider().getAttribute('aria-valuemax'))).toBe(row.max)
    expect([...view.container.querySelectorAll('[data-shell-panel-id]')].map(p => [p.getAttribute('data-shell-panel-id'), p.getAttribute('data-shell-container-kind')])).toEqual(row.panels.map((id: string) => [id, 'docked']))
    expect([...view.container.querySelectorAll('input')]).toEqual(bodies)
  }
  check(row.initial)
  for (const step of row.steps) {
    if (step.action === 'reset') fireEvent.click(view.getByRole('button', {name:'Reset panels'}))
    else if (step.action === 'key') fireEvent.keyDown(divider(), {key:step.key, shiftKey:step.shift ?? false})
    else {
      fireEvent.pointerDown(divider(), {button:0, clientX:0, clientY:0})
      fireEvent.pointerMove(divider(), {clientX:step.delta, clientY:step.delta})
      if (step.action === 'cancelDrag') fireEvent.pointerCancel(divider())
      else fireEvent.pointerUp(divider())
    }
    check(step.expected)
    if (step.action === 'drag') {
      const ratio = row.spread ? Number(view.container.querySelector('[data-split-ratio]')!.getAttribute('data-split-ratio')) : Number(view.container.querySelector<HTMLElement>('[data-shell-panel-id]')!.style.getPropertyValue('--hl-app-shell-panel-fraction'))
      expect(ratio * Math.max(row.extent, row.min + row.secondMin)).toBeCloseTo(step.expected)
    }
  }
  view.unmount()
})

// A focused divider must not swallow the shell's shortcuts. React's DockDivider stops propagation
// only inside its handled-key branch, and a synthetic stopPropagation does reach the native event,
// so an unguarded stop would kill the document-level shell listener. The Blazor mirror is
// AppShellDividerTests.ShortcutReachesTheShellFromAFocusedDivider; both read the same fixture rows.
for (const row of fixture.shellPassThroughKeys) it(`shell shortcut ${row.token} reaches the shell from a focused divider`, () => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1600 })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 900, height: 900 } as DOMRect)
  const search = vi.fn()
  const view = render(<AppShell shellId="dividers" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: fixture.panels }} roleVocabulary={RoleVocabulary.fromApi([])} heldRoles={{ roles: [] }} body="Body" defaultOpenPanelIds={fixture.panels.map((p: {id:string}) => p.id)} shortcuts={{ toggleRail: null, toggleNavigation: null, commandSurface: row.token }} onSearchCommand={search} panelContent={p => <span>{p.id}</span>} />)
  const divider = view.container.querySelector<HTMLElement>('.hl-app-shell__dock-divider[tabindex="0"]')!
  divider.focus()
  expect(document.activeElement).toBe(divider)
  fireEvent.keyDown(divider, { key: row.key, ctrlKey: row.ctrl === true, shiftKey: row.shift === true })
  expect(search).toHaveBeenCalledTimes(1)
  view.unmount()
})

it('the divider consumes exactly the keys the shared fixture declares for its orientation', () => {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1600 })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: 900, height: 900 } as DOMRect)
  const view = render(<AppShell shellId="dividers" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: fixture.panels }} roleVocabulary={RoleVocabulary.fromApi([])} heldRoles={{ roles: [] }} body="Body" defaultOpenPanelIds={fixture.panels.map((p: {id:string}) => p.id)} panelContent={p => <span>{p.id}</span>} />)
  const divider = view.container.querySelector<HTMLElement>('.hl-app-shell__dock-divider[tabindex="0"]')!
  const orientation = divider.getAttribute('aria-orientation') as 'vertical' | 'horizontal'
  const handled: string[] = fixture.handledKeys[orientation]
  const other = fixture.handledKeys[orientation === 'vertical' ? 'horizontal' : 'vertical'].filter((key: string) => !handled.includes(key))
  for (const key of handled) expect([key, fireEvent.keyDown(divider, { key })]).toEqual([key, false])
  for (const key of [...other, 'Tab', 'a', 'PageDown']) expect([key, fireEvent.keyDown(divider, { key })]).toEqual([key, true])
  view.unmount()
})

// The slice 3 residual: a Mod chord on a focused divider diverged. Blazor fired the shell shortcut AND
// cancelled the divider's edit; React swallowed the chord entirely. Both now let it pass and neither acts
// on it, so the divider's value is untouched until a BARE key arrives. Mirror: AppShellDividerTests.
it('a Mod chord on a focused divider neither resizes nor commits nor cancels the edit', () => {
  const row = fixture.cases.find((entry: {id:string}) => entry.id === fixture.modChordIgnored.case)
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1600 })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: row.extent, height: row.extent } as DOMRect)
  const view = render(<AppShell shellId="dividers" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet: row.panels.map((id: string) => fixture.panels.find((panel: {id:string}) => panel.id === id)) }} roleVocabulary={RoleVocabulary.fromApi([])} heldRoles={{ roles: [] }} body="Body" defaultOpenPanelIds={row.panels} defaultSpread={row.spread} panelContent={p => <span>{p.id}</span>} />)
  const divider = () => view.container.querySelector<HTMLElement>('.hl-app-shell__dock-divider[tabindex="0"]')!
  const value = () => Number(divider().getAttribute('aria-valuenow'))
  fireEvent.keyDown(divider(), { key: fixture.modChordIgnored.setup.key })
  expect(value()).toBeCloseTo(fixture.modChordIgnored.setup.expected)
  for (const chord of fixture.modChordIgnored.chords) {
    fireEvent.keyDown(divider(), { key: chord.key, ctrlKey: chord.ctrl === true })
    expect(value(), `Mod+${chord.key}`).toBeCloseTo(fixture.modChordIgnored.expectedAfterChords)
  }
  fireEvent.keyDown(divider(), { key: fixture.modChordIgnored.release.key })
  expect(value()).toBeCloseTo(fixture.modChordIgnored.release.expected)
  view.unmount()
})
