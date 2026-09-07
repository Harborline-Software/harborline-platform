import type * as React from 'react'

export function elementBoundary(tagName = 'div'): {
  element: HTMLElement
  child: HTMLElement
  ref: React.RefObject<HTMLElement | null>
} {
  const element = document.createElement(tagName)
  const child = document.createElement('span')
  element.append(child)
  document.body.append(element)
  return { element, child, ref: { current: element } }
}

export function listenerCalls(
  spy: { mock: { calls: unknown[][] } },
  eventType: 'mousedown' | 'pointerdown',
): number {
  return spy.mock.calls.filter(call => call[0] === eventType).length
}
