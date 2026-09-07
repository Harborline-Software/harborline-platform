import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { act, fireEvent, render } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { AppShell } from '../AppShell'
import { DOCK_MINIMUM_PANE_WIDTH, clampDockWidth, createDockLayout, dockWidthCeiling, type DockStateSnapshot } from '../dock-model'
import { shellBreakpoint } from '../types'

const path = resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.app-shell/persistence-v1.json')
const fixture = JSON.parse(readFileSync(path, 'utf8'))
// Missing fixture names fail loudly rather than silently skipping a rule.
for (const name of ['panels', 'rememberedWidthCases', 'roundTrip', 'staleOrMalformed', 'dockWidthProperty', 'dockPlacementProperty', 'dockTreeRetentionProperty', 'dockRestoreMeasurementProperty', 'dockSettlementProperty']) if (fixture[name] === undefined) throw new Error(`persistence-v1.json is missing "${name}"`)
afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

// A narrow scene inside a wide viewport: jsdom has no ResizeObserver, so the fixture's measured width is fed
// through a stub that reports the shell box the moment the shell observes it - the same seam the browser drives.
function mount(viewportWidth: number, restore: unknown, open: readonly string[] | undefined, emitted: DockStateSnapshot[], panelSet: unknown = fixture.panels, railCapable?: boolean, measuredWidth?: number, deferMeasurement = false) {
  Object.defineProperty(window, 'innerWidth', { configurable: true, value: viewportWidth })
  // The observer callback is kept so a RE-measurement can be replayed on the mounted shell, exactly as the
  // browser does when the box changes size without the viewport class changing.
  let notifyObserver: ((entries: { contentRect: { width: number } }[]) => void) | null = null
  if (measuredWidth !== undefined) vi.stubGlobal('ResizeObserver', class {
    constructor(private readonly notify: (entries: { contentRect: { width: number } }[]) => void) {}
    // The replay handle is captured when the SHELL's own box is observed, never at construction: descendants
    // (sheets, the end panel) build observers of their own as placement changes, and capturing the last one
    // constructed silently pointed `remeasure` at a child - a re-measurement then reached nobody and the shell
    // held its previous width while the assertions read a stale box.
    // deferMeasurement installs the observer WITHOUT reporting: the shell is mounted but not yet measured,
    // which is the only window in which the restore-before-measurement defect is observable.
    observe(node?: Element) {
      if (node?.classList?.contains('hl-app-shell')) notifyObserver = this.notify
      if (!deferMeasurement) this.notify([{ contentRect: { width: measuredWidth } }])
    }
    unobserve() {}
    disconnect() {}
  })
  Object.defineProperty(window, 'PointerEvent', { configurable: true, value: MouseEvent })
  HTMLElement.prototype.setPointerCapture = vi.fn()
  HTMLElement.prototype.releasePointerCapture = vi.fn()
  const element = (collapsed?: boolean) => <AppShell shellId="persistence" navigation={{ seedWorkspaces: [{ id: 'ops', labelKey: 'Ops' }], panelSet }} roleVocabulary={RoleVocabulary.fromApi([])} heldRoles={{ roles: [] }} body="Body"
    railCapable={railCapable} collapsed={collapsed} defaultDockState={restore} defaultOpenPanelIds={open} onDockStateChange={state => emitted.push(state)} panelContent={panel => <span>{panel.id}</span>} />
  const view = render(element())
  const query = <T extends HTMLElement>(selector: string) => view.container.querySelector<T>(selector)
  return {
    view, query,
    collapseRail: () => view.rerender(element(true)),
    remeasure: (next: number) => { if (notifyObserver === null) throw new Error('no ResizeObserver seam: mount with a measuredWidth'); act(() => notifyObserver!([{ contentRect: { width: next } }])) },
    width: () => { const raw = query<HTMLElement>('.hl-app-shell__dock')?.style.getPropertyValue('--hl-app-shell-dock-size'); return raw ? Number.parseFloat(raw) : null },
    order: () => [...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => panel.getAttribute('data-shell-panel-id')),
    kinds: () => [...view.container.querySelectorAll('[data-shell-panel-id]')].map(panel => panel.getAttribute('data-shell-container-kind')),
    remembered: () => emitted.at(-1)?.widths ?? {},
    // Three real open routes reach one seam: the inline bar action, the overflow menu below `large`, and the
    // live-panel control the bar keeps for pilot/notifications outside the declared action list.
    openPanel: (id: string) => {
      if (!query(`[data-action-id="${id}"] button`) && view.queryByRole('button', { name: 'Panels' })) fireEvent.click(view.getByRole('button', { name: 'Panels' }))
      const action = query<HTMLElement>(`[data-action-id="${id}"] button`)
      fireEvent.click(action ?? view.getByRole('button', { name: id.slice(0, 1).toUpperCase() + id.slice(1) }))
    },
  }
}

for (const row of fixture.rememberedWidthCases) it(`remembered dock width ${row.id}`, () => {
  const emitted: DockStateSnapshot[] = []
  const shell = mount(row.viewportWidth, row.restore, row.initialOpen, emitted, row.panels ?? fixture.panels, row.railCapable, row.measuredWidth)
  expect(shell.width()).toBe(row.initialWidth)
  expect(shell.remembered()).toEqual(row.initialRemembered)
  for (const step of row.steps) {
    if (step.action === 'open') shell.openPanel(step.panel)
    else if (step.action === 'close') fireEvent.click(shell.view.getByRole('button', { name: `Close ${step.panel}` }))
    else if (step.action === 'reset') fireEvent.click(shell.view.getByRole('button', { name: 'Reset panels' }))
    else {
      // The handle widens the dock as the pointer moves toward the content, so the fixture names only the
      // target width and the drag is derived from the width actually on screen.
      const handle = shell.query<HTMLElement>('.hl-app-shell__dock-resize')!
      fireEvent.pointerDown(handle, { clientX: 0 })
      fireEvent.pointerMove(handle, { clientX: shell.width()! - step.width })
      fireEvent.pointerUp(handle, { clientX: shell.width()! - step.width })
    }
    expect([step.action, step.panel ?? step.width, shell.width()]).toEqual([step.action, step.panel ?? step.width, step.expectedWidth])
    expect(shell.order()).toEqual(step.expectedOpen)
    expect(shell.remembered()).toEqual(step.expectedRemembered)
    if (step.expectedContainerKinds) expect(shell.kinds()).toEqual(step.expectedContainerKinds)
    // A sheet has no dock separator at all, so no width can reach it.
    expect(shell.query('.hl-app-shell__dock-resize') !== null).toBe(step.expectedWidth !== null)
  }
  shell.view.unmount()
})

it('round-trips a dock state through serialise then deserialise into an identical layout', () => {
  const emitted: DockStateSnapshot[] = []
  const row = fixture.roundTrip
  const shell = mount(row.viewportWidth, row.state, undefined, emitted)
  expect(shell.order()).toEqual(row.expectedOpen)
  expect(shell.width()).toBe(row.expectedWidth)
  expect([...shell.view.container.querySelectorAll<HTMLElement>('[data-shell-panel-id]')].map(panel => Number(panel.style.getPropertyValue('--hl-app-shell-panel-fraction')))).toEqual(row.expectedFractions)
  expect(emitted.at(-1)).toEqual(row.state)
  shell.view.unmount()
})

for (const row of fixture.staleOrMalformed) it(`a stale or malformed dock state is rejected without throwing: ${row.id}`, () => {
  const emitted: DockStateSnapshot[] = []
  const shell = mount(1600, row.state, undefined, emitted)
  expect(shell.order()).toEqual(row.expectedOpen)
  expect(shell.width()).toBe(row.expectedWidth)
  if (row.expectedSplitRatio !== undefined) expect(Number(shell.query('[data-split-ratio]')!.getAttribute('data-split-ratio'))).toBe(row.expectedSplitRatio)
  shell.view.unmount()
})

// The property, not a point fix. Over the whole W band - including the 840..936 rail band the four hand-picked
// widths used to skip - for every row width W, rail R, content floor F and requested width D the effective dock
// width is clamp(D, PaneMinimumWidth, W - R - F) AND the shell's pane capacity is derived from that same
// ceiling, so rail + dock + floor <= W always holds and where the ceiling cannot hold one pane nothing is
// docked at all. The sheet collapse is read from the MODEL's own containers, never from the fixture literal.
it('the model clamps every requested dock width to the content floor and counts the rail in pane capacity', () => {
  const property = fixture.dockWidthProperty
  expect([property.cases.length, property.minimumPaneWidth, property.contentFloor]).toEqual([270, DOCK_MINIMUM_PANE_WIDTH, 420])
  for (const row of property.cases) {
    const where = `${row.id} `
    expect(where + dockWidthCeiling(row.shellWidth, row.railWidth, property.contentFloor)).toBe(where + row.expectedCeiling)
    expect(where + clampDockWidth(row.requestedWidth, row.shellWidth, row.railWidth, property.contentFloor)).toBe(where + row.expectedWidth)
    const layout = createDockLayout(property.panels.slice(0, row.panelCount), false, shellBreakpoint(row.shellWidth), row.shellWidth, row.railWidth)
    const kinds = layout.containers.map(container => container.kind)
    expect(where + kinds.join(',')).toBe(where + row.expectedContainerKinds.join(','))
    expect(where + kinds.every(kind => kind !== 'docked')).toBe(where + row.expectedSheetCollapse)
    // Either the dock's right edge is inside the row (rail + dock + floor <= W), or there is no dock at all.
    expect(where + (kinds.every(kind => kind !== 'docked') || row.railWidth + row.expectedWidth + property.contentFloor <= row.shellWidth)).toBe(where + 'true')
  }
})

// The OFF-DIAGONAL half of the same invariant: the viewport class and the measured container are an independent
// pair, so this grid crosses them (the 270-cell property above only samples the diagonal, where they agree).
// Placement is decided by the class the MEASURED width falls in - the same width the clamp uses - so
// rail + dock + floor <= the measured width or nothing is docked; the viewport class drives sheet chrome only.
it('decides placement from the class the measured container falls in, never from the viewport class', () => {
  const property = fixture.dockPlacementProperty
  expect([property.cases.length, property.minimumPaneWidth, property.contentFloor]).toEqual([73, DOCK_MINIMUM_PANE_WIDTH, 420])
  for (const row of property.cases) {
    const where = `${row.id} `
    expect(where + shellBreakpoint(row.viewportWidth)).toBe(where + row.expectedViewportClass)
    expect(where + shellBreakpoint(row.measuredWidth)).toBe(where + row.expectedPlacementClass)
    expect(where + dockWidthCeiling(row.measuredWidth, row.railWidth, property.contentFloor)).toBe(where + row.expectedCeiling)
    expect(where + clampDockWidth(row.requestedWidth, row.measuredWidth, row.railWidth, property.contentFloor)).toBe(where + row.expectedWidth)
    // Built the way the shell builds it: the VIEWPORT class in the breakpoint argument, the MEASURED box as W.
    const layout = createDockLayout(property.panels.slice(0, row.panelCount), false, shellBreakpoint(row.viewportWidth), row.measuredWidth, row.railWidth)
    const kinds = layout.containers.map(container => container.kind)
    expect(where + kinds.join(',')).toBe(where + row.expectedContainerKinds.join(','))
    expect(where + kinds.every(kind => kind !== 'docked')).toBe(where + row.expectedSheetCollapse)
    // Either the dock's right edge is inside the measured box, or there is no dock at all.
    expect(where + (kinds.every(kind => kind !== 'docked') || row.railWidth + row.expectedWidth + property.contentFloor <= row.measuredWidth)).toBe(where + 'true')
  }
})

// The same grid replayed as a RE-RENDER of a mounted shell: mount at measuredWidth, then fire the shell's own
// ResizeObserver again at remeasuredWidth (the viewport class never moves). Placement must follow the SECOND
// measurement - a layout cached on anything less than every input the placement reads goes stale here.
for (const row of fixture.dockPlacementProperty.cases) it(`re-measures placement on re-render ${row.id}`, () => {
  const floor = fixture.dockPlacementProperty.contentFloor
  const open = fixture.dockPlacementProperty.panels.slice(0, row.panelCount).map((panel: { id: string }) => panel.id)
  const shell = mount(row.viewportWidth, undefined, open, [], fixture.dockPlacementProperty.panels, row.railWidth > 0, row.measuredWidth)
  // The gallery mounts this same shape at these same measured widths, so the separator - the ONLY affordance
  // that sets the dock width - is asserted here too: it exists exactly while something is docked.
  const separator = () => shell.query('.hl-app-shell__dock-resize[role="separator"]') !== null
  expect(shell.kinds()).toEqual(row.expectedContainerKinds)
  expect(shell.width()).toBe(row.expectedMountWidth)
  expect(separator()).toBe(row.expectedContainerKinds.includes('docked'))
  shell.remeasure(row.remeasuredWidth)
  expect(shell.kinds()).toEqual(row.expectedRemeasuredContainerKinds)
  expect(shell.width()).toBe(row.expectedRemeasuredWidth)
  expect(separator()).toBe(row.expectedRemeasuredContainerKinds.includes('docked'))
  // Either the dock's right edge is inside the RE-measured box, or there is no dock at all.
  expect(row.expectedRemeasuredSheetCollapse || row.railWidth + shell.width()! + floor <= row.remeasuredWidth).toBe(true)
  shell.view.unmount()
})

// The rail is the other placement input: collapsing it inside ONE measured box frees its inline size, so
// capacity is recounted on the re-render.
it('re-places the dock when the rail collapses inside one measured box', () => {
  const row = fixture.dockPlacementProperty.railToggleCase
  const open = fixture.dockPlacementProperty.panels.slice(0, row.panelCount).map((panel: { id: string }) => panel.id)
  const shell = mount(row.viewportWidth, undefined, open, [], fixture.dockPlacementProperty.panels, true, row.measuredWidth)
  expect(shell.kinds()).toEqual(row.expectedContainerKinds)
  expect(shell.width()).toBe(row.expectedWidth)
  shell.collapseRail()
  expect(shell.kinds()).toEqual(row.expectedCollapsedContainerKinds)
  expect(shell.width()).toBe(row.expectedCollapsedWidth)
  shell.view.unmount()
})

// Item 0 property. The AUTHORED tree (what the user dragged, or what the host restored) and the DERIVED
// placement are different things with different invalidation sets, so one field cannot carry both: placement
// and the width clamp derive PER RENDER from every input, while the tree is remembered against
// {open panels, spread, placement class} only. Re-measuring inside one class keeps the arrangement, crossing a
// class falls back to the derived tree, coming BACK restores it with the remembered widths, and the rail -
// which changes placement but not what the user arranged - never drops it. Persistence emits the tree only
// while it is the authored one, so a derived reset is never written back for the host to reload as a request.
for (const row of fixture.dockTreeRetentionProperty.cases) it(`keeps the authored dock tree across ${row.id}`, () => {
  const emitted: DockStateSnapshot[] = []
  const shell = mount(row.viewportWidth, row.state, undefined, emitted, fixture.dockTreeRetentionProperty.panels, row.railCapable, row.measuredWidth)
  const fractions = () => [...shell.view.container.querySelectorAll<HTMLElement>('[data-shell-panel-id]')].map(panel => Number(panel.style.getPropertyValue('--hl-app-shell-panel-fraction')))
  let measured = row.measuredWidth
  const check = (label: string, expected: { placementClass: string; kinds: readonly string[]; fractions: readonly number[]; width: number | null; authored: boolean }) => {
    const where = `${row.id} ${label} `
    expect(where + shellBreakpoint(measured)).toBe(where + expected.placementClass)
    expect(where + shell.kinds().join(',')).toBe(where + expected.kinds.join(','))
    expect(where + fractions().join(',')).toBe(where + expected.fractions.join(','))
    expect(where + shell.width()).toBe(where + expected.width)
    // The persistence seam is the observation: a derived tree is emitted as null, an authored one as the tree.
    expect(where + (emitted.at(-1)!.tree !== null)).toBe(where + expected.authored)
  }
  check('mount', { placementClass: row.expectedMountPlacementClass, kinds: row.expectedMountContainerKinds, fractions: row.expectedMountFractions, width: row.expectedMountWidth, authored: row.expectedMountAuthored })
  for (const step of row.steps) {
    if (step.action === 'remeasure') { measured = step.width; shell.remeasure(step.width) }
    else if (step.action === 'collapseRail') shell.collapseRail()
    else throw new Error(`unknown dockTreeRetentionProperty action "${step.action}"`)
    check(`${step.action}${step.width ?? ''}`, { placementClass: step.expectedPlacementClass, kinds: step.expectedContainerKinds, fractions: step.expectedFractions, width: step.expectedWidth, authored: step.expectedAuthored })
  }
  shell.view.unmount()
})

// GEOMETRY UP FRONT (checklist l). Restore and the FIRST measurement: persisted state may only be applied
// against a MEASURED placement class. Until the box is measured the restored tree is held pending - rendered
// (so the first paint is the arrangement the user left, not a derived guess that flashes) but unstamped and
// never emitted. The grid is viewport x measured x rail, with measured != viewport on 8 of 12 rows, so it sees
// the narrow-scene-inside-a-wide-viewport case the slice 4 retention property (measured == viewport) could not.
for (const row of fixture.dockRestoreMeasurementProperty.cases) it(`applies restored state against the measured class ${row.id}`, () => {
  const emitted: DockStateSnapshot[] = []
  const shell = mount(row.viewportWidth, row.state, undefined, emitted, fixture.dockRestoreMeasurementProperty.panels, row.railCapable, row.measuredWidth, true)
  const fractions = () => [...shell.view.container.querySelectorAll<HTMLElement>('[data-shell-panel-id]')].map(panel => Number(panel.style.getPropertyValue('--hl-app-shell-panel-fraction')))
  const geometry = (label: string, expected: { kinds: readonly string[]; fractions: readonly number[]; width: number | null }) => {
    const where = `${row.id} ${label} `
    expect(where + shell.kinds().join(',')).toBe(where + expected.kinds.join(','))
    expect(where + fractions().join(',')).toBe(where + expected.fractions.join(','))
    expect(where + shell.width()).toBe(where + expected.width)
    // rail + dock + content floor <= the width the geometry was derived from, or there is no dock at all.
    expect(where + (expected.width === null || row.railWidth + expected.width + fixture.dockRestoreMeasurementProperty.contentFloor <= Number(label))).toBe(where + 'true')
  }
  // Before the first measurement: the arrangement is SHOWN, the guess still fits, and nothing at all is written
  // back. The exact guessed width is deliberately NOT asserted - see the property's `why`: it is the one number
  // the two lanes cannot share, which is exactly why the emit waits for the measurement instead.
  const where = `${row.id} mount `
  expect(where + shell.kinds().join(',')).toBe(where + row.expectedMountContainerKinds.join(','))
  expect(where + fractions().join(',')).toBe(where + row.expectedMountFractions.join(','))
  expect(where + (shell.width() === null || row.railWidth + shell.width()! + fixture.dockRestoreMeasurementProperty.contentFloor <= row.viewportWidth)).toBe(where + 'true')
  expect(`${row.id} emissions ${emitted.length}`).toBe(`${row.id} emissions ${row.expectedMountEmissions}`)
  for (const step of row.steps) {
    shell.remeasure(step.width)
    // The placement class the shell must have used is the one the MEASURED width falls in, never the viewport's.
    expect(`${row.id} ${step.width} ${shellBreakpoint(step.width)}`).toBe(`${row.id} ${step.width} ${step.expectedPlacementClass}`)
    geometry(String(step.width), { kinds: step.expectedContainerKinds, fractions: step.expectedFractions, width: step.expectedWidth })
    // The persistence seam: an authored tree is emitted as the tree, a derived one honestly as null.
    expect(`${row.id} ${step.width} ${emitted.at(-1)!.tree !== null}`).toBe(`${row.id} ${step.width} ${step.expectedAuthored}`)
  }
  shell.view.unmount()
})

// Cross-shell equality is the fixture itself: AppShellPersistenceTests.RestoredStateIsAppliedAgainstTheMeasuredClass
// replays these same rows through the Blazor [JSInvokable] ShellMeasured seam and asserts the same five things.

// Settlement: only a report the shell ACCEPTS (width > 0) settles the measurement, and Reset panels clears the
// PENDING restored root. The Blazor mirror is AppShellPersistenceTests.SettlementIsDrivenByAcceptedReports.
for (const row of fixture.dockSettlementProperty.cases) it(`settles measurement only on an accepted report ${row.id}`, () => {
  const emitted: DockStateSnapshot[] = []
  const shell = mount(row.viewportWidth, row.state, undefined, emitted, fixture.dockSettlementProperty.panels, row.railCapable, row.viewportWidth, true)
  const fractions = () => [...shell.view.container.querySelectorAll<HTMLElement>('[data-shell-panel-id]')].map(panel => Number(panel.style.getPropertyValue('--hl-app-shell-panel-fraction')))
  const where0 = `${row.id} mount `
  expect(where0 + shell.kinds().join(',')).toBe(where0 + row.expectedMountContainerKinds.join(','))
  expect(where0 + fractions().join(',')).toBe(where0 + row.expectedMountFractions.join(','))
  expect(`${row.id} emissions ${emitted.length}`).toBe(`${row.id} emissions ${row.expectedMountEmissions}`)
  for (const step of row.steps) {
    if (step.action === 'reset') fireEvent.click(shell.view.getByRole('button', { name: 'Reset panels' }))
    else shell.remeasure(step.width)
    const where = `${row.id} ${step.action}:${step.width ?? ''} `
    if (step.expectedPlacementClass) expect(where + shellBreakpoint(step.width)).toBe(where + step.expectedPlacementClass)
    expect(where + shell.kinds().join(',')).toBe(where + step.expectedContainerKinds.join(','))
    expect(where + fractions().join(',')).toBe(where + step.expectedFractions.join(','))
    // A step with no expectedWidth key asserts no width: in the unmeasured window the guess is the one number
    // the two lanes cannot share, which is why nothing is emitted there.
    if ('expectedWidth' in step) expect(where + shell.width()).toBe(where + step.expectedWidth)
    expect(`${where}emissions ${emitted.length}`).toBe(`${where}emissions ${step.expectedEmissions}`)
    if (step.expectedAuthored !== undefined) expect(`${where}authored ${emitted.at(-1)!.tree !== null}`).toBe(`${where}authored ${step.expectedAuthored}`)
  }
  shell.view.unmount()
})
