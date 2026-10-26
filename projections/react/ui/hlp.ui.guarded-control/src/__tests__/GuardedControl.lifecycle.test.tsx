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
})
