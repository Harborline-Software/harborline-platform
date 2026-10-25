import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
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

  it('localizes the armed instruction and its duration through the catalog and the locale', () => {
    render(
      <HarborlineLocaleProvider locale="fr-FR" catalog={{ 'chrome.guard.armed.whole': 'Armé pour {action}. Activez de nouveau dans un délai de {duration} pour valider.' }}>
        <GuardedControl action="a" classificationId="c" label="Publier" onCommit={() => undefined}/>
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Publier' }))
    // The locale's unit pattern joins number and unit with a no-break space in French.
    expect(screen.getByRole('status').textContent).toBe('Armé pour \u2068Publier\u2069. Activez de nouveau dans un délai de 5\u00a0secondes pour valider.')
  })
})
