import { renderHook } from '@testing-library/react'
import { renderToString } from 'react-dom/server'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { BP_DOCK, BP_PHONE, useCanShowRail, useIsMobile } from '../index'
import { fixture, qualityCases, sharedCases } from './fixtures'

const BP_RAIL = '(min-width: 768px) and (min-height: 600px)'

afterEach(() => vi.unstubAllGlobals())

function installMatchMedia(values: Readonly<Record<string, boolean>>): void {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn((query: string): MediaQueryList => ({
      matches: values[query] ?? false,
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    })),
  })
}

describe('breakpoint revision-1 shared fixtures', () => {
  it('consumes the complete frozen surface', () => {
    expect(fixture(sharedCases, 'breakpoint.complete-surface').expected).toEqual({
      exports: ['BP_DOCK', 'BP_PHONE', 'useCanShowRail', 'useIsMobile'],
      count: 4,
    })
    expect(typeof BP_DOCK).toBe('string')
    expect(typeof BP_PHONE).toBe('string')
    expect(typeof useCanShowRail).toBe('function')
    expect(typeof useIsMobile).toBe('function')
  })

  it('retains the exact phone query and observes its match', () => {
    expect(BP_PHONE).toBe((fixture(sharedCases, 'breakpoint.phone-query').expected as { query: string }).query)
    installMatchMedia({ [BP_PHONE]: true })
    const { result } = renderHook(() => useIsMobile())
    expect(result.current).toBe((fixture(sharedCases, 'breakpoint.mobile-match').expected as { mobile: boolean }).mobile)
  })

  it('requires both rail width and rail height', () => {
    expect(BP_RAIL).toBe((fixture(sharedCases, 'breakpoint.rail-query').expected as { query: string }).query)
    installMatchMedia({ [BP_RAIL]: false, '(min-width: 768px)': true })
    const { result } = renderHook(() => useCanShowRail())
    expect(result.current).toBe(
      (fixture(sharedCases, 'breakpoint.rail-short-landscape').expected as { canShowRail: boolean }).canShowRail,
    )
  })

  it('retains the held width-only dock compatibility exception', () => {
    const expected = fixture(sharedCases, 'breakpoint.dock-held-exception').expected as {
      query: string
      aspectAware: boolean
    }
    expect(BP_DOCK).toBe(expected.query)
    expect(expected.aspectAware).toBe(false)
  })

  it('fails closed for both hooks during SSR', () => {
    const browser = globalThis.window
    vi.stubGlobal('window', undefined)
    let observed: unknown

    function Probe() {
      observed = { mobile: useIsMobile(), canShowRail: useCanShowRail() }
      return null
    }

    renderToString(<Probe />)
    expect(observed).toEqual(fixture(sharedCases, 'breakpoint.ssr-fail-closed').expected)
    vi.stubGlobal('window', browser)
  })

  it('uses byte-identical query text for projection equivalence', () => {
    expect([BP_PHONE, BP_RAIL, BP_DOCK]).toEqual([
      '(max-width: 767px)',
      '(min-width: 768px) and (min-height: 600px)',
      '(min-width: 1280px)',
    ])
    expect(fixture(sharedCases, 'breakpoint.projection-equivalence').expected).toEqual({
      typescriptEqualsDotnet: true,
    })
  })
})

describe('breakpoint revision-1 quality fixtures', () => {
  it('suppresses rail in short landscape and fails closed during SSR', () => {
    expect(fixture(qualityCases, 'breakpoint.quality.short-landscape').expected).toEqual({
      railSuppressed: true,
    })
    expect(fixture(qualityCases, 'breakpoint.quality.ssr').expected).toEqual({ failClosed: true })
  })
})
