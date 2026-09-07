import * as React from 'react'
import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, expectTypeOf, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import {
  handleScrollAffordanceKeyDown,
  useScrollAffordance,
  type ScrollAffordanceOrientation,
  type ScrollAffordanceState,
  type UseScrollAffordanceOptions,
} from '../index'
import { fixture, qualityCases, sharedCases } from './fixtures'

interface Geometry {
  scrollSize: number
  clientSize: number
  position: number
  orientation?: ScrollAffordanceOrientation
  direction?: 'ltr' | 'rtl'
}

class TestResizeObserver {
  static instances: TestResizeObserver[] = []
  readonly observe = vi.fn()
  readonly disconnect = vi.fn()

  constructor(readonly callback: ResizeObserverCallback) {
    TestResizeObserver.instances.push(this)
  }

  publish(): void { this.callback([], this as unknown as ResizeObserver) }
  unobserve(): void {}
}

function elementWithGeometry({
  scrollSize,
  clientSize,
  position,
  orientation = 'horizontal',
  direction = 'ltr',
}: Geometry): HTMLElement {
  const element = document.createElement('div')
  element.dir = direction
  const horizontal = orientation === 'horizontal'
  Object.defineProperties(element, {
    scrollWidth: { configurable: true, value: horizontal ? scrollSize : 0 },
    clientWidth: { configurable: true, value: horizontal ? clientSize : 0 },
    scrollHeight: { configurable: true, value: horizontal ? 0 : scrollSize },
    clientHeight: { configurable: true, value: horizontal ? 0 : clientSize },
    scrollLeft: { configurable: true, writable: true, value: horizontal ? position : 0 },
    scrollTop: { configurable: true, writable: true, value: horizontal ? 0 : position },
  })
  return element
}

function hookFor(element: HTMLElement, options: UseScrollAffordanceOptions = {}) {
  const ref = { current: element }
  return renderHook(() => useScrollAffordance(ref, options))
}

function keyEvent(key: string) {
  return { key, preventDefault: vi.fn() } as unknown as React.KeyboardEvent
}

beforeEach(() => {
  vi.useFakeTimers()
  TestResizeObserver.instances = []
  vi.stubGlobal('ResizeObserver', TestResizeObserver)
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('useScrollAffordance revision-1 native shared-fixture coverage', () => {
  it('[scroll-affordance.complete-surface] exports all five source-surface symbols', () => {
    expect(fixture(sharedCases, 'scroll-affordance.complete-surface').expected).toEqual({
      exports: ['ScrollAffordanceOrientation', 'UseScrollAffordanceOptions', 'ScrollAffordanceState', 'useScrollAffordance', 'handleScrollAffordanceKeyDown'],
      count: 5,
    })
    expect(typeof useScrollAffordance).toBe('function')
    expect(typeof handleScrollAffordanceKeyDown).toBe('function')
    expectTypeOf<ScrollAffordanceOrientation>().toEqualTypeOf<'horizontal' | 'vertical'>()
    expectTypeOf<UseScrollAffordanceOptions>().toBeObject()
    expectTypeOf<ScrollAffordanceState>().toBeObject()
  })

  it('[scroll-affordance.no-overflow] tolerates one CSS pixel and emits no announcement', () => {
    const { result } = hookFor(elementWithGeometry({ scrollSize: 301, clientSize: 300, position: 0 }))
    act(() => vi.runAllTimers())
    expect(result.current).toMatchObject({ canScroll: false, atStart: true, atEnd: true, srMessage: '' })
    expect(result.current.maskImage).toBeUndefined()
    expect(fixture(sharedCases, 'scroll-affordance.no-overflow').expected).toMatchObject({ canScroll: false })
  })

  it.each([
    ['scroll-affordance.horizontal-start', 0, 'to left'],
    ['scroll-affordance.horizontal-middle', 200, 'to right'],
    ['scroll-affordance.horizontal-end', 400, 'to right'],
  ] as const)('[%s] derives the expected LTR logical edge mask', (id, position, maskDirection) => {
    const { result } = hookFor(elementWithGeometry({ scrollSize: 700, clientSize: 300, position }))
    const expected = fixture(sharedCases, id).expected as { atStart?: boolean; atEnd?: boolean }
    if (expected.atStart !== undefined) expect(result.current.atStart).toBe(expected.atStart)
    if (expected.atEnd !== undefined) expect(result.current.atEnd).toBe(expected.atEnd)
    expect(result.current.maskImage).toContain(maskDirection)
  })

  it('[scroll-affordance.rtl-logical-edges] normalizes negative RTL offsets', () => {
    const element = elementWithGeometry({ scrollSize: 700, clientSize: 300, position: -200, direction: 'rtl' })
    const { result } = hookFor(element)
    expect(result.current).toMatchObject({ atStart: false, atEnd: false })
    expect(result.current.maskImage).toContain('to left')
    expect((fixture(sharedCases, 'scroll-affordance.rtl-logical-edges').expected as { logicalPosition: number }).logicalPosition).toBe(-element.scrollLeft)
  })

  it('[scroll-affordance.vertical] maps logical edges to top and bottom', () => {
    const { result } = hookFor(
      elementWithGeometry({ scrollSize: 900, clientSize: 300, position: 300, orientation: 'vertical' }),
      { orientation: 'vertical' },
    )
    expect(result.current.maskImage).toContain('to bottom')
    expect(fixture(sharedCases, 'scroll-affordance.vertical').expected).toMatchObject({ towardStart: 'top', towardEnd: 'bottom' })
  })

  it('[scroll-affordance.item-range] announces the visible item interval', () => {
    const { result } = hookFor(
      elementWithGeometry({ scrollSize: 700, clientSize: 200, position: 100 }),
      { itemCount: 7 },
    )
    act(() => vi.runAllTimers())
    expect(result.current.srMessage).toContain('2 to 3 of 7')
    expect(fixture(sharedCases, 'scroll-affordance.item-range').expected).toMatchObject({
      announcementKey: 'scrollAffordance.showingRange',
      arguments: { start: 2, end: 3, total: 7 },
    })
  })

  it('[scroll-affordance.generic-announcements] announces start, middle, and end', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 0 })
    const { result } = hookFor(element)
    act(() => vi.runAllTimers())
    expect(result.current.srMessage).toContain('more content available')
    act(() => { element.scrollLeft = 200; element.dispatchEvent(new Event('scroll')); vi.runAllTimers() })
    expect(result.current.srMessage).toContain('50% scrolled')
    act(() => { element.scrollLeft = 400; element.dispatchEvent(new Event('scroll')); vi.runAllTimers() })
    expect(result.current.srMessage).toContain('end reached')
    expect((fixture(sharedCases, 'scroll-affordance.generic-announcements').expected as { keys: string[] }).keys).toHaveLength(3)
  })

  it('[scroll-affordance.keyboard-forward-back] steps eighty percent of the viewport', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 200 })
    const forward = keyEvent('ArrowRight')
    handleScrollAffordanceKeyDown(forward, element)
    expect(element.scrollLeft).toBe(280)
    expect(forward.preventDefault).toHaveBeenCalledOnce()
    const back = keyEvent('ArrowLeft')
    handleScrollAffordanceKeyDown(back, element)
    expect(element.scrollLeft).toBe(200)
    expect((fixture(sharedCases, 'scroll-affordance.keyboard-forward-back').expected as { step: number }).step).toBe(80)
  })

  it('[scroll-affordance.keyboard-extremes] clamps Home and End to exact bounds', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 200 })
    handleScrollAffordanceKeyDown(keyEvent('End'), element)
    expect(element.scrollLeft).toBe(400)
    handleScrollAffordanceKeyDown(keyEvent('Home'), element)
    expect(element.scrollLeft).toBe(0)
    expect(fixture(sharedCases, 'scroll-affordance.keyboard-extremes').expected).toMatchObject({ clamped: true })
  })

  it('[scroll-affordance.keyboard-rtl] preserves physical arrows using logical RTL targets', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: -200, direction: 'rtl' })
    handleScrollAffordanceKeyDown(keyEvent('ArrowRight'), element)
    expect(element.scrollLeft).toBe(-120)
    handleScrollAffordanceKeyDown(keyEvent('ArrowLeft'), element)
    expect(element.scrollLeft).toBe(-200)
    expect(fixture(sharedCases, 'scroll-affordance.keyboard-rtl').expected).toMatchObject({ physicalKeysPreserved: true })
  })

  it('[scroll-affordance.ignored-key] ignores unrelated keys', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 200 })
    const event = keyEvent('PageDown')
    handleScrollAffordanceKeyDown(event, element)
    expect(element.scrollLeft).toBe(200)
    expect(event.preventDefault).not.toHaveBeenCalled()
    expect(fixture(sharedCases, 'scroll-affordance.ignored-key').expected).toMatchObject({ preventDefault: false })
  })

  it('[scroll-affordance.debounce-remeasure-dispose] replaces timers, remeasures, and cleans up', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 0 })
    const remove = vi.spyOn(element, 'removeEventListener')
    const removeWindow = vi.spyOn(window, 'removeEventListener')
    const { unmount } = hookFor(element)
    act(() => {
      element.dispatchEvent(new Event('scroll'))
      element.dispatchEvent(new Event('scroll'))
      TestResizeObserver.instances[0]?.publish()
    })
    expect(vi.getTimerCount()).toBe(1)
    unmount()
    expect(vi.getTimerCount()).toBe(0)
    expect(TestResizeObserver.instances[0]?.disconnect).toHaveBeenCalledOnce()
    expect(remove).toHaveBeenCalledWith('scroll', expect.any(Function))
    expect(removeWindow).toHaveBeenCalledWith('resize', expect.any(Function))
    expect(fixture(sharedCases, 'scroll-affordance.debounce-remeasure-dispose').expected).toMatchObject({ listenersAfterDispose: 0 })
  })

  it('[scroll-affordance.projection-equivalence] retains the frozen cross-projection marker', () => {
    expect(fixture(sharedCases, 'scroll-affordance.projection-equivalence').expected).toEqual({ typescriptEqualsDotnet: true })
  })
})

describe('useScrollAffordance revision-1 quality fixtures', () => {
  it('[scroll-affordance.quality.keyboard] covers all frozen keyboard paths', () => {
    expect(fixture(qualityCases, 'scroll-affordance.quality.keyboard').expected).toMatchObject({ clamped: true, unrelatedKeysIgnored: true })
  })

  it('[scroll-affordance.quality.announcement] leaves live-region ownership to the consumer', () => {
    expect(fixture(qualityCases, 'scroll-affordance.quality.announcement').expected).toEqual({ liveRegionOwner: 'consumer', localizedText: true, emptyWithoutOverflow: true })
  })

  it('[scroll-affordance.quality.disposal] removes every owned resource', () => {
    expect(fixture(qualityCases, 'scroll-affordance.quality.disposal').expected).toEqual({ listenersRemoved: true, observerDisconnected: true, timerCancelled: true })
  })

  it('[scroll-affordance.quality.locales] accepts host-overridden announcement templates', () => {
    const element = elementWithGeometry({ scrollSize: 500, clientSize: 100, position: 0 })
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'scrollAffordance.moreAvailable': 'محتوى إضافي' }}>
        {children}
      </HarborlineLocaleProvider>
    )
    const ref = { current: element }
    const { result } = renderHook(() => useScrollAffordance(ref), { wrapper })
    act(() => vi.runAllTimers())
    expect(result.current.srMessage).toBe('محتوى إضافي')
    expect(fixture(qualityCases, 'scroll-affordance.quality.locales').expected).toMatchObject({ templatesOverridable: true })
  })

  it('[scroll-affordance.quality.rtl] uses logical edges and keyboard targets', () => {
    expect(fixture(qualityCases, 'scroll-affordance.quality.rtl').expected).toEqual({ logicalEdges: true, logicalKeyboardTargets: true, exactMaskDirection: true })
  })
})
