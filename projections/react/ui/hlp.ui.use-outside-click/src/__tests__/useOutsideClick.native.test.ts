import { fireEvent, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { useOutsideClick } from '../index'
import { fixture, qualityCases } from './fixtures'
import { elementBoundary, listenerCalls } from './outsideClickHost'

afterEach(() => {
  document.body.replaceChildren()
  vi.restoreAllMocks()
})

describe('useOutsideClick React native behavior', () => {
  it('rebinds when the event type changes and leaves one active listener', () => {
    const added = vi.spyOn(document, 'addEventListener')
    const removed = vi.spyOn(document, 'removeEventListener')
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    const { rerender } = renderHook(
      ({ eventType }: { eventType: 'mousedown' | 'pointerdown' }) =>
        useOutsideClick(boundary.ref, onOutside, { eventType }),
      { initialProps: { eventType: 'mousedown' as const } },
    )

    rerender({ eventType: 'pointerdown' })
    fireEvent.mouseDown(document.body)
    fireEvent.pointerDown(document.body)

    expect(listenerCalls(added, 'mousedown')).toBe(1)
    expect(listenerCalls(removed, 'mousedown')).toBe(1)
    expect(listenerCalls(added, 'pointerdown')).toBe(1)
    expect(onOutside).toHaveBeenCalledOnce()
  })

  it('rebinds on boundary membership replacement without retaining stale containment', () => {
    const first = elementBoundary()
    const second = elementBoundary()
    const onOutside = vi.fn()
    const { rerender } = renderHook(
      ({ boundary }) => useOutsideClick(boundary, onOutside),
      { initialProps: { boundary: first.ref } },
    )

    rerender({ boundary: second.ref })
    fireEvent.mouseDown(first.element)
    fireEvent.mouseDown(second.element)

    expect(onOutside).toHaveBeenCalledOnce()
  })

  it('ignores null boundaries while still classifying unrelated targets as outside', () => {
    const live = elementBoundary()
    const nullBoundary = { current: null }
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick([nullBoundary, live.ref], onOutside))

    fireEvent.mouseDown(live.child)
    fireEvent.mouseDown(document.body)

    expect(onOutside).toHaveBeenCalledOnce()
  })

  it('defines pointer observation only; keyboard dismissal remains consumer-owned', () => {
    const pointer = fixture(qualityCases, 'outside-click.quality.pointer-only').expected as {
      keyboardDismissalSatisfiedByModule: boolean
    }
    const focus = fixture(qualityCases, 'outside-click.quality.focus-owned').expected as {
      focusReturnOwnedByConsumer: boolean
    }
    const escape = fixture(qualityCases, 'outside-click.quality.escape-owned').expected as {
      escapeOwnedByConsumer: boolean
    }
    const boundary = elementBoundary()
    const onOutside = vi.fn()
    renderHook(() => useOutsideClick(boundary.ref, onOutside))

    fireEvent.keyDown(document.body, { key: 'Escape' })

    expect(onOutside).not.toHaveBeenCalled()
    expect(pointer.keyboardDismissalSatisfiedByModule).toBe(false)
    expect(focus.focusReturnOwnedByConsumer).toBe(true)
    expect(escape.escapeOwnedByConsumer).toBe(true)
  })

  it('rejects event types outside the frozen vocabulary', () => {
    expect(() => renderHook(() => useOutsideClick(
      { current: null },
      () => undefined,
      { eventType: 'click' as 'mousedown' },
    ))).toThrow('unsupported-event-type')
  })
})
