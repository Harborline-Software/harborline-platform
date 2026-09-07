import { act, render, renderHook } from '@testing-library/react'
import { renderToString } from 'react-dom/server'
import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'

import { MediaQuery, type MediaQueryProps, useMediaQuery } from '../index'
import { fixture, sharedCases } from './fixtures'
import { MatchMediaHost } from './matchMediaHost'

afterEach(() => vi.unstubAllGlobals())

describe('useMediaQuery revision-1 shared fixtures', () => {
  it('consumes the complete frozen surface and case set', () => {
    const value = fixture(sharedCases, 'media-query.complete-surface').expected as {
      exports: string[]
      count: number
    }
    expect(value.exports).toEqual(['useMediaQuery', 'MediaQueryProps', 'MediaQuery'])
    expect(value.count).toBe(3)
    expect(typeof useMediaQuery).toBe('function')
    expect(typeof MediaQuery).toBe('function')
    expectTypeOf<MediaQueryProps>().toMatchTypeOf<{
      query: string
      children: (matches: boolean) => React.ReactNode
    }>()
  })

  it('returns false during SSR without browser access', () => {
    const value = fixture(sharedCases, 'media-query.ssr-false').input as { query: string }
    const browser = globalThis.window
    vi.stubGlobal('window', undefined)

    const html = renderToString(<MediaQuery query={value.query}>{matches => String(matches)}</MediaQuery>)

    expect(html).toBe('false')
    expect(globalThis.window).toBeUndefined()
    vi.stubGlobal('window', browser)
  })

  it.each([
    ['media-query.initial-match', true],
    ['media-query.initial-miss', false],
  ] as const)('reads the host snapshot and subscribes once: %s', (id, initial) => {
    const value = fixture(sharedCases, id).input as { query: string }
    const host = new MatchMediaHost()
    const mediaQuery = host.seed(value.query, initial)
    host.install()

    const { result, unmount } = renderHook(() => useMediaQuery(value.query))

    expect(result.current).toBe(initial)
    expect(mediaQuery.listeners.size).toBe(1)
    unmount()
  })

  it('publishes each host change while retaining one listener', () => {
    const value = fixture(sharedCases, 'media-query.change')
    const input = value.input as { initial: boolean; events: boolean[] }
    const expected = value.expected as { states: boolean[]; listeners: number }
    const query = '(min-width: 768px)'
    const host = new MatchMediaHost()
    const mediaQuery = host.seed(query, input.initial)
    host.install()
    const { result } = renderHook(() => useMediaQuery(query))
    const states = [result.current]

    for (const event of input.events) {
      act(() => mediaQuery.publish(event))
      states.push(result.current)
    }

    expect(states).toEqual(expected.states)
    expect(mediaQuery.listeners.size).toBe(expected.listeners)
  })

  it('removes the old query listener before observing its replacement', () => {
    const value = fixture(sharedCases, 'media-query.query-replacement')
    const input = value.input as { queries: [string, string] }
    const expected = value.expected as { oldRemovals: number; newListeners: number; activeListeners: number }
    const host = new MatchMediaHost()
    const oldQuery = host.seed(input.queries[0], false)
    const newQuery = host.seed(input.queries[1], true)
    host.install()
    const { result, rerender } = renderHook(({ query }) => useMediaQuery(query), {
      initialProps: { query: input.queries[0] },
    })

    rerender({ query: input.queries[1] })

    expect(result.current).toBe(true)
    expect(oldQuery.removals).toBe(expected.oldRemovals)
    expect(newQuery.additions).toBe(expected.newListeners)
    expect(oldQuery.listeners.size + newQuery.listeners.size).toBe(expected.activeListeners)
  })

  it('removes the active listener and ignores events after disposal', () => {
    const expected = fixture(sharedCases, 'media-query.dispose').expected as {
      removals: number
      updatesAfterDispose: number
    }
    const host = new MatchMediaHost()
    const mediaQuery = host.seed('(min-width: 768px)', false)
    host.install()
    let updates = 0
    const Probe = () => {
      useMediaQuery(mediaQuery.media)
      updates += 1
      return null
    }
    const { unmount } = render(<Probe />)
    unmount()
    const updatesAtDispose = updates

    act(() => mediaQuery.publish(true))

    expect(mediaQuery.removals).toBe(expected.removals)
    expect(updates - updatesAtDispose).toBe(expected.updatesAfterDispose)
  })

  it('delegates to a render prop without adding wrapper markup', () => {
    const expected = fixture(sharedCases, 'media-query.render-prop').expected as {
      childValue: boolean
      wrapperElements: number
    }
    const host = new MatchMediaHost()
    host.seed('(min-width: 768px)', expected.childValue)
    host.install()

    const { container } = render(
      <MediaQuery query="(min-width: 768px)">{matches => <span data-match={matches} />}</MediaQuery>,
    )

    expect(container.childElementCount - 1).toBe(expected.wrapperElements)
    expect(container.firstElementChild?.getAttribute('data-match')).toBe('true')
  })

  it('retains the same Boolean transition sequence as the neutral projection', () => {
    const value = fixture(sharedCases, 'media-query.projection-equivalence')
    const input = value.input as { sequence: boolean[] }
    const host = new MatchMediaHost()
    const mediaQuery = host.seed('(projection-equivalence)', input.sequence[0] ?? false)
    host.install()
    const { result } = renderHook(() => useMediaQuery(mediaQuery.media))
    const observed = [result.current]

    for (const next of input.sequence.slice(1)) {
      act(() => mediaQuery.publish(next))
      observed.push(result.current)
    }

    expect(observed).toEqual(input.sequence)
  })
})
