import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { Popover, PopoverContent, PopoverTrigger } from '../../../hlp.ui.popover/src/Popover'
import { Dialog } from '../Dialog'

// T-1128: a Popover inside a real modal Dialog. Its content is portaled to <body>, outside the Dialog.
// Tab out of the content follows the trigger inside the Dialog, Shift+Tab returns to the trigger, and
// the Dialog's own focus trap still owns its boundary: from a trigger that is last in the Dialog, Tab
// out of the content wraps to the Dialog's first control, exactly as native Tab from the trigger does.
function InDialog({ done }: { done: boolean }) {
  const [open, setOpen] = React.useState(true)
  return <>
    <button type="button">Page before</button>
    <Dialog open={open} onOpenChange={setOpen} title="Settings">
      <Popover><PopoverTrigger>Pick</PopoverTrigger><PopoverContent aria-label="Picker"><button type="button">Option</button></PopoverContent></Popover>
      {done ? <button type="button">Done</button> : null}
    </Dialog>
    <button type="button">Page after</button>
  </>
}

async function openPicker() {
  const user = userEvent.setup()
  fireEvent.click(screen.getByRole('button', { name: 'Pick' }))
  await act(async () => { await Promise.resolve() })
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Option' }))
  return user
}

const state = () => ({
  focus: document.activeElement === document.body ? 'BODY' : (document.activeElement?.getAttribute('aria-label') || document.activeElement?.textContent),
  picker: screen.queryByRole('dialog', { name: 'Picker' }) !== null,
  settings: screen.queryByRole('dialog', { name: 'Settings' }) !== null,
})

describe('Popover focus order inside a modal Dialog (T-1128)', () => {
  it('Tab out of the content goes to the control after the trigger inside the Dialog', async () => {
    render(<InDialog done/>)
    const user = await openPicker()
    await user.tab()
    expect(state()).toEqual({ focus: 'Done', picker: true, settings: true })
  })

  it('Shift+Tab out of the content goes to the trigger', async () => {
    render(<InDialog done/>)
    const user = await openPicker()
    await user.tab({ shift: true })
    expect(state()).toEqual({ focus: 'Pick', picker: true, settings: true })
  })

  it('the Dialog trap still wins at its boundary: from a trigger last in the Dialog, Tab out wraps to its first control', async () => {
    render(<InDialog done={false}/>)
    const user = await openPicker()
    await user.tab()
    expect(state()).toEqual({ focus: 'Close', picker: true, settings: true })
  })
})

// b31 review follow-up: nested popovers inside the Dialog, and the modal scope seen without inertness.
describe('Popover focus order inside a modal Dialog: nesting and scope (b31 review 1 and 3)', () => {
  const press = (element: Element, shiftKey = false) => {
    const native = fireEvent.keyDown(element, { key: 'Tab', shiftKey })
    return [native ? 'native' : 'handled', state().focus]
  }

  it('Tab from a nested popover whose trigger is last in the outer content continues after the outer trigger', async () => {
    function NestedInDialog() {
      const [open, setOpen] = React.useState(true)
      return <Dialog open={open} onOpenChange={setOpen} title="Settings">
        <Popover defaultOpen><PopoverTrigger>Pick</PopoverTrigger><PopoverContent aria-label="Picker">
          <button type="button">X</button>
          <Popover defaultOpen><PopoverTrigger>Open B</PopoverTrigger><PopoverContent aria-label="B"><button type="button">Y</button></PopoverContent></Popover>
        </PopoverContent></Popover>
        <button type="button">Done</button>
      </Dialog>
    }
    render(<NestedInDialog/>)
    await act(async () => { await Promise.resolve() })
    const y = screen.getByRole('button', { name: 'Y' })
    y.focus()
    expect(press(y)).toEqual(['handled', 'Done'])
  })

  // The Dialog makes its siblings inert, which hid the scope rule. A portal appended to <body> after the
  // Dialog opened (a toast region, say) is not inert, so only the modal scope keeps Tab inside.
  it('a non-inert portal after the Dialog does not take Tab from a trigger last in the Dialog: the trap wraps', async () => {
    render(<InDialog done={false}/>)
    await openPicker()
    const toast = document.createElement('div')
    toast.innerHTML = '<button type="button">Toast</button>'
    document.body.append(toast)
    try {
      const option = screen.getByRole('button', { name: 'Option' })
      // Handled by the Dialog's trap (it wraps to its first control), not moved to the toast.
      expect(press(option)).toEqual(['handled', 'Close'])
    } finally { toast.remove() }
  })
})

// b31 re-review: the way into the content inside a modal Dialog, where the trigger is last in the Dialog
// (the Dialog's trap would otherwise wrap Tab from it to the Dialog's first control).
describe('Popover focus order inside a modal Dialog: the way in (b31 re-review)', () => {
  const press = (element: Element, shiftKey = false) => {
    const native = fireEvent.keyDown(element, { key: 'Tab', shiftKey })
    return [native ? 'native' : 'handled', state().focus]
  }

  it('Tab on a trigger last in the Dialog enters the open content instead of wrapping', async () => {
    render(<InDialog done={false}/>)
    await openPicker()
    const pick = screen.getByRole('button', { name: 'Pick' })
    pick.focus()
    expect(press(pick)).toEqual(['handled', 'Option'])
  })

  it('Shift+Tab from the control after the trigger in the Dialog enters the content at its last stop', async () => {
    render(<InDialog done/>)
    await openPicker()
    const done = screen.getByRole('button', { name: 'Done' })
    done.focus()
    expect(press(done, true)).toEqual(['handled', 'Option'])
  })
})

// b31 final check: the reviewer's swap repro inside a modal Dialog (Close | Pick, the popover open from
// Pick, Pick re-rendered as a new element, Tab): the entry still beats the Dialog's trap.
describe('Popover focus order inside a modal Dialog: a re-rendered trigger (b31 final check)', () => {
  it('Tab on a trigger re-rendered while open, last in the Dialog, enters the content', async () => {
    function SwapInDialog() {
      const [generation, setGeneration] = React.useState(0)
      return <><button type="button" onClick={() => setGeneration(value => value + 1)}>Swap</button>
        <Dialog open onOpenChange={() => {}} title="Settings">
          <Popover defaultOpen><PopoverTrigger key={generation}>Pick</PopoverTrigger><PopoverContent aria-label="Picker"><button type="button">Option</button></PopoverContent></Popover>
        </Dialog></>
    }
    render(<SwapInDialog/>)
    await act(async () => { await Promise.resolve() })
    const before = screen.getByRole('button', { name: 'Pick' })
    act(() => { (screen.getByText('Swap') as HTMLButtonElement).click() })
    await act(async () => { await Promise.resolve() })
    const pick = screen.getByRole('button', { name: 'Pick' })
    expect(pick).not.toBe(before)
    pick.focus()
    expect([fireEvent.keyDown(pick, { key: 'Tab' }) ? 'native' : 'handled', state().focus]).toEqual(['handled', 'Option'])
  })
})
