import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { createToastService, Toaster } from '../Toaster'

describe('Toaster React projection', () => {
  it('isolates hosts, bounds newest entries, and applies roles without aria-live', () => {
    const service = createToastService({ maximumVisible: 2 })
    const other = createToastService()
    render(<Toaster service={service}/>)
    act(() => { service.show('A'); service.success('B'); service.error('C') })
    expect(screen.queryByText('A')).toBeNull()
    expect(screen.getByText('B').closest('[role="status"]')).not.toHaveAttribute('aria-live')
    expect(screen.getByText('C').closest('[role="alert"]')).not.toHaveAttribute('aria-live')
    expect(document.querySelectorAll('.hl-toaster__marker svg')).toHaveLength(2)
    for (const marker of document.querySelectorAll('.hl-toaster__marker')) expect(marker).not.toHaveTextContent(/[✓!•…]/)
    expect(other.getSnapshot().open).toHaveLength(0)
  })

  it('keeps persistent variants closable and invokes actions once without dismissal', () => {
    const action = vi.fn()
    const service = createToastService()
    render(<Toaster service={service} dismissLabel="Close toast"/>)
    act(() => { service.error('Failed', { action: { label: 'Retry', onActivate: action } }) })
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(action).toHaveBeenCalledOnce()
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close toast' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close toast' })).not.toHaveTextContent('×')
    fireEvent.click(screen.getByRole('button', { name: 'Close toast' }))
    expect(screen.queryByText('Failed')).toBeNull()
  })

  it('preserves identity across asynchronous success', async () => {
    const service = createToastService()
    render(<Toaster service={service}/>)
    await act(async () => expect(await service.trackAsync(Promise.resolve(7), { loading: 'Loading', success: value => `Ready ${value}`, error: 'Failed' })).toBe(7))
    expect(screen.getByText('Ready 7').closest('[data-toast-id]')).toHaveAttribute('data-toast-id', 'toast-1')
  })

  it('rejects invalid input deterministically', () => {
    const service = createToastService()
    expect(() => service.show('')).toThrow('toast-message-required')
    expect(() => service.configure({ maximumVisible: 0 })).toThrow('invalid-toast-limit')
    expect(() => service.show('x', { duration: -1 })).toThrow('invalid-toast-duration')
  })
})
