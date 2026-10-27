import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { Popover, PopoverContent, PopoverTrigger } from '../Popover'

// Opening moves focus into the content. With nothing focusable inside, the content element itself takes
// focus (it is programmatically focusable, tabindex -1), as the Blazor lane's popover.js does. Otherwise
// a pointer open that does not focus the trigger (WebKit) left focus outside, and Escape went to an
// enclosing layer instead of the popover.
describe('Popover focus fallback', () => {
  it('focuses the content element when it has nothing focusable', async () => {
    render(<Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><p>Read only</p></PopoverContent></Popover>)
    fireEvent.click(screen.getByRole('button', { name: 'Open' }))
    await act(async () => { await Promise.resolve() })
    const content = screen.getByRole('dialog', { name: 'Details' })
    expect([document.activeElement === content, content.getAttribute('tabindex')]).toEqual([true, '-1'])
  })

  it('still focuses the first focusable control when there is one', async () => {
    render(<Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Apply</button></PopoverContent></Popover>)
    fireEvent.click(screen.getByRole('button', { name: 'Open' }))
    await act(async () => { await Promise.resolve() })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Apply' }))
  })

  it('keeps a host tabIndex on the content', () => {
    render(<Popover open><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details" tabIndex={0}><p>Read only</p></PopoverContent></Popover>)
    expect(screen.getByRole('dialog', { name: 'Details' }).getAttribute('tabindex')).toBe('0')
  })

  it('an Escape from the focused content closes only the popover and is marked handled', async () => {
    const onOpenChange = vi.fn()
    const outer = vi.fn((event: KeyboardEvent) => { if (!event.defaultPrevented) outerClosed() })
    const outerClosed = vi.fn()
    document.addEventListener('keydown', outer)
    try {
      render(<Popover onOpenChange={onOpenChange}><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><p>Read only</p></PopoverContent></Popover>)
      fireEvent.click(screen.getByRole('button', { name: 'Open' }))
      await act(async () => { await Promise.resolve() })
      fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
      expect([onOpenChange.mock.calls.map(call => call[0]), outerClosed.mock.calls.length, document.activeElement === screen.getByRole('button', { name: 'Open' })]).toEqual([[true, false], 0, true])
    } finally { document.removeEventListener('keydown', outer) }
  })
})
