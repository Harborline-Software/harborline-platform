import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { Popover, PopoverAnchor, PopoverClose, PopoverContent, PopoverTrigger, place } from '../Popover'
import type { PlacementRect, PopoverAlign, PopoverSide } from '../Popover'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values, so a
// flipped expected value fails the case (the id-only checks in Popover.conformance.test.tsx did not).
// projection-equivalence compares the two lanes and has no single-lane assertion.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['popover.projection-equivalence']

const dialog = () => screen.queryByRole('dialog')
const trigger = () => document.querySelector<HTMLElement>('.hl-popover-trigger')!

// jsdom has no layout. Give the anchor and the trigger distinct boxes so placement can be traced to
// the element it was measured from; content measures 0x0 and the viewport is window.inner*.
const anchorRect: PlacementRect = { left: 300, top: 300, right: 380, bottom: 330, width: 80, height: 30 }
const triggerRect: PlacementRect = { left: 40, top: 40, right: 120, bottom: 70, width: 80, height: 30 }
function stubLayout() {
  vi.spyOn(Element.prototype, 'getBoundingClientRect').mockImplementation(function (this: Element) {
    const rect = this.classList.contains('hl-popover-anchor') ? anchorRect : this.classList.contains('hl-popover-trigger') ? triggerRect : null
    return (rect ?? { left: 0, top: 0, right: 0, bottom: 0, width: 0, height: 0 }) as DOMRect
  })
}
const viewport = () => ({ width: window.innerWidth, height: window.innerHeight })
const at = (element: HTMLElement) => ({ side: element.dataset.side, left: parseFloat(element.style.left), top: parseFloat(element.style.top) })
const samePlace = (element: HTMLElement, placed: { side: string; left: number; top: number }) => {
  const actual = at(element)
  return actual.side === placed.side && actual.left === placed.left && actual.top === placed.top
}

afterEach(() => { vi.restoreAllMocks() })

describe('Popover shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  it('asserts only fixture case ids: no stale assertion or unasserted key', () => {
    const ids = new Set(cases.map(entry => entry.id))
    for (const key of [...Object.keys(assertions), ...unasserted]) expect(ids.has(key), key).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'popover.closed': ({ expected }) => {
    render(<Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    expect(dialog() !== null).toBe(expected.contentMounted)
    expect(trigger().getAttribute('aria-expanded')).toBe(expected.ariaExpanded)
  },
  'popover.uncontrolled': async ({ input: given, expected }) => {
    const onOpenChange = vi.fn()
    render(<Popover onOpenChange={onOpenChange}><PopoverTrigger>Toggle</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    const user = userEvent.setup()
    const states = [dialog() !== null]
    for (let index = 0; index < given.activations; index++) { await user.click(trigger()); states.push(dialog() !== null) }
    expect(states).toEqual(expected.states)
    expect(onOpenChange.mock.calls.map(call => call[0])).toEqual(expected.emitted)
  },
  'popover.controlled': async ({ input: given, expected }) => {
    const onOpenChange = vi.fn()
    render(<Popover open={given.open} onOpenChange={onOpenChange}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    if (given.activate) await userEvent.setup().click(trigger())
    // A controlled popover requests the change and renders only what its host passes back.
    expect(dialog() !== null).toBe(expected.rendered)
    expect(onOpenChange.mock.calls.length === 1 && onOpenChange.mock.calls[0]![0] === !given.open).toBe(expected.emitted)
  },
  'popover.trigger-aria': ({ input: given, expected }) => {
    render(<Popover open={given.open}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    const content = dialog()
    expect(trigger().getAttribute('aria-haspopup')).toBe(expected.hasPopup)
    expect(trigger().getAttribute('aria-expanded')).toBe(expected.expanded)
    expect(content !== null && content.id !== '' && trigger().getAttribute('aria-controls') === content.id).toBe(expected.controlsMountedContent)
  },
  'popover.anchor': ({ input: given, expected }) => {
    stubLayout()
    render(
      <Popover open>
        {given.anchor === 'explicit' ? <PopoverAnchor>Anchor</PopoverAnchor> : null}
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details" side={given.side as PopoverSide} align={given.align as PopoverAlign} sideOffset={given.sideOffset}>Body</PopoverContent>
      </Popover>,
    )
    const content = dialog()!
    const fromAnchor = place(anchorRect, { width: 0, height: 0 }, given.side, given.align, given.sideOffset, 'ltr', viewport())
    const fromTrigger = place(triggerRect, { width: 0, height: 0 }, given.side, given.align, given.sideOffset, 'ltr', viewport())
    expect(fromAnchor).not.toEqual(fromTrigger)
    expect(content.style.position === 'fixed' && content.parentElement === document.body
      && samePlace(content, fromAnchor)).toBe(expected.positionedFromAnchor)
  },
  'popover.content-options': ({ input: given, expected }) => {
    stubLayout()
    render(
      <Popover open>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent role={given.role} aria-label={given.ariaLabel} className={given.class} side={given.side} align={given.align} sideOffset={given.sideOffset} data-test="content">Body</PopoverContent>
      </Popover>,
    )
    const content = document.querySelector<HTMLElement>('.hl-popover__content')!
    const placed = place(triggerRect, { width: 0, height: 0 }, given.side, given.align, given.sideOffset, 'ltr', viewport())
    const preserved = content.getAttribute('role') === given.role && content.getAttribute('aria-label') === given.ariaLabel
      && content.classList.contains(given.class) && content.dataset.test === 'content'
      && content.dataset.side === given.side && content.dataset.align === given.align
      && samePlace(content, placed)
    expect(preserved).toBe(expected.preserved)
  },
  'popover.escape': ({ input: given, expected }) => {
    const onOpenChange = vi.fn()
    render(<Popover defaultOpen={given.open} onOpenChange={onOpenChange}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    fireEvent.keyDown(document, { key: given.key })
    expect(dialog() !== null).toBe(expected.open)
    expect(onOpenChange).toHaveBeenCalledTimes(expected.callbacks)
  },
  'popover.outside': ({ input: given, expected }) => {
    const onOpenChange = vi.fn()
    render(<Popover defaultOpen={given.open} onOpenChange={onOpenChange}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">Body</PopoverContent></Popover>)
    if (given.outsidePointer) fireEvent.pointerDown(document.body)
    expect(dialog() !== null).toBe(expected.open)
    expect(onOpenChange).toHaveBeenCalledTimes(expected.callbacks)
  },
  'popover.close-control': async ({ input: given, expected }) => {
    const onOpenChange = vi.fn()
    render(<Popover defaultOpen={given.open} onOpenChange={onOpenChange}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><PopoverClose>Close</PopoverClose></PopoverContent></Popover>)
    if (given.closeActivation) await userEvent.setup().click(screen.getByRole('button', { name: 'Close' }))
    expect(dialog() !== null).toBe(expected.open)
    expect(onOpenChange).toHaveBeenCalledTimes(expected.callbacks)
  },
  'popover.focus': async ({ input: given, expected }) => {
    render(<Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><PopoverClose>Done</PopoverClose></PopoverContent></Popover>)
    const user = userEvent.setup()
    if (given.open) await user.click(trigger())
    const close = screen.queryByRole('button', { name: 'Done' })
    expect(close !== null && document.activeElement === close).toBe(expected.focusEnteredContent)
    if (given.dismiss && close) await user.click(close)
    expect(dialog() === null && document.activeElement === trigger()).toBe(expected.focusRestoredToTrigger)
  },
  // Every row of the placement table the case names, resolved side AND left/top (ticket 298); the
  // Blazor lane replays the same file in conformance/hlp.ui.popover/popover-placement.test.mjs.
  'popover.placement': ({ input: given, expected }) => {
    const table = JSON.parse(readFileSync(resolve(process.cwd(), `../../../../conformance/hlp.ui.popover/${given.fixture}`), 'utf8')) as {
      anchors: Record<string, PlacementRect>
      cases: Array<{ align: PopoverAlign; anchor: string; direction: 'ltr' | 'rtl'; expected: { left: number; side: PopoverSide; top: number }; side: PopoverSide }>
      content: { height: number; width: number }
      sideOffset: number
      viewport: { height: number; width: number }
    }
    expect(table.cases).toHaveLength(expected.cases)
    expect(table.cases.filter(row => row.expected.side !== row.side)).toHaveLength(expected.flippedCases)
    const mismatches = table.cases.filter(row => {
      const anchor = table.anchors[row.anchor]
      if (!anchor) throw new Error(`missing-placement-anchor: ${row.anchor}`)
      return JSON.stringify(place(anchor, table.content, row.side, row.align, table.sideOffset, row.direction, table.viewport)) !== JSON.stringify(row.expected)
    })
    expect(mismatches.length === 0).toBe(expected.resolvedSideAndOffsetPerCase)
  },
  'popover.class-vocabulary': ({ input: given, expected }) => {
    const { container } = render(
      <Popover open={given.open}>
        {given.anchor === 'explicit' ? <PopoverAnchor data-testid="anchor">anchor</PopoverAnchor> : null}
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    expect([...(container.firstElementChild as HTMLElement).classList]).toEqual(expected.rootClasses)
    expect([...screen.getByTestId('anchor').classList]).toEqual(expected.anchorClasses)
    expect([...dialog()!.classList]).toEqual(expected.contentClasses)
  },
}
