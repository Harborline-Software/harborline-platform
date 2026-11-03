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
