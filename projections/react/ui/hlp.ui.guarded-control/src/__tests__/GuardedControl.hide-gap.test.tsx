import * as React from 'react'
import { createRoot } from 'react-dom/client'
import { flushSync } from 'react-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GuardedControl, type GuardedControlEvent } from '../GuardedControl'

// A transition-priority hide runs the layout-effect cleanup during its commit and the passive cleanup
// later. A commit that settles in that gap (here, a sibling's layout effect resolves the caller's
// promise) must not queue focus: the reveal-focus policy says a reveal never moves focus. Runs outside
// act so React keeps the two cleanups apart, as in a browser.
const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))

function Settler({ run }: { run?: () => void }) {
  React.useLayoutEffect(() => { run?.() })
  return null
}

afterEach(() => {
  vi.restoreAllMocks()
  ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true
})

describe('GuardedControl settlement in the hide gap', () => {
  it.each(['focused', 'blurred'] as const)('does not move focus on reveal for a commit that settles between the hide cleanups (window %s)', async window => {
    ;(globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = false
    const events: GuardedControlEvent[] = []
    let finish!: () => void
    const onCommit = () => new Promise<void>(resolve => { finish = resolve })
    const host = document.createElement('div')
    document.body.append(host)
    const root = createRoot(host)
    const App = ({ mode, run }: { mode: 'visible' | 'hidden'; run?: () => void }) => (
      <>
        <input aria-label="Elsewhere" id="hide-gap-elsewhere"/>
        <React.Activity mode={mode}>
          <GuardedControl action="a" classificationId="c" label="Publish" onCommit={onCommit} observe={event => { events.push(event) }}/>
        </React.Activity>
        <Settler run={run}/>
      </>
    )
    const action = () => host.querySelector('button') as HTMLButtonElement
    const elsewhere = () => document.getElementById('hide-gap-elsewhere') as HTMLInputElement
    try {
      flushSync(() => root.render(<App mode="visible"/>))
      action().click()
      await wait(10)
      action().click()
      await wait(10)
      expect(action().dataset.guardState).toBe('committing')
      const hasFocus = vi.spyOn(document, 'hasFocus').mockReturnValue(window === 'focused')
      React.startTransition(() => root.render(<App mode="hidden" run={() => finish()}/>))
      await wait(50)
      hasFocus.mockReturnValue(true)
      elsewhere().focus()
      React.startTransition(() => root.render(<App mode="visible"/>))
      await wait(50)
      document.defaultView?.dispatchEvent(new Event('focus'))
      await wait(10)
      expect(action().dataset.guardState).toBe('covered')
      expect(events.filter(event => event.type === 'commit-settled')).toHaveLength(1)
      expect(document.activeElement).toBe(elsewhere())
    } finally {
      root.unmount()
      host.remove()
    }
  })
})
