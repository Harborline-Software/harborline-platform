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
})
