import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { Popover, PopoverContent, PopoverTrigger } from '../Popover'

const box = (left: number, top: number, width: number, height: number) =>
  ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top, toJSON() {} }) as DOMRect

// T-713: an open popover follows an anchor that moves after it opened (a page still settling). The
// unfixed lane placed once and again only on window resize or scroll, so this stayed at the old spot.
describe('Popover React placement follows its anchor (T-713)', () => {
  afterEach(() => { vi.useRealTimers() })

  it('re-places an open popover when its anchor moves without a resize or scroll', () => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    let anchorBox = box(600, 110, 112, 38)
    render(
      <Popover defaultOpen>
        <PopoverTrigger>Open details</PopoverTrigger>
        <PopoverContent aria-label="Details" side="bottom" align="start" sideOffset={6}>Body</PopoverContent>
      </Popover>,
    )
    screen.getByRole('button', { name: 'Open details' }).getBoundingClientRect = () => anchorBox
    act(() => { vi.advanceTimersToNextFrame() })
    const dialog = screen.getByRole('dialog', { name: 'Details' })
    expect([dialog.style.left, dialog.style.top]).toEqual(['600px', '154px'])

    anchorBox = box(616, 126, 112, 38)
    act(() => { vi.advanceTimersToNextFrame() })
    expect([dialog.style.left, dialog.style.top]).toEqual(['616px', '170px'])
  })
})
