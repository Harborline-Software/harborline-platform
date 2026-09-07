import { renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { BP_DOCK, BP_PHONE, useCanShowRail, useIsMobile } from '../index'

describe('breakpoint React native behavior', () => {
  it('delegates mobile and rail identity to the dependency unchanged', () => {
    const queries: string[] = []
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn((query: string): MediaQueryList => {
        queries.push(query)
        return {
          matches: true,
          media: query,
          onchange: null,
          addListener: () => undefined,
          removeListener: () => undefined,
          addEventListener: () => undefined,
          removeEventListener: () => undefined,
          dispatchEvent: () => false,
        }
      }),
    })

    const { result } = renderHook(() => ({ mobile: useIsMobile(), rail: useCanShowRail() }))

    expect(result.current).toEqual({ mobile: true, rail: true })
    // The rail literal below is OWNED by MASTER_DETAIL_RAIL_QUERY in
    // hlp.ui.use-can-show-master-detail (ticket 154): BP_RAIL is an equality-pinned copy —
    // a cross-package import would add a dependency edge this leaf module does not carry.
    // Retune the rail there first; this pin then names the copy that must follow.
    expect(new Set(queries)).toEqual(new Set([
      '(max-width: 767px)',
      '(min-width: 768px) and (min-height: 600px)',
    ]))
    expect(BP_PHONE).toBe('(max-width: 767px)')
    expect(BP_DOCK).toBe('(min-width: 1280px)')
  })
})
