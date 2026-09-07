import { renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import {
  useCanShowMasterDetail,
  useCanSplitBuilderPanes,
  useFormFactor,
  useShowHoverAffordance,
  useTouchSizing,
} from '../index'
import { FORM_FACTOR_QUERIES, MASTER_DETAIL_RAIL_QUERY } from '../formFactorPolicy'
import { MatchMediaHost } from './matchMediaHost'

function bound(query: string, dimension: string): number {
  const match = new RegExp(`\\(${dimension}:\\s*(\\d+)px\\)`).exec(query)
  if (!match) throw new Error(`query '${query}' carries no ${dimension} bound`)
  return Number(match[1])
}

describe('form-factor React native behavior', () => {
  it('rail thresholds exceed the phone and short-height bands (the coincidence is an invariant)', () => {
    // Ticket 154 review: the panel-level gate (the rail query alone) equals the policy
    // (mode !== phone AND rail) only because the rail's floors clear the phone-width and
    // short-height ceilings. Pin that arithmetic so retuning phone/shortHeight/rail breaks
    // a NAMED test instead of silently splitting the constant-bound gates (React policy,
    // .NET FormFactorQueries) from the equality-pinned raw-query copies (Blazor, use-is-mobile,
    // app-layout).
    expect(bound(MASTER_DETAIL_RAIL_QUERY, 'min-width')).toBeGreaterThan(bound(FORM_FACTOR_QUERIES.phoneWidth, 'max-width'))
    expect(bound(MASTER_DETAIL_RAIL_QUERY, 'min-height')).toBeGreaterThan(bound(FORM_FACTOR_QUERIES.shortHeight, 'max-height'))
  })

  it('useCanShowMasterDetail subscribes only to the queries the decision reads', () => {
    // Item: reduced subscription cost (useCanSplitBuilderPanes precedent). The decision reads
    // phone-mode inputs + the rail; pointer/hover/desktop-width flips must not re-render consumers.
    const host = new MatchMediaHost()
    host.install()

    renderHook(() => useCanShowMasterDetail())

    expect(new Set(host.queries)).toEqual(new Set([
      FORM_FACTOR_QUERIES.phoneWidth,
      FORM_FACTOR_QUERIES.landscape,
      FORM_FACTOR_QUERIES.shortHeight,
      FORM_FACTOR_QUERIES.masterDetailRail,
    ]))
  })

  it('delegates the exact authored signal queries to useMediaQuery', () => {
    const host = new MatchMediaHost()
    host.install()

    renderHook(() => useFormFactor())

    expect(new Set(host.queries)).toEqual(new Set([
      FORM_FACTOR_QUERIES.phoneWidth,
      FORM_FACTOR_QUERIES.desktopWidth,
      FORM_FACTOR_QUERIES.landscape,
      FORM_FACTOR_QUERIES.shortHeight,
      FORM_FACTOR_QUERIES.anyCoarse,
      FORM_FACTOR_QUERIES.anyFine,
      FORM_FACTOR_QUERIES.hover,
      FORM_FACTOR_QUERIES.masterDetailRail,
    ]))
  })

  it('keeps capability affordances independent of desktop width', () => {
    const host = new MatchMediaHost()
    host.install({
      [FORM_FACTOR_QUERIES.desktopWidth]: true,
      [FORM_FACTOR_QUERIES.anyCoarse]: true,
      [FORM_FACTOR_QUERIES.anyFine]: true,
      [FORM_FACTOR_QUERIES.masterDetailRail]: true,
    })

    const { result } = renderHook(() => ({
      masterDetail: useCanShowMasterDetail(),
      touchSizing: useTouchSizing(),
      hover: useShowHoverAffordance(),
      split: useCanSplitBuilderPanes(),
    }))

    expect(result.current).toEqual({ masterDetail: true, touchSizing: true, hover: true, split: false })
  })

  it('refuses master-detail at a tablet-mode viewport below the rail floor (768x550 class)', () => {
    const host = new MatchMediaHost()
    // Width >= 768 (not phone) but height < 600: the rail query does not match.
    host.install({ [FORM_FACTOR_QUERIES.masterDetailRail]: false })

    const { result } = renderHook(() => useCanShowMasterDetail())

    expect(result.current).toBe(false)
  })
})
