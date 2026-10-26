import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { GuardedControl, type GuardedControlEvent } from '../GuardedControl'

// The arm window is fixed at five seconds from the arming activation (interface invariant; the
// guard's safety property). A host that re-renders the locale provider while the control is armed
// (a clock, polling) passes new identities: an inline number formatter, a fresh catalog object, an
// inline observe callback, or no catalog prop at all (the provider's default is a new {} each render).
// None of them may move the deadline or drop the window-blur grace timer.
type Triggers = { formatter: boolean; catalog: 'stable' | 'fresh' | 'none'; observe: boolean }
const STABLE_CATALOG = { 'chrome.guard.recovered.whole': '{action} is protected again.' }
const action = () => screen.getByRole('button')

function Host({ tick, triggers, events }: { tick: number; triggers: Triggers; events: GuardedControlEvent[] }) {
  const catalog = triggers.catalog === 'fresh' ? { 'chrome.guard.recovered.whole': '{action} is protected again.' }
    : triggers.catalog === 'stable' ? STABLE_CATALOG : undefined
  return (
    <HarborlineLocaleProvider
      locale="en-US"
      {...(catalog ? { catalog } : {})}
      numberFormatter={triggers.formatter ? (value, request) => new Intl.NumberFormat(request.locale, request.options).format(value) : undefined}
    >
      <GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}
        observe={triggers.observe ? event => { events.push(event) } : undefined} data-host-tick={tick}/>
    </HarborlineLocaleProvider>
  )
}

afterEach(() => vi.useRealTimers())

describe('GuardedControl arm window under host re-renders', () => {
  it.each([
    ['a fresh number formatter', { formatter: true, catalog: 'stable', observe: false }],
    ['a fresh number formatter, a fresh catalog and an inline observe', { formatter: true, catalog: 'fresh', observe: true }],
    ['no catalog prop and an inline observe', { formatter: false, catalog: 'none', observe: true }],
  ] as Array<[string, Triggers]>)('re-covers at five seconds while the host re-renders every second with %s', (_triggers, triggers) => {
    vi.useFakeTimers()
    const events: GuardedControlEvent[] = []
    const view = render(<Host tick={0} triggers={triggers} events={events}/>)
    fireEvent.click(action())
    for (let second = 1; second <= 4; second++) {
      act(() => vi.advanceTimersByTime(1000))
      view.rerender(<Host tick={second} triggers={triggers} events={events}/>)
    }
    expect(action()).toHaveAttribute('data-guard-state', 'armed')
    expect(screen.getByRole('timer')).toHaveTextContent('1 second')
    act(() => vi.advanceTimersByTime(1000))
    view.rerender(<Host tick={5} triggers={triggers} events={events}/>)
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(screen.getByRole('status').textContent).toBe(triggers.catalog === 'none'
      ? 'Guard restored for ⁨Publish⁩.'
      : '⁨Publish⁩ is protected again.')
    if (triggers.observe) expect(events.filter(event => event.type === 'recovered')).toEqual([{ type: 'recovered', reason: 'timeout' }])
  })

  it('keeps the window-blur grace timer across a host re-render', () => {
    vi.useFakeTimers()
    const events: GuardedControlEvent[] = []
    const triggers: Triggers = { formatter: true, catalog: 'fresh', observe: true }
    const view = render(<Host tick={0} triggers={triggers} events={events}/>)
    fireEvent.click(action())
    act(() => { window.dispatchEvent(new Event('blur')) })
    act(() => vi.advanceTimersByTime(400))
    view.rerender(<Host tick={1} triggers={triggers} events={events}/>)
    act(() => vi.advanceTimersByTime(400))
    expect(action()).toHaveAttribute('data-guard-state', 'covered')
    expect(events.filter(event => event.type === 'recovered')).toEqual([{ type: 'recovered', reason: 'window-blur' }])
  })
})
