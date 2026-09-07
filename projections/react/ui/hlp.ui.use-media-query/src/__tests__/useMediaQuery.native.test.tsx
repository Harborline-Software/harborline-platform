import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { useMediaQuery } from '../index'
import { fixture, qualityCases } from './fixtures'
import { MatchMediaHost } from './matchMediaHost'

describe('useMediaQuery React native behavior', () => {
  it('re-reads the host snapshot before subscribing to close the render/effect race', () => {
    const host = new MatchMediaHost()
    const mediaQuery = host.seed('(race)', false)
    let calls = 0
    host.matchMedia.mockImplementation(() => {
      calls += 1
      if (calls === 2) mediaQuery.matches = true
      return mediaQuery
    })
    host.install()

    const { result } = renderHook(() => useMediaQuery('(race)'))

    expect(result.current).toBe(true)
    expect(host.matchMedia).toHaveBeenCalledTimes(2)
    expect(mediaQuery.listeners.size).toBe(1)
  })

  it('treats reduced-motion queries as opaque and preserves initial and changed values', () => {
    const expected = fixture(qualityCases, 'media-query.quality.reduced-motion').expected as {
      queryOpaque: boolean
      initialAndChangesPreserved: boolean
    }
    const query = '(prefers-reduced-motion: reduce)'
    const host = new MatchMediaHost()
    const mediaQuery = host.seed(query, false)
    host.install()
    const { result } = renderHook(() => useMediaQuery(query))
    act(() => mediaQuery.publish(true))

    expect(host.matchMedia).toHaveBeenCalledWith(query)
    expect(result.current).toBe(expected.initialAndChangesPreserved)
    expect(expected.queryOpaque).toBe(true)
  })

  it('passes compound pointer and hover capability queries through byte-identically', () => {
    const expected = fixture(qualityCases, 'media-query.quality.capability-query').expected as {
      pointerAndHoverQueriesPreserved: boolean
      policyInvented: boolean
    }
    const query = ' (pointer: coarse) and (hover: none) '
    const host = new MatchMediaHost()
    host.seed(query, true)
    host.install()

    const { result } = renderHook(() => useMediaQuery(query))

    expect(result.current).toBe(expected.pointerAndHoverQueriesPreserved)
    expect(host.matchMedia).toHaveBeenCalledWith(query)
    expect(expected.policyInvented).toBe(false)
  })

  it('reports the frozen error when an interactive host has no matchMedia capability', () => {
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: undefined })
    expect(() => renderHook(() => useMediaQuery('(min-width: 1px)'))).toThrow('media-query-unavailable')
  })
})
