import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { Popover, PopoverAnchor, PopoverClose, PopoverContent, PopoverTrigger, place } from '../Popover'
import type { PlacementRect, PopoverAlign, PopoverSide } from '../Popover'
import { fixture, sharedCases } from './fixtures'

interface PlacementTable {
  anchors: Record<string, PlacementRect>
  cases: Array<{
    align: PopoverAlign
    anchor: string
    direction: 'ltr' | 'rtl'
    expected: { left: number; side: PopoverSide; top: number }
    side: PopoverSide
  }>
  content: { height: number; width: number }
  sideOffset: number
  viewport: { height: number; width: number }
}

function readPlacementTable(name: string): PlacementTable {
  const path = resolve(process.cwd(), `../../../../conformance/hlp.ui.popover/${name}`)
  return JSON.parse(readFileSync(path, 'utf8')) as PlacementTable
}

describe('Popover shared fixtures', () => {
  it('popover.closed', () => {
    fixture(sharedCases, 'popover.closed')
    render(
      <Popover>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    expect(screen.getByRole('button', { name: 'Open' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('popover.uncontrolled', async () => {
    fixture(sharedCases, 'popover.uncontrolled')
    const onOpenChange = vi.fn()
    render(
      <Popover onOpenChange={onOpenChange}>
        <PopoverTrigger>Toggle</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    const trigger = screen.getByRole('button', { name: 'Toggle' })
    await userEvent.setup().click(trigger)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await userEvent.setup().click(trigger)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange.mock.calls.map(call => call[0])).toEqual([true, false])
  })

  it('popover.controlled', async () => {
    fixture(sharedCases, 'popover.controlled')
    const onOpenChange = vi.fn()
    render(
      <Popover onOpenChange={onOpenChange} open={false}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Open' }))
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(true)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('popover.trigger-aria', () => {
    fixture(sharedCases, 'popover.trigger-aria')
    render(
      <Popover open>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    const content = screen.getByRole('dialog', { name: 'Details' })
    expect(trigger).toHaveAttribute('aria-haspopup', 'dialog')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(trigger).toHaveAttribute('aria-controls', content.id)
  })

  it('popover.anchor', () => {
    fixture(sharedCases, 'popover.anchor')
    render(
      <Popover open>
        <PopoverAnchor data-test="anchor">Anchor</PopoverAnchor>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    const content = screen.getByRole('dialog', { name: 'Details' })
    expect(screen.getByText('Anchor')).toHaveAttribute('data-test', 'anchor')
    expect(content.parentElement).toBe(document.body)
    expect(content).toHaveAttribute('data-side', 'bottom')
    expect(content).toHaveAttribute('data-align', 'center')
    expect(content.style.position).toBe('fixed')
  })

  it('popover.content-options', () => {
    fixture(sharedCases, 'popover.content-options')
    render(
      <Popover open>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent
          aria-label="Details"
          className="consumer"
          data-test="content"
          side="right"
          align="end"
          sideOffset={12}
        >
          Body
        </PopoverContent>
      </Popover>,
    )
    const content = screen.getByRole('dialog', { name: 'Details' })
    expect(content).toHaveClass('consumer')
    expect(content).toHaveAttribute('data-test', 'content')
    expect(content).toHaveAttribute('data-align', 'end')
  })

  it('popover.escape', () => {
    fixture(sharedCases, 'popover.escape')
    const onOpenChange = vi.fn()
    render(
      <Popover defaultOpen onOpenChange={onOpenChange}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('popover.outside', () => {
    fixture(sharedCases, 'popover.outside')
    const onOpenChange = vi.fn()
    render(
      <Popover defaultOpen onOpenChange={onOpenChange}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    fireEvent.pointerDown(document.body)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('popover.close-control', async () => {
    fixture(sharedCases, 'popover.close-control')
    const onOpenChange = vi.fn()
    render(
      <Popover defaultOpen onOpenChange={onOpenChange}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details"><PopoverClose>Close</PopoverClose></PopoverContent>
      </Popover>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('popover.focus', async () => {
    const { input, expected } = fixture(sharedCases, 'popover.focus')
    const user = userEvent.setup()
    render(
      <>
        <Popover>
          <PopoverTrigger>Open</PopoverTrigger>
          <PopoverContent aria-label="Details"><PopoverClose>Done</PopoverClose></PopoverContent>
        </Popover>
        <input aria-label="Outside" />
        <div>Plain</div>
      </>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    const outside = screen.getByRole('textbox', { name: 'Outside' })
    const open = async () => {
      await user.click(trigger)
      return screen.getByRole('button', { name: 'Done' })
    }
    const restored: Record<string, boolean> = {}
    let entered = true
    let outsideKept = false
    let nonFocusableNotTrigger = false
    for (const method of input.methods as string[]) {
      const close = await open()
      entered &&= close === document.activeElement
      if (method === 'Escape') await user.keyboard('{Escape}')
      if (method === 'close-control') await user.click(close)
      if (method === 'trigger-toggle') await user.click(trigger)
      if (method === 'outside-pointer-focusable') {
        // A press with no mousedown default first, then the browser's focus: the trigger must never take it.
        let triggerTookFocus = false
        const onTriggerFocus = () => { triggerTookFocus = true }
        trigger.addEventListener('focus', onTriggerFocus)
        fireEvent.pointerDown(outside)
        act(() => outside.focus())
        trigger.removeEventListener('focus', onTriggerFocus)
        outsideKept = document.activeElement === outside && !triggerTookFocus
      }
      if (method === 'outside-pointer-non-focusable') {
        fireEvent.pointerDown(screen.getByText('Plain'))
        await act(async () => { await Promise.resolve() })
        nonFocusableNotTrigger = document.activeElement !== trigger
      }
      expect(screen.queryByRole('dialog')).toBeNull()
      if (['Escape', 'close-control', 'trigger-toggle'].includes(method)) restored[method] = document.activeElement === trigger
    }
    expect({
      focusEnteredContent: entered,
      escapeCloseControlAndToggleRestoreTrigger: Object.values(restored).length === 3 && Object.values(restored).every(Boolean),
      outsideTargetRetainsFocus: outsideKept,
      outsideNonFocusableNotTrigger: nonFocusableNotTrigger,
    }).toEqual(expected)
  })

  it('popover.projection-equivalence', () => {
    fixture(sharedCases, 'popover.projection-equivalence')
    render(
      <Popover open>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details"><PopoverClose>Close</PopoverClose></PopoverContent>
      </Popover>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    const content = screen.getByRole('dialog', { name: 'Details' })
    expect(trigger).toHaveAttribute('aria-controls', content.id)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(content).toHaveAttribute('data-side', 'bottom')
    expect(content).toHaveAttribute('data-align', 'center')
  })

  // Every row of the placement table, read from the file the fixture row names and asserted in the
  // Blazor lane too (conformance/hlp.ui.popover/popover-placement.test.mjs). Resolved side AND the
  // computed left/top, not a boolean: this lane flips at an edge and the Blazor lane used to only
  // clamp, and the equivalence case could not see it (ticket 298).
  it('popover.placement', () => {
    const expected = fixture(sharedCases, 'popover.placement').expected as Record<string, number | boolean>
    const table = readPlacementTable(fixture(sharedCases, 'popover.placement').input.fixture as string)
    expect(table.cases).toHaveLength(expected.cases as number)
    expect(table.cases.filter(row => row.expected.side !== row.side)).toHaveLength(expected.flippedCases as number)
    for (const row of table.cases) {
      const anchor = table.anchors[row.anchor]
      if (!anchor) throw new Error(`missing-placement-anchor: ${row.anchor}`)
      const resolved = place(anchor, table.content, row.side, row.align, table.sideOffset, row.direction, table.viewport)
      expect(resolved, `${row.anchor} ${row.side} ${row.align}`).toEqual(row.expected)
    }
  })

  // The placement contract uses logical alignment before viewport clamping. Interior literals
  // distinguish each alignment; the frozen edge corpus legitimately clamps several to one point.
  it.each([
    ['start', 'ltr', 300],
    ['center', 'ltr', 290],
    ['end', 'ltr', 280],
    ['start', 'rtl', 280],
    ['center', 'rtl', 290],
    ['end', 'rtl', 300],
  ] as const)('places an unclamped bottom surface with %s alignment in %s', (align, direction, left) => {
    const anchor = { left: 300, top: 300, right: 380, bottom: 330, width: 80, height: 30 }
    expect(place(anchor, { width: 100, height: 40 }, 'bottom', align, 6, direction, { width: 1000, height: 800 }))
      .toEqual({ side: 'bottom', left, top: 336 })
  })

  it.each([
    ['start', 300],
    ['center', 295],
    ['end', 290],
  ] as const)('places an unclamped right surface with %s alignment', (align, top) => {
    const anchor = { left: 300, top: 300, right: 380, bottom: 330, width: 80, height: 30 }
    expect(place(anchor, { width: 100, height: 40 }, 'right', align, 6, 'ltr', { width: 1000, height: 800 }))
      .toEqual({ side: 'right', left: 386, top })
  })

  it('retains the requested side when the surface fits exactly within the eight-pixel margin', () => {
    // 46px of bottom room exactly fits 40px of content plus the literal 6px side offset.
    const anchor = { left: 300, top: 716, right: 380, bottom: 746, width: 80, height: 30 }
    expect(place(anchor, { width: 100, height: 40 }, 'bottom', 'start', 6, 'ltr', { width: 1000, height: 800 }))
      .toEqual({ side: 'bottom', left: 300, top: 752 })
  })

  it('retains the requested side when neither side fits and the opposite has equal room', () => {
    // Both sides have 32px of room. Equal room is not the contract's strictly greater room.
    const anchor = { left: 300, top: 40, right: 380, bottom: 60, width: 80, height: 20 }
    expect(place(anchor, { width: 100, height: 40 }, 'bottom', 'start', 6, 'ltr', { width: 1000, height: 100 }))
      .toEqual({ side: 'bottom', left: 300, top: 52 })
  })

  // The class list of the root, the anchor and the content, from the fixture. The Blazor lane used
  // to style hl-popover-root, hl-popover-anchor and an hl-popover-content alias out of its own
  // wwwroot copy, none of which the authority defined, so the two lanes styled the same surface
  // differently and parity could not see it. Both lanes now read this row.
  it('popover.class-vocabulary', () => {
    const expected = fixture(sharedCases, 'popover.class-vocabulary').expected as Record<string, string[]>
    const { container } = render(
      <Popover open>
        <PopoverAnchor data-testid="anchor">anchor</PopoverAnchor>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    expect([...(container.firstElementChild as HTMLElement).classList]).toEqual(expected.rootClasses)
    expect([...screen.getByTestId('anchor').classList]).toEqual(expected.anchorClasses)
    expect([...screen.getByRole('dialog', { name: 'Details' }).classList]).toEqual(expected.contentClasses)
  })
})
