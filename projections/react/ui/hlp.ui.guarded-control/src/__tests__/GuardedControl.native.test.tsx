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

  // The duration is the locale's CLDR unit pattern (ECMA-402), as the relative time was before, with
  // its number written by the host number formatter, as the Blazor lane writes it. The formatter is
  // asked for the number alone, with no options, so a formatter that writes only the number cannot
  // drop the unit word.
  it('writes the duration number with the host number formatter and keeps the unit', () => {
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
    expect(screen.getByRole('status').textContent).toBe('Armed for \u2068Publish\u2069. Activate again within en:5 seconds to commit.')
    expect(screen.getByRole('timer').textContent).toBe('en:5 seconds')
    expect(screen.getByRole('timer')).toHaveAccessibleName('en:5 seconds')
    expect(requests.length).toBeGreaterThan(0)
    expect(requests.filter(request => request.options !== undefined)).toEqual([])
  })

  // A host formatter that writes Latin digits in Arabic: the unit word and its plural form stay the
  // locale's, the digits are the host's. Arabic writes 2 seconds as one word with no number in it,
  // so the host is not asked for digits there.
  it('takes the digits from the host and the unit word from the locale', () => {
    vi.useFakeTimers()
    const latinDigits: HarborlineNumberFormatter = (value, { locale, options }) =>
      new Intl.NumberFormat(locale, { ...options, numberingSystem: 'latn' }).format(value)
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{
        'chrome.guard.armed.whole': '\u062a\u0645 \u062a\u062c\u0647\u064a\u0632 {action}. \u0641\u0639\u0651\u0644 \u0645\u0631\u0629 \u0623\u062e\u0631\u0649 \u062e\u0644\u0627\u0644 {duration} \u0644\u0644\u062a\u0623\u0643\u064a\u062f.',
      }} numberFormatter={latinDigits}>
        <GuardedControl action="a" classificationId="c" label={'\u062a\u0635\u062f\u064a\u0631'} onCommit={() => undefined}/>
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: '\u062a\u0635\u062f\u064a\u0631' }))
    expect(screen.getByRole('status').textContent).toBe('\u062a\u0645 \u062a\u062c\u0647\u064a\u0632 \u2068\u062a\u0635\u062f\u064a\u0631\u2069. \u0641\u0639\u0651\u0644 \u0645\u0631\u0629 \u0623\u062e\u0631\u0649 \u062e\u0644\u0627\u0644 5 \u062b\u0648\u0627\u0646\u064d \u0644\u0644\u062a\u0623\u0643\u064a\u062f.')
    expect(screen.getByRole('timer').textContent).toBe('5 \u062b\u0648\u0627\u0646\u064d')
    act(() => vi.advanceTimersByTime(3000))
    expect(screen.getByRole('timer').textContent).toBe('\u062b\u0627\u0646\u064a\u062a\u0627\u0646')
  })

  // Known limitation, recorded rather than silent (T-1105). With no host catalog in a non-English
  // locale, React places the locale's unit pattern inside the English fallback sentence, so the
  // armed instruction mixes languages and differs from the Blazor lane, which announces "within 5
  // seconds" (GuardedControlAnnouncementTests.NoCatalogFallbackIsEnglishInANonEnglishLocale). This
  // test pins today's output so any change to it is deliberate. Closing the gap is T-1105's deferred
  // duration-parity work: it depends on round-2 batch b18 (CLDR plural rules in the Blazor locale
  // provider) reaching main and on an owner decision on the lanes' shared duration source.
  it.each([
    ['fr-FR', 'Armed for \u2068Publish\u2069. Activate again within 5\u00a0secondes to commit.', '5\u00a0secondes'],
    ['ar-SA', 'Armed for \u2068Publish\u2069. Activate again within \u0665 \u062b\u0648\u0627\u0646\u064d to commit.', '\u0665 \u062b\u0648\u0627\u0646\u064d'],
  ])('known limitation: with no host catalog in %s the armed instruction mixes English and the locale unit', (locale, status, timer) => {
    render(
      <HarborlineLocaleProvider locale={locale} catalog={{}}>
        <GuardedControl action="a" classificationId="c" label="Publish" onCommit={() => undefined}/>
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(screen.getByRole('status').textContent).toBe(status)
    expect(screen.getByRole('timer').textContent).toBe(timer)
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
