import { renderHook } from '@testing-library/react'
import { renderToString } from 'react-dom/server'
import { afterEach, describe, expect, expectTypeOf, it, vi } from 'vitest'

import {
  useCanShowMasterDetail,
  useCanSplitBuilderPanes,
  useFormFactor,
  useShowHoverAffordance,
  useTouchSizing,
  type FormFactorState,
} from '../index'
import {
  FORM_FACTOR_QUERIES,
  resolveFormFactor,
  type MatchedFormFactorSignals,
} from '../formFactorPolicy'
import { fixture, qualityCases, sharedCases } from './fixtures'
import { MatchMediaHost } from './matchMediaHost'

afterEach(() => vi.unstubAllGlobals())

function signals(input: Partial<MatchedFormFactorSignals> = {}): MatchedFormFactorSignals {
  return {
    phoneWidth: false,
    desktopWidth: false,
    landscape: false,
    shortHeight: false,
    anyCoarse: false,
    anyFine: false,
    hover: false,
    masterDetailRail: false,
    ...input,
  }
}

// Drive a `cases`-shaped fixture's inputs through the resolver and compare against its
// expected values — the fixture is the input, never a transcript the test re-types.
function resolveCaseValues(id: string, read: (value: ReturnType<typeof resolveFormFactor>) => boolean) {
  const shared = fixture(sharedCases, id)
  const inputs = (shared.input as { cases: Array<Partial<MatchedFormFactorSignals>> }).cases
  const expected = shared.expected as { values: boolean[] }
  expect(inputs.length).toBe(expected.values.length)
  return { values: inputs.map(input => read(resolveFormFactor(signals(input)))), expected: expected.values }
}

describe('form-factor revision-2 shared fixtures', () => {
  it.each([
    ['form-factor.phone-width', { phoneWidth: true }],
    ['form-factor.tablet-residual', {}],
    ['form-factor.desktop-width', { desktopWidth: true }],
    ['form-factor.short-landscape-fold', { desktopWidth: true, landscape: true, shortHeight: true }],
  ] as const)('%s', (id, input) => {
    const expected = fixture(sharedCases, id).expected as FormFactorState
    const { mode, orientation, heightClass } = resolveFormFactor(signals(input))
    expect({ mode, orientation, heightClass }).toEqual(expected)
  })

  it('resolves master-detail from the fixture cases (non-phone mode AND the shared rail query)', () => {
    // The 768x550 class lives in the fixture as the empty-signals case: tablet mode, below the rail floor.
    const { values, expected } = resolveCaseValues('form-factor.master-detail', value => value.canShowMasterDetail)
    expect(values).toEqual(expected)
  })

  it('resolves touch sizing from the fixture cases (phone mode or any coarse pointer)', () => {
    const { values, expected } = resolveCaseValues('form-factor.touch-sizing', value => value.touchSizing)
    expect(values).toEqual(expected)
  })

  it('resolves hover affordance from the fixture cases (hover or any fine pointer)', () => {
    const { values, expected } = resolveCaseValues('form-factor.hover-affordance', value => value.showHoverAffordance)
    expect(values).toEqual(expected)
  })

  it('delegates the split-builder query byte-identically', () => {
    const host = new MatchMediaHost()
    host.install({ [FORM_FACTOR_QUERIES.splitBuilderPanes]: true })
    const { result } = renderHook(() => useCanSplitBuilderPanes())
    const expected = fixture(sharedCases, 'form-factor.split-builder')
    expect(FORM_FACTOR_QUERIES.splitBuilderPanes).toBe((expected.input as { query: string }).query)
    expect(result.current).toBe((expected.expected as { value: boolean }).value)
  })

  it('fails closed during SSR while retaining the tablet residual policy', () => {
    const browser = globalThis.window
    vi.stubGlobal('window', undefined)
    let observed: unknown

    function Probe() {
      observed = {
        signals: useFormFactor(),
        masterDetail: useCanShowMasterDetail(),
        touchSizing: useTouchSizing(),
        hoverAffordance: useShowHoverAffordance(),
        splitBuilder: useCanSplitBuilderPanes(),
      }
      return null
    }

    renderToString(<Probe />)
    const expected = fixture(sharedCases, 'form-factor.ssr-fail-closed').expected as {
      mode: string
      masterDetail: boolean
      touchSizing: boolean
      hoverAffordance: boolean
      splitBuilder: boolean
    }
    expect(observed).toEqual({
      signals: { mode: expected.mode, orientation: 'portrait', heightClass: 'tall' },
      masterDetail: expected.masterDetail,
      touchSizing: expected.touchSizing,
      hoverAffordance: expected.hoverAffordance,
      splitBuilder: expected.splitBuilder,
    })
    vi.stubGlobal('window', browser)
  })

  it('is total over every Boolean signal combination', () => {
    const outputs = new Set<string>()
    for (let mask = 0; mask < 256; mask += 1) {
      const value = resolveFormFactor(signals({
        phoneWidth: Boolean(mask & 1),
        desktopWidth: Boolean(mask & 2),
        landscape: Boolean(mask & 4),
        shortHeight: Boolean(mask & 8),
        anyCoarse: Boolean(mask & 16),
        anyFine: Boolean(mask & 32),
        hover: Boolean(mask & 64),
        masterDetailRail: Boolean(mask & 128),
      }))
      outputs.add(JSON.stringify(value))
    }
    expect(outputs.size).toBeGreaterThan(0)
    expect(fixture(sharedCases, 'form-factor.projection-equivalence').expected).toEqual({
      typescriptEqualsDotnet: true,
    })
  })

  it('retains the five captured hook exports and state type', () => {
    expect(typeof useCanShowMasterDetail).toBe('function')
    expect(typeof useCanSplitBuilderPanes).toBe('function')
    expect(typeof useFormFactor).toBe('function')
    expect(typeof useShowHoverAffordance).toBe('function')
    expect(typeof useTouchSizing).toBe('function')
    expectTypeOf<FormFactorState>().toEqualTypeOf<{
      mode: 'phone' | 'tablet' | 'desktop'
      orientation: 'portrait' | 'landscape'
      heightClass: 'short' | 'tall'
    }>()
  })
})

describe('form-factor revision-2 quality fixtures', () => {
  it('uses capability signals instead of device identity', () => {
    expect(fixture(qualityCases, 'form-factor.quality.capabilities-over-device').expected).toEqual({
      deviceSniffing: false,
    })
    const desktopTouch = resolveFormFactor(signals({ desktopWidth: true, anyCoarse: true }))
    expect(desktopTouch.mode).toBe('desktop')
    expect(desktopTouch.touchSizing).toBe(true)
  })

  it('enforces the coarse-pointer touch floor', () => {
    expect(fixture(qualityCases, 'form-factor.quality.touch-floor').expected).toEqual({
      coarsePointerForcesTouchSizing: true,
    })
    expect(resolveFormFactor(signals({ anyCoarse: true })).touchSizing).toBe(true)
  })
})
