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

  // The armed instruction must state the fresh five-second window, not the value the previous
  // attempt's countdown ended on.
  it('announces a fresh five-second window when re-armed after a timeout', () => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    const armedMessage = 'Armed for \u2068Publish\u2069. Activate again within 5 seconds to commit.'
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe(armedMessage)
    act(() => vi.advanceTimersByTime(5000))
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe(armedMessage)
  })

  // Escape belongs to the focused widget: an armed guard recovers on its own Escape, but must not
  // swallow or react to Escape handled elsewhere on the page (the Blazor lane scopes it the same way).
  it('recovers on its own Escape but leaves Escape elsewhere on the page alone', () => {
    const observe = vi.fn()
    render(<><GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined} observe={observe}/><input aria-label="Other field"/></>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    const other = screen.getByRole('textbox', { name: 'Other field' })
    other.focus()
    const elsewhere = fireEvent.keyDown(other, { key: 'Escape' })
    expect(elsewhere).toBe(true)
    expect(observe).not.toHaveBeenCalledWith(expect.objectContaining({ type: 'recovered' }))
    expect(document.activeElement).toBe(other)
    const armed = screen.getByRole('button', { name: /Publish/ })
    expect(armed).toHaveAttribute('data-guard-state', 'armed')
    fireEvent.keyDown(armed, { key: 'Escape' })
    expect(observe).toHaveBeenCalledWith({ type: 'recovered', reason: 'escape' })
    expect(screen.getByRole('button', { name: 'Publish' })).toHaveAttribute('data-guard-state', 'covered')
  })
})
