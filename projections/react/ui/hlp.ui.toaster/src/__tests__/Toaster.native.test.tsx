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

  // Contract: TrackAsync returns or propagates the original operation result. A host disposed while
  // the operation runs (an unmounted root) must not replace that outcome or keep toast state.
  it('returns or rethrows the original outcome when the host is disposed during a tracked operation', async () => {
    const service = createToastService()
    let succeed!: (value: number) => void
    const tracked = service.trackAsync(new Promise<number>(done => { succeed = done }), { loading: 'Saving', success: 'Saved', error: 'Failed' })
    expect(service.getSnapshot().open).toHaveLength(1)
    service.dispose()
    succeed(7)
    await expect(tracked).resolves.toBe(7)
    expect(service.getSnapshot().open).toHaveLength(0)

    const failing = createToastService()
    let fail!: (reason: Error) => void
    const original = new Error('network down')
    const rejected = failing.trackAsync(new Promise<number>((_, reject) => { fail = reject }), { loading: 'Saving', success: 'Saved', error: 'Failed' })
    failing.dispose()
    fail(original)
    await expect(rejected).rejects.toBe(original)
    expect(failing.getSnapshot().open).toHaveLength(0)
  })
})
