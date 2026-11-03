import { fireEvent, render, screen } from '@testing-library/react'
import { expect, it, vi } from 'vitest'

import { Dialog } from '../Dialog'

// Oracle: HTML tabindex and APG modal-dialog sequential focus wrapping.
it('wraps between sequential stops, excluding a negative-tabindex button', () => {
  render(
    <Dialog open onOpenChange={vi.fn()} title="Settings">
      <button>Sequential stop</button>
      <button tabIndex={-1}>Programmatic only</button>
    </Dialog>,
  )
  const close = screen.getByRole('button', { name: 'Close' })
  const last = screen.getByRole('button', { name: 'Sequential stop' })
  close.focus()
  fireEvent.keyDown(close, { key: 'Tab', shiftKey: true })
  expect(last).toHaveFocus()
  fireEvent.keyDown(last, { key: 'Tab' })
  expect(close).toHaveFocus()
})

it('contains Tab from a programmatically focused control beyond the last sequential stop', () => {
  render(<Dialog open onOpenChange={vi.fn()} title="Settings"><button>Sequential stop</button><button tabIndex={-1}>Programmatic only</button></Dialog>)
  const excluded = screen.getByRole('button', { name: 'Programmatic only' })
  excluded.focus()
  fireEvent.keyDown(excluded, { key: 'Tab' })
  expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
})

it('contains reverse Tab from the programmatically focused dialog before its first stop', () => {
  render(<Dialog open onOpenChange={vi.fn()} title="Settings"><button>Sequential stop</button></Dialog>)
  const dialog = screen.getByRole('dialog', { name: 'Settings' })
  dialog.focus()
  fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
  expect(screen.getByRole('button', { name: 'Sequential stop' })).toHaveFocus()
})
