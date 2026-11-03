import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl } from '../GuardedControl'

// Focus returns to the covered action after every recovery and commit settlement (interface
// invariant), whatever delivered the transition: a key event, the arm-window timer, or the caller's
// commit promise. Each test first moves focus to another field, because the covered and armed actions
// can share one DOM node.
const action = () => screen.getByRole('button')
const moveFocusElsewhere = () => act(() => { screen.getByRole('textbox', { name: 'Elsewhere' }).focus() })

afterEach(() => vi.useRealTimers())

describe('GuardedControl focus restoration', () => {
  it.each([
    ['completed', () => Promise.resolve()],
    ['rejected', () => Promise.reject(new Error('caller failure'))],
  ] as const)('returns focus to the covered action once a %s commit settles', async (_outcome, onCommit) => {
    vi.useFakeTimers()
    render(<><input aria-label="Elsewhere"/><GuardedControl action="a" classificationId="c" label="Publish" onCommit={onCommit}/></>)
    fireEvent.click(action())
    fireEvent.click(action())
    moveFocusElsewhere()
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveFocus()
    // The rejected commit's failure is rethrown on a timer; drop it unrun.
    vi.clearAllTimers()
  })

  it('returns focus to the covered action when the arm window runs out', () => {
    vi.useFakeTimers()
    render(<><input aria-label="Elsewhere"/><GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/></>)
    fireEvent.click(action())
    moveFocusElsewhere()
    act(() => vi.advanceTimersByTime(5000))
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveFocus()
  })

  it('returns focus to the covered action after Escape', async () => {
    render(<><input aria-label="Elsewhere"/><GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/></>)
    fireEvent.click(action())
    moveFocusElsewhere()
    fireEvent.keyDown(document, { key: 'Escape' })
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveFocus()
  })
})
