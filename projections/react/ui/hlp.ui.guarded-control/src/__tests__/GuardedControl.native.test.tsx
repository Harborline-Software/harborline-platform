import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider, type HarborlineNumberFormatRequest, type HarborlineNumberFormatter } from '@harborline-platform/hlp.ui.locale-provider'
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

  // T-1105 (2): the recovered whole message is announced with the transition back to covered.
  it('announces the recovered whole message once the arm window runs out', () => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    act(() => vi.advanceTimersByTime(5000))
    expect(screen.getByRole('button', { name: 'Publish' })).toHaveAttribute('data-guard-state', 'covered')
    expect(screen.getByRole('status').textContent).toBe('Guard restored for \u2068Publish\u2069.')
  })

  // T-1105 (3): the armed instruction is one grammatical whole message; the duration is an amount
  // of time ("5 seconds"), not a relative time ("in 5 seconds") placed after "within".
  it('arms with one grammatical whole instruction and a plain duration', () => {
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe('Armed for \u2068Publish\u2069. Activate again within 5 seconds to commit.')
    expect(screen.getByRole('timer')).toHaveAccessibleName('5 seconds')
    expect(screen.getByRole('timer')).toHaveTextContent('5 seconds')
  })

  it('counts down to a singular "1 second"', () => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    act(() => vi.advanceTimersByTime(4000))
    expect(screen.getByRole('timer').textContent).toBe('1 second')
    expect(screen.getByRole('timer')).toHaveAccessibleName('1 second')
  })

  // Every arm announces the full five-second window, never the countdown a previous attempt left.
  it.each([
    ['after the arm window ran out', () => act(() => vi.advanceTimersByTime(5000))],
    ['after Escape', () => {
      act(() => vi.advanceTimersByTime(2000))
      fireEvent.keyDown(document, { key: 'Escape' })
    }],
  ])('re-arms with the full window %s', (_when, recover) => {
    vi.useFakeTimers()
    render(<GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    recover()
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe('Armed for \u2068Publish\u2069. Activate again within 5 seconds to commit.')
    expect(screen.getByRole('timer').textContent).toBe('5 seconds')
  })

  // The duration is the locale's CLDR unit pattern (ECMA-402), as the relative time was before. No
  // unit options reach the host number formatter, whose request carries none in the Blazor lane, so
  // a formatter that writes only the number cannot drop the unit.
  it('keeps the unit whatever the host number formatter writes', () => {
    const requests: HarborlineNumberFormatRequest[] = []
    const numberFormatter: HarborlineNumberFormatter = (value, request) => {
      requests.push(request)
      return `${request.locale}:${value}`
    }
    render(
      <HarborlineLocaleProvider locale="en" catalog={{}} numberFormatter={numberFormatter}>
        <GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe('Armed for \u2068Publish\u2069. Activate again within 5 seconds to commit.')
    expect(screen.getByRole('timer').textContent).toBe('5 seconds')
    expect(requests.filter(request => request.options?.style === 'unit')).toEqual([])
  })

  // The catalog localizes the armed whole message, as before; the duration inside it is the locale's
  // unit pattern. The chrome.guard.duration forms belong to the Blazor lane's catalog lookup and React
  // does not read them: one duration source for both lanes is T-1105's deferred duration-parity work.
  it('localizes the armed instruction through the catalog and its duration through the locale', () => {
    render(
      <HarborlineLocaleProvider locale="fr-FR" catalog={{
        'chrome.guard.armed.whole': 'Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.',
        'chrome.guard.duration.other': '{count} s',
      }}>
        <GuardedControl action="a" classificationId="c" label="Publier" onCommit={() => undefined}/>
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Publier' }))
    // The French unit pattern joins number and unit with a no-break space.
    expect(screen.getByRole('status').textContent).toBe('Armé pour \u2068Publier\u2069. Activez de nouveau dans un délai de 5\u00a0secondes pour valider.')
    expect(screen.getByRole('timer').textContent).toBe('5\u00a0secondes')
  })
})
