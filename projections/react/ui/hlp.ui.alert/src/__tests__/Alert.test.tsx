import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Alert } from '../Alert'

describe('Alert revision-1', () => {
  it('maps urgency, content, attributes, and dismissal', async () => {
    const close = vi.fn()
    const { rerender } = render(<Alert variant="info" title="Saved" action={<button>Undo</button>} closable dismissLabel="Close notice" onClose={close} className="consumer" data-case="alert">Record updated</Alert>)
    expect(screen.getByRole('status')).toHaveClass('consumer')
    expect(screen.getByText('Saved')).toBeVisible()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Close notice' }))
    expect(close).toHaveBeenCalledOnce()
    rerender(<Alert variant="error">Failed</Alert>)
    expect(screen.getByRole('alert')).toHaveTextContent('Failed')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
