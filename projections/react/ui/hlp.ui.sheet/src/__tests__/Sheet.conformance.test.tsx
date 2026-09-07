import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'

import {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
  type SheetSide,
} from '../Sheet'
import { fixture, sharedCases } from './fixtures'

const content = (side?: SheetSide) => (
  <SheetContent closeLabel="Close panel" side={side}>
    <SheetHeader>
      <SheetTitle>Calendar</SheetTitle>
      <SheetDescription>Choose a date</SheetDescription>
    </SheetHeader>
    <button type="button">First action</button>
    <SheetFooter><SheetClose>Done</SheetClose></SheetFooter>
  </SheetContent>
)

describe('Sheet shared fixtures', () => {
  it('sheet.closed', () => {
    fixture(sharedCases, 'sheet.closed')
    render(<Sheet>{content()}</Sheet>)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByTestId('sheet-overlay')).toBeNull()
  })

  it('sheet.uncontrolled', async () => {
    fixture(sharedCases, 'sheet.uncontrolled')
    const onOpenChange = vi.fn()
    render(<Sheet onOpenChange={onOpenChange}><SheetTrigger>Toggle</SheetTrigger>{content()}</Sheet>)
    const trigger = screen.getByRole('button', { name: 'Toggle' })
    await userEvent.setup().click(trigger)
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Done' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange.mock.calls.map(call => call[0])).toEqual([true, false])
  })

  it('sheet.controlled', async () => {
    fixture(sharedCases, 'sheet.controlled')
    const onOpenChange = vi.fn()
    render(<Sheet onOpenChange={onOpenChange} open={false}><SheetTrigger>Open</SheetTrigger>{content()}</Sheet>)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Open' }))
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(true)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('sheet.dialog-semantics', () => {
    fixture(sharedCases, 'sheet.dialog-semantics')
    render(<Sheet open>{content()}</Sheet>)
    const dialog = screen.getByRole('dialog', { name: 'Calendar' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAccessibleDescription('Choose a date')
  })

  it('sheet.sides', () => {
    fixture(sharedCases, 'sheet.sides')
    const { rerender } = render(<Sheet open>{content()}</Sheet>)
    expect(screen.getByRole('dialog')).toHaveAttribute('data-side', 'right')
    for (const side of ['top', 'right', 'bottom', 'left'] as const) {
      rerender(<Sheet open>{content(side)}</Sheet>)
      expect(screen.getByRole('dialog')).toHaveAttribute('data-side', side)
    }
  })

  it('sheet.escape', () => {
    fixture(sharedCases, 'sheet.escape')
    const onOpenChange = vi.fn()
    render(<Sheet defaultOpen onOpenChange={onOpenChange}>{content()}</Sheet>)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('sheet.outside', () => {
    fixture(sharedCases, 'sheet.outside')
    const onOpenChange = vi.fn()
    render(<Sheet defaultOpen onOpenChange={onOpenChange}>{content()}</Sheet>)
    fireEvent.pointerDown(screen.getByTestId('sheet-overlay'))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
  })

  it('sheet.close-control', async () => {
    fixture(sharedCases, 'sheet.close-control')
    const onOpenChange = vi.fn()
    function Harness() {
      const [open, setOpen] = useState(true)
      return (
        <Sheet open={open} onOpenChange={next => { onOpenChange(next); setOpen(next) }}>
          <SheetTrigger>Open</SheetTrigger>
          {content()}
        </Sheet>
      )
    }
    const { rerender } = render(<Harness />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Close panel' }))
    expect(onOpenChange).toHaveBeenLastCalledWith(false)
    expect(onOpenChange).toHaveBeenCalledTimes(1)

    rerender(<Harness />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Open' }))
    await userEvent.setup().click(screen.getByRole('button', { name: 'Done' }))
    expect(onOpenChange).toHaveBeenLastCalledWith(false)
    expect(onOpenChange).toHaveBeenCalledTimes(3)
  })

  it('sheet.focus', async () => {
    fixture(sharedCases, 'sheet.focus')
    render(<Sheet><SheetTrigger>Open</SheetTrigger>{content()}</Sheet>)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await userEvent.setup().click(trigger)
    expect(screen.getByRole('button', { name: 'First action' })).toHaveFocus()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Done' }))
    expect(trigger).toHaveFocus()
  })

  it('sheet.host-content', () => {
    fixture(sharedCases, 'sheet.host-content')
    render(
      <Sheet open>
        <SheetContent className="consumer" closeLabel="Close" data-sheet="calendar">
          <SheetHeader data-header="yes"><SheetTitle>Header</SheetTitle></SheetHeader>
          <SheetFooter data-footer="yes">Footer</SheetFooter>
        </SheetContent>
      </Sheet>,
    )
    const dialog = screen.getByRole('dialog', { name: 'Header' })
    expect(dialog).toHaveClass('consumer')
    expect(dialog).toHaveAttribute('data-sheet', 'calendar')
    expect(screen.getByText('Header').parentElement).toHaveAttribute('data-header', 'yes')
    expect(screen.getByText('Footer')).toHaveAttribute('data-footer', 'yes')
  })

  it('sheet.close-classes', () => {
    const expected = fixture(sharedCases, 'sheet.close-classes').expected as { builtInCloseClasses: string[] }
    render(<Sheet open>{content()}</Sheet>)
    expect([...screen.getByRole('button', { name: 'Close panel' }).classList]).toEqual(expected.builtInCloseClasses)
  })

  it('sheet.projection-equivalence', () => {
    fixture(sharedCases, 'sheet.projection-equivalence')
    render(<Sheet open><SheetTrigger>Open</SheetTrigger>{content('left')}</Sheet>)
    const trigger = screen.getByRole('button', { name: 'Open' })
    const dialog = screen.getByRole('dialog', { name: 'Calendar' })
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(trigger).toHaveAttribute('aria-controls', dialog.id)
    expect(dialog).toHaveAttribute('data-side', 'left')
  })
})
