import { act, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { flushSync } from 'react-dom'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Popover, PopoverClose, PopoverContent, PopoverTrigger } from '../Popover'

// G5 (R-0125, owner ruling 2026-10-27). As in WHATWG HTML's hide popover algorithm (light dismiss passes
// focusPreviousElement false; other hides restore focus only when it is inside the popover, without
// scrolling): an outside pointer dismissal never moves focus, and every other close restores the trigger
// with preventScroll only when focus is inside the closing content or was lost with it to the body.
// The restore effect used to focus the trigger on every close. In a browser, React flushes the close
// before the mousedown default, so a pressed field kept focus only by accident of event order; a press
// with no mousedown default (here fireEvent, in a browser a touch that becomes a scroll) gave the
// trigger focus.
function Page({ open, onOpenChange }: { open?: boolean, onOpenChange?: (open: boolean) => void }) {
  return (
    <main>
      <Popover open={open} onOpenChange={onOpenChange}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">
          <button type="button">Apply</button>
          <PopoverClose>Done</PopoverClose>
        </PopoverContent>
      </Popover>
      <input aria-label="Outside field" />
      <div>Plain text</div>
    </main>
  )
}

async function openPage() {
  const user = userEvent.setup()
  render(<Page />)
  const trigger = screen.getByRole('button', { name: 'Open' })
  await user.click(trigger)
  expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Apply' }))
  const triggerFocus = vi.spyOn(trigger, 'focus')
  return { user, trigger, triggerFocus }
}

describe('Popover focus after a dismissal (G5)', () => {
  it('a pointer press on an outside field closes the popover and leaves focus and typing in the field', async () => {
    const { user, trigger, triggerFocus } = await openPage()
    const field = screen.getByRole('textbox', { name: 'Outside field' })
    await user.click(field)
    await user.keyboard('abc')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(field)
    expect(field).toHaveValue('abc')
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a pointer press on a non-focusable node closes the popover without focusing the trigger', async () => {
    const { user, trigger, triggerFocus } = await openPage()
    await user.click(screen.getByText('Plain text'))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an outside pointerdown with no mousedown default (a touch that scrolls) does not focus the trigger', async () => {
    const { trigger, triggerFocus } = await openPage()
    fireEvent.pointerDown(screen.getByText('Plain text'))
    await act(async () => { await Promise.resolve() })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('Escape from inside the content restores the trigger without scrolling', async () => {
    const { user, trigger, triggerFocus } = await openPage()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('the close control restores the trigger without scrolling', async () => {
    const { user, trigger, triggerFocus } = await openPage()
    await user.click(screen.getByRole('button', { name: 'Done' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('trigger activation closes with focus on the trigger', async () => {
    const { user, trigger } = await openPage()
    await user.click(trigger)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
  })

  it('trigger activation that does not focus the trigger (WebKit) restores it from the body without scrolling', async () => {
    const { trigger, triggerFocus } = await openPage()
    fireEvent.click(trigger) // a click with no focus change, as WebKit does for buttons
    await act(async () => { await Promise.resolve() })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('a controlled close while focus is outside the content does not move focus', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<Page open />)
    const field = screen.getByRole('textbox', { name: 'Outside field' })
    const trigger = screen.getByRole('button', { name: 'Open' })
    act(() => field.focus())
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} />)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(field)
    expect(triggerFocus).not.toHaveBeenCalled()
    await user.keyboard('x')
    expect(field).toHaveValue('x')
  })

  it('a controlled close while focus is inside the content restores the trigger without scrolling', () => {
    const { rerender } = render(<Page open />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    act(() => screen.getByRole('button', { name: 'Apply' }).focus())
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} />)
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('an outside press does not carry over: after reopening, Escape restores the trigger', async () => {
    const { user, trigger, triggerFocus } = await openPage()
    await user.click(screen.getByText('Plain text'))
    await user.click(trigger)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenLastCalledWith({ preventScroll: true })
  })

  // A controlled host may refuse the close it is asked for. The outside press then did not close the
  // popover, so a later host-driven close follows the ordinary rule.
  it('an outside press a controlled host refuses, then focus back in the content, then a host close: the trigger is restored', async () => {
    const user = userEvent.setup()
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(screen.getByText('Plain text'))
    expect(refuse).toHaveBeenCalledWith(false)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
    act(() => screen.getByRole('button', { name: 'Apply' }).focus())
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(trigger).toHaveFocus()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('an outside press a controlled host refuses, focus left on the outside field, then a host close: focus stays on the field', async () => {
    const user = userEvent.setup()
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    const field = screen.getByRole('textbox', { name: 'Outside field' })
    await user.click(field)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(document.activeElement).toBe(field)
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  // b32 review: refusal is not inferred from timing. The flag is set before the request and stays until
  // focus comes back into the content, a press lands in the content or on the trigger, or another close
  // path (Escape, the close control, the trigger) starts.
  it('a host that accepts an outside press after a delay (a fetch) does not focus the trigger', async () => {
    const user = userEvent.setup()
    function Delayed() {
      const [open, setOpen] = useState(true)
      return <Page open={open} onOpenChange={next => { setTimeout(() => setOpen(next), 20) }} />
    }
    render(<Delayed />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    const triggerFocus = vi.spyOn(trigger, 'focus')
    await user.click(screen.getByText('Plain text'))
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 60)) })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a host that closes with flushSync in onOpenChange does not focus the trigger on an outside press without a mousedown default', async () => {
    function Synced() {
      const [open, setOpen] = useState(true)
      return <Page open={open} onOpenChange={next => flushSync(() => setOpen(next))} />
    }
    render(<Synced />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Apply' }))
    const triggerFocus = vi.spyOn(trigger, 'focus')
    fireEvent.pointerDown(screen.getByText('Plain text'))
    await act(async () => { await Promise.resolve() })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('two outside presses, the first refused and the second accepted: the trigger is not focused', async () => {
    const user = userEvent.setup()
    function SecondAccepted() {
      const [open, setOpen] = useState(true)
      const [requests, setRequests] = useState(0)
      return <Page open={open} onOpenChange={next => { setRequests(requests + 1); if (requests >= 1) setOpen(next) }} />
    }
    render(<SecondAccepted />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    const triggerFocus = vi.spyOn(trigger, 'focus')
    await user.click(screen.getByText('Plain text'))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await user.click(screen.getByText('Plain text'))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a refused press on a non-focusable node, focus left on the body, then a host close: focus stays on the body (ruling point 1)', async () => {
    const user = userEvent.setup()
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(screen.getByText('Plain text'))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 10)) })
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(trigger).not.toHaveFocus()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a refused press, then a press inside the content that does not focus (WebKit buttons), then a host close: the trigger is restored', async () => {
    const user = userEvent.setup()
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(screen.getByText('Plain text'))
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Apply' }))
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('a refused press, then Escape with focus on the body that the host accepts: the trigger is restored', async () => {
    const user = userEvent.setup()
    function RefuseFirst() {
      const [open, setOpen] = useState(true)
      const [requests, setRequests] = useState(0)
      return <Page open={open} onOpenChange={next => { setRequests(requests + 1); if (requests >= 1) setOpen(next) }} />
    }
    render(<RefuseFirst />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(screen.getByText('Plain text'))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(document.activeElement).toBe(document.body)
    const triggerFocus = vi.spyOn(trigger, 'focus')
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  // Confirmation pass: an outside press that does not take focus leaves focus in the content (no focusin). A key
  // in the content clears the flag too.
  it('an outside press that keeps focus in the content, refused, then Enter in the content, then a host close: the trigger is restored', () => {
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    const apply = screen.getByRole('button', { name: 'Apply' })
    expect(document.activeElement).toBe(apply)
    fireEvent.pointerDown(screen.getByText('Plain text')) // no mousedown default: focus stays on Apply
    expect(refuse).toHaveBeenCalledWith(false)
    fireEvent.keyDown(apply, { key: 'Enter' })
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('an outside press that keeps focus in the content, refused, no key, then a host close: not restored (residual case, pinned)', () => {
    const refuse = vi.fn()
    const { rerender } = render(<Page open onOpenChange={refuse} />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    fireEvent.pointerDown(screen.getByText('Plain text'))
    const triggerFocus = vi.spyOn(trigger, 'focus')
    rerender(<Page open={false} onOpenChange={refuse} />)
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an outside press the host prevents keeps the popover open, and a later Escape restores the trigger', async () => {
    const user = userEvent.setup()
    function Prevented() {
      return (
        <Popover defaultOpen>
          <PopoverTrigger>Open</PopoverTrigger>
          <PopoverContent aria-label="Details" onInteractOutside={event => event.preventDefault()}>
            <button type="button">Apply</button>
          </PopoverContent>
        </Popover>
      )
    }
    render(<><Prevented /><div>Plain text</div></>)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await user.click(screen.getByText('Plain text'))
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Apply' }))
    await user.keyboard('{Escape}')
    expect(trigger).toHaveFocus()
  })
})
