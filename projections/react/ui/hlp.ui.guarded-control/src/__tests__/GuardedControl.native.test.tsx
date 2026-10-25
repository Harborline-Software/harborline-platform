import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider, type HarborlineNumberFormatter } from '@harborline-platform/hlp.ui.locale-provider'
import { GuardedControl } from '../GuardedControl'
import { durationCases } from './fixtures'

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

  // T-1105 review: both lanes replay conformance/hlp.ui.guarded-control/duration-v1.json, so one
  // catalog, locale and host formatter give one armed instruction and one countdown in both.
  describe('duration (conformance/hlp.ui.guarded-control/duration-v1.json)', () => {
    it.each(durationCases.map(fixture => [fixture.id, fixture] as const))('%s', (_id, fixture) => {
      vi.useFakeTimers()
      const numberingSystem = fixture.hostNumberingSystem
      const numberFormatter: HarborlineNumberFormatter | undefined = numberingSystem === undefined
        ? undefined
        : (value, { locale, options }) => new Intl.NumberFormat(locale, { ...options, numberingSystem }).format(value)
      render(
        <HarborlineLocaleProvider locale={fixture.locale} catalog={fixture.catalog} numberFormatter={numberFormatter}>
          <GuardedControl action="a" classificationId="c" label={fixture.label} onCommit={() => undefined}/>
        </HarborlineLocaleProvider>,
      )
      for (const step of fixture.steps) {
        if (step === 'arm') fireEvent.click(screen.getByRole('button'))
        else if (step === 'tick') act(() => vi.advanceTimersByTime(1000))
        else if (step === 'escape') fireEvent.keyDown(document, { key: 'Escape' })
        else throw new Error(`unknown-step: ${String(step)}`)
      }
      const expected = fixture.divergence?.lane === 'react' ? fixture.divergence.expected : fixture.expected
      expect(screen.getByRole('status').textContent).toBe(expected.status)
      expect(screen.getByRole('timer').textContent).toBe(expected.timer)
      expect(screen.getByRole('timer')).toHaveAccessibleName(expected.timer)
    })
  })
})
