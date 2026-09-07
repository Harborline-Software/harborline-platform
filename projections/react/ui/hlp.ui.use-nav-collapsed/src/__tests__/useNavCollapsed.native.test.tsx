import { act, renderHook } from '@testing-library/react'
import { renderToString } from 'react-dom/server'
import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'

import {
  useNavCollapsed,
  type UseNavCollapsedOptions,
  type UseNavCollapsedResult,
} from '../index'
import { fixture, qualityCases, sharedCases } from './fixtures'
import { MatchMediaHost } from './matchMediaHost'

afterEach(() => vi.unstubAllGlobals())

function setup(auto = false): MatchMediaHost {
  const host = new MatchMediaHost()
  host.seed('(max-width: 767px)', auto)
  host.install()
  return host
}

describe('useNavCollapsed revision-1 native shared-fixture coverage', () => {
  it('[nav-collapse.complete-surface] exports the frozen surface', () => {
    expect(fixture(sharedCases, 'nav-collapse.complete-surface').expected).toEqual({
      exports: ['UseNavCollapsedOptions', 'UseNavCollapsedResult', 'useNavCollapsed'],
      count: 3,
    })
    expect(typeof useNavCollapsed).toBe('function')
    expectTypeOf<UseNavCollapsedOptions>().toBeObject()
    expectTypeOf<UseNavCollapsedResult>().toBeObject()
  })

  it.each([
    ['nav-collapse.default-expanded', false],
    ['nav-collapse.default-collapsed', true],
  ] as const)('[%s] respects the uncontrolled default', (id, defaultCollapsed) => {
    setup(false)
    const { result } = renderHook(() => useNavCollapsed({ defaultCollapsed }))
    expect(result.current.collapsed).toBe(defaultCollapsed)
    expect((fixture(sharedCases, id).expected as { collapsed: boolean }).collapsed).toBe(defaultCollapsed)
  })

  it('[nav-collapse.narrow-auto] overlays responsive collapse without changing the base preference', () => {
    setup(true)
    const onChange = vi.fn()
    const { result } = renderHook(() => useNavCollapsed({ onCollapsedChange: onChange }))
    const expected = fixture(sharedCases, 'nav-collapse.narrow-auto').expected as { collapsed: boolean; requestCount: number }
    expect(result.current.collapsed).toBe(expected.collapsed)
    expect(onChange).toHaveBeenCalledTimes(expected.requestCount)
  })

  it('[nav-collapse.narrow-to-wide-restores-base] restores the uncontrolled preference', () => {
    const host = setup(false)
    const narrow = host.get('(max-width: 767px)')
    const { result } = renderHook(() => useNavCollapsed())
    act(() => narrow.publish(true))
    expect(result.current.collapsed).toBe(true)
    act(() => narrow.publish(false))
    expect(result.current.collapsed).toBe(
      (fixture(sharedCases, 'nav-collapse.narrow-to-wide-restores-base').expected as { collapsed: boolean }).collapsed,
    )
  })

  it('[nav-collapse.explicit-override] lets an explicit choice win over responsive state', () => {
    const host = setup(false)
    const { result } = renderHook(() => useNavCollapsed())
    act(() => result.current.setCollapsed(false))
    act(() => host.get('(max-width: 767px)').publish(true))
    expect(result.current.collapsed).toBe(
      (fixture(sharedCases, 'nav-collapse.explicit-override').expected as { collapsed: boolean }).collapsed,
    )
  })

  it('[nav-collapse.controlled-toggle] emits a request without mutating controlled state', () => {
    setup(false)
    const requests: boolean[] = []
    const { result } = renderHook(() => useNavCollapsed({ collapsed: false, onCollapsedChange: value => requests.push(value) }))
    act(() => result.current.toggle())
    const expected = fixture(sharedCases, 'nav-collapse.controlled-toggle').expected as { collapsed: boolean; requested: boolean[] }
    expect(result.current.collapsed).toBe(expected.collapsed)
    expect(requests).toEqual(expected.requested)
  })

  it('[nav-collapse.controlled-initial-narrow] does not emit on an initial narrow render', () => {
    setup(true)
    const onChange = vi.fn()
    const { result } = renderHook(() => useNavCollapsed({ collapsed: false, onCollapsedChange: onChange }))
    const expected = fixture(sharedCases, 'nav-collapse.controlled-initial-narrow').expected as { collapsed: boolean; requestCount: number }
    expect(result.current.collapsed).toBe(expected.collapsed)
    expect(onChange).toHaveBeenCalledTimes(expected.requestCount)
  })

  it('[nav-collapse.controlled-narrow-crossing] emits exactly one request per crossing', () => {
    const host = setup(false)
    const onChange = vi.fn()
    renderHook(() => useNavCollapsed({ collapsed: false, onCollapsedChange: onChange }))
    const narrow = host.get('(max-width: 767px)')
    act(() => narrow.publish(true))
    act(() => narrow.publish(true))
    expect(onChange.mock.calls.map(([value]) => value)).toEqual(
      (fixture(sharedCases, 'nav-collapse.controlled-narrow-crossing').expected as { requested: boolean[] }).requested,
    )
  })

  it('[nav-collapse.custom-thresholds] emits exact custom media queries', () => {
    const host = new MatchMediaHost()
    host.seed('(max-width: 899px)', true)
    host.seed('(max-width: 599px)', false)
    host.install()
    const { result } = renderHook(() => useNavCollapsed({ autoCollapseBelow: 900, overlayBelow: 600 }))
    const expected = fixture(sharedCases, 'nav-collapse.custom-thresholds').expected as {
      collapsed: boolean; isOverlay: boolean; autoQuery: string; overlayQuery: string
    }
    expect(result.current).toMatchObject({ collapsed: expected.collapsed, isOverlay: expected.isOverlay })
    expect(host.matchMedia).toHaveBeenCalledWith(expected.autoQuery)
    expect(host.matchMedia).toHaveBeenCalledWith(expected.overlayQuery)
  })

  it('[nav-collapse.disable-responsive] ignores host matches when thresholds are disabled', () => {
    const host = new MatchMediaHost()
    host.seed('(max-width: 0px)', true)
    host.install()
    const { result } = renderHook(() => useNavCollapsed({ autoCollapseBelow: 0, overlayBelow: 0 }))
    expect(result.current).toMatchObject(
      fixture(sharedCases, 'nav-collapse.disable-responsive').expected as { collapsed: boolean; isOverlay: boolean },
    )
  })

  it('[nav-collapse.overlay-independent] derives overlay independently of collapse', () => {
    const host = new MatchMediaHost()
    host.seed('(max-width: 999px)', false)
    host.seed('(max-width: 499px)', true)
    host.install()
    const { result } = renderHook(() => useNavCollapsed({ autoCollapseBelow: 1000, overlayBelow: 500 }))
    expect(result.current).toMatchObject(
      fixture(sharedCases, 'nav-collapse.overlay-independent').expected as { collapsed: boolean; isOverlay: boolean },
    )
  })

  it('[nav-collapse.ssr-fail-closed] renders expanded with no overlay during SSR', () => {
    const browser = globalThis.window
    vi.stubGlobal('window', undefined)
    let observed: Pick<UseNavCollapsedResult, 'collapsed' | 'isOverlay'> | undefined
    function Probe() {
      const { collapsed, isOverlay } = useNavCollapsed()
      observed = { collapsed, isOverlay }
      return null
    }
    renderToString(<Probe />)
    const expected = fixture(sharedCases, 'nav-collapse.ssr-fail-closed').expected as {
      collapsed: boolean
      isOverlay: boolean
    }
    expect(observed).toEqual({ collapsed: expected.collapsed, isOverlay: expected.isOverlay })
    vi.stubGlobal('window', browser)
  })

  it('[nav-collapse.projection-equivalence] follows the frozen transition policy', () => {
    setup(true)
    const { result } = renderHook(() => useNavCollapsed())
    expect(result.current.collapsed).toBe(true)
    act(() => result.current.setCollapsed(false))
    expect(result.current.collapsed).toBe(false)
    act(() => result.current.toggle())
    expect(result.current.collapsed).toBe(true)
    expect(fixture(sharedCases, 'nav-collapse.projection-equivalence').expected).toEqual({ typescriptEqualsDotnet: true })
  })
})

describe('useNavCollapsed revision-1 quality fixtures', () => {
  it('[nav-collapse.quality.preference] preserves explicit preference', () => {
    const expected = fixture(qualityCases, 'nav-collapse.quality.preference').expected
    expect(expected).toEqual({ explicitUserChoiceWins: true, responsiveStateDoesNotDestroyPreference: true })
  })

  it('[nav-collapse.quality.ssr] has no server-side effects', () => {
    expect(fixture(qualityCases, 'nav-collapse.quality.ssr').expected).toEqual({
      serverCollapsed: false,
      serverOverlay: false,
      sideEffects: 0,
    })
  })
})
