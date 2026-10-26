import * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl, type GuardedControlEvent } from '../GuardedControl'

// React runs effect cleanup and then setup again under StrictMode in development, and when an
// <Activity> boundary is hidden and revealed (routers that keep hidden routes alive). The control must
// come back fully live: a commit settles to covered, reports commit-settled once, and focus returns to
// the covered action. The covered and armed actions can share one DOM node, so each test first moves
// focus to another field (a browser also takes focus off an action that becomes disabled) and then
// checks that the control brings it back.
const action = () => screen.getByRole('button')
const elsewhere = <input aria-label="Elsewhere"/>
const dropFocus = () => act(() => { screen.getByRole('textbox', { name: 'Elsewhere' }).focus() })

afterEach(() => vi.useRealTimers())

async function armCommitAndFlush() {
  fireEvent.click(action())
  expect(action()).toHaveAttribute('data-guard-state', 'armed')
  fireEvent.click(action())
  dropFocus()
  expect(action()).not.toHaveFocus()
  await act(async () => undefined)
}

describe('GuardedControl after an effect cleanup and re-setup', () => {
  it('settles a commit under StrictMode, reports it once and restores focus', async () => {
    const events: GuardedControlEvent[] = []
    render(<React.StrictMode>{elsewhere}<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined} observe={event => { events.push(event) }}/></React.StrictMode>)
    await armCommitAndFlush()
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(events.filter(event => event.type === 'commit-settled')).toEqual([{ type: 'commit-settled', outcome: 'completed' }])
    expect(screen.getByRole('status').textContent).toBe('Guard restored for ⁨Publish⁩.')
    expect(action()).toHaveFocus()
  })

  it('settles a commit after an Activity hidden and visible cycle, reports it once and restores focus', async () => {
    const events: GuardedControlEvent[] = []
    const tree = (mode: 'visible' | 'hidden') => (
      <React.Activity mode={mode}>
        {elsewhere}
        <GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined} observe={event => { events.push(event) }}/>
      </React.Activity>
    )
    const view = render(tree('visible'))
    view.rerender(tree('hidden'))
    await act(async () => undefined)
    view.rerender(tree('visible'))
    await act(async () => undefined)
    await armCommitAndFlush()
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(events.filter(event => event.type === 'commit-settled')).toEqual([{ type: 'commit-settled', outcome: 'completed' }])
    expect(screen.getByRole('status').textContent).toBe('Guard restored for ⁨Publish⁩.')
    expect(action()).toHaveFocus()
  })

  it('restores focus after a recovery under StrictMode', async () => {
    render(<React.StrictMode>{elsewhere}<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/></React.StrictMode>)
    fireEvent.click(action())
    dropFocus()
    fireEvent.keyDown(document, { key: 'Escape' })
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(action()).toHaveFocus()
  })

  // A commit that settles while an <Activity> boundary is hidden is applied when it is revealed, once.
  it.each([
    ['completed', 'Guard restored for \u2068Publish\u2069.'],
    ['rejected', 'Could not complete \u2068Publish\u2069. Guard restored.'],
  ] as const)('applies a commit that %s while hidden once the control is revealed, exactly once', async (outcome, status) => {
    vi.useFakeTimers()
    const events: GuardedControlEvent[] = []
    let finish!: () => void
    let fail!: (error: Error) => void
    const onCommit = () => new Promise<void>((resolve, reject) => { finish = resolve; fail = reject })
    const tree = (mode: 'visible' | 'hidden') => (
      <React.Activity mode={mode}>
        <GuardedControl action="a" classificationId="c" label="Publish" onCommit={onCommit} observe={event => { events.push(event) }}/>
      </React.Activity>
    )
    const view = render(tree('visible'))
    fireEvent.click(action())
    fireEvent.click(action())
    expect(action()).toHaveAttribute('data-guard-state', 'committing')
    view.rerender(tree('hidden'))
    await act(async () => { if (outcome === 'completed') finish(); else fail(new Error('caller failure')) })
    expect(events.filter(event => event.type === 'commit-settled')).toEqual([])
    view.rerender(tree('visible'))
    await act(async () => undefined)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(events.filter(event => event.type === 'commit-settled')).toEqual([{ type: 'commit-settled', outcome }])
    expect(screen.getByRole('status').textContent).toBe(status)
    view.rerender(tree('hidden'))
    view.rerender(tree('visible'))
    await act(async () => undefined)
    expect(events.filter(event => event.type === 'commit-settled')).toHaveLength(1)
    // The rejected commit's failure is rethrown on a timer when it settles; drop it unrun.
    vi.clearAllTimers()
  })

  it('emits nothing for a commit that settles after a real unmount', async () => {
    const events: GuardedControlEvent[] = []
    let finish!: () => void
    const view = render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => new Promise<void>(resolve => { finish = resolve })} observe={event => { events.push(event) }}/>)
    fireEvent.click(action())
    fireEvent.click(action())
    view.unmount()
    await act(async () => finish())
    expect(events.filter(event => event.type === 'commit-settled')).toEqual([])
  })
})
