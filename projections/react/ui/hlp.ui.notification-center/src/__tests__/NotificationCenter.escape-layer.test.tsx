import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { Sheet, SheetContent, SheetTitle } from '../../../hlp.ui.sheet/src/Sheet'
import { NotificationCenter } from '../NotificationCenter'

// Informational items with no handlers leave the panel with no focusable control. A pointer open that
// does not focus the bell (WebKit) must still put focus in the panel, so one Escape closes only the
// panel inside a real Sheet, focus returns to the bell, and the next Escape closes the Sheet.
function InSheet() {
  const [open, setOpen] = React.useState(true)
  return <><span data-testid="sheet-open">{String(open)}</span>
    <Sheet open={open} onOpenChange={setOpen}><SheetContent closeLabel="Close panel"><SheetTitle>Panel</SheetTitle>
      <NotificationCenter items={[{ id: 'r1', title: 'Export ready', timestamp: 'now', kind: 'result' }]}/>
    </SheetContent></Sheet></>
}

describe('NotificationCenter Escape inside a Sheet with no focusable control in the panel', () => {
  it('a pointer open that does not focus the bell: one Escape closes only the panel, the next the Sheet', async () => {
    render(<InSheet/>)
    const bell = document.querySelector<HTMLButtonElement>('.hl-notification-center__trigger')!
    // As in WebKit, the click does not focus the bell: focus stays where it was (here, nowhere).
    await act(async () => { await new Promise(resolve => requestAnimationFrame(resolve)) })
    ;(document.activeElement as HTMLElement | null)?.blur()
    fireEvent.click(bell)
    await act(async () => { await Promise.resolve() })
    const panel = document.querySelector<HTMLElement>('.hl-notification-center__dialog')!
    expect(document.activeElement).toBe(panel)
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await act(async () => { await Promise.resolve() })
    expect([document.querySelector('.hl-notification-center__dialog'), screen.getByTestId('sheet-open').textContent, document.activeElement === bell]).toEqual([null, 'true', true])
    fireEvent.keyDown(document.activeElement!, { key: 'Escape' })
    await act(async () => { await Promise.resolve() })
    expect(screen.getByTestId('sheet-open').textContent).toBe('false')
  })
})
