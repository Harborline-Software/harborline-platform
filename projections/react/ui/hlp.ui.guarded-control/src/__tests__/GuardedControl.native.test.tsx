import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl } from '../GuardedControl'

afterEach(() => vi.useRealTimers())
describe('GuardedControl React projection', () => {
  it('requires two activations and latches asynchronous commit', async () => {
    let resolve!: () => void
    const commit = vi.fn(() => new Promise<void>(done => { resolve = done }))
    render(<GuardedControl action="packages.export" classificationId="new-record-type" label="Sign and save" onCommit={commit}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Sign and save' }))
    expect(commit).not.toHaveBeenCalled()
    const armed = screen.getByRole('button', { name: /Sign and save/ })
    fireEvent.click(armed)
    fireEvent.click(armed)
    expect(commit).toHaveBeenCalledOnce()
    await act(async () => resolve())
    expect(screen.getByRole('button', { name: 'Sign and save' })).toHaveAttribute('data-guard-state', 'covered')
  })

  it('recovers exactly once on timeout', () => {
    vi.useFakeTimers()
    const observe = vi.fn()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined} observe={observe}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    act(() => vi.advanceTimersByTime(5000))
    expect(observe.mock.calls.filter(([event]) => event.type === 'recovered')).toHaveLength(1)
  })

  it('requires a fresh arming activation after the arm window expires', () => {
    vi.useFakeTimers()
    const commit = vi.fn()
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={commit}/>)

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    act(() => vi.advanceTimersByTime(5000))

    const restored = screen.getByRole('button', { name: 'Publish' })
    expect(restored).toHaveAttribute('data-guard-state', 'covered')
    fireEvent.click(restored)
    expect(commit).not.toHaveBeenCalled()
  })

  it.each([
    ['action', { action: '   ', classificationId: 'classification', label: 'Publish' }],
    ['classification', { action: 'publish', classificationId: '   ', label: 'Publish' }],
    ['label', { action: 'publish', classificationId: 'classification', label: '   ' }],
  ])('refuses a guard with a blank %s', (_field, props) => {
    const commit = vi.fn()
    render(<GuardedControl {...props} onCommit={commit}/>)

    const control = screen.getByRole('button')
    expect(control).toBeDisabled()
    fireEvent.click(control)
    expect(commit).not.toHaveBeenCalled()
  })

  it('publishes each state transition with its previous and next states', () => {
    const observe = vi.fn()
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} observe={observe}/>)

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))

    expect(observe).toHaveBeenCalledWith({ type: 'transition', previous: 'covered', next: 'armed' })
  })

  it('restores the cover after escape and requires another arming activation', () => {
    const commit = vi.fn()
    const observe = vi.fn()
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={commit} observe={observe}/>)

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    fireEvent.keyDown(document, { key: 'Escape' })
    const restored = screen.getByRole('button', { name: 'Publish' })
    expect(restored).toHaveAttribute('data-guard-state', 'covered')
    expect(observe).toHaveBeenCalledWith({ type: 'recovered', reason: 'escape' })
    fireEvent.click(restored)
    expect(commit).not.toHaveBeenCalled()
  })

  it('does not restore a guard after its commit has started', () => {
    const commit = vi.fn(() => new Promise<void>(() => undefined))
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={commit}/>)

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    const armed = screen.getByRole('button', { name: /Publish/ })
    act(() => {
      armed.click()
      document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    })

    expect(commit).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: /Publish/ })).toHaveAttribute('data-guard-state', 'committing')
  })

  it('reports why an armed guard is restored', () => {
    vi.useFakeTimers()
    const observe = vi.fn()
    const { rerender, unmount } = render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} observe={observe}/>)
    const arm = () => fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    const recoveredReasons = () => observe.mock.calls.filter(([event]) => event.type === 'recovered').map(([event]) => event.reason)

    arm()
    rerender(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} disabled observe={observe}/>)
    expect(recoveredReasons()).toContain('became-disabled')
    rerender(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} observe={observe}/>)

    arm()
    act(() => vi.advanceTimersByTime(5000))
    expect(recoveredReasons()).toContain('timeout')

    arm()
    fireEvent.popState(window)
    expect(recoveredReasons()).toContain('navigation')

    arm()
    fireEvent.blur(window)
    act(() => vi.advanceTimersByTime(800))
    expect(recoveredReasons()).toContain('window-blur')

    unmount()
  })

  it('reports document-hidden when visibility changes while armed', () => {
    const visibility = Object.getOwnPropertyDescriptor(document, 'visibilityState')
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    const observe = vi.fn()
    try {
      render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} observe={observe}/>)
      fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
      fireEvent(document, new Event('visibilitychange'))
      expect(observe).toHaveBeenCalledWith({ type: 'recovered', reason: 'document-hidden' })
    } finally {
      if (visibility) Object.defineProperty(document, 'visibilityState', visibility)
    }
  })

  it('announces completed and rejected commits to observers', async () => {
    vi.useFakeTimers()
    const completed = vi.fn()
    const { unmount } = render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => undefined} observe={completed}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    fireEvent.click(screen.getByRole('button', { name: /Publish/ }))
    await act(async () => undefined)
    expect(completed).toHaveBeenCalledWith({ type: 'commit-settled', outcome: 'completed' })
    unmount()

    const rejected = vi.fn()
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => Promise.reject(new Error('declined'))} observe={rejected}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    fireEvent.click(screen.getByRole('button', { name: /Publish/ }))
    await act(async () => undefined)
    expect(rejected).toHaveBeenCalledWith({ type: 'commit-settled', outcome: 'rejected' })
  })

  it('reports a synchronous commit failure as rejected before rethrowing it', () => {
    vi.useFakeTimers()
    const observe = vi.fn()
    render(<GuardedControl action="publish" classificationId="classification" label="Publish" onCommit={() => { throw new Error('declined') }} observe={observe}/>)

    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    fireEvent.click(screen.getByRole('button', { name: /Publish/ }))

    expect(observe).toHaveBeenCalledWith({ type: 'commit-settled', outcome: 'rejected' })
    expect(() => vi.advanceTimersByTime(0)).toThrow('declined')
  })
})
