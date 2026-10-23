import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { AspectLens, CanvasNode } from '@harborline-platform/hlp.ui.aspect-lens'
import { LayersRail } from '../LayersRail'
import { sharedCases } from './fixtures'
import { Host, baseLenses, baseNodes, tenant } from './setup'

// Each neutral case runs the production component against its own input and expected values.
// compact asserts hidden headings and named controls; the compact width is stylesheet behaviour left
// to the gallery. projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['layers-rail.projection-equivalence']
const control = (label: string) => screen.getByText(label).closest('button')!
// The treeitem is the focus target (APG treeview); its buttons are pointer-only.
const item = (label: string) => screen.getByText(label).closest<HTMLElement>('[role="treeitem"]')!
const focusedItemLabel = () => document.activeElement?.getAttribute('role') === 'treeitem' ? document.activeElement.querySelector('.hl-layers-rail__node-label')?.textContent : `${document.activeElement?.tagName}.${document.activeElement?.className}`
const tabStops = () => Array.from(screen.getByRole('tree').querySelectorAll<HTMLElement>('*')).filter(element => element.tabIndex >= 0)
const itemState = (element: Element | null) => ({
  role: element?.getAttribute('role') ?? null,
  label: element?.querySelector('.hl-layers-rail__node-label')?.textContent ?? null,
  level: Number(element?.getAttribute('aria-level')),
  expanded: element?.getAttribute('aria-expanded') == null ? null : element.getAttribute('aria-expanded') === 'true',
  selected: element?.getAttribute('aria-selected') === 'true',
})
const lensesFor = (ids: string[]): AspectLens[] => ids.map(id => ({ id, label: id.charAt(0).toUpperCase() + id.slice(1), tone: 'accent', kind: 'colorize', project: () => ({ active: true }) }))
const nodesFor = (count: number): CanvasNode[] => [{ id: 'n0', kind: 'section', label: 'N0', depth: 0, parentId: null, hasChildren: count > 1 }, ...Array.from({ length: count - 1 }, (_, i) => ({ id: `n${i + 1}`, kind: 'field', label: `N${i + 1}`, depth: 1, parentId: 'n0' }))]

describe('LayersRail shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'layers-rail.structure': ({ input, expected }) => {
    render(<Host lenses={lensesFor(['layout', 'rules'].slice(0, input.lenses))} nodes={nodesFor(input.nodes)}/>)
    expect(screen.getByRole('complementary').getAttribute('aria-label')).toBe(expected.region)
    expect(screen.queryByRole('group') !== null).toBe(expected.lensGroup)
    expect(screen.queryByRole('tree') !== null).toBe(expected.tree)
    const order = [screen.getByRole('group'), screen.getByRole('tree')].sort((a, b) => a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1).map(node => node.getAttribute('role') === 'group' ? 'lenses' : 'outline')
    expect(order).toEqual(expected.order)
  },
  'layers-rail.lenses': ({ input, expected }) => {
    render(<Host lenses={lensesFor(input.lensIds)} initialActive={input.activeId} initialEnabled={input.enabledIds}/>)
    const pressed = (selector: string) => Array.from(document.querySelectorAll<HTMLButtonElement>(selector)).filter(button => button.getAttribute('aria-pressed') === 'true').map((_, i, all) => all[i]!)
    const activeIds = input.lensIds.filter((_: string, i: number) => document.querySelectorAll('.hl-layers-rail__lens-activate')[i]!.getAttribute('aria-pressed') === 'true')
    const enabledIds = input.lensIds.filter((_: string, i: number) => document.querySelectorAll('.hl-layers-rail__lens-toggle')[i]!.getAttribute('aria-pressed') === 'true')
    expect(pressed('.hl-layers-rail__lens-activate')).toHaveLength(expected.activePressed.length)
    expect(activeIds).toEqual(expected.activePressed)
    expect(enabledIds).toEqual(expected.enabledPressed)
  },
  'layers-rail.active-status': ({ input, expected }) => {
    render(<Host compact={input.compact} initialActive={input.activeId}/>)
    const status = screen.getByRole('status')
    expect(status.textContent).toBe(expected.status)
    expect(status.getAttribute('aria-live')).toBe(expected.live)
    expect(screen.queryByRole('button', { name: 'Exit lens (back to Layout)' }) !== null).toBe(expected.exitPresent)
  },
  'layers-rail.toggle': async ({ input, expected }) => {
    render(<Host initialActive={input.activeId}/>)
    const label = baseLenses.find(lens => lens.id === input.toggle)!.label
    const toggle = screen.getByRole('button', { name: `Toggle ${label} lens` })
    const before = toggle.getAttribute('aria-pressed')
    await userEvent.setup().click(toggle)
    expect(toggle.getAttribute('aria-pressed') !== before ? input.toggle : null).toBe(expected.enabledChanged)
    const active = document.querySelectorAll('.hl-layers-rail__lens-activate')
    const activeAfter = baseLenses.find((_, i) => active[i]!.getAttribute('aria-pressed') === 'true')?.id ?? null
    expect(activeAfter).toBe(expected.activeId)
    // Toggling enablement never activates a lens.
    expect(activeAfter === input.activeId ? 0 : 1).toBe(expected.activationCount)
  },
  'layers-rail.shortcuts': async ({ input, expected }) => {
    const user = userEvent.setup()
    render(<><Host lenses={lensesFor(input.lensIds)} initialActive={null}/><input aria-label="Outside"/></>)
    const activeId = () => input.lensIds.find((_: string, i: number) => document.querySelectorAll('.hl-layers-rail__lens-activate')[i]!.getAttribute('aria-pressed') === 'true') ?? null
    document.querySelector<HTMLButtonElement>('.hl-layers-rail__lens-activate')!.focus()
    const states = []
    for (const key of input.keys as string[]) { await user.keyboard(key === 'Escape' ? '[Escape]' : key); states.push(activeId()) }
    expect(states).toEqual(expected.states)
    // Scoped: the same key typed outside the rail changes nothing.
    screen.getByRole('textbox', { name: 'Outside' }).focus()
    await user.keyboard(input.keys[0])
    expect(activeId() === null).toBe(expected.scopedToRail)
  },
  'layers-rail.compact': ({ input, expected }) => {
    render(<Host compact={input.compact} onInsert={() => undefined}/>)
    expect(document.querySelectorAll('.hl-layers-rail h2').length > 0).toBe(expected.textHeadingsVisible)
    const named = [screen.getByRole('group'), screen.getByRole('tree'), ...screen.getAllByRole('button')].every(element => (element.getAttribute('aria-label') ?? element.textContent ?? '').trim() !== '')
    expect(named).toBe(expected.accessibleNamesPresent)
    expect(screen.getByRole('complementary').classList.contains('hl-layers-rail--compact') ? 'compact' : 'full').toBe(expected.width)
  },
  'layers-rail.outline': ({ input, expected }) => {
    const nodes = input.nodes.map((node: any) => ({ ...node, kind: 'field', label: node.id }))
    render(<Host nodes={nodes}/>)
    expect(screen.getByRole('tree').querySelectorAll('[role="treeitem"]').length === nodes.length).toBe(expected.role === 'tree')
    expect(screen.getAllByRole('treeitem').map(item => Number(item.getAttribute('aria-level')))).toEqual(expected.levels)
    expect(screen.getAllByRole('treeitem')[0]!.getAttribute('aria-expanded') === 'true').toBe(expected.expanded)
  },
  'layers-rail.tree-keyboard': async ({ input, expected }) => {
    const user = userEvent.setup()
    render(<Host/>)
    item('Root').focus()
    const visits: Array<string | null | undefined> = []
    for (const key of input.keys as string[]) { await user.keyboard(`[${key}]`); visits.push(focusedItemLabel()) }
    // Down → Child, Up → Root, Right on an expanded parent → Child, Left on a leaf → Root, Home → Root, End → Sibling.
    expect(input.keys).toEqual(['ArrowDown', 'ArrowUp', 'ArrowRight', 'ArrowLeft', 'Home', 'End'])
    const apg = JSON.stringify(visits) === JSON.stringify(['Child', 'Root', 'Child', 'Root', 'Root', 'Sibling'])
    expect(apg).toBe(expected.apgMovement)
    const stops = tabStops()
    expect(stops.length === 1 && stops[0] === document.activeElement).toBe(expected.rovingTabIndex)
    expect(stops[0]?.getAttribute('role')).toBe(expected.tabStopRole)
  },
  'layers-rail.collapse-focus': ({ input, expected }) => {
    render(<Host/>)
    item(input.focus === 'child' ? 'Child' : 'Root').focus()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Collapse' })) })
    const visible = screen.getAllByRole('treeitem').map(item => item.querySelector('.hl-layers-rail__node-label')!.textContent!.toLowerCase()).filter(id => id !== 'sibling')
    expect(visible).toEqual(expected.visibleIds)
    expect(document.activeElement?.closest('[role="treeitem"]')?.querySelector('.hl-layers-rail__node-label')?.textContent?.toLowerCase()).toBe(expected.focusedId)
    expect(document.activeElement?.getAttribute('role')).toBe(expected.focusedRole)
    expect(document.activeElement === document.body).toBe(expected.bodyFocused)
  },
  'layers-rail.treeitem-focus': async ({ input, expected }) => {
    const user = userEvent.setup()
    render(<><Host selected={input.selectedId}/><button type="button">After</button></>)
    const stops = tabStops()
    expect(stops).toHaveLength(expected.tabStops)
    expect(stops[0]!.getAttribute('role')).toBe(expected.tabStopRole)
    // The tree is the rail's last tab stop, so Shift+Tab from the button after it reaches the tree's one stop.
    screen.getByRole('button', { name: 'After' }).focus()
    await user.tab({ shift: true })
    expect(document.activeElement).toBe(stops[0])
    const visits = [itemState(document.activeElement)]
    for (const key of input.keys as string[]) { await user.keyboard(`[${key}]`); visits.push(itemState(document.activeElement)) }
    expect(visits).toEqual(expected.focused)
    // The focused treeitem is named by its label element alone: no disclosure or provenance text is folded in.
    const focused = document.activeElement as HTMLElement
    const label = focused.querySelector('.hl-layers-rail__node-label')!
    const named = focused.getAttribute('aria-labelledby') === label.id && screen.getByRole('treeitem', { name: label.textContent! }) === focused
    expect(named).toBe(expected.accessibleNameIsLabel)
  },
  'layers-rail.selection': async ({ input, expected }) => {
    const selected: string[] = []
    render(<Host onSelect={id => selected.push(id)}/>)
    await userEvent.setup().click(control(input.activateNode === 'child' ? 'Child' : 'Root'))
    expect(screen.getAllByRole('treeitem').filter(item => item.getAttribute('aria-selected') === 'true').map(item => item.querySelector('.hl-layers-rail__node-label')!.textContent!.toLowerCase())).toEqual(expected.selectedIds)
    expect(selected).toHaveLength(expected.callbackCount)
    // The click hands focus to the selected row's treeitem.
    expect(document.activeElement?.getAttribute('role')).toBe(expected.focusedRole)
    expect(document.activeElement).toBe(item(input.activateNode === 'child' ? 'Child' : 'Root'))
  },
  'layers-rail.filter-badges': ({ input, expected }) => {
    const lenses: AspectLens[] = [
      { id: 'rules', label: 'Rules', tone: 'warning', kind: input.activeKind, project: id => ({ active: id !== input.inactiveNode }) },
      { id: 'access', label: 'Access', tone: 'danger', kind: 'colorize', project: id => ({ active: id !== input.inactiveNode }) },
    ]
    render(<Host lenses={lenses} initialActive="rules" initialEnabled={['rules', 'access']}/>)
    const child = screen.getByText('Child').closest('[role="treeitem"]')
    expect(child !== null).toBe(expected.childPresent)
    expect(child!.getAttribute('data-hl-ghosted') === 'true').toBe(expected.ghosted)
    expect(screen.getByRole('button', { name: 'Show Access lens on the canvas' }).querySelector('.hl-layers-rail__count')?.textContent).toBe(expected.passiveText)
    const passive = screen.getByRole('button', { name: 'Show Access lens on the canvas' }).querySelector('.hl-layers-rail__count')?.textContent ?? ''
    expect(passive).toBe(`+${input.passiveMatches}`)
    // Not colour alone: the ghosted state is an attribute and the passive count is text.
    expect(!(child!.hasAttribute('data-hl-ghosted') && /\+\d/.test(passive))).toBe(expected.colorOnly)
  },
  'layers-rail.provenance': ({ input, expected }) => {
    render(<Host provenance={{ resolve: id => id === 'child' ? { ...tenant, source: input.unresolved.source, locked: input.unresolved.locked, resolved: false } : { ...tenant, source: input.resolved.source, locked: input.resolved.locked } }}/>)
    expect(control('Root').querySelector('.hl-layers-rail__source')!.textContent).toBe(expected.resolvedText)
    const unresolved = control('Child').querySelector('.hl-layers-rail__source')!.textContent!
    expect(unresolved.startsWith('source: unknown') && unresolved.includes('Locked') === input.unresolved.locked).toBe(expected.unresolvedFailsClosed)
  },
  'layers-rail.insert': async ({ input, expected }) => {
    let calls = 0
    render(<Host onInsert={input.hasCallback ? () => { calls++ } : undefined}/>)
    const insert = screen.getByRole('button', { name: expected.accessibleName })
    for (let i = 0; i < input.activations; i++) await userEvent.setup().click(insert)
    expect(calls).toBe(expected.callbackCount)
  },
  'layers-rail.empty': ({ input, expected }) => {
    const lenses: AspectLens[] = [{ ...baseLenses[1]!, empty: input.activeLensEmpty }]
    const view = render(<Host nodes={input.nodes} lenses={lenses} initialActive="rules"/>)
    expect(screen.queryByText(input.activeLensEmpty) !== null).toBe(expected.lensHintWhenApplicable)
    view.unmount()
    render(<Host nodes={input.nodes} initialActive={null}/>)
    expect(screen.getByRole('tree').querySelector('p') !== null).toBe(expected.emptyVisible)
  },
  'layers-rail.continuity': async ({ input, expected }) => {
    const user = userEvent.setup()
    render(<Host selected={input.selectedId}/>)
    const before = item('Child')
    before.focus()
    await user.click(screen.getByRole('button', { name: `Show ${input.switchLens === 'rules' ? 'Rules' : 'Layout'} lens on the canvas` }))
    const after = item('Child')
    expect(after.getAttribute('aria-selected') === 'true').toBe(expected.selectionPreserved)
    expect(after.getAttribute('tabindex') === '0').toBe(expected.focusPreserved)
    expect(after === before).toBe(expected.nodeIdentityPreserved)
  },
  'layers-rail.invalid-input': ({ input, expected }) => {
    const errors: string[] = []
    for (const props of [{ lenses: lensesFor(input.lensIds), nodes: baseNodes }, { lenses: baseLenses, nodes: (input.nodeIds as string[]).map(id => ({ id, kind: 'field', label: id, depth: 0, parentId: null })) }]) {
      try { render(<LayersRail layers={{ activeId: null, enabledIds: new Set(), setActive: () => undefined, clearActive: () => undefined, toggleEnabled: () => undefined, isEnabled: () => false, isActive: () => false }} lenses={props.lenses} model={{ nodes: props.nodes, selectedId: null, select: () => undefined }} provenance={{ resolve: () => tenant }}/>) } catch (error) { errors.push((error as Error).message) }
    }
    expect([...new Set(errors)]).toEqual(expected.errors)
    expect(errors).toHaveLength(2)
  },
}
