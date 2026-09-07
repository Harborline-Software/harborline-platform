import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { DataExportButton } from '../DataExportButton'

describe('DataExportButton revision-1', () => {
  it('opens, moves focus, invokes one ordered format, and closes', async () => {
    const exported = vi.fn()
    const user = userEvent.setup()
    render(<DataExportButton formats={['pdf', 'json', 'md']} onExport={exported} className="consumer" />)
    const trigger = screen.getByRole('button', { name: 'Export' })
    await user.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    const items = screen.getAllByRole('menuitem')
    expect(items.map(item => item.getAttribute('data-format'))).toEqual(['pdf', 'json', 'md'])
    items[0]!.focus()
    await user.keyboard('{ArrowDown}{ArrowUp}{Enter}')
    expect(exported).toHaveBeenCalledOnce()
    expect(exported).toHaveBeenCalledWith('pdf')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
  it('keeps disabled and loading triggers inert', async () => {
    const exported = vi.fn()
    const { rerender } = render(<DataExportButton disabled onExport={exported} />)
    expect(screen.getByRole('button')).toBeDisabled()
    rerender(<DataExportButton loading onExport={exported} />)
    expect(screen.getByRole('button', { name: 'Loading' })).toBeDisabled()
    expect(exported).not.toHaveBeenCalled()
  })
  it('invokes a single configured format directly without menu semantics', async () => {
    const exported = vi.fn()
    const user = userEvent.setup()
    render(<DataExportButton formats={['csv']} onExport={exported} />)
    const trigger = screen.getByRole('button', { name: 'Export' })
    expect(trigger).not.toHaveAttribute('aria-haspopup')
    await user.click(trigger)
    expect(exported).toHaveBeenCalledOnce()
    expect(exported).toHaveBeenCalledWith('csv')
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })
})
