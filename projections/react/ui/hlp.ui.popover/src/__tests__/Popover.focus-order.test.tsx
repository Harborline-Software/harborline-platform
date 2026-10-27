import '@testing-library/jest-dom/vitest'
import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { Sheet, SheetContent, SheetTitle } from '../../../hlp.ui.sheet/src/Sheet'
import { Popover, PopoverContent, PopoverTrigger } from '../Popover'

// T-1128. The content is portaled to the end of <body>, so native Tab order put it after the whole
// page: Tab out of it went to the end of the page and Shift+Tab to the last control, with the popover
// still open. As in the HTML popover focus navigation model (WHATWG HTML 6.6.2, a showing popover's
// focus navigation scope is inserted after its invoker), the content sits right after its trigger in
// sequential focus order: Tab out lands on what follows the trigger, Shift+Tab out lands on the
// trigger. Tab within the content is untouched, and leaving does not close it (it is not a menu).
function Page({ content, after = true }: { content: React.ReactNode, after?: boolean }) {
  return <>
    <button type="button">Before</button>
    <Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details">{content}</PopoverContent></Popover>
    {after ? <button type="button">After</button> : null}
  </>
}

async function open() {
  const user = userEvent.setup()
  fireEvent.click(screen.getByRole('button', { name: 'Open' }))
  await act(async () => { await Promise.resolve() })
  return user
}

const popoverOpen = () => screen.queryByRole('dialog', { name: 'Details' }) !== null
const focused = () => (document.activeElement === document.body ? 'BODY' : document.activeElement?.textContent || document.activeElement?.getAttribute('aria-label'))

describe('Popover sequential focus order (T-1128)', () => {
  it('Tab from the last control in the content moves to the element after the trigger and keeps it open', async () => {
    render(<Page content={<button type="button">Apply</button>}/>)
    const user = await open()
    expect(focused()).toBe('Apply')
    await user.tab()
    expect([focused(), popoverOpen()]).toEqual(['After', true])
  })

  it('Shift+Tab from the first control in the content moves to the trigger and keeps it open', async () => {
    render(<Page content={<button type="button">Apply</button>}/>)
    const user = await open()
    await user.tab({ shift: true })
    expect([focused(), popoverOpen()]).toEqual(['Open', true])
  })

  it('Tab and Shift+Tab between controls inside the content stay native and keep it open', async () => {
    render(<Page content={<><button type="button">First</button><button type="button">Second</button></>}/>)
    await open()
    const first = screen.getByRole('button', { name: 'First' })
    const second = screen.getByRole('button', { name: 'Second' })
    expect(document.activeElement).toBe(first)
    // Not intercepted: the key is not marked handled and focus is not moved by the popover.
    expect([fireEvent.keyDown(first, { key: 'Tab' }), document.activeElement === first]).toEqual([true, true])
    second.focus()
    expect([fireEvent.keyDown(second, { key: 'Tab', shiftKey: true }), document.activeElement === second]).toEqual([true, true])
    expect(popoverOpen()).toBe(true)
  })

  it('with nothing tabbable in the content, Tab from the content element goes after the trigger and Shift+Tab to it', async () => {
    render(<Page content={<p>Read only</p>}/>)
    const user = await open()
    expect(document.activeElement).toBe(screen.getByRole('dialog', { name: 'Details' }))
    await user.tab()
    expect([focused(), popoverOpen()]).toEqual(['After', true])
    screen.getByRole('dialog', { name: 'Details' }).focus()
    await user.tab({ shift: true })
    expect([focused(), popoverOpen()]).toEqual(['Open', true])
  })

  it('Shift+Tab from the content element itself goes to the trigger even when the content has controls', async () => {
    render(<Page content={<button type="button">Apply</button>}/>)
    const user = await open()
    screen.getByRole('dialog', { name: 'Details' }).focus()
    await user.tab({ shift: true })
    expect([focused(), popoverOpen()]).toEqual(['Open', true])
  })

  // The trigger is the last tabbable element on the page: Tab out of the content goes wherever native
  // Tab from the trigger goes. The popover leaves the key to the browser (not handled) with focus on the
  // trigger, so the browser's own Tab continues from there (to the browser chrome, in a real page).
  it('when the trigger is last on the page, Tab out leaves the key to the browser, continuing from the trigger', async () => {
    render(<Page content={<button type="button">Apply</button>} after={false}/>)
    await open()
    const apply = screen.getByRole('button', { name: 'Apply' })
    expect([fireEvent.keyDown(apply, { key: 'Tab' }), focused(), popoverOpen()]).toEqual([true, 'Open', true])
  })

  it('a host onKeyDown that handles Tab wins', async () => {
    render(<Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details" onKeyDown={event => { if (event.key === 'Tab') event.preventDefault() }}><button type="button">Apply</button></PopoverContent></Popover>)
    await open()
    const apply = screen.getByRole('button', { name: 'Apply' })
    fireEvent.keyDown(apply, { key: 'Tab' })
    expect(document.activeElement).toBe(apply)
  })

  // Inside a modal Sheet, the elements after the trigger inside the Sheet follow it; the Sheet's own
  // trap still owns its boundary.
  it('inside a modal Sheet, Tab out goes to the next control in the Sheet and Shift+Tab to the trigger', async () => {
    render(<Sheet defaultOpen><SheetContent closeLabel="Close panel"><SheetTitle>Panel</SheetTitle>
      <Popover><PopoverTrigger>Open</PopoverTrigger><PopoverContent aria-label="Details"><button type="button">Option</button></PopoverContent></Popover>
    </SheetContent></Sheet>)
    const user = await open()
    expect(focused()).toBe('Option')
    await user.tab()
    expect([focused(), popoverOpen()]).toEqual(['Close panel', true])
    screen.getByRole('button', { name: 'Option' }).focus()
    await user.tab({ shift: true })
    expect([focused(), popoverOpen()]).toEqual(['Open', true])
  })
})
