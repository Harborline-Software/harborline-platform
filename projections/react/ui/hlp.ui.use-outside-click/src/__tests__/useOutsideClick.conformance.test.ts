import { fireEvent, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { useOutsideClick } from '../index'
import { fixture, sharedCases } from './fixtures'
import { elementBoundary, listenerCalls } from './outsideClickHost'

afterEach(() => {
  document.body.replaceChildren()
  vi.restoreAllMocks()
})

describe('useOutsideClick revision-1 shared fixtures', () => {
  it('uses mousedown as the default and installs one listener', () => {
    const expected = fixture(sharedCases, 'outside-click.default-event').expected as {
      eventType: string
      listeners: number
    }
    const added = vi.spyOn(document, 'addEventListener')
    const boundary = elementBoundary()
    const { unmount } = renderHook(() => useOutsideClick(boundary.ref, () => undefined))

    expect(listenerCalls(added, expected.eventType as 'mousedown')).toBe(expected.listeners)
    unmount()
  })

  it.each([
    ['outside-click.inside', 'root'],
    ['outside-click.descendant', 'child'],
  ] as const)('ignores a boundary or descendant target: %s', (id, targetKind) => {
    fixture(sharedCases, id)
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick(boundary.ref, onOutside))

    fireEvent.mouseDown(targetKind === 'root' ? boundary.element : boundary.child)

    expect(onOutside).not.toHaveBeenCalled()
  })

  it('treats every supplied live boundary as inside', () => {
    const value = fixture(sharedCases, 'outside-click.multi-boundary')
    const expected = value.expected as { callbacks: number }
    const menu = elementBoundary()
    const trigger = elementBoundary('button')
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick([menu.ref, trigger.ref], onOutside))

    fireEvent.mouseDown(menu.child)
    fireEvent.mouseDown(trigger.element)

    expect(onOutside).toHaveBeenCalledTimes(expected.callbacks)
  })

  it('invokes once with the originating outside event', () => {
    const expected = fixture(sharedCases, 'outside-click.outside').expected as {
      callbacks: number
      originatingEventPreserved: boolean
    }
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick(boundary.ref, onOutside))
    const event = new MouseEvent('mousedown', { bubbles: true })

    document.body.dispatchEvent(event)

    expect(onOutside).toHaveBeenCalledTimes(expected.callbacks)
    expect(onOutside.mock.calls[0]?.[0] === event).toBe(expected.originatingEventPreserved)
  })

  it('attaches nothing while disabled', () => {
    const expected = fixture(sharedCases, 'outside-click.disabled').expected as {
      listeners: number
      callbacks: number
    }
    const added = vi.spyOn(document, 'addEventListener')
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick(boundary.ref, onOutside, { enabled: false }))
    fireEvent.mouseDown(document.body)

    expect(listenerCalls(added, 'mousedown')).toBe(expected.listeners)
    expect(onOutside).toHaveBeenCalledTimes(expected.callbacks)
  })

  it('begins observing when enabled', () => {
    const expected = fixture(sharedCases, 'outside-click.enable').expected as {
      activeListeners: number
      callbacksAfterEnable: number
    }
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    const { rerender } = renderHook(
      ({ enabled }) => useOutsideClick(boundary.ref, onOutside, { enabled }),
      { initialProps: { enabled: false } },
    )

    rerender({ enabled: true })
    fireEvent.mouseDown(document.body)

    expect(onOutside).toHaveBeenCalledTimes(expected.callbacksAfterEnable)
    expect(expected.activeListeners).toBe(1)
  })

  it('observes only pointerdown when explicitly selected', () => {
    const expected = fixture(sharedCases, 'outside-click.pointerdown').expected as {
      callbacks: number
      callbackEvent: string
    }
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick(boundary.ref, onOutside, { eventType: 'pointerdown' }))

    fireEvent.mouseDown(document.body)
    fireEvent.pointerDown(document.body)

    expect(onOutside).toHaveBeenCalledTimes(expected.callbacks)
    expect(onOutside.mock.calls[0]?.[0].type).toBe(expected.callbackEvent)
  })

  it('uses the latest callback without replacing the listener', () => {
    const expected = fixture(sharedCases, 'outside-click.latest-callback').expected as {
      subscriptions: number
      invokedVersion: number
    }
    const added = vi.spyOn(document, 'addEventListener')
    const boundary = elementBoundary()
    const callbacks = [vi.fn(), vi.fn()]
    const { rerender } = renderHook(
      ({ version }) => useOutsideClick(boundary.ref, callbacks[version - 1]!),
      { initialProps: { version: 1 } },
    )

    rerender({ version: 2 })
    fireEvent.mouseDown(document.body)

    expect(listenerCalls(added, 'mousedown')).toBe(expected.subscriptions)
    expect(callbacks[0]).not.toHaveBeenCalled()
    expect(callbacks[expected.invokedVersion - 1]).toHaveBeenCalledOnce()
  })

  it('removes the listener and invokes nothing after disposal', () => {
    const expected = fixture(sharedCases, 'outside-click.dispose').expected as {
      removals: number
      callbacksAfterDispose: number
    }
    const removed = vi.spyOn(document, 'removeEventListener')
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    const { unmount } = renderHook(() => useOutsideClick(boundary.ref, onOutside))
    unmount()

    fireEvent.mouseDown(document.body)

    expect(listenerCalls(removed, 'mousedown')).toBe(expected.removals)
    expect(onOutside).toHaveBeenCalledTimes(expected.callbacksAfterDispose)
  })

  it('matches the neutral inside/outside callback sequence', () => {
    const expected = fixture(sharedCases, 'outside-click.projection-equivalence').expected as {
      callbackSequence: boolean[]
      typescriptEqualsDotnet: boolean
    }
    const first = elementBoundary()
    const second = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick([first.ref, second.ref], onOutside))
    const observed: boolean[] = []

    for (const target of [first.child, document.body, second.element]) {
      const before = onOutside.mock.calls.length
      fireEvent.mouseDown(target)
      observed.push(onOutside.mock.calls.length > before)
    }

    expect(observed).toEqual(expected.callbackSequence)
    expect(expected.typescriptEqualsDotnet).toBe(true)
  })
})
