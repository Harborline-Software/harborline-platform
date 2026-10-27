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
