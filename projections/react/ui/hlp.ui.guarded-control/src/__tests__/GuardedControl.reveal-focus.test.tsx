import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl, type GuardedControlEvent } from '../GuardedControl'

// The reveal-focus policy. Focus moves only while the control is connected: a focus request that is
// pending when an <Activity> boundary hides the control is dropped, a settlement or a timeout applied
// on reveal moves no focus, and a reveal never moves focus into the control. A document that is hidden,
// or a window that is blurred, defers restoration until the window is focused and visible again.
const action = () => screen.getByRole('button')
const elsewhere = () => screen.getByRole('textbox', { name: 'Elsewhere' })
const moveFocusElsewhere = () => act(() => { elsewhere().focus() })
const visibility = (value: DocumentVisibilityState) => Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => value })

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
  visibility('visible')
})

function activityTree(mode: 'visible' | 'hidden', onCommit: () => void | Promise<void>, events: GuardedControlEvent[] = []) {
  return (
    <>
      <input aria-label="Elsewhere"/>
      <React.Activity mode={mode}>
        <GuardedControl action="a" classificationId="c" label="Publish" onCommit={onCommit} observe={event => { events.push(event) }}/>
      </React.Activity>
    </>
  )
}

describe('GuardedControl reveal-focus policy', () => {
  it('does not move focus to the armed action when an armed control is revealed', () => {
    vi.useFakeTimers()
    const view = render(activityTree('visible', () => undefined))
    fireEvent.click(action())
    expect(action()).toHaveFocus()
    view.rerender(activityTree('hidden', () => undefined))
    act(() => vi.advanceTimersByTime(1000))
    moveFocusElsewhere()
    view.rerender(activityTree('visible', () => undefined))
    expect(action()).toHaveAttribute('data-guard-state', 'armed')
    expect(elsewhere()).toHaveFocus()
  })

  it('drops a cover-focus request that is pending when the control hides', async () => {
    let finish!: () => void
    const onCommit = () => new Promise<void>(resolve => { finish = resolve })
    const view = render(activityTree('visible', onCommit))
    fireEvent.click(action())
    fireEvent.click(action())
    // The commit settles and the host hides the boundary in the same burst, before React renders.
    await act(async () => {
      finish()
      await Promise.resolve()
      view.rerender(activityTree('hidden', onCommit))
    })
    moveFocusElsewhere()
    view.rerender(activityTree('visible', onCommit))
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(elsewhere()).toHaveFocus()
  })

  it('applies a settlement on reveal without moving focus', async () => {
    let finish!: () => void
    const onCommit = () => new Promise<void>(resolve => { finish = resolve })
    const view = render(activityTree('visible', onCommit))
    fireEvent.click(action())
    fireEvent.click(action())
    view.rerender(activityTree('hidden', onCommit))
    await act(async () => finish())
    moveFocusElsewhere()
    view.rerender(activityTree('visible', onCommit))
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(elsewhere()).toHaveFocus()
  })

  it('recovers on a reveal past the window without moving focus', () => {
    vi.useFakeTimers()
    const view = render(activityTree('visible', () => undefined))
    fireEvent.click(action())
    view.rerender(activityTree('hidden', () => undefined))
    moveFocusElsewhere()
    act(() => vi.advanceTimersByTime(10_000))
    view.rerender(activityTree('visible', () => undefined))
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(elsewhere()).toHaveFocus()
  })
})

describe('GuardedControl deferred focus restoration', () => {
  async function commitWhileUnsafe(makeUnsafe: () => void) {
    let finish!: () => void
    render(<><input aria-label="Elsewhere"/><GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => new Promise<void>(resolve => { finish = resolve })}/></>)
    fireEvent.click(action())
    fireEvent.click(action())
    moveFocusElsewhere()
    makeUnsafe()
    await act(async () => finish())
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(elsewhere()).toHaveFocus()
  }

  it('restores focus to the covered action when the blurred window regains focus', async () => {
    const hasFocus = vi.spyOn(document, 'hasFocus').mockReturnValue(false)
    await commitWhileUnsafe(() => undefined)
    hasFocus.mockReturnValue(true)
    act(() => { window.dispatchEvent(new Event('focus')) })
    expect(action()).toHaveFocus()
  })

  it('restores focus to the covered action when the hidden document becomes visible', async () => {
    await commitWhileUnsafe(() => visibility('hidden'))
    visibility('visible')
    act(() => { document.dispatchEvent(new Event('visibilitychange')) })
    expect(action()).toHaveFocus()
  })

  it('drops the deferred restoration once the control is armed again', async () => {
    const hasFocus = vi.spyOn(document, 'hasFocus').mockReturnValue(false)
    await commitWhileUnsafe(() => undefined)
    hasFocus.mockReturnValue(true)
    fireEvent.click(action())
    fireEvent.keyDown(document, { key: 'Escape' })
    await act(async () => undefined)
    moveFocusElsewhere()
    act(() => { window.dispatchEvent(new Event('focus')) })
    expect(elsewhere()).toHaveFocus()
  })
})
