import { fireEvent, render, screen } from '@testing-library/react'
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
    fixture(sharedCases, 'popover.focus')
    render(
      <Popover>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details"><PopoverClose>Done</PopoverClose></PopoverContent>
      </Popover>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    await userEvent.setup().click(trigger)
    const close = screen.getByRole('button', { name: 'Done' })
    expect(close).toHaveFocus()
    await userEvent.setup().click(close)
    expect(trigger).toHaveFocus()
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
